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

        // ── PS Server ──────────────────────────────────────────────────────────────

        public async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckForUpdateAsync(string _unused)
        {
            return await CheckReleaseAsync(ServerReleaseUrl, "ps_version.txt", r => r.EndsWith(".zip") || r.EndsWith(".7z"));
        }

        public async Task<bool> DownloadAndInstallAsync(string launcherDir, string downloadUrl, string version, Action<string> onProgress)
        {
            // Auto-organize: PS server always goes into ./server/
            string serverDir = Path.Combine(launcherDir, "server");
            Directory.CreateDirectory(serverDir);

            bool ok = await DownloadAndExtractAsync(downloadUrl, serverDir, onProgress);
            if (ok) SetVersionFile(serverDir, "ps_version.txt", version);
            return ok;
        }

        // ── Proxy ──────────────────────────────────────────────────────────────────

        public async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckProxyUpdateAsync(string _unused)
        {
            return await CheckReleaseAsync(ProxyReleaseUrl, "proxy_version.txt", r => r.Equals(ProxyExeName, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> DownloadAndInstallProxyAsync(string launcherDir, string downloadUrl, string version, Action<string> onProgress)
        {
            // Auto-organize: Proxy always goes into ./proxy/
            string proxyDir = Path.Combine(launcherDir, "proxy");
            Directory.CreateDirectory(proxyDir);

            bool ok = await DownloadFileAsync(downloadUrl, Path.Combine(proxyDir, ProxyExeName), onProgress);
            if (ok) SetVersionFile(proxyDir, "proxy_version.txt", version);
            return ok;
        }

        // ── Shared helpers ─────────────────────────────────────────────────────────

        private async Task<(bool Available, string LatestVersion, string DownloadUrl)> CheckReleaseAsync(
            string apiUrl, string versionFile, Func<string, bool> assetPredicate)
        {
            try
            {
                using HttpClient client = MakeClient();
                var json = await client.GetStringAsync(apiUrl);
                using var doc = JsonDocument.Parse(json);

                // Gitea releases endpoint returns an array
                var root = doc.RootElement;
                JsonElement release;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return (false, "", "");
                    release = root[0];
                }
                else
                {
                    release = root;
                }

                string latest = release.GetProperty("tag_name").GetString() ?? "";
                string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
                string current  = GetVersionFile(launcherDir, versionFile);

                if (latest == current) return (false, latest, "");

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

                return (!string.IsNullOrEmpty(downloadUrl), latest, downloadUrl);
            }
            catch
            {
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
                using HttpClient client = MakeClient();
                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                long? total = resp.Content.Headers.ContentLength;
                using var src  = await resp.Content.ReadAsStreamAsync();
                using var dest = File.Create(destFile);

                byte[] buf = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dest.WriteAsync(buf.AsMemory(0, read));
                    downloaded += read;
                    if (total.HasValue)
                        onProgress($"Downloading... {downloaded * 100 / total.Value}%");
                }
                return true;
            }
            catch (Exception ex)
            {
                onProgress("Download error: " + ex.Message);
                return false;
            }
        }

        private static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.Add("User-Agent", "SilverWolf-Launcher");
            return c;
        }

        public string GetCurrentVersion(string dir) => GetVersionFile(dir, "ps_version.txt");

        private string GetVersionFile(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path)) return File.ReadAllText(path).Trim();

            // Also check subfolders (server/ or proxy/)
            string serverPath = Path.Combine(dir, "server", file);
            if (File.Exists(serverPath)) return File.ReadAllText(serverPath).Trim();

            string proxyPath = Path.Combine(dir, "proxy", file);
            if (File.Exists(proxyPath)) return File.ReadAllText(proxyPath).Trim();

            return "0.0.0";
        }

        private void SetVersionFile(string dir, string file, string version)
        {
            File.WriteAllText(Path.Combine(dir, file), version);
        }

        public void SetCurrentVersion(string dir, string version) => SetVersionFile(dir, "ps_version.txt", version);
    }
}