using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class EaScannerTests
{
    [Fact]
    public void BuildLaunchUri_EscapedDieOfferIdUndVerwendetDasEaProtokoll()
    {
        string uri = EaScanner.BuildLaunchUri("OFFER ID&1");

        Assert.Equal("origin2://game/launch?offerIds=OFFER%20ID%261", uri);
    }

    [Fact]
    public void IsEaGameInstallation_ErkenntInstallerMetadatenUnabhängigVomPublisher()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("GameLauncherEaTests_").FullName;

        try
        {
            string installerDirectory = Directory.CreateDirectory(
                Path.Combine(temporaryDirectory, "__Installer")).FullName;
            File.WriteAllText(Path.Combine(installerDirectory, "installerdata.xml"), "<installer />");

            bool isEaGame = EaScanner.IsEaGameInstallation("BioWare", temporaryDirectory);

            Assert.True(isEaGame);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Steam App 1222670")]
    [InlineData("steam app 620")]
    public void IsSteamManagedEntry_ErkenntDeinstallationseintraegeVonSteam(string subKeyName)
    {
        Assert.True(EaScanner.IsSteamManagedEntry(subKeyName));
    }

    [Theory]
    [InlineData("{48EBEBBF-B9F8-4520-A3CF-89A730721917}")]
    [InlineData("Origin.OFR.50.0002694")]
    [InlineData("")]
    public void IsSteamManagedEntry_LaesstEigenstaendigeEintraegeZu(string subKeyName)
    {
        Assert.False(EaScanner.IsSteamManagedEntry(subKeyName));
    }

    [Fact]
    public void IsEaGameInstallation_VerwendetDenLegacyPublisherNurAlsFallback()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("GameLauncherEaTests_").FullName;

        try
        {
            Assert.True(EaScanner.IsEaGameInstallation("Electronic Arts", temporaryDirectory));
            Assert.False(EaScanner.IsEaGameInstallation("BioWare", temporaryDirectory));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
