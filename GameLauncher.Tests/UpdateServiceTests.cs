using System.Reflection;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("GameLauncher_Setup_2.0.0.0.exe", true)]
        [InlineData("GameLauncher_Setup-v1.6.4.exe", true)]
        [InlineData("GameLauncher_Setup1-4-5.exe", true)]
        [InlineData("OtherLauncher_Setup_2.0.0.exe", false)]
        [InlineData("GameLauncher.zip", false)]
        [InlineData("", false)]
        public void IsInstallerAssetName_RecognizesExpectedReleaseFiles(string assetName, bool expected)
        {
            var method = typeof(UpdateService).GetMethod(
                "IsInstallerAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = (bool)method!.Invoke(null, new object[] { assetName })!;

            Assert.Equal(expected, result);
        }
    }
}
