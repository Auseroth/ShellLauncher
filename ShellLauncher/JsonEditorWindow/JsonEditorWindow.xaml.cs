using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ShellLauncher
{
    public partial class JsonEditorWindow : Window
    {
        public ObservableCollection<AppConfig> AppConfigs { get; set; }

        private readonly string _filePath;

        public JsonEditorWindow(string filePath, List<AppConfig> defaultConfigs, string? iconPath = null)
        {
            InitializeComponent();
            _filePath = filePath;
            AppConfigs = new ObservableCollection<AppConfig>(defaultConfigs);
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

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string json = JsonSerializer.Serialize(AppConfigs, new JsonSerializerOptions { WriteIndented = true });
                string? dir = System.IO.Path.GetDirectoryName(_filePath);

                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_filePath))
                    File.Delete(_filePath);

                System.Threading.Thread.Sleep(1000);

                File.WriteAllText(_filePath, json, System.Text.Encoding.UTF8);

                MessageBox.Show("Configuration saved successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n\n{ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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