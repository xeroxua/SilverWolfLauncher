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
        
        private string serverVersion = "0.0.0";
        private string proxyVersion = "0.0.0";
        private bool isInitializingUI = false;
        
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
            StartBackgroundVideo();
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
            } catch { /* WebView might not be ready yet */ }
            
            if (!string.IsNullOrEmpty(gameExecutablePath))
            {
                await CheckAndPromptPSInstallation();
            }

            // Load version info for Settings panel
            LoadVersionInfo();

            // Update check moved to hamburger menu → "Check for Updates Server & Proxy"
        }

        private const string CurrentLauncherVersion = "1.2.0";
        private const string LauncherGitUrl = "https://git.kain.io.vn/api/v1/repos/Firefly-Shelter/Firefly_Launcher/releases";

        private void LoadVersionInfo()
        {
            string sVer = serverVersion == "0.0.0" ? "Not Installed" : serverVersion;
            string pVer = proxyVersion == "0.0.0" ? "Not Installed" : proxyVersion;

            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            if (serverVersion == "0.0.0" && File.Exists(Path.Combine(launcherDir, "server", "firefly-go_win.exe")))
                sVer = "Unknown (Update required)";
            if (proxyVersion == "0.0.0" && File.Exists(Path.Combine(launcherDir, "proxy", "firefly-go-proxy.exe")))
                pVer = "Unknown (Update required)";

            TxtVersionInfo.Text = $"Server: {sVer}  Proxy: {pVer}  Launcher: {CurrentLauncherVersion}";
        }

        // Removed legacy ReadVersionFile
        private async void BtnCheckLauncherUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckLauncherUpdate.IsEnabled = false;
            var originalContent = BtnCheckLauncherUpdate.Content;
            BtnCheckLauncherUpdate.Content = "Checking...";
            
            try
            {
                // The instruction refers to UpdateService.cs for CancellationTokenSource,
                // but the snippet is for MainWindow.xaml.cs.
                // Assuming the intent is to increase the HttpClient timeout here.
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.Add("User-Agent", "SilverWolf-Launcher");
                var json = await client.GetStringAsync(LauncherGitUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    string latest = root[0].GetProperty("tag_name").GetString() ?? "";
                    if (!string.IsNullOrEmpty(latest) && latest != CurrentLauncherVersion)
                    {
                        ShowPrompt("LAUNCHER UPDATE", $"A new version of the launcher is available ({latest}).\nWould you like to visit the release page and download it?",
                            () => Process.Start(new ProcessStartInfo("https://github.com/xeroxua/SilverWolfLauncher/releases") { UseShellExecute = true }),
                            "Download", "Later");
                    }
                    else
                        ShowInfo("Up to Date", "Your launcher is already the latest version.");
                }
                else ShowInfo("Error", "Could not fetch release info.");
            }
            catch (Exception ex) { ShowInfo("Error", $"Failed to check: {ex.Message}"); }
            finally
            {
                BtnCheckLauncherUpdate.Content = originalContent;
                BtnCheckLauncherUpdate.IsEnabled = true;
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

        private async void BtnUpdatePS_Click(object sender, RoutedEventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            BtnUpdatePS.IsEnabled = false;
            var originalContent = BtnUpdatePS.Content;
            BtnUpdatePS.Content = "CHECKING...";

            try
            {
                var (avail, ver, url) = await updateService.CheckForUpdateAsync(serverVersion);
                if (avail)
                {
                    ShowPrompt("UPDATE PRIVATE SERVER", $"A new Private Server version ({ver}) is available.\nUpdate now?",
                        async () => {
                            await PerformPSUpdate(launcherDir, ver, url);
                        },
                        "DOWNLOAD", "LATER");
                }
                else
                {
                    ShowInfo("UP TO DATE", "Private Server is already the latest version.");
                }
            }
            finally
            {
                BtnUpdatePS.Content = originalContent;
                BtnUpdatePS.IsEnabled = true;
            }
        }
        private async Task CheckAndPromptPSInstallation()
        {
            string gameDir = Path.GetDirectoryName(gameExecutablePath) ?? "";
            if (string.IsNullOrEmpty(gameDir)) return;

            string psExe1 = Path.Combine(gameDir, "firefly-go_win.exe");
            string psExe2 = Path.Combine(gameDir, "server", "firefly-go_win.exe");
            string launcherPsExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server", "firefly-go_win.exe");
            
            if (!File.Exists(psExe1) && !File.Exists(psExe2) && !File.Exists(launcherPsExe))
            {
                ShowPrompt("INSTALL PRIVATE SERVER", "Private Server (PS) files not found in your game directory or launcher. Would you like to install them now?", async () => {
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

        private void BtnPromptClose_Click(object sender, RoutedEventArgs e)
        {
            GridPrompt.Visibility = Visibility.Collapsed;
            RestoreAirspace();
        }

        private void HideAirspace()
        {
            if (PanelSRTools.Visibility == Visibility.Visible) { airspaceHiddenPanel = PanelSRTools; PanelSRTools.Visibility = Visibility.Hidden; }
        }

        private void RestoreAirspace()
        {
            if (airspaceHiddenPanel != null) { airspaceHiddenPanel.Visibility = Visibility.Visible; airspaceHiddenPanel = null; }
        }

        private void OpenDiscord(object sender, RoutedEventArgs e) { CloseHamburgerMenu(); Process.Start(new ProcessStartInfo("https://discord.gg/QwfTnEdAtN") { UseShellExecute = true }); }
        private void OpenGithub(object sender, RoutedEventArgs e) { CloseHamburgerMenu(); Process.Start(new ProcessStartInfo("https://github.com/xeroxua") { UseShellExecute = true }); }

        // ── Hamburger Menu ────────────────────────────────────────────────────────
        private void BtnHamburger_Click(object sender, RoutedEventArgs e)
        {
            GridHamburgerOverlay.Visibility = GridHamburgerOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CloseHamburgerMenu() => GridHamburgerOverlay.Visibility = Visibility.Collapsed;

        private void GridHamburgerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseHamburgerMenu();
        }

        private void BtnMenuChangePath_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            BtnBrowseGame_Click(sender, e);
        }

        private async void BtnMenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            await CheckAndPromptServerProxyUpdate();
        }

        private void BtnOpenVoiceFolder_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            string gameDir = !string.IsNullOrEmpty(gameExecutablePath) ? Path.GetDirectoryName(gameExecutablePath) ?? "" : "";
            if (string.IsNullOrEmpty(gameDir)) { ShowInfo("Error", "Set your game path first."); return; }
            string voicePath = Path.Combine(gameDir, "StarRail_Data", "Persistent", "Audio", "AudioPackage", "Windows");
            if (!Directory.Exists(voicePath)) voicePath = Path.Combine(gameDir, "StarRail_Data", "Persistent", "Audio");
            if (!Directory.Exists(voicePath)) { ShowInfo("Not Found", "Voice folder not found. Make sure the game path is correct."); return; }
            Process.Start("explorer.exe", voicePath);
        }

        private async Task CheckAndPromptServerProxyUpdate()
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            
            GridStatus.Visibility = Visibility.Visible;
            TxtGlobalStatus.Text = "CHECKING FOR UPDATES...";
            ProgGlobal.IsIndeterminate = true;

            try
            {
                var psTask = updateService.CheckForUpdateAsync(serverVersion);
                var proxyTask = updateService.CheckProxyUpdateAsync(proxyVersion);
                await Task.WhenAll(psTask, proxyTask);
                
                var (psAvail, psVer, psUrl) = psTask.Result;
            var (proxyAvail, proxyVer, proxyUrl) = proxyTask.Result;

            if (!psAvail && !proxyAvail)
            {
                if (!string.IsNullOrEmpty(updateService.LastErrorMessage))
                {
                    ShowInfo("UPDATE FAILED", $"Could not check for updates. Reason:\n{updateService.LastErrorMessage}");
                }
                else
                {
                    ShowInfo("Up to Date", "Server and Proxy are already up to date.");
                }
                return;
            }

                ShowPrompt("Update Data", "Do you want to update data server and proxy?",
                    async () => {
                        if (psAvail) await PerformPSUpdate(launcherDir, psVer, psUrl);
                        if (proxyAvail) await PerformProxyUpdate(launcherDir, proxyVer, proxyUrl);
                    },
                    "Yes", "No");
            }
            finally
            {
                GridStatus.Visibility = Visibility.Collapsed;
                ProgGlobal.IsIndeterminate = false;
                ProgGlobal.Value = 0;
            }
        }

        // ── Video Background ─────────────────────────────────────────────────────
        private void StartBackgroundVideo()
        {
            try
            {
                string videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "SilverWolfBG.mp4");
                if (File.Exists(videoPath))
                {
                    BgVideo.Source = new Uri(videoPath, UriKind.Absolute);
                    BgVideo.Play();
                }
            }
            catch { /* fallback to static image */ }
        }

        private void BgVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Video loaded successfully — hide the static fallback image
            BgFallbackImage.Visibility = Visibility.Collapsed;
        }

        private void BgVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the video seamlessly
            BgVideo.Position = TimeSpan.Zero;
            BgVideo.Play();
        }

        private void BgVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // Video failed — keep static fallback image visible
            BgFallbackImage.Visibility = Visibility.Visible;
            BgVideo.Visibility = Visibility.Collapsed;
        }

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
                    if (root.TryGetProperty("minimizeToTray", out var mt))  { 
                        isInitializingUI = true;
                        minimizeToTrayEnabled = mt.GetBoolean(); 
                        ChkMinimizeToTray.IsChecked = minimizeToTrayEnabled; 
                        isInitializingUI = false;
                    }
                    if (root.TryGetProperty("dontAskOnClose", out var da))  { dontAskOnClose        = da.GetBoolean(); }
                    if (root.TryGetProperty("autoServices",   out var ast)) { ChkAutoServices.IsChecked    = ast.GetBoolean(); }
                    if (root.TryGetProperty("serverVersion",  out var sv))  { serverVersion = sv.GetString() ?? "0.0.0"; }
                    if (root.TryGetProperty("proxyVersion",   out var pv))  { proxyVersion = pv.GetString() ?? "0.0.0"; }
                }
                catch { }
            }
            else if (File.Exists(legacyPath))
            {
                gameExecutablePath = File.ReadAllText(legacyPath).Trim();
                TxtGamePath.Text = gameExecutablePath;
            }

            // Cleanup legacy .txt version files
            try { File.Delete(legacyPath); } catch {}
            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server", "ps_version.txt")); } catch {}
            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proxy", "proxy_version.txt")); } catch {}

            UpdatePlayButton();
        }

        private void SaveConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            var obj = new {
                gamePath       = gameExecutablePath,
                minimizeToTray = minimizeToTrayEnabled,
                dontAskOnClose = dontAskOnClose,
                autoServices   = ChkAutoServices.IsChecked ?? true,
                serverVersion  = serverVersion,
                proxyVersion   = proxyVersion
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
            PanelSettings.Visibility = Visibility.Hidden;
            PanelInfo.Visibility = Visibility.Hidden;
            PanelProxy.Visibility = Visibility.Hidden;
            
            BtnHome.Style = (Style)Resources["ModernNavButton"];
            BtnPatcher.Style = (Style)Resources["ModernNavButton"];
            BtnLanguage.Style = (Style)Resources["ModernNavButton"];
            BtnSRTools.Style = (Style)Resources["ModernNavButton"];
            BtnSettings.Style = (Style)Resources["ModernNavButton"];
            BtnInfo.Style = (Style)Resources["ModernNavButton"];
            BtnProxy.Style = (Style)Resources["ModernNavButton"];
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelHome); BtnHome.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnProxy_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelProxy); BtnProxy.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnPatcher_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelPatcher); BtnPatcher.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnLanguage_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelLanguage); BtnLanguage.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnSRTools_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelSRTools); BtnSRTools.Style = (Style)Resources["ActiveNavButton"]; }
        private void BtnSettings_Click(object sender, RoutedEventArgs e) 
        { 
            if (PanelSettings.Visibility == Visibility.Visible)
            {
                SwitchPanel(PanelHome); 
                BtnHome.Style = (Style)Resources["ActiveNavButton"];
            }
            else
            {
                SwitchPanel(PanelSettings); 
                BtnSettings.Style = (Style)Resources["ActiveNavButton"];
            }
        }
        private void BtnInfo_Click(object sender, RoutedEventArgs e) { SwitchPanel(PanelInfo); BtnInfo.Style = (Style)Resources["ActiveNavButton"]; }

        private void SwitchPanel(UIElement panel)
        {
            HideAllPanels();
            panel.Visibility = Visibility.Visible;
            AnimatePanelIn(panel);

            // Dynamic Background Dim (Exclude Home)
            double targetOpacity = (panel == PanelHome) ? 0 : 0.8;
            var dimAnim = new System.Windows.Media.Animation.DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(300));
            dimAnim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            BgDimOverlay.BeginAnimation(UIElement.OpacityProperty, dimAnim);
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

        private void UpdatePlayButton()
        {
            bool canPlay = !string.IsNullOrEmpty(gameExecutablePath) && File.Exists(gameExecutablePath);
            if (BtnLaunchGame != null) 
            {
                BtnLaunchGame.IsEnabled = canPlay;
                BtnLaunchGame.Opacity = canPlay ? 1.0 : 0.5;
            }
            if (TxtPlayLabel != null) 
            {
                TxtPlayLabel.Opacity = canPlay ? 1.0 : 0.5;
            }
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
                UpdatePlayButton();
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

        // Removed duplicated BtnUpdatePS_Click

        private void ChkMinimizeToTray_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitializingUI) return;
            minimizeToTrayEnabled = true;
            SaveConfig();
        }

        private void ChkMinimizeToTray_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isInitializingUI) return;
            minimizeToTrayEnabled = false;
            dontAskOnClose = false; // Reset if they disable it
            SaveConfig();
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
                var info = await updateService.CheckForUpdateAsync(serverVersion);
                url = info.DownloadUrl; version = info.LatestVersion;
                if (string.IsNullOrEmpty(url)) {
                    if (string.IsNullOrEmpty(updateService.LastErrorMessage))
                        ShowInfo("UP TO DATE", "Private Server is already up to date.");
                    else
                        ShowInfo("UPDATE ERROR", updateService.LastErrorMessage);
                    return;
                }
            }
            GridStatus.Visibility = Visibility.Visible;
            try {
                bool success = await updateService.DownloadAndInstallAsync(launcherDir, url, msg => {
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
                if (success) {
                    serverVersion = version;
                    SaveConfig();
                    LoadVersionInfo();
                    ShowInfo("INSTALLED", "Private Server installed to ./server/ successfully!");
                }
                else ShowInfo("ERROR", updateService.LastErrorMessage);
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
            var originalContent = BtnUpdateProxy.Content;
            BtnUpdateProxy.Content = "CHECKING...";
            
            try
            {
                var (avail, ver, url) = await updateService.CheckProxyUpdateAsync(proxyVersion);
                
                if (avail) {
                    await PerformProxyUpdate(launcherDir, ver, url);
                } else {
                    ShowInfo("UP TO DATE", "Proxy is already the latest version.");
                }
            }
            finally
            {
                BtnUpdateProxy.Content = originalContent;
                BtnUpdateProxy.IsEnabled = true;
            }
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
                var info = await updateService.CheckProxyUpdateAsync(proxyVersion);
                url = info.DownloadUrl; version = info.LatestVersion;
                if (string.IsNullOrEmpty(url)) {
                    if (string.IsNullOrEmpty(updateService.LastErrorMessage))
                        ShowInfo("UP TO DATE", "Proxy is already up to date.");
                    else
                        ShowInfo("UPDATE ERROR", updateService.LastErrorMessage);
                    return;
                }
            }
            GridStatus.Visibility = Visibility.Visible;
            try {
                bool success = await updateService.DownloadAndInstallProxyAsync(launcherDir, url, msg => {
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
                if (success) {
                    proxyVersion = version;
                    SaveConfig();
                    LoadVersionInfo();
                    ShowInfo("INSTALLED", "Proxy installed to ./proxy/ successfully!");
                }
                else ShowInfo("ERROR", updateService.LastErrorMessage);
            } finally {
                GridStatus.Visibility = Visibility.Collapsed;
                ProgGlobal.IsIndeterminate = false;
                ProgGlobal.Value = 0;
            }
        }

        private async void BtnInstallPSFromFile_Click(object sender, RoutedEventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            OpenFileDialog dialog = new OpenFileDialog { Filter = "ZIP Files (*.zip)|*.zip|7z Files (*.7z)|*.7z|All Files (*.*)|*.*" };
            
            if (dialog.ShowDialog() == true)
            {
                GridStatus.Visibility = Visibility.Visible;
                try
                {
                    string serverDir = Path.Combine(launcherDir, "server");
                    Directory.CreateDirectory(serverDir);
                    
                    TxtGlobalStatus.Text = "Installing from file...";
                    ProgGlobal.IsIndeterminate = true;
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            using var archive = ArchiveFactory.Open(dialog.FileName);
                            foreach (var entry in archive.Entries)
                                if (!entry.IsDirectory)
                                    entry.WriteToDirectory(serverDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => ShowInfo("ERROR", $"Failed to extract: {ex.Message}"));
                        }
                    });
                    
                    // Save newly installed version to global config properly
                    serverVersion = "1.0.0";
                    SaveConfig();
                    LoadVersionInfo();
                    ShowInfo("SUCCESS", "Private Server installed successfully!");
                }
                catch (Exception ex)
                {
                    ShowInfo("ERROR", $"Installation failed: {ex.Message}");
                }
                finally
                {
                    GridStatus.Visibility = Visibility.Collapsed;
                    ProgGlobal.IsIndeterminate = false;
                    ProgGlobal.Value = 0;
                }
            }
        }

        private async void BtnInstallProxyFromFile_Click(object sender, RoutedEventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            OpenFileDialog dialog = new OpenFileDialog { Filter = "EXE Files (*.exe)|*.exe|All Files (*.*)|*.*" };
            
            if (dialog.ShowDialog() == true)
            {
                GridStatus.Visibility = Visibility.Visible;
                try
                {
                    string proxyDir = Path.Combine(launcherDir, "proxy");
                    Directory.CreateDirectory(proxyDir);
                    string destFile = Path.Combine(proxyDir, "firefly-go-proxy.exe");
                    
                    TxtGlobalStatus.Text = "Installing from file...";
                    ProgGlobal.IsIndeterminate = true;
                    
                    File.Copy(dialog.FileName, destFile, overwrite: true);
                    
                    // Save newly installed version to global config properly
                    proxyVersion = "1.0.0";
                    SaveConfig();
                    LoadVersionInfo();
                    ShowInfo("SUCCESS", "Proxy installed successfully!");
                }
                catch (Exception ex)
                {
                    ShowInfo("ERROR", $"Installation failed: {ex.Message}");
                }
                finally
                {
                    GridStatus.Visibility = Visibility.Collapsed;
                    ProgGlobal.IsIndeterminate = false;
                    ProgGlobal.Value = 0;
                }
            }
        }
    }

    public class HDiffData {
        [JsonPropertyName("source_file_name")] public string SourceFileName { get; set; } = "";
        [JsonPropertyName("target_file_name")] public string TargetFileName { get; set; } = "";
        [JsonPropertyName("patch_file_name")] public string PatchFileName { get; set; } = "";
    }
}