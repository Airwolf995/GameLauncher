using System.Text.Json;
using GameLauncher.Services;
using GameLauncher.Services.Settings;

namespace GameLauncher.Tests;

public sealed class ConfigTransferServiceTests
{
    [Fact]
    public void Export_SchreibtDenStandAusDemSpeicher()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var configService = new ConfigService(configPath);
            var transfer = new ConfigTransferService(configService);

            configService.UpdateConfig(config => config.Favorites.Add("steam:620"));
            string targetPath = Path.Combine(directory, "sicherung.json");

            Assert.Equal(ConfigTransferResult.Success, transfer.Export(targetPath));

            // Der Stand liegt wegen der verzoegerten Speicherung noch nicht in der
            // Konfigurationsdatei; die Sicherung muss ihn trotzdem enthalten.
            Assert.Contains("steam:620", File.ReadAllText(targetPath));
        });
    }

    [Fact]
    public void Import_UebernimmtDieDateiUndSichertDenBisherigenStand()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var configService = new ConfigService(configPath);
            var transfer = new ConfigTransferService(configService);

            configService.UpdateConfig(config => config.Favorites.Add("steam:alt"));
            configService.SaveConfigImmediate(configService.Config);

            string sourcePath = Path.Combine(directory, "sicherung.json");
            File.WriteAllText(sourcePath, """{"favorites":["steam:neu"],"theme":"Blue"}""");

            Assert.Equal(ConfigTransferResult.Success, transfer.Import(sourcePath));

            Assert.Contains("steam:neu", File.ReadAllText(configPath));
            string[] backups = Directory.GetFiles(directory, "*.vor-import-*.bak");
            Assert.Single(backups);
            Assert.Contains("steam:alt", File.ReadAllText(backups[0]));
        });
    }

    /// <summary>
    /// Nach dem Einspielen fuehrt die laufende Anwendung noch die alte
    /// Konfiguration im Speicher. Wuerde sie weiter schreiben, waere die gerade
    /// eingespielte Datei beim naechsten Speichern wieder ueberschrieben.
    /// </summary>
    [Fact]
    public void Import_HaeltWeiteresSpeichernAn()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var configService = new ConfigService(configPath);
            var transfer = new ConfigTransferService(configService);

            configService.UpdateConfig(config => config.Favorites.Add("steam:alt"));
            configService.SaveConfigImmediate(configService.Config);

            string sourcePath = Path.Combine(directory, "sicherung.json");
            File.WriteAllText(sourcePath, """{"favorites":["steam:neu"],"theme":"Blue"}""");
            Assert.Equal(ConfigTransferResult.Success, transfer.Import(sourcePath));

            // Ein Speicherversuch mit dem alten Stand aus dem Speicher.
            configService.SaveConfigImmediate(configService.Config);

            Assert.Contains("steam:neu", File.ReadAllText(configPath));
            Assert.DoesNotContain("steam:alt", File.ReadAllText(configPath));
        });
    }

    [Fact]
    public void Import_LehntFremdeDateiAbUndLaesstDenBestandUnberuehrt()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var configService = new ConfigService(configPath);
            var transfer = new ConfigTransferService(configService);

            configService.UpdateConfig(config => config.Favorites.Add("steam:alt"));
            configService.SaveConfigImmediate(configService.Config);

            string sourcePath = Path.Combine(directory, "fremd.json");
            File.WriteAllText(sourcePath, """{"irgendwas":true}""");

            Assert.Equal(ConfigTransferResult.NotAConfigFile, transfer.Import(sourcePath));
            Assert.Contains("steam:alt", File.ReadAllText(configPath));
            Assert.Empty(Directory.GetFiles(directory, "*.vor-import-*.bak"));
        });
    }

    [Fact]
    public void Import_MeldetFehlendeDatei()
    {
        RunInTempDirectory(directory =>
        {
            using var configService = new ConfigService(Path.Combine(directory, "config.json"));
            var transfer = new ConfigTransferService(configService);

            Assert.Equal(
                ConfigTransferResult.SourceUnreadable,
                transfer.Import(Path.Combine(directory, "gibtesnicht.json")));
        });
    }

    /// <summary>
    /// Eine leere, aber gueltige JSON-Datei ergaebe eine vollstaendig leere
    /// Konfiguration - die falsche Datei im Auswahldialog haette damit alle
    /// Spielzeiten stillschweigend geloescht.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("kein json")]
    [InlineData("")]
    [InlineData("""{"foo":1,"bar":2}""")]
    public void IsConfigFile_LehntAlleseAbWasKeineKonfigurationIst(string json)
    {
        Assert.False(ConfigTransferService.IsConfigFile(json));
    }

    [Theory]
    [InlineData("""{"favorites":[]}""")]
    [InlineData("""{"play_time":{}}""")]
    [InlineData("""{"ui_settings":{}}""")]
    [InlineData("""{"theme":"Blue"}""")]
    public void IsConfigFile_ErkenntKonfigurationAnIhrenFeldern(string json)
    {
        Assert.True(ConfigTransferService.IsConfigFile(json));
    }

    /// <summary>
    /// Eine gesicherte Datei muss sich auch wieder einspielen lassen; die Probe
    /// auf die eigene Ausgabe faengt ein Auseinanderlaufen beider Seiten ab.
    /// </summary>
    [Fact]
    public void Export_ErzeugtEineDateiDieAlsKonfigurationGilt()
    {
        RunInTempDirectory(directory =>
        {
            using var configService = new ConfigService(Path.Combine(directory, "config.json"));
            var transfer = new ConfigTransferService(configService);
            string targetPath = Path.Combine(directory, "sicherung.json");

            Assert.Equal(ConfigTransferResult.Success, transfer.Export(targetPath));
            Assert.True(ConfigTransferService.IsConfigFile(File.ReadAllText(targetPath)));
        });
    }

    /// <summary>
    /// Die geprueften Feldnamen muessen zu den tatsaechlichen Namen im Modell
    /// passen; sonst wuerde eine echte Sicherung abgelehnt.
    /// </summary>
    [Fact]
    public void IsConfigFile_PasstZuDenFeldnamenDesModells()
    {
        string json = JsonSerializer.Serialize(new GameLauncher.Models.GameConfig());

        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            Assert.True(
                ConfigTransferService.IsConfigFile($$"""{"{{property.Name}}":null}"""),
                $"Das Feld '{property.Name}' des Modells wird nicht als Kennzeichen einer Konfiguration erkannt.");
        }
    }

    /// <summary>
    /// Der Zweck der ganzen Funktion: eine gesicherte Spielzeit muss sich nach
    /// einem Verlust wieder einspielen lassen. Geprueft wird der vollstaendige
    /// Weg aus Sichern, Veraendern und Wiederherstellen.
    /// </summary>
    [Fact]
    public void ExportUndImport_StellenEineVerloreneSpielzeitWiederHer()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            string backupPath = Path.Combine(directory, "sicherung.json");

            using (var configService = new ConfigService(configPath))
            {
                var transfer = new ConfigTransferService(configService);
                configService.UpdateConfig(config =>
                {
                    config.Favorites.Add("steam:620");
                    config.PlayTime["steam:620"] = new GameLauncher.Models.PlayTimeEntry
                    {
                        Name = "Portal 2",
                        Seconds = 7200
                    };
                });

                Assert.Equal(ConfigTransferResult.Success, transfer.Export(backupPath));

                // Die Spielzeit geht verloren.
                configService.UpdateConfig(config => config.PlayTime.Clear());
                configService.SaveConfigImmediate(configService.Config);
                Assert.DoesNotContain("Portal 2", File.ReadAllText(configPath));

                Assert.Equal(ConfigTransferResult.Success, transfer.Import(backupPath));
            }

            // Ein neuer Start liest die eingespielte Datei.
            using var restarted = new ConfigService(configPath);
            Assert.Equal(7200, restarted.Config.PlayTime["steam:620"].Seconds);
            Assert.Equal("Portal 2", restarted.Config.PlayTime["steam:620"].Name);
            Assert.Contains("steam:620", restarted.Config.Favorites);
        });
    }

    private static void RunInTempDirectory(Action<string> test)
    {
        string directory = Directory.CreateTempSubdirectory("GameLauncherConfigTransfer_").FullName;
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
