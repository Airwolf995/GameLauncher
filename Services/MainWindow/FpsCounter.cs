using System;
using System.Windows.Media;

namespace GameLauncher.Services.MainWindow
{
    public sealed class FpsCounter : IFpsCounter
    {
        private Action<int>? _onFpsCalculated;
        private FpsFrameCalculator _calculator = new(DateTime.Now);
        private bool _isRunning;

        public void Start(Action<int> onFpsCalculated)
        {
            _onFpsCalculated = onFpsCalculated ?? throw new ArgumentNullException(nameof(onFpsCalculated));
            Resume();
        }

        /// <summary>
        /// Setzt den FPS-Zähler fort, ohne den Callback zu ändern.
        /// Wird verwendet, wenn das Fenster wieder sichtbar wird.
        /// </summary>
        public void Resume()
        {
            if (_isRunning || _onFpsCalculated == null)
            {
                return;
            }

            _calculator = new FpsFrameCalculator(DateTime.Now);
            CompositionTarget.Rendering += OnRendering;
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            _isRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var renderingArgs = e as RenderingEventArgs;
            if (_calculator.TryProcessFrame(renderingArgs?.RenderingTime, DateTime.Now, out var fps))
            {
                _onFpsCalculated?.Invoke(fps);
            }
        }
    }
}
