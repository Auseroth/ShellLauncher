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
            {
                Icon = new BitmapImage(new System.Uri(iconPath, System.UriKind.Absolute));
            }
        }

        private void AddApplication_Click(object sender, RoutedEventArgs e)
        {
            AppConfigs.Add(new AppConfig { Name = "", Path = "", Args = "" });
        }

        private void RemoveApplication_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AppConfig app)
            {
                AppConfigs.Remove(app);
            }
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            string jsonContent = JsonSerializer.Serialize(AppConfigs, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, jsonContent);
            MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}