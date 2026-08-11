using GameLauncher.Controls;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class AsyncCoverImageTests
    {
        [Fact]
        public async Task LoadWithSingleRetryAsync_RetriesTemporaryFailureExactlyOnce()
        {
            int loadCount = 0;
            int delayCount = 0;
            TimeSpan retryDelay = TimeSpan.FromSeconds(30);

            GameImageLoadResult result = await AsyncCoverImage.LoadWithSingleRetryAsync(
                "cover.png",
                (_, _) =>
                {
                    loadCount++;
                    return Task.FromResult(new GameImageLoadResult(
                        null,
                        GameImageLoadStatus.TemporaryFailure,
                        retryDelay));
                },
                (delay, _) =>
                {
                    delayCount++;
                    Assert.Equal(retryDelay, delay);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.Equal(GameImageLoadStatus.TemporaryFailure, result.Status);
            Assert.Equal(2, loadCount);
            Assert.Equal(1, delayCount);
        }

        [Fact]
        public async Task LoadWithSingleRetryAsync_DoesNotRetryMissingImage()
        {
            int loadCount = 0;
            int delayCount = 0;

            GameImageLoadResult result = await AsyncCoverImage.LoadWithSingleRetryAsync(
                "missing-cover.png",
                (_, _) =>
                {
                    loadCount++;
                    return Task.FromResult(new GameImageLoadResult(
                        null,
                        GameImageLoadStatus.NotFound,
                        TimeSpan.FromHours(24)));
                },
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.Equal(GameImageLoadStatus.NotFound, result.Status);
            Assert.Equal(1, loadCount);
            Assert.Equal(0, delayCount);
        }
    }
}
