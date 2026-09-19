using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Settings;
using GameLauncher.ViewModels;

namespace GameLauncher.Tests
{
    /// <summary>
    /// Die Erreichbarkeit der Sensorquelle wird im Hintergrund geprueft. Bis das
    /// Ergebnis vorliegt, darf die Anzeige keine der beiden Antworten
    /// vorwegnehmen - andernfalls meldet das Einstellungsfenster beim Oeffnen
    /// kurz eine Verbindung, die es nie gab.
    /// </summary>
    public class SettingsViewModelSensorSourceTests
    {
        [Fact]
        public void SensorSource_ReportsCheckingBeforeTheProbeAnswers()
        {
            var probe = new BlockingSensorSourceProbe();

            RunWithViewModel(probe, viewModel =>
            {
                Assert.True(viewModel.IsSensorSourceChecking);
                Assert.False(viewModel.IsSensorSourceConnected);
                Assert.False(viewModel.IsSensorSourceMissing);

                probe.Answer(isAvailable: false);
                viewModel.SensorSourceCheck.Wait();

                Assert.True(viewModel.IsSensorSourceMissing);
                Assert.False(viewModel.IsSensorSourceChecking);
            });
        }

        [Fact]
        public void SensorSource_ReportsConnectedOnceTheProbeSucceeds()
        {
            var probe = new BlockingSensorSourceProbe();

            RunWithViewModel(probe, viewModel =>
            {
                probe.Answer(isAvailable: true);
                viewModel.SensorSourceCheck.Wait();

                Assert.True(viewModel.IsSensorSourceConnected);
                Assert.False(viewModel.IsSensorSourceMissing);
                Assert.False(viewModel.IsSensorSourceChecking);
            });
        }

        [Fact]
        public void SensorSource_ReturnsToCheckingWhenRecheckIsRequested()
        {
            var probe = new BlockingSensorSourceProbe();

            RunWithViewModel(probe, viewModel =>
            {
                probe.Answer(isAvailable: true);
                viewModel.SensorSourceCheck.Wait();
                Assert.True(viewModel.IsSensorSourceConnected);

                probe.Reset();
                viewModel.RecheckSensorSourceCommand.Execute(null);

                Assert.True(viewModel.IsSensorSourceChecking);
                Assert.False(viewModel.IsSensorSourceConnected);

                probe.Answer(isAvailable: false);
                viewModel.SensorSourceCheck.Wait();

                Assert.True(viewModel.IsSensorSourceMissing);
            });
        }

        [Fact]
        public void SensorSource_NotifiesTheDisplayAboutEveryState()
        {
            var probe = new BlockingSensorSourceProbe();

            RunWithViewModel(probe, viewModel =>
            {
                var changed = new List<string>();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != null && e.PropertyName.StartsWith("IsSensorSource", StringComparison.Ordinal))
                    {
                        changed.Add(e.PropertyName);
                    }
                };

                probe.Answer(isAvailable: true);
                viewModel.SensorSourceCheck.Wait();

                Assert.Contains(nameof(SettingsViewModel.IsSensorSourceChecking), changed);
                Assert.Contains(nameof(SettingsViewModel.IsSensorSourceConnected), changed);
                Assert.Contains(nameof(SettingsViewModel.IsSensorSourceMissing), changed);
            });
        }

        [Fact]
        public void SensorSource_BlocksTheRecheckButtonWhileCheckingOnly()
        {
            var probe = new BlockingSensorSourceProbe();

            RunWithViewModel(probe, viewModel =>
            {
                Assert.False(viewModel.RecheckSensorSourceCommand.CanExecute(null));

                probe.Answer(isAvailable: false);
                viewModel.SensorSourceCheck.Wait();

                Assert.True(viewModel.RecheckSensorSourceCommand.CanExecute(null));
            });
        }

        private static void RunWithViewModel(ISensorSourceProbe probe, Action<SettingsViewModel> assert)
        {
            RunInSta(() =>
            {
                string tempRoot = CreateTempRoot();
                string configPath = Path.Combine(tempRoot, "game_launcher_config.json");

                try
                {
                    using var manager = new GameManager(configPath);
                    using var viewModel = new SettingsViewModel(
                        manager,
                        _ => { },
                        _ => { },
                        new StubAutostartService(),
                        new StubSettingsDialogService(),
                        new StubSettingsUpdateService(),
                        new StubPlatformStatusService(),
                        probe,
                        new StubConfigTransferService());

                    assert(viewModel);
                }
                finally
                {
                    CleanupTempRoot(tempRoot);
                }
            });
        }

        /// <summary>
        /// Antwortet erst auf Zuruf, damit der Zustand waehrend der laufenden
        /// Pruefung ueberhaupt beobachtbar ist.
        /// </summary>
        private sealed class BlockingSensorSourceProbe : ISensorSourceProbe
        {
            private ManualResetEventSlim _released = new(initialState: false);
            private bool _isAvailable;

            public bool IsAvailable()
            {
                _released.Wait();
                return _isAvailable;
            }

            public void Answer(bool isAvailable)
            {
                _isAvailable = isAvailable;
                _released.Set();
            }

            public void Reset()
            {
                _released = new ManualResetEventSlim(initialState: false);
            }
        }

        private sealed class StubAutostartService : IAutostartService
        {
            public bool IsEnabled(bool fallbackValue) => fallbackValue;

            public void SetEnabled(bool enabled)
            {
            }
        }

        private sealed class StubSettingsDialogService : ISettingsDialogService
        {
            public string? SelectBackgroundImage() => null;

            public bool ConfirmReset() => false;

            public string? SelectConfigExportTarget(string suggestedFileName) => null;

            public string? SelectConfigImportSource() => null;

            public bool ConfirmImport() => false;

            public void ShowConfigTransferResult(string message, string title)
            {
            }
        }

        private sealed class StubConfigTransferService : IConfigTransferService
        {
            public ConfigTransferResult Export(string targetPath) => ConfigTransferResult.Success;

            public ConfigTransferResult Import(string sourcePath) => ConfigTransferResult.Success;
        }

        private sealed class StubSettingsUpdateService : ISettingsUpdateService
        {
            public Task CheckForUpdatesAsync() => Task.CompletedTask;
        }

        private sealed class StubPlatformStatusService : IPlatformStatusService
        {
            public IReadOnlyList<string> GetGogLibraryPaths() => Array.Empty<string>();

            public IReadOnlyList<string> GetUbisoftLibraryPaths() => Array.Empty<string>();

            public IReadOnlyList<string> GetEaLibraryPaths() => Array.Empty<string>();
        }

        private static void RunInSta(Action action)
        {
            Exception? capturedException = null;
            using var finished = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    finished.Set();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            finished.Wait();
            thread.Join();

            if (capturedException != null)
            {
                throw capturedException;
            }
        }

        private static string CreateTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
        }

        private static void CleanupTempRoot(string tempRoot)
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
