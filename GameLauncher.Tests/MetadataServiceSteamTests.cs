using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;
using System.Collections.Concurrent;
using System.Net;

namespace GameLauncher.Tests;

public sealed class MetadataServiceSteamTests
{
    private const string ResponseJson = """{"620":{"success":true,"data":{"short_description":"Neue Beschreibung","release_date":{"date":"2026"},"developers":["Entwickler"],"publishers":["Verlag"],"genres":[{"description":"Action"}]}}}""";

    [Fact]
    public async Task AbbruchNachHttpAbschlussÜbernimmtKeineMetadaten()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new ResponseHandler(_ => response.Task));
        var service = new MetadataService(client);
        using var cts = new CancellationTokenSource();
        var game = new Game { Id = "steam:620", Description = "Bisherige Beschreibung", Developer = "Bisheriger Entwickler" };
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        Task<bool> fetch;
        SynchronizationContext.SetSynchronizationContext(context);
        try { fetch = service.FetchSteamMetadataAsync(game, AppLanguage.German, cts.Token); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) });
        await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        context.RunNext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Equal("Bisherige Beschreibung", game.Description);
        Assert.Equal("Bisheriger Entwickler", game.Developer);
        Assert.Empty(game.Genres);
    }

    [Fact]
    public async Task AbrufVerwendetDieSpracheDesAuftrags()
    {
        Uri? requested = null;
        using var client = new HttpClient(new ResponseHandler(request =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) });
        }));
        var service = new MetadataService(client);
        var game = new Game { Id = "steam:620" };
        Assert.True(await service.FetchSteamMetadataAsync(game, AppLanguage.German));
        Assert.Contains("l=german", requested!.Query);
        Assert.Equal("Neue Beschreibung", game.Description);
        Assert.Equal("Entwickler", game.Developer);
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
            Posted.TrySetResult();
        }
        public void RunNext()
        {
            Assert.True(_callbacks.TryDequeue(out var entry));
            entry.Callback(entry.State);
        }
    }

    /// <summary>
    /// Ein Abbruch ist kein Abruffehler. Er muss beim Aufrufer ankommen, damit
    /// dieser ihn als Abbruch behandelt, statt ihn als Fehler zu protokollieren.
    /// </summary>
    [Fact]
    public async Task FetchSteamMetadataAsync_ReichtAbbruchAnDenAufruferWeiter()
    {
        var service = new MetadataService();
        var game = new Game { Id = "steam:620", Name = "Portal 2", Platform = "Steam" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.FetchSteamMetadataAsync(game, cts.Token));
    }
}
