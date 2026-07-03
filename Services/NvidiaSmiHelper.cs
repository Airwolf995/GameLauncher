using System;
using System.Diagnostics;
using System.IO;

namespace GameLauncher.Services
{
    internal static class NvidiaSmiHelper
    {
        public static string? ResolvePath()
        {
            string[] environmentRoots =
            {
                Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
                Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty,
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty
            };

            foreach (var root in environmentRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            string system32Candidate = Path.Combine(systemRoot, "System32", "nvidia-smi.exe");
            return File.Exists(system32Candidate) ? system32Candidate : null;
        }

        public static string? TryQueryFirstLine(string nvidiaSmiPath, string queryArgument, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(nvidiaSmiPath) || !File.Exists(nvidiaSmiPath))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = nvidiaSmiPath,
                Arguments = $"{queryArgument} --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : null;
        }
    }
}
