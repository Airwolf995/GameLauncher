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
            File.WriteAllText(sourcePath, """{"favorites":["steam:neu"],"theme":"Blue","ui_settings":{}}""");

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
            File.WriteAllText(sourcePath, """{"favorites":["steam:neu"],"theme":"Blue","ui_settings":{}}""");
            Assert.Equal(ConfigTransferResult.Success, transfer.Import(sourcePath));

            // Ein Speicherversuch mit dem alten Stand aus dem Speicher.
            configService.SaveConfigImmediate(configService.Config);

            Assert.Contains("steam:neu", File.ReadAllText(configPath));
            Assert.DoesNotContain("steam:alt", File.ReadAllText(configPath));
        });
    }

    /// <summary>
    /// File.Copy uebernimmt das Aenderungsdatum der Quelle. Ohne Nachsetzen
    /// truege die eingespielte Konfiguration den Zeitpunkt der Sicherung und
    /// saehe im Ordner aelter aus, als der Import war.
    /// </summary>
    [Fact]
    public void Import_SetztDasAenderungsdatumAufDenZeitpunktDesImports()
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var configService = new ConfigService(configPath);
            var transfer = new ConfigTransferService(configService);

            string sourcePath = Path.Combine(directory, "sicherung.json");
            File.WriteAllText(sourcePath, """{"favorites":[],"theme":"Blue","ui_settings":{}}""");

            var alterZeitpunkt = DateTime.Now.AddDays(-30);
            File.SetLastWriteTime(sourcePath, alterZeitpunkt);

            var vorDemImport = DateTime.Now.AddSeconds(-5);
            Assert.Equal(ConfigTransferResult.Success, transfer.Import(sourcePath));

            var datum = File.GetLastWriteTime(configPath);
            Assert.True(
                datum >= vorDemImport,
                $"Die eingespielte Konfiguration traegt {datum:g}, erwartet wurde der Zeitpunkt des Imports.");
            Assert.Equal(alterZeitpunkt, File.GetLastWriteTime(sourcePath), TimeSpan.FromSeconds(2));
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
            File.WriteAllText(sourcePath, """{"theme":"dark","editor":{"fontSize":14}}""");

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
    public void IsConfigFile_LehntAllesAbWasKeineKonfigurationIst(string json)
    {
        Assert.False(ConfigTransferService.IsConfigFile(json));
    }

    /// <summary>
    /// "theme" und "favorites" sind gebraeuchliche Schluesselnamen und stehen
    /// auch in der Konfigurationsdatei ganz anderer Anwendungen. Genuegte ein
    /// einzelner Treffer, ginge eine solche Datei als Sicherung durch und
    /// loeschte beim Einspielen den gesamten Bestand.
    /// </summary>
    [Theory]
    [InlineData("""{"theme":"dark"}""")]
    [InlineData("""{"favorites":["etwas"]}""")]
    [InlineData("""{"theme":"dark","editor":{"fontSize":14}}""")]
    [InlineData("""{"theme":"dark","favorites":[]}""")]
    public void IsConfigFile_LehntFremdeDateiMitGebraeuchlichenNamenAb(string json)
    {
        Assert.False(ConfigTransferService.IsConfigFile(json));
    }

    [Theory]
    [InlineData("""{"favorites":[],"play_time":{},"ui_settings":{}}""")]
    [InlineData("""{"theme":"Blue","hidden_games":[],"game_tags":{}}""")]
    [InlineData("""{"steam_library_paths":[],"manual_games":[],"last_played":{}}""")]
    public void IsConfigFile_ErkenntKonfigurationAnMehrerenFeldern(string json)
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
    /// passen. Wird ein Feld umbenannt, ohne die Liste nachzuziehen, sinkt die
    /// Zahl der Treffer - bei genug Umbenennungen wuerde eine echte Sicherung
    /// abgelehnt. Geprueft wird deshalb jedes einzelne Feld.
    /// </summary>
    [Fact]
    public void IsConfigFile_KenntJedesFeldnamenDesModells()
    {
        string json = JsonSerializer.Serialize(new GameLauncher.Models.GameConfig());

        using var document = JsonDocument.Parse(json);
        string[] modelProperties = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        foreach (string propertyName in modelProperties)
        {
            // Zwei andere Felder als Sockel, damit allein das dritte entscheidet.
            // Sie muessen sich vom geprueften Namen unterscheiden: derselbe Name
            // zweimal in einem Objekt zaehlt nur als ein Treffer.
            string[] socle = modelProperties.Where(name => name != propertyName).Take(2).ToArray();
            string socleJson = string.Join(",", socle.Select(name => $"\"{name}\":null"));

            Assert.False(
                ConfigTransferService.IsConfigFile($"{{{socleJson}}}"),
                "Der Sockel allein darf noch nicht genuegen.");
            Assert.True(
                ConfigTransferService.IsConfigFile($"{{{socleJson},\"{propertyName}\":null}}"),
                $"Das Feld '{propertyName}' des Modells wird nicht als Kennzeichen einer Konfiguration erkannt.");
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

    [Theory]
    [InlineData("""{"favorites":42,"theme":"Blue","ui_settings":{}}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","ui_settings":{"overlay_hotkey_ctrl":{}}}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","last_played":{"steam:1":"kein Datum"}}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","manual_games":[null]}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","play_time":{"steam:1":-1}}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","play_time":{"steam:1":{"seconds":"falsch"}}}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","manual_games":[{"id":null}]}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","manual_games":[{"name":"Spiel ohne ID"}]}""")]
    [InlineData("""{"favorites":[],"theme":"Blue","game_tags":{"steam:1":null}}""")]
    public void Import_LehntDefekteDatenAbOhneBestandOderSpeichernZuVerändern(string json)
    {
        RunInTempDirectory(directory =>
        {
            string configPath = Path.Combine(directory, "config.json");
            using var service = new ConfigService(configPath);
            string original = File.ReadAllText(configPath);
            string source = Path.Combine(directory, "defekt.json");
            File.WriteAllText(source, json);

            Assert.Equal(ConfigTransferResult.NotAConfigFile, new ConfigTransferService(service).Import(source));
            Assert.Equal(original, File.ReadAllText(configPath));
            Assert.Empty(Directory.GetFiles(directory, "*.vor-import-*.bak"));
            service.UpdateConfig(config => config.Favorites.Add("steam:weiter"));
            service.SaveConfigImmediate(service.Config);
            Assert.Contains("steam:weiter", File.ReadAllText(configPath));
        });
    }

    [Fact]
    public void Import_SichertAuchNochNichtGespeicherteÄnderungen()
    {
        RunInTempDirectory(directory =>
        {
            using var service = new ConfigService(Path.Combine(directory, "config.json"));
            service.UpdateConfig(config => config.Favorites.Add("steam:ungespeichert"));
            service.SaveConfig();
            string source = Path.Combine(directory, "sicherung.json");
            File.WriteAllText(source, """{"favorites":[],"theme":"Blue","ui_settings":{}}""");

            Assert.Equal(ConfigTransferResult.Success, new ConfigTransferService(service).Import(source));
            string backup = Assert.Single(Directory.GetFiles(directory, "*.vor-import-*.bak"));
            Assert.Contains("steam:ungespeichert", File.ReadAllText(backup));
            service.FlushPendingSave();
            Assert.Equal(File.ReadAllText(source), File.ReadAllText(service.ConfigPath));
        });
    }

    [Fact]
    public void Import_SchreibfehlerLässtBestandUndWeiteresSpeichernIntakt()
    {
        RunInTempDirectory(directory =>
        {
            using var service = new ConfigService(Path.Combine(directory, "config.json"));
            string original = File.ReadAllText(service.ConfigPath);
            string source = Path.Combine(directory, "sicherung.json");
            File.WriteAllText(source, """{"favorites":[],"theme":"Blue","ui_settings":{}}""");
            using (var blockedFile = new FileStream(service.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(ConfigTransferResult.WriteFailed, new ConfigTransferService(service).Import(source));
                Assert.Equal(original, File.ReadAllText(service.ConfigPath));
            }

            service.UpdateConfig(config => config.Favorites.Add("steam:weiter"));
            service.SaveConfigImmediate(service.Config);
            Assert.Contains("steam:weiter", File.ReadAllText(service.ConfigPath));
        });
    }

    [Fact]
    public void Import_AkzeptiertÄltereSpielzeitUndOptionaleNullFelder()
    {
        RunInTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "config.json");
            using (var service = new ConfigService(path))
            {
                string source = Path.Combine(directory, "alt.json");
                File.WriteAllText(source, """{"favorites":null,"play_time":{"steam:1":120},"ui_settings":null}""");
                Assert.Equal(ConfigTransferResult.Success, new ConfigTransferService(service).Import(source));
            }
            using var restarted = new ConfigService(path);
            Assert.Equal(120, restarted.Config.PlayTime["steam:1"].Seconds);
            Assert.Empty(restarted.Config.Favorites);
        });
    }

    [Fact]
    public void Import_VerwendetDenGeprüftenInhaltAuchWennDieQuelleDanachWechselt()
    {
        RunInTempDirectory(directory =>
        {
            string source = Path.Combine(directory, "sicherung.json");
            string imported = """{"favorites":["steam:neu"],"theme":"Blue","ui_settings":{}}""";
            File.WriteAllText(source, imported);
            bool replaceSource = false;
            using var service = new ConfigService(Path.Combine(directory, "config.json"), config =>
            {
                if (replaceSource)
                {
                    File.WriteAllText(source, "defekte neue Quelle");
                }
                return JsonSerializer.Serialize(config);
            });
            replaceSource = true;

            Assert.Equal(ConfigTransferResult.Success, new ConfigTransferService(service).Import(source));
            Assert.Equal(imported, File.ReadAllText(service.ConfigPath));
        });
    }

    [Fact]
    public async Task Import_WirdNichtDurchEinenBereitsLaufendenSpeichervorgangÜberschrieben()
    {
        string directory = Directory.CreateTempSubdirectory("GameLauncherConfigTransfer_").FullName;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int block = 0;
        DateTime now = DateTime.UtcNow;
        try
        {
            using var service = new ConfigService(Path.Combine(directory, "config.json"), config =>
            {
                if (Interlocked.Exchange(ref block, 0) == 1)
                {
                    started.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Testserialisierung wurde nicht freigegeben.");
                }
                return JsonSerializer.Serialize(config);
            }, TimeSpan.FromHours(1).TotalMilliseconds, () => now);
            service.UpdateConfig(config => config.Favorites.Add("steam:alt"));
            service.SaveConfig();
            now = now.AddHours(1);
            block = 1;
            Task flush = Task.Run(service.FlushPendingSave);
            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
                string source = Path.Combine(directory, "sicherung.json");
                string imported = """{"favorites":["steam:neu"],"theme":"Blue","ui_settings":{}}""";
                File.WriteAllText(source, imported);
                Task<ConfigTransferResult> import = Task.Run(() => new ConfigTransferService(service).Import(source));
                release.Set();
                await Task.WhenAll(flush, import).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(ConfigTransferResult.Success, await import);
                service.SaveConfigImmediate(service.Config);
                service.Dispose();
                Assert.Equal(imported, File.ReadAllText(service.ConfigPath));
                Assert.Contains("steam:alt", File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.vor-import-*.bak"))));
            }
            finally
            {
                release.Set();
                await flush.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            release.Set();
            Directory.Delete(directory, recursive: true);
        }
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
