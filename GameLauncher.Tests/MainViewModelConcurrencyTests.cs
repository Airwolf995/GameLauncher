using System.IO;
using System.Windows.Threading;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;
using GameLauncher.ViewModels;

namespace GameLauncher.Tests;

public sealed class MainViewModelConcurrencyTests
{
    [Fact]
    public Task NeuerSuchtextUnmittelbarVorDemSwapVerhindertDasAlteErgebnis() => RunOnDispatcher(async () =>
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherView_").FullName;
        try
        {
            using var manager = new GameManager(Path.Combine(root, "config.json"));
            bool built = false;
            using var viewModel = new MainViewModel(manager, (games, search, filter, sort, columns) =>
            {
                var result = LibraryViewSnapshotBuilder.Create(games, search, filter, sort, columns);
                Volatile.Write(ref built, true);
                return result;
            });
            viewModel.Games.Add(new Game { Id = "a", Name = "Alpha" });
            viewModel.Games.Add(new Game { Id = "b", Name = "Beta" });
            int swaps = 0;
            viewModel.LibraryViewRefreshed += (_, _) => swaps++;
            bool changed = false;
            var dispatcher = Dispatcher.CurrentDispatcher;
            void BeforeSwap(object? sender, DispatcherHookEventArgs e)
            {
                // Eine Änderung nach der äußeren Versionsprüfung, aber bevor
                // der gerade eingereihte UI-Swap tatsächlich ausgeführt wird.
                if (!changed && Volatile.Read(ref built) && dispatcher.CheckAccess())
                {
                    changed = true;
                    viewModel.SearchText = "Beta";
                }
            }
            dispatcher.Hooks.OperationPosted += BeforeSwap;
            try { await viewModel.RebuildLibraryViewAsync(); }
            finally { dispatcher.Hooks.OperationPosted -= BeforeSwap; }
            Assert.True(changed);
            Assert.Equal(0, swaps);

            await viewModel.RebuildLibraryViewAsync();
            Assert.Equal("b", Assert.Single(viewModel.GamesView.Cast<Game>()).Id);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task SpaltenwechselVorDemSwapBleibtErhalten() => RunOnDispatcher(async () =>
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherColumns_").FullName;
        try
        {
            using var manager = new GameManager(Path.Combine(root, "config.json"));
            bool built = false;
            using var viewModel = new MainViewModel(manager, (games, search, filter, sort, columns) =>
            {
                var result = LibraryViewSnapshotBuilder.Create(games, search, filter, sort, columns);
                Volatile.Write(ref built, true);
                return result;
            });
            for (int i = 0; i < 5; i++) viewModel.Games.Add(new Game { Id = $"{i}", Name = $"Spiel {i}" });
            bool changed = false;
            var dispatcher = Dispatcher.CurrentDispatcher;
            void BeforeSwap(object? sender, DispatcherHookEventArgs e)
            {
                if (!changed && Volatile.Read(ref built) && dispatcher.CheckAccess())
                {
                    changed = true;
                    viewModel.UpdateCardColumns(2);
                }
            }
            dispatcher.Hooks.OperationPosted += BeforeSwap;
            try { await viewModel.RebuildLibraryViewAsync(); }
            finally { dispatcher.Hooks.OperationPosted -= BeforeSwap; }
            Assert.True(changed);
            Assert.Equal([2, 2, 1], viewModel.CardRows.Select(row => row.Games.Count));
        }
        finally { Directory.Delete(root, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ÜberholterAufbauUndBeendenÜbernehmenKeineErgebnisse(bool changeSort) => RunOnDispatcher(async () =>
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherCancel_").FullName;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var manager = new GameManager(Path.Combine(root, "config.json"));
            int builds = 0;
            using var viewModel = new MainViewModel(manager, (games, search, filter, sort, columns) =>
            {
                if (Interlocked.Increment(ref builds) == 1)
                {
                    started.SetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                }
                return LibraryViewSnapshotBuilder.Create(games, search, filter, sort, columns);
            });
            viewModel.Games.Add(new Game { Id = "a", Name = "Alpha" });
            viewModel.Games.Add(new Game { Id = "b", Name = "Beta", IsFavorite = true, PlayTime = 100 });
            var oldRefresh = viewModel.RebuildLibraryViewAsync();
            await started.Task;
            if (changeSort) viewModel.SelectedSort = GameSortMode.PlayTime;
            else viewModel.SelectedFilter = Constants.Filters.Favorites;
            await viewModel.RebuildLibraryViewAsync();
            var latest = viewModel.GamesView.Cast<Game>().ToList();
            if (changeSort) Assert.Equal(["b", "a"], latest.Select(game => game.Id));
            else Assert.Equal("b", Assert.Single(latest).Id);
            int swapsAfterLatest = 0;
            viewModel.LibraryViewRefreshed += (_, _) => swapsAfterLatest++;
            var concurrent = Enumerable.Range(0, 10).Select(_ => Task.Run(viewModel.RebuildLibraryViewAsync)).ToArray();
            viewModel.Dispose();
            release.Set();
            await Task.WhenAll(concurrent.Append(oldRefresh));
            await viewModel.RebuildLibraryViewAsync();
            await viewModel.RefreshSteamMetadataAsync();
            Assert.Equal(0, swapsAfterLatest);
        }
        finally
        {
            release.Set();
            Directory.Delete(root, true);
        }
    });

    private static async Task RunOnDispatcher(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.InvokeShutdown(); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
    }
}
