using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests
{
    public class XboxScannerTests
    {
        [Fact]
        public void ComputeStableId_ReturnsDeterministicId_ForSameSource()
        {
            const string source = "MICROSOFT.MINECRAFTUWP";

            string id1 = XboxScanner.ComputeStableId(source);
            string id2 = XboxScanner.ComputeStableId(source);

            Assert.Equal(id1, id2);
            Assert.Equal(16, id1.Length);
        }

        [Fact]
        public void ComputeStableId_ReturnsDifferentIds_ForDifferentSources()
        {
            string id1 = XboxScanner.ComputeStableId("MICROSOFT.MINECRAFTUWP");
            string id2 = XboxScanner.ComputeStableId("BohemiaInteractivea.s.64c65697-cc6b-45a2-b1a5-1c03");

            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void TryCreateGameFromDirectory_ReturnsNull_WhenManifestFilesAreMissing()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherXboxTests", Guid.NewGuid().ToString("N"));
            string gameRoot = Path.Combine(tempRoot, "Broken Game");

            try
            {
                Directory.CreateDirectory(Path.Combine(gameRoot, "Content"));

                var game = XboxScanner.TryCreateGameFromDirectory(gameRoot);

                Assert.Null(game);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public void TryCreateGameFromDirectory_ReturnsNull_WhenLaunchAumidIsMissing()
        {
            string root = CreateXboxGameDirectory("Minecraft for Windows", "MICROSOFT.MINECRAFTUWP", "Minecraft for Windows");

            try
            {
                var game = XboxScanner.TryCreateGameFromDirectory(root);

                Assert.Null(game);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }

        [Fact]
        public void TryCreateGameFromDirectory_UsesPackageAumid_WhenIdentityMatches()
        {
            string root = CreateXboxGameDirectory("Minecraft for Windows", "MICROSOFT.MINECRAFTUWP", "Minecraft for Windows");
            var packages = new[]
            {
                new XboxScanner.XboxPackageInfo(
                    "MICROSOFT.MINECRAFTUWP",
                    Path.Combine(root, "Content"),
                    "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game")
            };

            try
            {
                var game = XboxScanner.TryCreateGameFromDirectory(root, packages);

                Assert.NotNull(game);
                Assert.Equal("uri", game!.LaunchType);
                Assert.Equal(@"shell:AppsFolder\MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game", game.Path);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }

        [Fact]
        public void TryCreateGameFromDirectory_UsesPackageAumid_WhenInstallLocationMatches()
        {
            string root = CreateXboxGameDirectory("DayZ", "BohemiaInteractive.DayZ", "DayZ");
            var packages = new[]
            {
                new XboxScanner.XboxPackageInfo(
                    "Different.Identity",
                    Path.Combine(root, "Content"),
                    "BohemiaInteractive.DayZ_12345!Game")
            };

            try
            {
                var game = XboxScanner.TryCreateGameFromDirectory(root, packages);

                Assert.NotNull(game);
                Assert.Equal("uri", game!.LaunchType);
                Assert.Equal(@"shell:AppsFolder\BohemiaInteractive.DayZ_12345!Game", game.Path);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }

        [Fact]
        public void TryCreateGameFromDirectory_ReturnsNull_ForDlcPackageWithoutApplicationId()
        {
            string root = CreateXboxGameDirectory(
                "Anniversary DLC",
                "FocusHomeInteractiveSA.SRDLC12",
                "Anniversary DLC",
                includeApplication: false);

            try
            {
                var game = XboxScanner.TryCreateGameFromDirectory(root);

                Assert.Null(game);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }

        [Fact]
        public void TryCreateGameFromDirectory_PrefersAumidMatchingApplicationId()
        {
            string root = CreateXboxGameDirectory("Minecraft for Windows", "MICROSOFT.MINECRAFTUWP", "Minecraft for Windows");
            var packages = new[]
            {
                new XboxScanner.XboxPackageInfo(
                    "MICROSOFT.MINECRAFTUWP",
                    Path.Combine(root, "Content"),
                    "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Launcher"),
                new XboxScanner.XboxPackageInfo(
                    "MICROSOFT.MINECRAFTUWP",
                    Path.Combine(root, "Content"),
                    "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game")
            };

            try
            {
                var game = XboxScanner.TryCreateGameFromDirectory(root, packages);

                Assert.NotNull(game);
                Assert.Equal("uri", game!.LaunchType);
                Assert.Equal(@"shell:AppsFolder\MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game", game.Path);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }

        private static string CreateXboxGameDirectory(
            string gameName,
            string identityName,
            string displayName,
            bool includeApplication = true)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherXboxTests", Guid.NewGuid().ToString("N"));
            string gameRoot = Path.Combine(tempRoot, gameName);
            string contentRoot = Path.Combine(gameRoot, "Content");
            string executableList = includeApplication
                ? """
                    <ExecutableList>
                      <Executable Name="Game.exe" Id="Game" />
                    </ExecutableList>
                  """
                : "";
            string applications = includeApplication
                ? """
                    <Applications>
                      <Application Id="Game" Executable="GameLaunchHelper.exe" />
                    </Applications>
                  """
                : "";

            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(
                Path.Combine(contentRoot, "MicrosoftGame.Config"),
                $"""
                <Game>
                  <Identity Name="{identityName}" />
                  <ShellVisuals DefaultDisplayName="{displayName}" Square150x150Logo="Logo.png" />
                  {executableList}
                </Game>
                """);
            File.WriteAllText(
                Path.Combine(contentRoot, "appxmanifest.xml"),
                $"""
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Identity Name="{identityName}" Publisher="CN=Test" Version="1.0.0.0" />
                  <Properties>
                    <DisplayName>{displayName}</DisplayName>
                    <Logo>Logo.png</Logo>
                  </Properties>
                  {applications}
                </Package>
                """);
            File.WriteAllBytes(Path.Combine(contentRoot, "Logo.png"), new byte[] { 1, 2, 3 });

            return gameRoot;
        }
    }
}
