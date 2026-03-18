using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Windows.Input;
using SilverWolfLauncher.Services;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System.Drawing; // For Icon
using System.Windows.Media; // For Color
using System.Windows.Media.Effects;

namespace SilverWolfLauncher
{
    public partial class MainWindow : Window
    {
        private string selectedPatchPath = "";
        private string gameExecutablePath = ""; 
        private LanguageService languageService;
        private UpdateService updateService;
        

        private bool minimizeToTrayEnabled = false; // Controlled via Settings
        private bool dontAskOnClose = false;         // Remember 'Don't show again'
        private bool isExiting = false;
        private Forms.NotifyIcon? notifyIcon;
        private System.Windows.Threading.DispatcherTimer? watchdogTimer;
        
        // Process Tracking
        private Process? gameProcess;
        private Process? psProcess;
        private Process? proxyProcess;
        private UIElement? airspaceHiddenPanel = null;
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        public MainWindow()
        {
            InitializeComponent();
            languageService = new LanguageService();
            updateService = new UpdateService();
            SetupNotifyIcon();
            SetupWatchdog();
            LoadConfig();
            InitializeAsync();
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new Forms.NotifyIcon();
            notifyIcon.Icon = IconToDrawingIcon(new Uri("pack://application:,,,/Assets/SilverWolficon.ico"));
            notifyIcon.Text = "SilverWolf Launcher";
            notifyIcon.Visible = true; // Always visible for reliability
            notifyIcon.DoubleClick += (s, e) => RestoreFromTray();

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Launcher", null, (s, e) => RestoreFromTray());
            contextMenu.Items.Add("Exit", null, (s, e) => { isExiting = true; Application.Current.Shutdown(); });
            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private System.Drawing.Icon IconToDrawingIcon(Uri uri)
        {
            try {
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null) {
                    using (var bitmap = new System.Drawing.Bitmap(streamInfo.Stream)) {
                        IntPtr hIcon = bitmap.GetHicon();
                        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                        DestroyIcon(hIcon); // Clean up GDI handle
                        return icon;
                    }
                }
            } catch { }
            return System.Drawing.SystemIcons.Application;
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MinimizeToTray()
        {
            this.Hide();
        }
        async void InitializeAsync()
        {
            try {
                await WebViewSRTools.EnsureCoreWebView2Async(null);
                await WebViewTutorials.EnsureCoreWebView2Async(null);
            } catch { /* WebView might not be ready yet */ }
            
        if (!string.IsNullOrEmpty(gameExecutablePath))
        {
            await CheckAndPromptPSInstallation();
        }
    }

    private void SetupWatchdog()
    {
        watchdogTimer = new System.Windows.Threading.DispatcherTimer();
        watchdogTimer.Interval = TimeSpan.FromSeconds(5);
        watchdogTimer.Tick += (s, e) => {
            if (gameProcess == null || gameProcess.HasExited)
            {
                StopWatchdog();
                return;
            }

            // Ensure dependencies are running
            if (psProcess == null || psProcess.HasExited) LaunchPS();
            if (proxyProcess == null || proxyProcess.HasExited) LaunchProxy();
        };
    }

    private void StartWatchdog()
    {
        if (watchdogTimer != null) watchdogTimer.Start();
        TxtGlobalStatus.Text = "GAME IS RUNNING — WATCHDOG ACTIVE";
        GridStatus.Visibility = Visibility.Visible;
        ProgGlobal.IsIndeterminate = true;
        if (notifyIcon != null) notifyIcon.Text = "SilverWolf Launcher (Game Running)";
    }

    private void StopWatchdog()
    {
        if (watchdogTimer != null) watchdogTimer.Stop();
        GridStatus.Visibility = Visibility.Collapsed;
        ProgGlobal.IsIndeterminate = false;
        gameProcess = null;
        if (notifyIcon != null) notifyIcon.Text = "SilverWolf Launcher";
    }


    private void LaunchPS()
    {
        if (psProcess != null && !psProcess.HasExited) return;

        string gameDir = !string.IsNullOrEmpty(gameExecutablePath) ? Path.GetDirectoryName(gameExecutablePath) ?? "" : "";
        string launcherDir = AppDomain.CurrentDomain.BaseDirectory;

        string[] psSearchPaths = {
            Path.Combine(launcherDir, "server", "firefly-go_win.exe"),
            Path.Combine(gameDir, "server", "firefly-go_win.exe"),
            Path.Combine(gameDir, "firefly-go_win.exe"),
        };

        foreach (var psPath in psSearchPaths)
        {
            if (File.Exists(psPath))
            {
                try
                {
                    var psi = new ProcessStartInfo(psPath) { WorkingDirectory = Path.GetDirectoryName(psPath), UseShellExecute = false, CreateNoWindow = false };
                    psProcess = Process.Start(psi);
                } catch { }
                break;
            }
        }
    }

    private void LaunchProxy()
    {
        if (proxyProcess != null && !proxyProcess.HasExited) return;

        string gameDir = !string.IsNullOrEmpty(gameExecutablePath) ? Path.GetDirectoryName(gameExecutablePath) ?? "" : "";
        string launcherDir = AppDomain.CurrentDomain.BaseDirectory;

        string[] proxySearchPaths = {
            Path.Combine(launcherDir, "proxy", "firefly-go-proxy.exe"),
            Path.Combine(gameDir, "proxy", "firefly-go-proxy.exe"),
            Path.Combine(gameDir, "firefly-go-proxy.exe"),
        };

        foreach (var proxyPath in proxySearchPaths)
        {
            if (File.Exists(proxyPath))
            {
                try
                {
                    var psi = new ProcessStartInfo(proxyPath) { WorkingDirectory = Path.GetDirectoryName(proxyPath), UseShellExecute = false, CreateNoWindow = false };
                    proxyProcess = Process.Start(psi);
                } catch { }
                break;
            }
        }
    }

    private async Task CheckAndPromptPSInstallation()
        {
            string gameDir = Path.GetDirectoryName(gameExecutablePath) ?? "";
            if (string.IsNullOrEmpty(gameDir)) return;

            string psVersionFile = Path.Combine(gameDir, "ps_version.txt");
            if (!File.Exists(psVersionFile))
            {
                ShowPrompt("INSTALL PRIVATE SERVER", "Private Server (PS) files not found in your game directory. Would you like to install them now?", async () => {
                    await PerformPSUpdate(gameDir);
                });
            }
        }

        // Show styled notification (replaces MessageBox.Show)
        private void ShowInfo(string title, string message, Action? onOk = null)
        {
            Dispatcher.Invoke(() => {
                ChkDontShowAgain.Visibility = Visibility.Collapsed;
                TxtPromptTitle.Text = title;
                TxtPromptMessage.Text = message;
                BtnPromptOk.Content = "OK";
                BtnPromptCancel.Content = "";
                BtnPromptCancel.Visibility = Visibility.Collapsed;
                BtnPromptOk.Tag = onOk;
                BtnPromptCancel.Tag = (Action?)null;
                ShowPromptAnimated();
            });
        }

        private void ShowPrompt(string title, string message, Action onConfirm, string okText = "OK", string cancelText = "LATER", Action? onCancel = null, bool showCheckbox = false)
        {
            Dispatcher.Invoke(() => {
                ChkDontShowAgain.Visibility = showCheckbox ? Visibility.Visible : Visibility.Collapsed;
                TxtPromptTitle.Text = title;
                TxtPromptMessage.Text = message;
                BtnPromptOk.Content = okText;
                BtnPromptCancel.Content = cancelText;
                BtnPromptCancel.Visibility = Visibility.Visible;
                BtnPromptOk.Tag = onConfirm;
                BtnPromptCancel.Tag = onCancel;
                ShowPromptAnimated();
            });
        }

        private void ShowPromptAnimated()
        {
            HideAirspace();
            GridPrompt.Visibility = Visibility.Visible;
            GridPrompt.Opacity = 0;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            GridPrompt.BeginAnimation(UIElement.OpacityProperty, fade);

            // Scale the dialog in
            if (GridPrompt.Children.Count > 0 && GridPrompt.Children[0] is FrameworkElement dlg)
            {
                dlg.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                var st = new System.Windows.Media.ScaleTransform(0.85, 0.85);
                dlg.RenderTransform = st;
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(220));
                var scaleY = new System.Windows.Media.Animation.DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(220));
                scaleX.EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.3 };
                scaleY.EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.3 };
                st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
            }
        }

        private void BtnPromptOk_Click(object sender, RoutedEventArgs e)
        {
            GridPrompt.Visibility = Visibility.Collapsed;
            RestoreAirspace();
            if (BtnPromptOk.Tag is Action action) action();
        }

        private void BtnPromptCancel_Click(object sender, RoutedEventArgs e)
        {
            GridPrompt.Visibility = Visibility.Collapsed;
            RestoreAirspace();
            if (BtnPromptCancel.Tag is Action action) action();
        }

        private void HideAirspace()
        {
            if (PanelSRTools.Visibility == Visibility.Visible) { airspaceHiddenPanel = PanelSRTools; PanelSRTools.Visibility = Visibility.Hidden; }
            else if (PanelTutorials.Visibility == Visibility.Visible) { airspaceHiddenPanel = PanelTutorials; PanelTutorials.Visibility = Visibility.Hidden; }
        }

        private void RestoreAirspace()
        {
            if (airspaceHiddenPanel != null) { airspaceHiddenPanel.Visibility = Visibility.Visible; airspaceHiddenPanel = null; }
        }

        private void OpenDiscord(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://discord.gg/QwfTnEdAtN") { UseShellExecute = true });
        private void OpenGithub(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/xeroxua") { UseShellExecute = true });

        private void LoadConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            // Legacy plain-text config support
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("gamePath",       out var gp))  { gameExecutablePath    = gp.GetString() ?? ""; TxtGamePath.Text = gameExecutablePath; }
                    if (root.TryGetProperty("minimizeToTray", out var mt))  { minimizeToTrayEnabled = mt.GetBoolean(); ChkMinimizeToTray.IsChecked = minimizeToTrayEnabled; }
                    if (root.TryGetProperty("dontAskOnClose", out var da))  { dontAskOnClose        = da.GetBoolean(); }
                    if (root.TryGetProperty("autoServices",   out var ast)) { ChkAutoServices.IsChecked    = ast.GetBoolean(); }
                }
                catch { }
            }
            else if (File.Exists(legacyPath))
            {
                gameExecutablePath = File.ReadAllText(legacyPath).Trim();
                TxtGamePath.Text = gameExecutablePath;
            }
        }

        private void SaveConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            var obj = new {
                gamePath       = gameExecutablePath,
                minimizeToTray = minimizeToTrayEnabled,
                dontAskOnClose = this.dontAskOnClose,
                autoServices   = ChkAutoServices.IsChecked ?? true
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (minimizeToTrayEnabled) MinimizeToTray();
            else WindowState = WindowState.Minimized;
        }

        // Intercept all close events — X button, taskbar close, Alt+F4
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (isExiting)
            {
                base.OnClosing(e);
                return;
            }

            // Always cancel the raw close, handle it ourselves
            e.Cancel = true;

            // If user explicitly enabled minimize-to-tray in settings, or chose "Don't ask"
            if (minimizeToTrayEnabled || dontAskOnClose)
            {
                if (minimizeToTrayEnabled) MinimizeToTray();
                else { isExiting = true; Application.Current.Shutdown(); }
                return;
            }

            // Show styled prompt
            ChkDontShowAgain.IsChecked = false;

            ShowPrompt(
                (string)FindResource("L_ExitTitle"),
                (string)FindResource("L_ExitMsg"),
                () => { // EXIT
                    if (ChkDontShowAgain.IsChecked == true)
                    {
                        dontAskOnClose = true;
                        minimizeToTrayEnabled = false;
                        ChkMinimizeToTray.IsChecked = false;
                        SaveConfig();
                    }
                    isExiting = true;
                    Application.Current.Shutdown();
                },
                (string)FindResource("L_ExitBtn"),
                (string)FindResource("L_MinimizeBtn"),
                () => { // MINIMIZE
                    if (ChkDontShowAgain.IsChecked == true)
                    {
                        dontAskOnClose = true;
                        minimizeToTrayEnabled = true;
                        ChkMinimizeToTray.IsChecked = true;
                        SaveConfig();
                    }
                    MinimizeToTray();
                },
                true // Show checkbox
            );
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Delegate to the unified Closing handler
            OnClosing(new System.ComponentModel.CancelEventArgs());
        }

        private void HideAllPanels()
        {
            PanelHome.Visibility = Visibility.Hidden;
            PanelPatcher.Visibility = Visibility.Hidden;
            PanelLanguage.Visibility = Visibility.Hidden;
            PanelSRTools.Visibility = Visibility.Hidden;
            PanelTutorials.Visibility = Visibility.Hidden;
            PanelSettings.Visibility = Visibility.Hidden;
            PanelInfo.Visibility = Visibility.Hidden;
            PanelProxy.Visibility = Visibility.Hidden;
            
            BtnHome.Style = (Style)Resources["ModernNavButton"];
            BtnPatcher.Style = (Style)Resources["ModernNavButton"];
            BtnLanguage.Style = (Style)Resources["ModernNavButton"];
            BtnSRTools.Style = (Style)Resources["ModernNavButton"];
            BtnTutorials.Style = (Style)Resources["ModernNavButton"];
            BtnSettings.Style = (Style)Resources["ModernNavButton"];
            BtnInfo.Style = (Style)Resources["ModernNavButton"];
            BtnProxy.Style = (Style)Resources["ModernNavButton"];
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelHome); BtnHome.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnProxy_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelProxy); BtnProxy.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnPatcher_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelPatcher); BtnPatcher.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnLanguage_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelLanguage); BtnLanguage.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnSRTools_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelSRTools); BtnSRTools.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnTutorials_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelTutorials); BtnTutorials.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnSettings_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelSettings); BtnSettings.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnInfo_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelInfo); BtnInfo.Style = (Style)Resources["ActiveNavButton"]; }

        private void SwitchPanel(UIElement panel)
        {
            HideAllPanels();
            panel.Visibility = Visibility.Visible;
            AnimatePanelIn(panel);

            // Dynamic Background Blur (Exclude Home)
            double targetBlur = (panel == PanelHome) ? 0 : 12; // 12 is a good "frosted" value
            var blurAnim = new System.Windows.Media.Animation.DoubleAnimation(targetBlur, TimeSpan.FromMilliseconds(400));
            blurAnim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            BackgroundBlur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnim);
        }

        private void AnimatePanelIn(UIElement panel)
        {
            // Fade in
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            fadeIn.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Slide from right (TranslateTransform)
            if (panel is FrameworkElement fe)
            {
                var tt = new System.Windows.Media.TranslateTransform(20, 0);
                fe.RenderTransform = tt;
                var slideIn = new System.Windows.Media.Animation.DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(250));
                slideIn.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
            }
        }

        private void WebViewSRTools_InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // Set zoom to 0.9 so site fits without overflowing at 100%
                WebViewSRTools.ZoomFactor = 0.9;

                WebViewSRTools.CoreWebView2.NavigationCompleted += async (s, args) => {
                    await WebViewSRTools.CoreWebView2.ExecuteScriptAsync(@"
                        (function() {
                            var existing = document.getElementById('sw-launcher-style');
                            if (existing) return;
                            var style = document.createElement('style');
                            style.id = 'sw-launcher-style';
                            style.innerHTML = [
                                '::-webkit-scrollbar { display: none !important; }',
                                '::-webkit-scrollbar-track { display: none !important; }',
                                'html { overflow-x: hidden !important; }',
                                'body { overflow-x: hidden !important; }'
                            ].join(' ');
                            document.head.appendChild(style);
                        })();
                    ");
                };
            }
        }

        private void WebViewTutorials_InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                WebViewTutorials.CoreWebView2.NavigationCompleted += async (s, args) => {
                    await WebViewTutorials.CoreWebView2.ExecuteScriptAsync(@"
                        (function() {
                            var style = document.createElement('style');
                            style.id = 'sw-launcher-style';
                            if (document.getElementById('sw-launcher-style')) return;
                            style.innerHTML = [                                
                                '::-webkit-scrollbar { display: none !important; }',
                                'html { overflow-x: hidden !important; max-width: 100% !important; }',
                                'body { overflow-x: hidden !important; max-width: 100% !important; }'
                            ].join(' ');
                            document.head.appendChild(style);
                        })();
                    ");
                };
            }
        }

        private void CmbLauncherLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLauncherLang.SelectedItem is ComboBoxItem item)
            {
                string lang = item.Tag?.ToString() ?? "en";
                SetLauncherLanguage(lang);
            }
        }

        private void SetLauncherLanguage(string? lang)
        {
            if (string.IsNullOrEmpty(lang)) return;
            try
            {
                var dict = new ResourceDictionary { Source = new Uri($"Localization/AppStrings.{lang}.xaml", UriKind.Relative) };
                ResourceDictionary? oldDict = null;
                foreach (var d in Application.Current.Resources.MergedDictionaries)
                {
                    if (d.Source != null && d.Source.OriginalString.Contains("Localization/AppStrings."))
                    {
                        oldDict = d;
                        break;
                    }
                }
                if (oldDict != null) Application.Current.Resources.MergedDictionaries.Remove(oldDict);
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch { }
        }

        private async void BtnBrowseGame_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Executable Files|*.exe";
            if (openFileDialog.ShowDialog() == true)
            {
                gameExecutablePath = openFileDialog.FileName;
                TxtGamePath.Text = gameExecutablePath;
                SaveConfig();
                await CheckAndPromptPSInstallation();
            }
        }

        private void BtnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(gameExecutablePath) || !File.Exists(gameExecutablePath)) return;

            if (ChkAutoServices.IsChecked == true)
            {
                LaunchPS();
                LaunchProxy();
            }

            try
            {
                var psi = new ProcessStartInfo(gameExecutablePath) { WorkingDirectory = Path.GetDirectoryName(gameExecutablePath) };
                gameProcess = Process.Start(psi);
                StartWatchdog();
                if (minimizeToTrayEnabled) MinimizeToTray();
            }
            catch (Exception ex) { ShowInfo("ERROR", $"Failed to launch game: {ex.Message}"); }
        }


        private void BtnSelectPatch_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Archives|*.7z;*.zip";
            if (openFileDialog.ShowDialog() == true)
            {
                selectedPatchPath = openFileDialog.FileName;
                TxtPatchPath.Text = Path.GetFileName(selectedPatchPath);
            }
        }

        private async void BtnApplyPatch_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedPatchPath) || string.IsNullOrEmpty(gameExecutablePath)) return;
            string gameDir = Path.GetDirectoryName(gameExecutablePath) ?? "";
            
            GridStatus.Visibility = Visibility.Visible;
            TxtGlobalStatus.Text = "APPLYING MANUALLY PATCH...";
            ProgGlobal.IsIndeterminate = true;

            try {
                await Task.Run(() => ApplyPatchRoutine(selectedPatchPath, gameDir));
                ShowInfo("SUCCESS", "Patch applied successfully!");
            } catch (Exception ex) {
                ShowInfo("ERROR", ex.Message);
            } finally {
                GridStatus.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyPatchRoutine(string archivePath, string gameDir)
        {
            using (var archive = ArchiveFactory.Open(archivePath)) {
                foreach (var entry in archive.Entries) {
                    if (!entry.IsDirectory) entry.WriteToDirectory(gameDir, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                }
            }
            // (Re-using logic from previous turns)
            string hpatchzPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "hpatchz.exe");
            if (!File.Exists(hpatchzPath)) return;
            List<HDiffData> diffMap = ParseHDiffMaps(gameDir);
            foreach (var entry in diffMap) {
                string sourceFile = Path.Combine(gameDir, entry.SourceFileName ?? "");
                string patchFile = Path.Combine(gameDir, entry.PatchFileName ?? "");
                string targetFile = Path.Combine(gameDir, entry.TargetFileName ?? "");
                if (File.Exists(patchFile)) {
                    string tempTarget = targetFile + ".tmp";
                    RunHPatchz(hpatchzPath, sourceFile, patchFile, tempTarget);
                    if (File.Exists(tempTarget)) {
                        if (File.Exists(targetFile)) File.Delete(targetFile);
                        File.Move(tempTarget, targetFile);
                    }
                    File.Delete(patchFile);
                }
            }
        }

        private void RunHPatchz(string hpatchzPath, string oldFile, string diffFile, string newFile)
        {
            ProcessStartInfo psi = new ProcessStartInfo(hpatchzPath);
            psi.Arguments = $"-f \"{oldFile}\" \"{diffFile}\" \"{newFile}\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (var process = Process.Start(psi)) { process?.WaitForExit(); }
        }

        private List<HDiffData> ParseHDiffMaps(string gameDir)
        {
            List<HDiffData> result = new List<HDiffData>();
            string hdiffMapPath = Path.Combine(gameDir, "hdiffmap.json");
            if (File.Exists(hdiffMapPath)) {
                string json = File.ReadAllText(hdiffMapPath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("diff_map", out JsonElement diffMap)) {
                    foreach (var element in diffMap.EnumerateArray()) {
                        result.Add(new HDiffData {
                            SourceFileName = element.GetProperty("source_file_name").GetString() ?? "",
                            TargetFileName = element.GetProperty("target_file_name").GetString() ?? "",
                            PatchFileName = element.GetProperty("patch_file_name").GetString() ?? ""
                        });
                    }
                }
            }
            return result;
        }

        private void BtnApplyLanguage_Click(object sender, RoutedEventArgs e)
        {
            if (CmbTextLang.SelectedItem is ComboBoxItem textItem && CmbVoiceLang.SelectedItem is ComboBoxItem voiceItem) {
                var textLang = ParseLanguage(textItem.Tag?.ToString());
                var voiceLang = ParseLanguage(voiceItem.Tag?.ToString());
                if (languageService.SetGameLanguage(textLang, voiceLang)) TxtLangStatus.Text = "CHANGES APPLIED!";
                else TxtLangStatus.Text = "FAILED TO APPLY CHANGES.";
            }
        }

        private LanguageService.GameLanguage ParseLanguage(string? code)
        {
            return code switch { "cn" => LanguageService.GameLanguage.CN, "jp" => LanguageService.GameLanguage.JP, "kr" => LanguageService.GameLanguage.KR, _ => LanguageService.GameLanguage.EN };
        }

        private async void BtnUpdatePS_Click(object sender, RoutedEventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            BtnUpdatePS.IsEnabled = false;

            // Check PS Server
            var (psAvail, psVer, psUrl) = await updateService.CheckForUpdateAsync(launcherDir);
            // Check Proxy
            var (proxyAvail, proxyVer, proxyUrl) = await updateService.CheckProxyUpdateAsync(launcherDir);

            if (!psAvail && !proxyAvail)
            {
                ShowInfo("UP TO DATE", "Private Server and Proxy are both up to date.");
            }
            else
            {
                string details = "";
                if (psAvail) details += $"• PS Server → v{psVer}\n";
                if (proxyAvail) details += $"• Proxy → v{proxyVer}";

                ShowPrompt("UPDATE AVAILABLE", $"Updates ready:\n{details}\n\nDownload and install now?",
                    async () => {
                        if (psAvail) await PerformPSUpdate(launcherDir, psVer, psUrl);
                        if (proxyAvail) await PerformProxyUpdate(launcherDir, proxyVer, proxyUrl);
                    },
                    "DOWNLOAD", "LATER");
            }

            BtnUpdatePS.IsEnabled = true;
        }

        private void ChkMinimizeToTray_Checked(object sender, RoutedEventArgs e)
        {
            minimizeToTrayEnabled = true;
            SaveConfig();
            ShowInfo("SETTINGS", "Minimize to Tray: ENABLED");
        }

        private void ChkMinimizeToTray_Unchecked(object sender, RoutedEventArgs e)
        {
            minimizeToTrayEnabled = false;
            dontAskOnClose = false; // Reset if they disable it
            SaveConfig();
            ShowInfo("SETTINGS", "Minimize to Tray: DISABLED (Exit completely)");
        }

        private void ChkAutoServices_Checked(object sender, RoutedEventArgs e)
        {
            // Update internal state if there's a variable for this
            // config.autoLaunch = true;
            SaveConfig();
        }

        private void ChkAutoServices_Unchecked(object sender, RoutedEventArgs e)
        {
            // config.autoLaunch = false;
            SaveConfig();
        }

        private async Task PerformPSUpdate(string launcherDir, string version = "", string url = "")
        {
            if (string.IsNullOrEmpty(url)) {
                var info = await updateService.CheckForUpdateAsync(launcherDir);
                url = info.DownloadUrl; version = info.LatestVersion;
            }
            GridStatus.Visibility = Visibility.Visible;
            try {
                bool success = await updateService.DownloadAndInstallAsync(launcherDir, url, version, msg => {
                    Dispatcher.Invoke(() => {
                        TxtGlobalStatus.Text = msg.ToUpper();
                        bool hasPercent = msg.Contains("%");
                        ProgGlobal.IsIndeterminate = !hasPercent;
                        if (hasPercent) {
                            if (int.TryParse(new string(msg.Where(char.IsDigit).ToArray()), out int pct))
                                ProgGlobal.Value = pct;
                        }
                    });
                });
                if (success) ShowInfo("INSTALLED", "Private Server installed to ./server/ successfully!");
                else ShowInfo("ERROR", "Download failed. Check your internet connection.");
            } finally {
                GridStatus.Visibility = Visibility.Collapsed;
                ProgGlobal.IsIndeterminate = false;
                ProgGlobal.Value = 0;
            }
        }

        private async void BtnUpdateProxy_Click(object sender, RoutedEventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            BtnUpdateProxy.IsEnabled = false;
            var (avail, ver, url) = await updateService.CheckProxyUpdateAsync(launcherDir);
            
            if (avail) {
                await PerformProxyUpdate(launcherDir, ver, url);
            } else {
                ShowInfo("UP TO DATE", "Proxy is already the latest version.");
            }
            BtnUpdateProxy.IsEnabled = true;
        }

        private void BtnOpenServerFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        private void BtnOpenProxyFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proxy");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        private async Task PerformProxyUpdate(string launcherDir, string version = "", string url = "")
        {
            if (string.IsNullOrEmpty(url)) {
                var info = await updateService.CheckProxyUpdateAsync(launcherDir);
                url = info.DownloadUrl; version = info.LatestVersion;
            }
            GridStatus.Visibility = Visibility.Visible;
            try {
                bool success = await updateService.DownloadAndInstallProxyAsync(launcherDir, url, version, msg => {
                    Dispatcher.Invoke(() => {
                        TxtGlobalStatus.Text = msg.ToUpper();
                        bool hasPercent = msg.Contains("%");
                        ProgGlobal.IsIndeterminate = !hasPercent;
                        if (hasPercent) {
                            if (int.TryParse(new string(msg.Where(char.IsDigit).ToArray()), out int pct))
                                ProgGlobal.Value = pct;
                        }
                    });
                });
                if (success) ShowInfo("INSTALLED", "Proxy installed to ./proxy/ successfully!");
                else ShowInfo("ERROR", "Proxy download failed.");
            } finally {
                GridStatus.Visibility = Visibility.Collapsed;
                ProgGlobal.IsIndeterminate = false;
                ProgGlobal.Value = 0;
            }
        }
    }

    public class HDiffData {
        [JsonPropertyName("source_file_name")] public string SourceFileName { get; set; } = "";
        [JsonPropertyName("target_file_name")] public string TargetFileName { get; set; } = "";
        [JsonPropertyName("patch_file_name")] public string PatchFileName { get; set; } = "";
    }
}