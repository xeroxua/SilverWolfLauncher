using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SilverWolfLauncher.Models;

namespace SilverWolfLauncher.Services
{
    public class PatcherService
    {
        // Known game executables to validate a game directory
        private static readonly string[] GameExecutables = { "StarRail.exe", "BH3.exe", "GenshinImpact.exe", "YuanShen.exe" };
        private static readonly string[] DiffExtensions = { ".hdiff", ".diff" };

        /// <summary>Validate that the given path is a real game directory</summary>
        public (bool IsValid, string Message) ValidateGameDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return (false, "Directory does not exist.");

            foreach (var exe in GameExecutables)
            {
                if (File.Exists(Path.Combine(path, exe)))
                    return (true, $"✓ Valid game directory found! ({exe})");
            }
            // Also check if StarRail_Data exists (common subfolder)
            if (Directory.Exists(Path.Combine(path, "StarRail_Data")))
                return (true, "✓ Valid game directory found!");

            return (false, "✗ No game executable found in this folder.");
        }

        /// <summary>Validate that the given path contains diff/hdiff files</summary>
        public (bool IsValid, string Message, int FileCount) ValidateDiffDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return (false, "Directory does not exist.", 0);

            int count = 0;
            foreach (var ext in DiffExtensions)
                count += Directory.GetFiles(path, $"*{ext}", SearchOption.AllDirectories).Length;

            // Also check for hdiffmap.json
            bool hasMap = File.Exists(Path.Combine(path, "hdiffmap.json"));

            if (count > 0 || hasMap)
                return (true, $"✓ Found {count} diff file(s)" + (hasMap ? " + hdiffmap.json" : ""), count);

