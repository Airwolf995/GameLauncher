using System.Text;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

/// <summary>
/// Die Testdaten bilden das binäre VDF-Format nach: 0x00 leitet eine
/// Unterstruktur ein, 0x01 eine Zeichenkette, 0x02 eine 32-Bit-Zahl und 0x08
/// schliesst die aktuelle Struktur ab.
/// </summary>
public sealed class SteamShortcutsReaderTests
{
    private static void WriteKey(List<byte> buffer, byte type, string key)
    {
        buffer.Add(type);
        buffer.AddRange(Encoding.UTF8.GetBytes(key));
        buffer.Add(0x00);
    }

    private static void WriteString(List<byte> buffer, string key, string value)
    {
        WriteKey(buffer, 0x01, key);
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0x00);
    }

    private static void WriteInt32(List<byte> buffer, string key, uint value)
    {
        WriteKey(buffer, 0x02, key);
        buffer.AddRange(BitConverter.GetBytes(value));
    }

    private static byte[] BuildFile(params Action<List<byte>>[] entries)
    {
        var buffer = new List<byte>();
        WriteKey(buffer, 0x00, "shortcuts");

        for (int index = 0; index < entries.Length; index++)
        {
            WriteKey(buffer, 0x00, index.ToString());
            entries[index](buffer);
            buffer.Add(0x08); // Eintrag abschliessen
        }

        buffer.Add(0x08); // shortcuts abschliessen
        buffer.Add(0x08); // Dokument abschliessen
        return buffer.ToArray();
    }

    [Fact]
    public void Parse_LiestEinenVollstaendigenEintrag()
    {
        byte[] data = BuildFile(buffer =>
        {
            WriteInt32(buffer, "appid", 2147483649);
            WriteString(buffer, "AppName", "Mein Spiel");
            WriteString(buffer, "Exe", "\"C:\\Spiele\\spiel.exe\"");
            WriteString(buffer, "StartDir", "\"C:\\Spiele\\\"");
            WriteString(buffer, "LaunchOptions", "-windowed");
            WriteInt32(buffer, "IsHidden", 0);
        });

        var shortcuts = SteamShortcutsReader.Parse(data);

        var shortcut = Assert.Single(shortcuts);
        Assert.Equal("Mein Spiel", shortcut.Name);
        Assert.Equal(@"C:\Spiele\spiel.exe", shortcut.ExecutablePath);
        Assert.Equal(@"C:\Spiele\", shortcut.StartDirectory);
        Assert.Equal("-windowed", shortcut.LaunchOptions);
        Assert.Equal("2147483649", shortcut.AppId);
        Assert.False(shortcut.IsHidden);
    }

    /// <summary>
    /// Bildet die Feldfolge nach, die Steam beim Hinzufügen eines Nicht-Steam-Spiels
    /// tatsächlich schreibt: "Exe" steht in Anführungszeichen, "StartDir" nicht,
    /// danach folgen zahlreiche weitere Felder und die verschachtelte Schlagwortliste.
    /// </summary>
    [Fact]
    public void Parse_LiestEintragImFormatEinerEchtenSteamDatei()
    {
        byte[] data = BuildFile(buffer =>
        {
            WriteInt32(buffer, "appid", 3799996914);
            WriteString(buffer, "AppName", "Blender 5.2");
            WriteString(buffer, "Exe", "\"C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender-launcher.exe\"");
            WriteString(buffer, "StartDir", "C:\\Program Files\\Blender Foundation\\Blender 5.2\\");
            WriteString(buffer, "icon", "");
            WriteString(buffer, "ShortcutPath", "");
            WriteString(buffer, "LaunchOptions", "");
            WriteInt32(buffer, "IsHidden", 0);
            WriteInt32(buffer, "AllowDesktopConfig", 1);
            WriteInt32(buffer, "AllowOverlay", 1);
            WriteInt32(buffer, "OpenVR", 0);
            WriteInt32(buffer, "Devkit", 0);
            WriteString(buffer, "DevkitGameID", "");
            WriteInt32(buffer, "DevkitOverrideAppID", 0);
            WriteInt32(buffer, "LastPlayTime", 0);
            WriteString(buffer, "FlatpakAppID", "");
            WriteString(buffer, "sortas", "");
            WriteKey(buffer, 0x00, "tags");
            buffer.Add(0x08);
        });

        var shortcut = Assert.Single(SteamShortcutsReader.Parse(data));

        Assert.Equal("3799996914", shortcut.AppId);
        Assert.Equal("Blender 5.2", shortcut.Name);
        Assert.Equal(@"C:\Program Files\Blender Foundation\Blender 5.2\blender-launcher.exe", shortcut.ExecutablePath);
        Assert.Equal(@"C:\Program Files\Blender Foundation\Blender 5.2\", shortcut.StartDirectory);
        Assert.Equal(string.Empty, shortcut.LaunchOptions);
        Assert.False(shortcut.IsHidden);
    }

    [Fact]
    public void Parse_LiestMehrereEintraege()
    {
        byte[] data = BuildFile(
            buffer =>
            {
                WriteString(buffer, "AppName", "Erstes Spiel");
                WriteString(buffer, "Exe", "\"C:\\A\\a.exe\"");
            },
            buffer =>
            {
                WriteString(buffer, "AppName", "Zweites Spiel");
                WriteString(buffer, "Exe", "\"C:\\B\\b.exe\"");
            });

        var shortcuts = SteamShortcutsReader.Parse(data);

        Assert.Equal(2, shortcuts.Count);
        Assert.Equal("Erstes Spiel", shortcuts[0].Name);
        Assert.Equal("Zweites Spiel", shortcuts[1].Name);
    }

    [Fact]
    public void Parse_UeberspringtVerschachtelteStrukturen()
    {
        byte[] data = BuildFile(buffer =>
        {
            WriteString(buffer, "AppName", "Spiel mit Schlagworten");
            WriteKey(buffer, 0x00, "tags");
            WriteString(buffer, "0", "Favorit");
            WriteString(buffer, "1", "Koop");
            buffer.Add(0x08); // tags abschliessen
            WriteString(buffer, "Exe", "\"C:\\C\\c.exe\"");
        });

        var shortcuts = SteamShortcutsReader.Parse(data);

        var shortcut = Assert.Single(shortcuts);
        Assert.Equal("Spiel mit Schlagworten", shortcut.Name);
        Assert.Equal(@"C:\C\c.exe", shortcut.ExecutablePath);
    }

    [Fact]
    public void Parse_ErkenntAusgeblendeteEintraege()
    {
        byte[] data = BuildFile(buffer =>
        {
            WriteString(buffer, "AppName", "Verstecktes Spiel");
            WriteString(buffer, "Exe", "\"C:\\D\\d.exe\"");
            WriteInt32(buffer, "IsHidden", 1);
        });

        var shortcut = Assert.Single(SteamShortcutsReader.Parse(data));

        Assert.True(shortcut.IsHidden);
    }

    [Fact]
    public void Parse_KommtMitFeldnamenInAbweichenderSchreibweiseZurecht()
    {
        byte[] data = BuildFile(buffer =>
        {
            WriteString(buffer, "appname", "Kleingeschrieben");
            WriteString(buffer, "exe", "\"C:\\E\\e.exe\"");
        });

        var shortcut = Assert.Single(SteamShortcutsReader.Parse(data));

        Assert.Equal("Kleingeschrieben", shortcut.Name);
        Assert.Equal(@"C:\E\e.exe", shortcut.ExecutablePath);
    }

    [Fact]
    public void Parse_LiefertBeiLeererDateiKeineEintraege()
    {
        // Entspricht einer unbenutzten shortcuts.vdf: 0x00 "shortcuts" 0x00 0x08 0x08
        var buffer = new List<byte>();
        WriteKey(buffer, 0x00, "shortcuts");
        buffer.Add(0x08);
        buffer.Add(0x08);

        Assert.Empty(SteamShortcutsReader.Parse(buffer.ToArray()));
        Assert.Equal(13, buffer.Count);
    }

    [Fact]
    public void Parse_LiefertBeiFremdemWurzelschluesselKeineEintraege()
    {
        var buffer = new List<byte>();
        WriteKey(buffer, 0x00, "etwasanderes");
        buffer.Add(0x08);

        Assert.Empty(SteamShortcutsReader.Parse(buffer.ToArray()));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    public void Parse_LiefertBeiUnbrauchbaremInhaltKeineEintraege(byte[] data)
    {
        Assert.Empty(SteamShortcutsReader.Parse(data));
    }

    [Fact]
    public void Parse_BrichtBeiAbgeschnittenerDateiSauberAb()
    {
        var buffer = new List<byte>();
        WriteKey(buffer, 0x00, "shortcuts");
        WriteKey(buffer, 0x00, "0");
        WriteString(buffer, "AppName", "Unvollstaendig");
        WriteKey(buffer, 0x01, "Exe"); // Wert fehlt

        var shortcuts = SteamShortcutsReader.Parse(buffer.ToArray());

        var shortcut = Assert.Single(shortcuts);
        Assert.Equal("Unvollstaendig", shortcut.Name);
        Assert.Equal(string.Empty, shortcut.ExecutablePath);
    }

    /// <summary>
    /// Die Kennung eines Nicht-Steam-Spiels entsteht aus seiner AppID. Steht dieselbe
    /// Verknüpfung in zwei Benutzerprofilen, entstünde sonst zweimal derselbe Eintrag -
    /// mit gemeinsamer Spielzeit, gemeinsamen Favoriten und einem Löschen, das nur eine
    /// der beiden Kacheln entfernt.
    /// </summary>
    [Fact]
    public void ReadShortcutGames_LiefertEinSpielTrotzZweierBenutzerprofile()
    {
        string steamRoot = Directory.CreateTempSubdirectory("GameLauncherSteamShortcuts_").FullName;
        try
        {
            string program = Path.Combine(steamRoot, "MeinSpiel.exe");
            File.WriteAllBytes(program, new byte[16]);

            byte[] file = BuildFile(buffer =>
            {
                WriteInt32(buffer, "appid", 2846102741);
                WriteString(buffer, "AppName", "Mein Spiel");
                WriteString(buffer, "Exe", $"\"{program}\"");
                WriteString(buffer, "StartDir", steamRoot);
            });

            foreach (string user in new[] { "11111111", "22222222" })
            {
                string configDirectory = Path.Combine(steamRoot, "userdata", user, "config");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllBytes(Path.Combine(configDirectory, "shortcuts.vdf"), file);
            }

            var games = SteamShortcutsReader.ReadShortcutGames(steamRoot);

            var game = Assert.Single(games);
            Assert.Equal("steamshortcut_2846102741", game.Id);
            Assert.Equal("Mein Spiel", game.Name);
        }
        finally
        {
            Directory.Delete(steamRoot, recursive: true);
        }
    }
}
