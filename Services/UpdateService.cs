using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SilverWolfLauncher.Services
{
    public class UpdateService
    {
        // Gitea API endpoints (mirrored from Firefly source constants)
        private const string ServerReleaseUrl  = "https://git.kain.io.vn/api/v1/repos/Firefly-Shelter/FireflyGo_Local_Archive/releases";
        private const string ProxyReleaseUrl   = "https://git.kain.io.vn/api/v1/repos/Firefly-Shelter/FireflyGo_Proxy/releases";
        private const string ServerZipName     = "prebuild_win_x86.zip";
        private const string ProxyExeName      = "firefly-go-proxy.exe";

        private static readonly HttpClient _httpClient = CreateSharedClient();
        public string LastErrorMessage { get; set; } = "";

        private static HttpClient CreateSharedClient()
        {
            var c = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.Add("User-Agent", "SilverWolf-Launcher");
            return c;
        }

        // ── PS Server ──────────────────────────────────────────────────────────────

        public async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckForUpdateAsync(string currentVersion)
        {
            return await CheckReleaseAsync(ServerReleaseUrl, currentVersion, r => r.EndsWith(".zip") || r.EndsWith(".7z"));
        }

        public async Task<bool> DownloadAndInstallAsync(string targetDir, string downloadUrl, Action<string> onProgress)
        {
            // Auto-organize: PS server always goes into ./server/ inside target directory
            string serverDir = Path.Combine(targetDir, "server");
            Directory.CreateDirectory(serverDir);

            return await DownloadAndExtractAsync(downloadUrl, serverDir, onProgress);
        }

        // ── Proxy ──────────────────────────────────────────────────────────────────

        public async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckProxyUpdateAsync(string currentVersion)
        {
            return await CheckReleaseAsync(ProxyReleaseUrl, currentVersion, r => r.Equals(ProxyExeName, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> DownloadAndInstallProxyAsync(string targetDir, string downloadUrl, Action<string> onProgress)
        {
            // Auto-organize: Proxy always goes into ./proxy/ inside target directory
            string proxyDir = Path.Combine(targetDir, "proxy");
            Directory.CreateDirectory(proxyDir);

            return await DownloadFileAsync(downloadUrl, Path.Combine(proxyDir, ProxyExeName), onProgress);
        }

        // ── Shared helpers ─────────────────────────────────────────────────────────

        private async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckReleaseAsync(
            string apiUrl, string currentVersion, Func<string, bool> assetPredicate)
        {
            LastErrorMessage = string.Empty;
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.GetAsync(apiUrl, cts.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                
                using var doc = JsonDocument.Parse(json);

                // Gitea releases endpoint returns an array
                var root = doc.RootElement;
                JsonElement release;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0)
                    {
                        LastErrorMessage = "No releases found on server";
                        return (false, "", "");
                    }
                    release = root[0];
                }
                else
                {
                    release = root;
                }

                string latest = release.GetProperty("tag_name").GetString() ?? "";

                if (latest == currentVersion) return (false, latest, "");

                string downloadUrl = "";
                if (release.TryGetProperty("assets", out var assets))
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString() ?? "";
                        if (assetPredicate(name))
                        {
                            downloadUrl = a.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    LastErrorMessage = "No matching asset found in release";
                }

                return (!string.IsNullOrEmpty(downloadUrl), latest, downloadUrl);
            }
            catch (HttpRequestException hex)
            {
                LastErrorMessage = $"Network error: {hex.Message}. Check if you can access https://git.kain.io.vn";
                return (false, "", "");
            }
            catch (JsonException jex)
            {
                LastErrorMessage = $"Invalid server response: {jex.Message}";
                return (false, "", "");
            }
            catch (TaskCanceledException)
            {
                LastErrorMessage = "Connection timed out. The server (git.kain.io.vn) took too long to respond. Please try again later.";
                return (false, "", "");
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Unexpected error: {ex.Message}";
                return (false, "", "");
            }
        }

        private async Task<bool> DownloadAndExtractAsync(string url, string destDir, Action<string> onProgress)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "sw_dl_" + Guid.NewGuid().ToString("N")[..8] + ".zip");
            try
            {
                onProgress("Downloading...");
                bool dl = await DownloadFileAsync(url, tmp, onProgress);
                if (!dl) return false;

                onProgress("Extracting...");
                using var archive = ArchiveFactory.Open(tmp);
                foreach (var entry in archive.Entries)
                    if (!entry.IsDirectory)
                        entry.WriteToDirectory(destDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });

                return true;
            }
            catch (Exception ex)
            {
                onProgress("Error: " + ex.Message);
                return false;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private async Task<bool> DownloadFileAsync(string url, string destFile, Action<string> onProgress)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    LastErrorMessage = "Download URL is empty";
                    onProgress("ERROR: Download URL is empty");
                    return false;
                }

                onProgress($"Connecting to {new Uri(url).Host}...");
                using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                long? total = resp.Content.Headers.ContentLength;
                using var src  = await resp.Content.ReadAsStreamAsync();
                using var dest = File.Create(destFile);

                byte[] buf = new byte[81920];
                long downloaded = 0;
                int read;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dest.WriteAsync(buf.AsMemory(0, read));
                    downloaded += read;
                    if (total.HasValue && total.Value > 0)
                    {
                        double pct = downloaded * 100.0 / total.Value;
                        double elapsed = sw.Elapsed.TotalSeconds;
                        string speed = elapsed > 0.01 ? FormatSpeed(downloaded / elapsed) : "...";
                        onProgress($"Downloading...  {speed}  {pct:F1}%");
                    }
                }
                LastErrorMessage = "";
                return true;
            }
            catch (HttpRequestException hex)
            {
                LastErrorMessage = $"Network error: {hex.Message}. Check your internet connection.";
                onProgress($"ERROR: {LastErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Download failed: {ex.Message}";
                onProgress($"ERROR: {LastErrorMessage}");
                return false;
            }
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):F1}MB/s";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:F1}KB/s";
            return $"{bytesPerSec:F0}B/s";
        }
    }
}