            return (false, "✗ No .hdiff or .diff files found.", 0);
        }

        /// <summary>Apply all diffs from diffDir onto gameDir using hpatchz</summary>
        public async Task ApplyDiffFolderAsync(string gameDir, string diffDir, Action<string> onStatus)
        {
            await Task.Run(() =>
            {
                string hpatchzPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "hpatchz.exe");
                if (!File.Exists(hpatchzPath))
                {
                    onStatus("ERROR: hpatchz.exe not found in Assets!");
                    return;
                }

                // 1. Apply hdiffmap.json if present
                string hdiffMapPath = Path.Combine(diffDir, "hdiffmap.json");
                if (File.Exists(hdiffMapPath))
                {
                    onStatus("READING HDIFF MAP...");
                    var diffMap = ParseHDiffMaps(diffDir);
                    int i = 0;
                    foreach (var entry in diffMap)
                    {
                        i++;
                        string sourceFile = Path.Combine(gameDir, entry.SourceFileName);
                        string patchFile = Path.Combine(diffDir, entry.PatchFileName);
                        string targetFile = Path.Combine(gameDir, entry.TargetFileName);

                        onStatus($"PATCHING ({i}/{diffMap.Count}): {Path.GetFileName(entry.TargetFileName)}");

                        if (File.Exists(patchFile) && File.Exists(sourceFile))
                        {
                            string tempTarget = targetFile + ".tmp";
                            RunHPatchz(hpatchzPath, sourceFile, patchFile, tempTarget);
                            if (File.Exists(tempTarget))
                            {
                                if (File.Exists(targetFile)) File.Delete(targetFile);
                                File.Move(tempTarget, targetFile);
                            }
                        }
                    }
                }

                // 2. Apply any remaining .hdiff files not in the map
                var hdiffFiles = Directory.GetFiles(diffDir, "*.hdiff", SearchOption.AllDirectories);
                int j = 0;
                foreach (var hdiff in hdiffFiles)
                {
                    j++;
                    // Determine the relative path and apply to game dir
                    string relativePath = Path.GetRelativePath(diffDir, hdiff);
                    string targetPath = Path.Combine(gameDir, Path.ChangeExtension(relativePath, null));

                    onStatus($"APPLYING HDIFF ({j}/{hdiffFiles.Length}): {Path.GetFileName(hdiff)}");

                    if (File.Exists(targetPath))
                    {
                        string tempTarget = targetPath + ".tmp";
                        RunHPatchz(hpatchzPath, targetPath, hdiff, tempTarget);
                        if (File.Exists(tempTarget))
                        {
                            File.Delete(targetPath);
                            File.Move(tempTarget, targetPath);
                        }
                    }
                }

                // 3. Copy any non-hdiff files (new assets, configs, etc.)
                var otherFiles = Directory.GetFiles(diffDir, "*", SearchOption.AllDirectories)
                    .Where(f => !DiffExtensions.Contains(Path.GetExtension(f).ToLower())
                             && !f.EndsWith("hdiffmap.json", StringComparison.OrdinalIgnoreCase));
                foreach (var file in otherFiles)
                {
                    string relativePath = Path.GetRelativePath(diffDir, file);
                    string destPath = Path.Combine(gameDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? gameDir);
                    File.Copy(file, destPath, true);
                }

                onStatus("✓ GAME UPDATE COMPLETE!");
            });
        }

        /// <summary>Original single-archive patch method</summary>
        public async Task ApplyPatchAsync(string archivePath, string gameDir, Action<string> onStatus)
        {
            await Task.Run(() =>
            {
                onStatus("EXTRACTING PATCH...");
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                            entry.WriteToDirectory(gameDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                    }
                }

                string hpatchzPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "hpatchz.exe");
                if (!File.Exists(hpatchzPath))
                {
                    onStatus("HPATCHZ NOT FOUND!");
                    return;
                }

                onStatus("APPLYING HDIFF PATCHES...");
                List<HDiffData> diffMap = ParseHDiffMaps(gameDir);
                foreach (var entry in diffMap)
                {
                    string sourceFile = Path.Combine(gameDir, entry.SourceFileName);
                    string patchFile = Path.Combine(gameDir, entry.PatchFileName);
                    string targetFile = Path.Combine(gameDir, entry.TargetFileName);

                    if (File.Exists(patchFile))
                    {
                        string tempTarget = targetFile + ".tmp";
                        RunHPatchz(hpatchzPath, sourceFile, patchFile, tempTarget);
                        if (File.Exists(tempTarget))
                        {
                            if (File.Exists(targetFile)) File.Delete(targetFile);
                            File.Move(tempTarget, targetFile);
                        }
                        try { File.Delete(patchFile); } catch { }
                    }
                }
                onStatus("PATCH APPLIED SUCCESSFULLY!");
            });
        }

        private void RunHPatchz(string hpatchzPath, string oldFile, string diffFile, string newFile)
        {
            ProcessStartInfo psi = new ProcessStartInfo(hpatchzPath)
            {
                Arguments = $"-f \"{oldFile}\" \"{diffFile}\" \"{newFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }

        private List<HDiffData> ParseHDiffMaps(string dir)
        {
            var result = new List<HDiffData>();
            string hdiffMapPath = Path.Combine(dir, "hdiffmap.json");
            if (!File.Exists(hdiffMapPath)) return result;

            try
            {
                string json = File.ReadAllText(hdiffMapPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("diff_map", out JsonElement diffMap))
                {
                    foreach (var element in diffMap.EnumerateArray())
                    {
                        result.Add(new HDiffData
                        {
                            SourceFileName = element.GetProperty("source_file_name").GetString() ?? "",
                            TargetFileName = element.GetProperty("target_file_name").GetString() ?? "",
                            PatchFileName = element.GetProperty("patch_file_name").GetString() ?? ""
                        });
                    }
                }
            }
            catch { }
            return result;
        }
    }

    public class HDiffData
    {
        public string SourceFileName { get; set; } = "";
        public string TargetFileName { get; set; } = "";
        public string PatchFileName { get; set; } = "";
    }
}
