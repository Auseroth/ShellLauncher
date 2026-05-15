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

namespace ShellTaskbar
{
    public class AppConfig
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        public bool ExcludeFromTaskbar { get; set; } = false;
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

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        const int SW_RESTORE  = 9;
        const int SW_MINIMIZE = 6;
        const int SW_MAXIMIZE = 3;
        const int SM_CXSCREEN = 0;
        const int SM_CYSCREEN = 1;
        const uint WM_CLOSE        = 0x0010;
        const uint SPI_SETWORKAREA = 0x002F;
        const uint SPIF_SENDCHANGE = 0x0002;

        private readonly DispatcherTimer _clockTimer;
        private FileSystemWatcher? _configWatcher;
        private DispatcherTimer? _reloadDebounce;

        public TaskbarWindow()
        {
            InitializeComponent();
            Loaded          += OnLoaded;
            ContentRendered += OnContentRendered;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("hh:mm:ss tt");
            _clockTimer.Start();

            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadApps();
            StartConfigWatcher();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnContentRendered;
            PositionTaskbar();
            Opacity = 1;
        }

        // Fires on resolution, orientation, or DPI change
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            // Delay lets the display driver fully apply the new mode before we measure
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                UpdateLayout(); // force WPF to remeasure at the new resolution
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

            // Changed fires multiple times per save — debounce so we only reload once
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
                                !a.ExcludeFromTaskbar &&
                                !System.IO.Path.GetFileNameWithoutExtension(a.Path)
                                    .Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch
            {
                // Swallow silently — a MessageBox here freezes the taskbar UI thread
            }
        }

        private static IntPtr GetAppWindow(AppConfig app)
        {
            string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Path);
            return Process.GetProcessesByName(exeName)
                          .Where(p => p.MainWindowHandle != IntPtr.Zero)
                          .Select(p => p.MainWindowHandle)
                          .FirstOrDefault();
        }

        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AppConfig app) return;

            IntPtr hWnd = GetAppWindow(app);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = app.Path,
                        Arguments = app.Args,
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

        //  App context menu

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
            {
                btn.ContextMenu.IsOpen = true;
            }
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
            int screenWidthPx  = GetSystemMetrics(SM_CXSCREEN);
            int screenHeightPx = GetSystemMetrics(SM_CYSCREEN);

            var workArea = new RECT
            {
                Left = 0, Top = 0,
                Right = screenWidthPx, Bottom = screenHeightPx
            };
            SystemParametersInfo(SPI_SETWORKAREA, 0, ref workArea, SPIF_SENDCHANGE);
        }
    }
}