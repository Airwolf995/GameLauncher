using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace GameLauncher.Models
{
    public static class Logger
    {
        private static string _logPath = string.Empty;
        private static readonly object _lock = new();
        private static StreamWriter? _writer;
        private static int _pendingLines = 0;
        private const int MaxPendingLines = 20;
        private static System.Timers.Timer? _flushTimer;

        public static void Initialize()
        {
            // Store logs in Documents folder for better compatibility
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string logDir = Path.Combine(documentsPath, "GameLauncher", "Logs");
            
            // Ensure directory exists
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string logFileName = $"launcher_{timestamp}.log";
            _logPath = Path.Combine(logDir, logFileName);

            try
            {
                // Set AutoFlush to false for batch logging performance
                _writer = new StreamWriter(_logPath, append: true) { AutoFlush = false };

                // Initialize a timer to flush logs periodically even if buffer isn't full
                _flushTimer = new System.Timers.Timer(30000); // 30 seconds
                _flushTimer.Elapsed += (s, e) => Flush();
                _flushTimer.AutoReset = true;
                _flushTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logger writer: {ex.Message}");
            }

            // Rotate logs
            RotateLogs(logDir);

            Log("Logger initialized.");
            Log($"Log Directory: {logDir}");
        }

        public static void Log(string message)
        {
            if (_writer == null) return;

            try
            {
                lock (_lock)
                {
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                    _writer.WriteLine(line);
                    _pendingLines++;

                    // Manual flush if buffer is full
                    if (_pendingLines >= MaxPendingLines)
                    {
                        FlushInternal();
                    }
                }
            }
            catch (Exception)
            {
                // If logging fails, we can't really log that failure easily without recursion potential or crash.
                // Just swallow for now or write to debug.
                System.Diagnostics.Debug.WriteLine($"Failed to write to log: {message}");
            }
        }

        public static void Error(string message, Exception? ex = null)
        {
            string fullMessage = $"ERROR: {message}";
            if (ex != null)
            {
                fullMessage += $" - Exception: {ex.Message}";
            }
            Log(fullMessage);
        }

        public static void Flush()
        {
            lock (_lock)
            {
                FlushInternal();
            }
        }

        private static void FlushInternal()
        {
            try
            {
                if (_writer != null && _pendingLines > 0)
                {
                    _writer.Flush();
                    _pendingLines = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flush failed: {ex.Message}");
            }
        }

        private static void RotateLogs(string logDir)
        {
            try
            {
                var directory = new DirectoryInfo(logDir);
                var logFiles = directory.GetFiles("launcher_*.log")
                                        .OrderByDescending(f => f.CreationTime)
                                        .ToList();

                // Keep the 4 most recent logs (including the one just created)
                if (logFiles.Count > 4)
                {
                    var filesToDelete = logFiles.Skip(4).ToList();
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            file.Delete();
                            // We can't log this yet as we might strictly not have initialized _logPath fully if we called this before setting it, 
                            // but in this flow Initialize calls RotateLogs after setting _logPath, so we can Log.
                            // However, let's just do it silently or write to the new log.
                        }
                        catch { } 
                    }
                    // We can log to the new file about the cleanup
                    // But Initialize hasn't written the first line yet. It's fine.
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log rotation failed: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            _flushTimer?.Stop();
            _flushTimer?.Dispose();

            lock (_lock)
            {
                if (_writer != null)
                {
                    try
                    {
                        FlushInternal();
                        _writer.Dispose();
                        _writer = null;
                    }
                    catch { }
                }
            }
        }
    }
}
