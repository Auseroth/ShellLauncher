using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ShellLauncher
{
    /// <summary>
    /// Facade over <see cref="GitHubUpdater"/>.
    /// Adapted from the UpdateService pattern; takes a log delegate and an <see cref="UpdateConfig"/>.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        private readonly Action<string> _log;
        private GitHubUpdater? _updater;

        public event EventHandler<UpdateCheckResult>? UpdateAvailable;

        public UpdateService(Action<string> log)
        {
            _log = log;
        }

        public void Configure(UpdateConfig config)
        {
            _updater?.Dispose();

            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            _updater = new GitHubUpdater(new GitHubUpdaterOptions
            {
                ReleasesApiUrl         = config.GithubReleasesUrl,
                CurrentVersion         = version,
                DownloadDirectory      = Path.Combine(@"C:\ProgramData\ShellLauncher", "updates"),
                UserAgent              = "ShellLauncher",
                AssetNameFilter        = ".exe",
            });

            _updater.UpdateAvailable += (_, result) => UpdateAvailable?.Invoke(this, result);
        }

        public async Task<UpdateCheckResult?> CheckNowAsync(CancellationToken ct = default)
        {
            if (_updater is null) return null;

            var result = await _updater.CheckForUpdateAsync(ct);
            if (result?.UpdateAvailable == true)
                _log($"Update available: {result.TagName}");

            return result;
        }

        public void StartAutoChecks()
        {
            _updater?.StartAutoChecks();
        }

        public async Task<bool> DownloadAndLaunchAsync(
            UpdateCheckResult result,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (_updater is null) return false;

            var installerPath = await _updater.DownloadInstallerAsync(result, progress, ct);
            if (string.IsNullOrWhiteSpace(installerPath)) return false;

            _updater.LaunchInstaller(installerPath);
            return true;
        }

        public void Dispose()
        {
            _updater?.Dispose();
        }
    }
}