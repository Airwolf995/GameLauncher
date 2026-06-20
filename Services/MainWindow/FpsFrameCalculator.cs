using System;

namespace GameLauncher.Services.MainWindow
{
    public sealed class FpsFrameCalculator
    {
        private int _frameCount;
        private DateTime _lastFpsUpdate;
        private TimeSpan? _lastRenderingTime;

        public FpsFrameCalculator(DateTime initialTimestamp)
        {
            _lastFpsUpdate = initialTimestamp;
        }

        public bool TryProcessFrame(TimeSpan? renderingTime, DateTime now, out int fps)
        {
            fps = 0;

            if (renderingTime.HasValue)
            {
                if (_lastRenderingTime.HasValue && renderingTime.Value == _lastRenderingTime.Value)
                {
                    return false;
                }

                _lastRenderingTime = renderingTime.Value;
            }

            _frameCount++;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed < 1.0)
            {
                return false;
            }

            fps = (int)(_frameCount / elapsed);
            _frameCount = 0;
            _lastFpsUpdate = now;
            return true;
        }
    }
}
