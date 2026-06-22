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
}
