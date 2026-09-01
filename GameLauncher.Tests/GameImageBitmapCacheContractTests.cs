using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    /// <summary>
    /// Hält die Zusagen des Cover-Ladepfads fest.
    ///
    /// Hintergrund: Die Zusage "Cover werden nie im aufrufenden (UI-)Thread
    /// dekodiert" war mit Commit 33c604e etabliert und ist beim Refactoring
    /// 137c8bc unbemerkt verlorengegangen, weil kein Test sie abgesichert hat.
    /// Diese Tests prüfen bewusst beobachtbares Verhalten statt Interna, damit
    /// sie einen Umbau des Loaders überleben.
    /// </summary>
    public class GameImageBitmapCacheContractTests : IDisposable
    {
        private readonly string _tempRoot;

        public GameImageBitmapCacheContractTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "GameLauncherTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Die zentrale Zusage: LoadAsync darf die Dekodierung nicht im
        /// aufrufenden Thread erledigen. Gemessen wird, wie lange der Aufruf den
        /// Aufrufer blockiert - im UI-Thread ist genau das das sichtbare Ruckeln.
        /// </summary>
        [Fact]
        public async Task LoadAsync_DecodesWithoutBlockingTheCallingThread()
        {
            string imagePath = CreateLargePng("blocking-probe.png");

            // Vergleichsmass auf derselben Maschine: was kostet die Dekodierung,
            // wenn sie im aufrufenden Thread passiert?
            TimeSpan inlineDecodeDuration = MeasureInlineDecode(imagePath);

            var callWatch = Stopwatch.StartNew();
            Task<GameImageLoadResult> loadTask = GameImageBitmapCache.LoadAsync(imagePath);
            callWatch.Stop();

            GameImageLoadResult result = await loadTask;

            Assert.Equal(GameImageLoadStatus.Success, result.Status);
            Assert.NotNull(result.Bitmap);

            // Grosszuegige Schwelle: der Aufruf darf einen Bruchteil dessen
            // kosten, was die Dekodierung selbst braucht. Bei Dekodierung im
            // Aufrufer-Thread laege die Dauer bei ~100 Prozent.
            Assert.True(
                callWatch.Elapsed < inlineDecodeDuration / 2,
                $"LoadAsync hat den aufrufenden Thread {callWatch.Elapsed.TotalMilliseconds:F1} ms blockiert, " +
                $"eine Inline-Dekodierung kostet {inlineDecodeDuration.TotalMilliseconds:F1} ms. " +
                "Die Dekodierung laeuft damit vermutlich wieder im aufrufenden Thread.");
        }

        /// <summary>
        /// Auch mit installiertem SynchronizationContext (wie im UI-Thread) darf
        /// die Dekodierung nicht auf diesen Kontext zurueckfallen.
        /// </summary>
        [Fact]
        public async Task LoadAsync_DoesNotDecodeOnTheCapturedSynchronizationContext()
        {
            string imagePath = CreateLargePng("context-probe.png");
            TimeSpan inlineDecodeDuration = MeasureInlineDecode(imagePath);

            var originalContext = SynchronizationContext.Current;
            var trackingContext = new TrackingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(trackingContext);

            try
            {
                var callWatch = Stopwatch.StartNew();
                Task<GameImageLoadResult> loadTask = GameImageBitmapCache.LoadAsync(imagePath);
                callWatch.Stop();

                GameImageLoadResult result = await loadTask;

                Assert.Equal(GameImageLoadStatus.Success, result.Status);
                Assert.True(
                    callWatch.Elapsed < inlineDecodeDuration / 2,
                    $"LoadAsync hat den Kontext-Thread {callWatch.Elapsed.TotalMilliseconds:F1} ms blockiert " +
                    $"(Inline-Dekodierung: {inlineDecodeDuration.TotalMilliseconds:F1} ms).");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }
        }

        /// <summary>
        /// Gleichzeitige Anfragen fuer denselben Pfad duerfen nicht doppelt laden.
        /// </summary>
        [Fact]
        public async Task LoadAsync_ConcurrentRequestsForSamePathShareOneBitmap()
        {
            string imagePath = CreateLargePng("dedupe-probe.png");

            Task<GameImageLoadResult>[] loads =
            [
                GameImageBitmapCache.LoadAsync(imagePath),
                GameImageBitmapCache.LoadAsync(imagePath),
                GameImageBitmapCache.LoadAsync(imagePath),
                GameImageBitmapCache.LoadAsync(imagePath)
            ];

            GameImageLoadResult[] results = await Task.WhenAll(loads);

            Assert.All(results, result => Assert.Equal(GameImageLoadStatus.Success, result.Status));
            BitmapImage? first = results[0].Bitmap;
            Assert.NotNull(first);
            Assert.All(results, result => Assert.Same(first, result.Bitmap));
        }

        /// <summary>
        /// Ein bereits geladenes Cover muss aus dem Cache kommen - dieselbe
        /// Instanz, ohne erneutes Dekodieren.
        /// </summary>
        [Fact]
        public async Task LoadAsync_SecondCallIsServedFromCache()
        {
            string imagePath = CreateLargePng("cache-probe.png");

            GameImageLoadResult first = await GameImageBitmapCache.LoadAsync(imagePath);
            Assert.Equal(GameImageLoadStatus.Success, first.Status);
            Assert.True(GameImageBitmapCache.IsCached(imagePath));

            GameImageLoadResult second = await GameImageBitmapCache.LoadAsync(imagePath);

            Assert.Same(first.Bitmap, second.Bitmap);
        }

        /// <summary>
        /// Gelieferte Bitmaps muessen eingefroren sein, sonst sind sie ausserhalb
        /// des erzeugenden Threads nicht verwendbar.
        /// </summary>
        [Fact]
        public async Task LoadAsync_ReturnsFrozenBitmap()
        {
            string imagePath = CreateLargePng("frozen-probe.png");

            GameImageLoadResult result = await GameImageBitmapCache.LoadAsync(imagePath);

            Assert.NotNull(result.Bitmap);
            Assert.True(result.Bitmap!.IsFrozen);
        }

        /// <summary>
        /// Abbruch beim Wegscrollen muss durchschlagen.
        /// </summary>
        [Fact]
        public async Task LoadAsync_HonoursAlreadyCancelledToken()
        {
            string imagePath = CreateLargePng("cancel-probe.png");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => GameImageBitmapCache.LoadAsync(imagePath, cts.Token));
        }

        /// <summary>
        /// Ein fehlendes Bild darf keine Ausnahme nach aussen werfen, sondern
        /// muss als Fehlerergebnis zurueckkommen.
        /// </summary>
        [Fact]
        public async Task LoadAsync_ReportsMissingFileAsFailureInsteadOfThrowing()
        {
            string missingPath = Path.Combine(_tempRoot, "gibt-es-nicht.png");

            GameImageLoadResult result = await GameImageBitmapCache.LoadAsync(missingPath);

            Assert.NotEqual(GameImageLoadStatus.Success, result.Status);
            Assert.Null(result.Bitmap);
        }

        /// <summary>
        /// Erzeugt ein Bild, das gross genug ist, dass die Dekodierung messbar
        /// Zeit kostet. Nur so laesst sich Inline- von Hintergrund-Dekodierung
        /// zuverlaessig unterscheiden.
        /// </summary>
        private string CreateLargePng(string fileName)
        {
            string path = Path.Combine(_tempRoot, fileName);

            const int width = 2400;
            const int height = 2400;
            var pixels = new byte[width * height * 4];

            // Rauschen statt einer Flaeche: gleichmaessige Farbflaechen komprimiert
            // PNG so stark, dass die Dekodierung zu schnell zum Messen waere.
            var random = new Random(20260831);
            random.NextBytes(pixels);

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = File.Create(path);
            encoder.Save(stream);

            return path;
        }

        /// <summary>
        /// Dekodiert dasselbe Bild mit denselben Einstellungen wie der Cache im
        /// aufrufenden Thread und liefert die dafuer noetige Zeit.
        /// </summary>
        private static TimeSpan MeasureInlineDecode(string imagePath)
        {
            var watch = Stopwatch.StartNew();

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.DecodePixelWidth = 256;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            watch.Stop();
            return watch.Elapsed;
        }

        private sealed class TrackingSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                ThreadPool.QueueUserWorkItem(_ => d(state));
            }
        }
    }
}
