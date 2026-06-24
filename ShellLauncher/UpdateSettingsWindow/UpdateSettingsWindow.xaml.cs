using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ShellLauncher
{
    public partial class UpdateSettingsWindow : Window
    {
        private readonly UpdateService _service;
        private readonly UpdateConfig  _config;
        private CancellationTokenSource? _cts;

        public UpdateSettingsWindow(UpdateService service, UpdateConfig config, string? iconPath = null)
        {
            InitializeComponent();
            _service = service;
            _config  = config;

            var version     = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            LblVersion.Text = version.ToString();
            TxtUrl.Text     = config.GithubReleasesUrl;

            if (iconPath != null && System.IO.File.Exists(iconPath))
                Icon = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(iconPath, UriKind.Absolute));
        }

        private async void CheckNow_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            SetBusy(true);
            SetStatus("Checking for updates...", Brushes.Gray);

            try
            {
                // Apply the URL from the box before checking so the user can test changes live
                _config.GithubReleasesUrl = TxtUrl.Text.Trim();
                _service.Configure(_config);

                var result = await _service.CheckNowAsync(_cts.Token);

                if (result == null)
                {
                    SetStatus("Could not reach the update server. Check the URL and your internet connection.",
                        Brushes.OrangeRed);
                    return;
                }

                if (result.UpdateAvailable)
                {
                    SetStatus($"✔  Update available: {result.TagName}", Brushes.Green);
                    PromptDownload(result);
                }
                else
                {
                    SetStatus($"✔  You are up to date.  (latest: {result.TagName})", Brushes.DimGray);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", Brushes.OrangeRed);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PromptDownload(UpdateCheckResult result)
        {
            if (string.IsNullOrWhiteSpace(result.DownloadUrl))
            {
                MessageBox.Show(
                    $"Version {result.TagName} is available.\n\n" +
                    "No installer asset was found attached to this release.\n" +
                    "Please download it manually from GitHub.",
                    "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                $"Version {result.TagName} is available.\n\n" +
                "Download and install now?\n\n" +
                "ShellLauncher will close once the installer launches.",
                "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
                _ = DoDownloadAsync(result);
        }

        private async Task DoDownloadAsync(UpdateCheckResult result)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            SetStatus("Downloading...", Brushes.Gray);

            try
            {
                var progress = new Progress<double>(p =>
                    SetStatus($"Downloading...  {p:P0}", Brushes.Gray));

                bool ok = await _service.DownloadAndLaunchAsync(result, progress, _cts.Token);

                if (ok)
                {
                    SetStatus("Installer launched. Closing ShellLauncher...", Brushes.DimGray);
                    await Task.Delay(1500);
                    Environment.Exit(0);
                }
                else
                {
                    SetStatus("Download failed. Please try again or download manually from GitHub.",
                        Brushes.OrangeRed);
                    SetBusy(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus($"Download error: {ex.Message}", Brushes.OrangeRed);
                SetBusy(false);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.GithubReleasesUrl = TxtUrl.Text.Trim();
            _config.Save();
            _service.Configure(_config);
            SetStatus("URL saved.", Brushes.DimGray);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void SetBusy(bool busy)
        {
            BtnCheck.IsEnabled = !busy;
            BtnSave.IsEnabled  = !busy;
        }

        private void SetStatus(string text, Brush brush)
        {
            LblStatus.Text       = text;
            LblStatus.Foreground = brush;
        }
    }
}