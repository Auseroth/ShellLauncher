using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ShellLauncher
{
    public partial class JsonEditorWindow : Window
    {
        public ObservableCollection<AppConfig> AppConfigs { get; set; }
        public bool LaunchAfterSave { get; private set; } = false;
        public bool ShutdownAfterKill { get; private set; } = false;

        private readonly string _filePath;
        private readonly Dictionary<string, int>? _launchedPids;
        private readonly Action? _onKillComplete;

        public JsonEditorWindow(
            string filePath,
            List<AppConfig> defaultConfigs,
            string? iconPath = null,
            Dictionary<string, int>? launchedPids = null,
            Action? onKillComplete = null)
        {
            InitializeComponent();
            _filePath       = filePath;
            _launchedPids   = launchedPids;
            _onKillComplete = onKillComplete;
            AppConfigs      = new ObservableCollection<AppConfig>(defaultConfigs);
            AppList.ItemsSource = AppConfigs;

            if (iconPath != null && File.Exists(iconPath))
                Icon = new BitmapImage(new System.Uri(iconPath, System.UriKind.Absolute));
        }

        private void AddApplication_Click(object sender, RoutedEventArgs e)
        {
            AppConfigs.Add(new AppConfig { Name = "", Path = "", Args = "" });
        }

        private void RemoveApplication_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AppConfig app)
                AppConfigs.Remove(app);
        }

        private bool TrySave()
        {
            try
            {
                string json = JsonSerializer.Serialize(AppConfigs, new JsonSerializerOptions { WriteIndented = true });

                string? dir = System.IO.Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_filePath))
                    File.Delete(_filePath);

                File.WriteAllText(_filePath, json, System.Text.Encoding.UTF8);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n\n{ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySave()) return;
            MessageBox.Show("Configuration saved successfully!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
            LaunchAfterSave = false;
            Close();
        }

        private void SaveAndLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySave()) return;
            MessageBox.Show("Configuration saved. ShellLauncher will now start monitoring.", "Saved — Launching",
                MessageBoxButton.OK, MessageBoxImage.Information);
            LaunchAfterSave = true;
            Close();
        }

        private void CloseAllApps_Click(object sender, RoutedEventArgs e)
        {
            if (_launchedPids == null || _launchedPids.Count == 0)
            {
                MessageBox.Show("No tracked applications to close.", "Close All Apps",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"This will close all {_launchedPids.Count} monitored application(s), ShellTaskbar, and ShellLauncher itself.\n\nContinue?",
                "Close All Apps",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            int killed = 0;
            foreach (var kvp in _launchedPids)
            {
                try
                {
                    var proc = Process.GetProcessById(kvp.Value);
                    if (!proc.HasExited) { proc.Kill(); killed++; }
                }
                catch { /* process already gone */ }
            }

            foreach (var p in Process.GetProcessesByName("ShellTaskbar"))
            {
                try { p.Kill(); killed++; } catch { }
            }

            _launchedPids.Clear();
            _onKillComplete?.Invoke();

            ShutdownAfterKill = true;
            Close();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "SHELLLAUNCHER — CONFIGURATION HELP\n" +
                "????????????????????????????????????\n\n" +

                "COLUMNS\n" +
                "???????\n" +
                "Name            Display name shown in logs and the taskbar button.\n" +
                "Path            Full path or filename of the executable.\n" +
                "                  Examples: notepad.exe\n" +
                "                            C:\\Program Files\\App\\App.exe\n" +
                "Arguments       Command-line args passed to the executable.\n" +
                "                  Leave blank if none are needed.\n" +
                "Hide from Taskbar  When checked, the app runs silently in the\n" +
                "                   background with no taskbar button.\n" +
                "Run Once        When checked, the app launches once at startup\n" +
                "                and is never restarted if it exits.\n" +
                "                  Note: Has no effect if Depends On is set —\n" +
                "                  the app always fires once per parent session.\n" +
                "Depends On      Enter the exact Name of another app in this list.\n" +
                "                The app will only launch when that parent app is\n" +
                "                running (has a visible window), and will re-launch\n" +
                "                once each time the parent restarts.\n" +
                "                  Leave blank for no dependency.\n\n" +

                "HOTKEYS\n" +
                "???????\n" +
                "Ctrl+Shift+Alt+S   Show / hide the console status window.\n" +
                "Ctrl+Shift+Alt+C   Open this config editor at any time.\n\n" +

                "DIRECT JSON EDITING  (for automated deployment)\n" +
                "????????????????????????????????????????????????\n" +
                "File location:  C:\\ProgramData\\ShellLauncher\\config.json\n\n" +
                "[\n" +
                "  {\n" +
                "    \"Name\": \"Edge\",\n" +
                "    \"Path\": \"C:\\\\Program Files (x86)\\\\Microsoft\\\\Edge\\\\Application\\\\msedge.exe\",\n" +
                "    \"Args\": \"--no-first-run --start-maximized --app=https://example.com\",\n" +
                "    \"ExcludeFromTaskbar\": false,\n" +
                "    \"RunOnce\": false,\n" +
                "    \"DependsOn\": null\n" +
                "  },\n" +
                "  {\n" +
                "    \"Name\": \"LoginScript\",\n" +
                "    \"Path\": \"Powershell.exe\",\n" +
                "    \"Args\": \"-WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"C:\\\\Scripts\\\\login.ps1\\\"\",\n" +
                "    \"ExcludeFromTaskbar\": true,\n" +
                "    \"RunOnce\": false,\n" +
                "    \"DependsOn\": \"Edge\"\n" +
                "  }\n" +
                "]\n\n" +
                "To re-open this editor after first run:\n" +
                "  Press Ctrl+Shift+Alt+C, or delete config.json and restart ShellLauncher.",
                "ShellLauncher Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}