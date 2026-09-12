using System.Text.Json;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class UpdateServiceAssetTests
    {
        [Fact]
        public void ReadAssetSha256_StripsPrefixFromDigest()
        {
            JsonElement asset = ParseAsset("{\"digest\": \"sha256:25a8bc7ac7a0b18a4d64881b1678b6d1aba6fe01450ea9706964caaed8bad531\"}");

            Assert.Equal("25a8bc7ac7a0b18a4d64881b1678b6d1aba6fe01450ea9706964caaed8bad531", UpdateService.ReadAssetSha256(asset));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"digest\": null}")]
        [InlineData("{\"digest\": \"\"}")]
        [InlineData("{\"digest\": \"md5:25a8bc7a\"}")]
        public void ReadAssetSha256_ReturnsEmptyWhenDigestIsMissingOrUnsupported(string json)
        {
            Assert.Equal("", UpdateService.ReadAssetSha256(ParseAsset(json)));
        }

        [Theory]
        [InlineData("https://github.com/Airwolf995/GameLauncher/releases/download/v2.2.0/GameLauncher_Setup_2.2.0.0.exe")]
        [InlineData("https://objects.githubusercontent.com/github-production-release-asset/123/456")]
        [InlineData("https://GITHUB.COM/Airwolf995/GameLauncher/releases/download/v2.2.0/Setup.exe")]
        public void IsTrustedDownloadUrl_AcceptsGitHubAddresses(string url)
        {
            Assert.True(UpdateService.IsTrustedDownloadUrl(url));
        }

        [Theory]
        [InlineData("http://github.com/Airwolf995/GameLauncher/releases/download/v2.2.0/Setup.exe")]
        [InlineData("https://github.com.angreifer.example/releases/download/v2.2.0/Setup.exe")]
        [InlineData("https://notgithub.com/releases/download/v2.2.0/Setup.exe")]
        [InlineData("file:///C:/Temp/Setup.exe")]
        [InlineData("nicht einmal eine adresse")]
        [InlineData("")]
        public void IsTrustedDownloadUrl_RejectsEverythingElse(string url)
        {
            Assert.False(UpdateService.IsTrustedDownloadUrl(url));
        }

        private static JsonElement ParseAsset(string json)
        {
            // Das Dokument muss den Aufruf ueberdauern, daher geklont.
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
