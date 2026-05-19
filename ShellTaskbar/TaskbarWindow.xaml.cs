using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Interop;

namespace ShellTaskbar
{
    public class AppConfig : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name    { get; set; } = "";
        public string Path    { get; set; } = "";
        public string Args    { get; set; } = "";
        public bool ExcludeFromTaskbar { get; set; } = false;
        public string LaunchMode { get; set; } = "Monitor"; // Monitor | RunOnce | NoLaunch

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRunning))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class TaskbarWindow : Window
    {
        private const string ConfigPath = @"C:\ProgramData\ShellLauncher\config.json";

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        const int SW_RESTORE       = 9;
        const int SW_MINIMIZE      = 6;
        const int SW_MAXIMIZE      = 3;
        const int SM_CXSCREEN      = 0;
        const int SM_CYSCREEN      = 1;
        const uint WM_CLOSE        = 0x0010;
        const uint SPI_SETWORKAREA = 0x002F;
        const uint SPIF_SENDCHANGE = 0x0002;

        private readonly DispatcherTimer _clockTimer;
        private FileSystemWatcher? _configWatcher;
        private DispatcherTimer? _reloadDebounce;
        private bool _isMuted    = false;
        private float _volumeLevel = 1.0f;

        private IAudioEndpointVolume? _audioEndpoint;

        // Transient process tracking (settings-launched windows)
        private readonly System.Collections.ObjectModel.ObservableCollection<TransientApp> _transientApps = new();

        private class TransientApp : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public string Name  { get; set; } = "";
            public Process? Proc { get; set; }

            private bool _isRunning;
            public bool IsRunning
            {
                get => _isRunning;
                set { _isRunning = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRunning))); }
            }
        }

  
        private readonly AppConfig _editorEntry  = new() { Name = "Config Editor", Path = "" };

        public TaskbarWindow()
        {
            InitializeComponent();
            Loaded          += OnLoaded;
            ContentRendered += OnContentRendered;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) =>
            {
                var now = DateTime.Now;
                ClockText.Text = now.ToString("hh:mm:ss tt");
                DateText.Text  = now.ToString("ddd MM/dd");
                PollVolumeState();
                PollAppState();
                PollTransientApps();
            };
            _clockTimer.Start();

            Closed += OnClosed;
        }

        private void PollVolumeState()
        {
            var ep = GetOrCreateAudioEndpoint();
            if (ep == null) return;

            try
            {
                ep.GetMute(out bool muted);
                ep.GetMasterVolumeLevelScalar(out float level);

                if (muted != _isMuted || Math.Abs(level - _volumeLevel) > 0.005f)
                {
                    _isMuted     = muted;
                    _volumeLevel = level;
                    RenderVolumeDisplay();
                }
            }
            catch
            {
                // Device changed — force recreate next poll
                _audioEndpoint = null;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadApps();
            StartConfigWatcher();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            UpdateVolumeDisplay();
            TransientPanel.ItemsSource = _transientApps;
            SystemPanel.ItemsSource    = new[] { _editorEntry };
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnContentRendered;
            try
            {
                PositionTaskbar();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    @"C:\ProgramData\ShellLauncher\taskbar_crash.log",
                    $"{DateTime.Now:HH:mm:ss} - PositionTaskbar failed: {ex}\n\n");
            }
            finally
            {
                Opacity = 1;
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                UpdateLayout();
                PositionTaskbar();
            });
        }

        private void StartConfigWatcher()
        {
            string? dir = System.IO.Path.GetDirectoryName(ConfigPath);
            if (dir == null || !Directory.Exists(dir)) return;

            _configWatcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(ConfigPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _configWatcher.Changed += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _reloadDebounce?.Stop();
                    _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); LoadApps(); };
                    _reloadDebounce.Start();
                });
            };
        }

        private void PositionTaskbar()
        {
            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int screenWidthPx  = GetSystemMetrics(SM_CXSCREEN);
            int screenHeightPx = GetSystemMetrics(SM_CYSCREEN);

            Width = screenWidthPx  / dpiScaleX;
            Left  = 0;
            Top   = screenHeightPx / dpiScaleY - ActualHeight;

            int taskbarHeightPx = (int)Math.Ceiling(ActualHeight * dpiScaleY);

            var workArea = new RECT
            {
                Left   = 0,
                Top    = 0,
                Right  = screenWidthPx,
                Bottom = screenHeightPx - taskbarHeightPx
            };
            SystemParametersInfo(SPI_SETWORKAREA, 0, ref workArea, SPIF_SENDCHANGE);
        }

        private void LoadApps()
        {
            if (!File.Exists(ConfigPath)) return;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var apps    = JsonSerializer.Deserialize<List<AppConfig>>(File.ReadAllText(ConfigPath), options);
                string selfName = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");

                AppButtonsPanel.ItemsSource = apps?
                    .Where(a => !string.IsNullOrWhiteSpace(a.Path) &&
                                !System.IO.Path.GetFileNameWithoutExtension(a.Path)
                                    .Equals(selfName, StringComparison.OrdinalIgnoreCase) &&
                                // Show if: not excluded, OR it's NoLaunch (always needs taskbar button)
                                (!a.ExcludeFromTaskbar || a.LaunchMode == "NoLaunch"))
                    .ToList();
            }
            catch { }
        }

        private static IntPtr GetAppWindow(AppConfig app)
        {
            if (string.IsNullOrWhiteSpace(app.Path)) return IntPtr.Zero;
            try
            {
                string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Path);
                return Process.GetProcessesByName(exeName)
                              .Where(p => p.MainWindowHandle != IntPtr.Zero)
                              .Select(p => p.MainWindowHandle)
                              .FirstOrDefault();
            }
            catch { return IntPtr.Zero; }
        }

        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AppConfig app) return;

            IntPtr hWnd = GetAppWindow(app);
            if (hWnd != IntPtr.Zero)
            {
                var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                GetWindowPlacement(hWnd, ref wp);

                if (wp.showCmd == SW_SHOWMINIMIZED)
                {
                    // Minimized — restore it
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }
                else if (wp.showCmd == SW_SHOWMAXIMIZED)
                {
                    // Already maximized — just focus it
                    SetForegroundWindow(hWnd);
                }
                else
                {
                    // Normal/restored — maximize it
                    ShowWindow(hWnd, SW_MAXIMIZE);
                    SetForegroundWindow(hWnd);
                }
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = app.Path,
                        Arguments       = app.Args,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch {app.Name}: {ex.Message}", "ShellTaskbar",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // App context menu

        private void ContextRestore_Click(object sender, RoutedEventArgs e)
        {
            if (GetTagApp(sender) is AppConfig app && GetAppWindow(app) is IntPtr h && h != IntPtr.Zero)
            { ShowWindow(h, SW_RESTORE); SetForegroundWindow(h); }
        }

        private void ContextMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (GetTagApp(sender) is AppConfig app && GetAppWindow(app) is IntPtr h && h != IntPtr.Zero)
                ShowWindow(h, SW_MINIMIZE);
        }

        private void ContextMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (GetTagApp(sender) is AppConfig app && GetAppWindow(app) is IntPtr h && h != IntPtr.Zero)
            { ShowWindow(h, SW_MAXIMIZE); SetForegroundWindow(h); }
        }

        private void ContextClose_Click(object sender, RoutedEventArgs e)
        {
            if (GetTagApp(sender) is AppConfig app && GetAppWindow(app) is IntPtr h && h != IntPtr.Zero)
                PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private static AppConfig? GetTagApp(object sender) =>
            (sender as MenuItem)?.Tag as AppConfig;

        // Power button

        private void PowerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.ContextMenu.IsOpen = true;
        }

        private void PowerShutdown_Click(object sender, RoutedEventArgs e)
        {
            if (Confirm("Shut down this computer?"))
                Process.Start("shutdown", "/s /t 0");
        }

        private void PowerRestart_Click(object sender, RoutedEventArgs e)
        {
            if (Confirm("Restart this computer?"))
                Process.Start("shutdown", "/r /t 0");
        }

        private static bool Confirm(string message) =>
            MessageBox.Show(message, "Power Options",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

        // Cleanup

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            int screenWidthPx  = GetSystemMetrics(SM_CXSCREEN);
            int screenHeightPx = GetSystemMetrics(SM_CYSCREEN);

            var workArea = new RECT { Left = 0, Top = 0, Right = screenWidthPx, Bottom = screenHeightPx };
            SystemParametersInfo(SPI_SETWORKAREA, 0, ref workArea, SPIF_SENDCHANGE);
        }

        // --- Global low-level keyboard hook ---

        private const int  WH_KEYBOARD_LL  = 13;
        private const int  WM_KEYDOWN      = 0x0100;
        private const byte VK_VOLUME_MUTE  = 0xAD;
        private const byte VK_VOLUME_DOWN  = 0xAE;
        private const byte VK_VOLUME_UP    = 0xAF;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc? _keyboardProc;
        private IntPtr _keyboardHook = IntPtr.Zero;

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            _keyboardProc = KeyboardHookCallback;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool isInjected = (kb.flags & 0x10) != 0;

                if (!isInjected)
                {
                    switch (kb.vkCode)
                    {
                        case VK_VOLUME_UP:
                            AdjustVolume(+1);
                            _volumeLevel = Math.Clamp(_volumeLevel + 0.02f, 0f, 1f);
                            Dispatcher.BeginInvoke(RenderVolumeDisplay);
                            return (IntPtr)1;
                        case VK_VOLUME_DOWN:
                            AdjustVolume(-1);
                            _volumeLevel = Math.Clamp(_volumeLevel - 0.02f, 0f, 1f);
                            Dispatcher.BeginInvoke(RenderVolumeDisplay);
                            return (IntPtr)1;
                        case VK_VOLUME_MUTE:
                            ToggleMute();
                            _isMuted = !_isMuted;
                            Dispatcher.BeginInvoke(RenderVolumeDisplay);
                            return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private const int WM_APPCOMMAND          = 0x0319;
        private const int APPCOMMAND_VOLUME_MUTE = 8;
        private const int APPCOMMAND_VOLUME_DOWN = 9;
        private const int APPCOMMAND_VOLUME_UP   = 10;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_APPCOMMAND)
            {
                int cmd = (lParam.ToInt32() >> 16) & 0xFFF;
                switch (cmd)
                {
                    case APPCOMMAND_VOLUME_UP:
                        AdjustVolume(+1);
                        _volumeLevel = Math.Clamp(_volumeLevel + 0.02f, 0f, 1f);
                        RenderVolumeDisplay();
                        handled = true;
                        return (IntPtr)1;
                    case APPCOMMAND_VOLUME_DOWN:
                        AdjustVolume(-1);
                        _volumeLevel = Math.Clamp(_volumeLevel - 0.02f, 0f, 1f);
                        RenderVolumeDisplay();
                        handled = true;
                        return (IntPtr)1;
                    case APPCOMMAND_VOLUME_MUTE:
                        ToggleMute();
                        _isMuted = !_isMuted;
                        RenderVolumeDisplay();
                        handled = true;
                        return (IntPtr)1;
                }
            }
            return IntPtr.Zero;
        }

        // Volume button click handlers

        private void VolumeDown_Click(object sender, RoutedEventArgs e)
        {
            AdjustVolume(-2);
            _volumeLevel = Math.Clamp(_volumeLevel - 0.04f, 0f, 1f);
            RefreshVolumeFromCom();
        }

        private void VolumeUp_Click(object sender, RoutedEventArgs e)
        {
            AdjustVolume(+2);
            _volumeLevel = Math.Clamp(_volumeLevel + 0.04f, 0f, 1f);
            RefreshVolumeFromCom();
        }

        private void VolumeMute_Click(object sender, RoutedEventArgs e)
        {
            ToggleMute();
            _isMuted = !_isMuted;
            RenderVolumeDisplay();
        }

        private void RefreshVolumeFromCom()
        {
            var ep = GetAudioEndpoint();
            if (ep != null)
            {
                ep.GetMute(out bool muted);
                ep.GetMasterVolumeLevelScalar(out float level);
                _isMuted     = muted;
                _volumeLevel = level;
            }
            RenderVolumeDisplay();
        }

        private void RenderVolumeDisplay()
        {
            if (MuteButtonText != null)
                MuteButtonText.Text = _isMuted ? "\uE74F" : "\uE995";
            if (VolumeText != null)
                VolumeText.Text = _isMuted ? "mute" : $"{(int)Math.Round(_volumeLevel * 100)}";
        }

        private void UpdateVolumeDisplay()
        {
            var ep = GetAudioEndpoint();
            if (ep != null)
            {
                ep.GetMute(out bool muted);
                ep.GetMasterVolumeLevelScalar(out float level);
                _isMuted     = muted;
                _volumeLevel = level;
            }
            RenderVolumeDisplay();
        }

        private IAudioEndpointVolume? GetOrCreateAudioEndpoint()
        {
            if (_audioEndpoint != null) return _audioEndpoint;
            _audioEndpoint = GetAudioEndpoint();
            return _audioEndpoint;
        }

        // --- Core Audio COM API ---

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int dwStateMask, [MarshalAs(UnmanagedType.Interface)] out object ppDevices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppEndpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            void Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.Interface)] out object ppInterface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr pNotify);
            void UnregisterControlChangeNotify(IntPtr pNotify);
            void GetChannelCount(out uint pnChannelCount);
            void SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            void SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            void GetMasterVolumeLevel(out float pfLevelDB);
            void GetMasterVolumeLevelScalar(out float pfLevel);
            void SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            void SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            void GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            void GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private static IAudioEndpointVolume? GetAudioEndpoint()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 1, IntPtr.Zero, out object vol);
                return vol as IAudioEndpointVolume;
            }
            catch { return null; }
        }

        private static void AdjustVolume(int direction)
        {
            var ep = GetAudioEndpoint();
            if (ep == null) return;
            ep.GetMasterVolumeLevelScalar(out float current);
            var guid = Guid.Empty;
            ep.SetMasterVolumeLevelScalar(Math.Clamp(current + direction * 0.02f, 0f, 1f), ref guid);
        }

        private static void ToggleMute()
        {
            var ep = GetAudioEndpoint();
            if (ep == null) return;
            ep.GetMute(out bool muted);
            var guid = Guid.Empty;
            ep.SetMute(!muted, ref guid);
        }

        // Settings button

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.ContextMenu.IsOpen = true;
        }

        private void PairDevice_Click(object sender, RoutedEventArgs e)
            => TryLaunch("devicepairingwizard.exe", "Pair Device", useShell: true, track: false);

        private void AudioDevices_Click(object sender, RoutedEventArgs e)
            => TryLaunch("mmsys.cpl", "Sound", useShell: true, track: false);

        private void MouseSettings_Click(object sender, RoutedEventArgs e)
            => TryLaunch("main.cpl", "Mouse", useShell: true, track: false);

        private void CursorSize1_Click(object sender, RoutedEventArgs e)  => SetCursorSize(1);
        private void CursorSize3_Click(object sender, RoutedEventArgs e)  => SetCursorSize(3);
        private void CursorSize5_Click(object sender, RoutedEventArgs e)  => SetCursorSize(5);
        private void CursorSize15_Click(object sender, RoutedEventArgs e) => SetCursorSize(15);

        private static void SetCursorSize(int size)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Accessibility", writable: true)
                    ?? Microsoft.Win32.Registry.CurrentUser
                    .CreateSubKey(@"Software\Microsoft\Accessibility");
                key.SetValue("CursorSize", size, Microsoft.Win32.RegistryValueKind.DWord);

                MessageBox.Show("Cursor size updated. Changes take effect after next sign-in.",
                    "Cursor Size", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set cursor size:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PollAppState()
        {
            if (AppButtonsPanel.ItemsSource is not List<AppConfig> apps) return;
            foreach (var app in apps)
            {
                try { app.IsRunning = GetAppWindow(app) != IntPtr.Zero; }
                catch { app.IsRunning = false; }
            }

            _editorEntry.IsRunning  = FindWindow(null, "Edit JSON Configuration") != IntPtr.Zero;
        }

        private void PollTransientApps()
        {
            for (int i = _transientApps.Count - 1; i >= 0; i--)
            {
                var ta = _transientApps[i];
                bool dead = false;
                try
                {
                    dead = ta.Proc == null || ta.Proc.HasExited;
                }
                catch (InvalidOperationException) { dead = true; }
                catch (System.ComponentModel.Win32Exception) { dead = true; }

                if (dead)
                    _transientApps.RemoveAt(i);
            }
        }

        private void TransientButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not TransientApp ta) return;

            try
            {
                if (ta.Proc == null || ta.Proc.HasExited) return;
                var hWnd = ta.Proc.MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private void RestartTaskbar_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = Environment.ProcessPath ?? "ShellTaskbar.exe",
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }

        private void NetworkSettings_Click(object sender, RoutedEventArgs e)
            => TryLaunch("ncpa.cpl", "Network", useShell: true, track: false);

        private void TryLaunch(string target, string displayName, string args = "", bool useShell = false, bool track = true)
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName        = target,
                    Arguments       = args,
                    UseShellExecute = useShell
                });

                if (track && proc != null)
                {
                    var ta = new TransientApp { Name = displayName, Proc = proc, IsRunning = true };
                    _transientApps.Add(ta);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open:\n{ex.Message}", "ShellTaskbar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SystemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AppConfig) return;

            IntPtr hWnd = FindWindow(null, "Edit JSON Configuration");
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
       }

        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWMINIMIZED  = 2;
    }
}