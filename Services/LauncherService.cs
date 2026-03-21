using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using SilverWolfLauncher.Models;

namespace SilverWolfLauncher.Services
{
    public class LauncherService
    {
        private Process? gameProcess;
        private Process? psProcess;
        private Process? proxyProcess;
        private DispatcherTimer? watchdogTimer;

        public event Action<string, bool>? OnStatusChanged;

        public void StartWatchdog(Action onUpdate)
        {
            watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            watchdogTimer.Tick += (s, e) => onUpdate();
            watchdogTimer.Start();
        }

        public void LaunchGame(string gamePath, bool autoServices, Action<bool> onMinimize)
        {
            if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath)) return;

            if (autoServices)
            {
                EnsureServiceRunning("server", "firefly-go_win.exe", ref psProcess);
                EnsureServiceRunning("proxy", "firefly-go-proxy.exe", ref proxyProcess);
            }

            try
            {
                var psi = new ProcessStartInfo(gamePath) { WorkingDirectory = Path.GetDirectoryName(gamePath) };
                gameProcess = Process.Start(psi);
                onMinimize(true);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"Launch Error: {ex.Message}", false);
            }
        }

        public void EnsureServiceRunning(string subDir, string exeName, ref Process? proc)
        {
            if (proc != null && !proc.HasExited) return;

            // Check current directory Data/_Data
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.Combine(launcherDir, "_Data", subDir, exeName);

            // Legacy check
            if (!File.Exists(fullPath)) fullPath = Path.Combine(launcherDir, subDir, exeName);

            if (File.Exists(fullPath))
            {
                try
                {
                    var psi = new ProcessStartInfo(fullPath)
                    {
                        WorkingDirectory = Path.GetDirectoryName(fullPath) ?? "",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    proc = Process.Start(psi);
                }
                catch { }
            }
        }

        public (bool PS, bool Proxy) GetServiceStatus()
        {
            bool ps = psProcess != null && !psProcess.HasExited;
            bool proxy = proxyProcess != null && !proxyProcess.HasExited;
            return (ps, proxy);
        }

        public void KillAll()
        {
            try { psProcess?.Kill(); } catch { }
            try { proxyProcess?.Kill(); } catch { }
        }
    }
}
