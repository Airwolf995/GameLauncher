using System.Net;
using System.Security.Cryptography;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public sealed class UpdateDownloadTests
{
    [Fact]
    public async Task AbbruchEntferntTeilweiseGeladenenInstaller()
    {
        string directory = Directory.CreateTempSubdirectory("GameLauncherUpdateCancel_").FullName;
        try
        {
            string path = Path.Combine(directory, "setup.exe");
            using var cts = new CancellationTokenSource();
            var stream = new BlockingStream();
            using var service = new UpdateService("test/repo", new HttpClient(new BlockingHandler(new StreamContent(stream))), path);
            var download = service.DownloadUpdateAsync("https://github.com/test/setup.exe", new string('a', 64), ct: cts.Token);
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            Assert.Equal(UpdateDownloadResult.Failed, await download.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(File.Exists(path));
            Assert.False(service.InstallUpdate());
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class BlockingHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class BlockingStream() : MemoryStream("Teil des Installers"u8.ToArray())
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Position < Length) return await base.ReadAsync(buffer, offset, count, cancellationToken);
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abcd")]
    [InlineData("０１２３４５６７８９abcdef０１２３４５６７８９abcdef０１２３４５６７８９abcdef０１２３４５６７８９abcdef")]
    [InlineData("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    public async Task Download_LehntUngültigePrüfsummeVorNetzwerkzugriffAb(string? hash)
    {
        await RunDownloadTest(async (service, handler, path) =>
        {
            File.WriteAllText(path, "alter Installer");
            Assert.Equal(UpdateDownloadResult.ChecksumMismatch,
                await service.DownloadUpdateAsync("https://github.com/test/setup.exe", hash!));
            Assert.Equal(0, handler.RequestCount);
            Assert.False(File.Exists(path));
            Assert.False(service.InstallUpdate());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_AkzeptiertPassendePrüfsummeUnabhängigVonGroßschreibung(bool uppercase)
    {
        await RunDownloadTest(async (service, handler, path) =>
        {
            string hash = Convert.ToHexString(SHA256.HashData(handler.Content));
            Assert.Equal(UpdateDownloadResult.Succeeded,
                await service.DownloadUpdateAsync("https://github.com/test/setup.exe",
                    uppercase ? hash : hash.ToLowerInvariant()));
            Assert.Equal(handler.Content, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public async Task Download_VerwirftDateiBeiAbweichenderPrüfsumme()
    {
        await RunDownloadTest(async (service, handler, path) =>
        {
            Assert.Equal(UpdateDownloadResult.ChecksumMismatch,
                await service.DownloadUpdateAsync("https://github.com/test/setup.exe", new string('0', 64)));
            Assert.Equal(1, handler.RequestCount);
            Assert.False(File.Exists(path));
            Assert.False(service.InstallUpdate());
        });
    }

    private static async Task RunDownloadTest(Func<UpdateService, DownloadHandler, string, Task> test)
    {
        string directory = Directory.CreateTempSubdirectory("GameLauncherUpdate_").FullName;
        try
        {
            string path = Path.Combine(directory, "setup.exe");
            var handler = new DownloadHandler();
            using var service = new UpdateService("test/repo", new HttpClient(handler), path);
            await test(service, handler, path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        public byte[] Content { get; } = "Testinstaller"u8.ToArray();
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Content)
            });
        }
    }
}
