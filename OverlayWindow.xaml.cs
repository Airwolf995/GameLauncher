using System;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class OverlayWindow : Window
    {
        private readonly HardwareMonitorService _hardwareMonitor;
        private readonly PlayTimeService _playTimeService;
        private readonly DispatcherTimer _timer;
        private readonly DateTime _launcherStartTime;
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private string? _lastActiveGameId;
        private bool _isUpdating;

        public OverlayWindow(HardwareMonitorService hardwareMonitor, PlayTimeService playTimeService)
        {
            InitializeComponent();
            _hardwareMonitor = hardwareMonitor;
            _playTimeService = playTimeService;
            _launcherStartTime = DateTime.Now;

            // Position at top-right by default
            Left = SystemParameters.PrimaryScreenWidth - Width - 20;
            Top = 20;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += UpdateStats;

            // Timer nur laufen lassen wenn Overlay sichtbar ist
            IsVisibleChanged += OnVisibilityChanged;

            UpdateStats(null, null);
        }

        private async void UpdateStats(object? sender, EventArgs? e)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                var stats = await _hardwareMonitor.GetHardwareStatsAsync();

                // CPU
                string cpuTempStr = (stats.cpuTemp.HasValue && stats.cpuTemp.Value > 0) ? $"{Math.Round(stats.cpuTemp.Value)}°C" : "--°C";
                string cpuLoadStr = stats.cpuUsage.HasValue ? $"{Math.Round(stats.cpuUsage.Value)}%" : "--%";
                CpuText.Text = $"{cpuTempStr} | {cpuLoadStr}";

                // GPU
                string gpuTempStr = (stats.gpuTemp.HasValue && stats.gpuTemp.Value > 0) ? $"{Math.Round(stats.gpuTemp.Value)}°C" : "--°C";
                string gpuLoadStr = stats.gpuUsage.HasValue ? $"{Math.Round(stats.gpuUsage.Value)}%" : "--%";
                GpuText.Text = $"{gpuTempStr} | {gpuLoadStr}";

                // RAM
                RamText.Text = FormatMemory(stats.ramUsedGb, stats.ramTotalGb, stats.ramLoad);

                // VRAM
                VramText.Text = FormatMemory(stats.vramUsedGb, stats.vramTotalGb, stats.vramLoad);

                // Clock
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

                // Game & Playtime
                if (_playTimeService.ActiveGame != null && _playTimeService.SessionStartTime.HasValue)
                {
                    // Spielname nur bei Wechsel aktualisieren (vermeidet ToUpper() jede Sekunde)
                    if (_lastActiveGameId != _playTimeService.ActiveGame.Id)
                    {
                        ActiveGameText.Text = _playTimeService.ActiveGame.Name.ToUpper();
                        _lastActiveGameId = _playTimeService.ActiveGame.Id;
                    }
                    var elapsed = DateTime.Now - _playTimeService.SessionStartTime.Value;
                    PlayTimeText.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }
                else
                {
                    if (_lastActiveGameId != null)
                    {
                        ActiveGameText.Text = _localization.Get("Overlay.NoActiveGame").ToUpper(_localization.CurrentCulture);
                        PlayTimeText.Text = "00:00:00";
                        _lastActiveGameId = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Overlay Update Error", ex);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                UpdateStats(null, null);
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private static string FormatMemory(float? usedGb, float? totalGb, float? loadPercent)
        {
            string usedStr = usedGb.HasValue ? $"{usedGb.Value:F1} GB" : "-- GB";
            string? totalStr = totalGb.HasValue ? $"{totalGb.Value:F1} GB" : null;
            string loadStr = loadPercent.HasValue ? $"{Math.Round(loadPercent.Value)}%" : "--%";

            if (totalStr != null)
            {
                return $"{usedStr}/{totalStr} | {loadStr}";
            }

            return $"{usedStr} | {loadStr}";
        }

        // Make window "invisible" to Win+Tab and Task Alt+Tab (partially handled by ToolWindow style if needed, but Topmost+NoTaskbar is usually enough)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Allow clicking through the window (IsHitTestVisible="False" handles WPF hits, but we need Win32 for mouse events)
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            int extendedStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }
}
