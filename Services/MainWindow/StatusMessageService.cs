using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameLauncher.Services.MainWindow
{
    public sealed class StatusMessageService : IStatusMessageService
    {
        private readonly Action<string> _setStatusText;
        private readonly Action _restoreDefaultStatus;
        private CancellationTokenSource _statusCts = new();
        private bool _disposed;

        public StatusMessageService(Action<string> setStatusText, Action restoreDefaultStatus)
        {
            _setStatusText = setStatusText ?? throw new ArgumentNullException(nameof(setStatusText));
            _restoreDefaultStatus = restoreDefaultStatus ?? throw new ArgumentNullException(nameof(restoreDefaultStatus));
        }

        public void ShowStatus(string message, int delayMs = 3000)
        {
            if (_disposed)
            {
                return;
            }

            _statusCts.Cancel();
            _statusCts.Dispose();
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            _setStatusText(message);
            _ = RestoreStatusAsync(delayMs, token);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _statusCts.Cancel();
            _statusCts.Dispose();
        }

        private async Task RestoreStatusAsync(int delayMs, CancellationToken token)
        {
            try
            {
                await Task.Delay(delayMs, token);
                if (!token.IsCancellationRequested)
                {
                    _restoreDefaultStatus();
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when a newer status message overrides an older one.
            }
        }
    }
}
