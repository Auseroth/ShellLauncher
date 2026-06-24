using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShellLauncher
{
    public sealed class GitHubUpdaterOptions
    {
        public string  ReleasesApiUrl         { get; set; } = "";
        public Version CurrentVersion         { get; set; } = new Version(1, 0, 0);
        public string  DownloadDirectory      { get; set; } = @"C:\ProgramData\ShellLauncher\updates";
        public string  UserAgent              { get; set; } = "ShellLauncher";
        public string  AssetNameFilter        { get; set; } = ".exe";
        public double  AutoCheckIntervalHours { get; set; } = 24;
    }

    public sealed class GitHubUpdater : IDisposable
    {
        private readonly GitHubUpdaterOptions _opts;
        private readonly HttpClient           _http;
        private Timer? _timer;
        private bool   _disposed;

        public event EventHandler<UpdateCheckResult>? UpdateAvailable;

        public GitHubUpdater(GitHubUpdaterOptions opts)
        {
            _opts = opts;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(_opts.UserAgent, _opts.CurrentVersion.ToString()));
        }

        public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                string json  = await _http.GetStringAsync(_opts.ReleasesApiUrl, ct);
                using var doc = JsonDocument.Parse(json);
                var root      = doc.RootElement;

                string tag   = root.GetProperty("tag_name").GetString() ?? "";
                string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                string? dlUrl = null;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.Contains(_opts.AssetNameFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            dlUrl = asset.TryGetProperty("browser_download_url", out var u)
                                ? u.GetString() : null;
                            break;
                        }
                    }
                }

                string vStr = tag.TrimStart('v');
                if (!Version.TryParse(vStr, out Version? latest))
                    return null;

                bool available = latest > _opts.CurrentVersion;
                var result = new UpdateCheckResult
                {
                    UpdateAvailable = available,
                    TagName         = tag,
                    DownloadUrl     = dlUrl,
                    ReleaseNotes    = notes,
                    LatestVersion   = latest
                };

                if (available)
                    UpdateAvailable?.Invoke(this, result);

                return result;
            }
            catch
            {
                return null;
            }
        }

        public void StartAutoChecks()
        {
            if (_opts.AutoCheckIntervalHours <= 0) return;

            _timer?.Dispose();
            var interval = TimeSpan.FromHours(_opts.AutoCheckIntervalHours);
            _timer = new Timer(async _ =>
            {
                if (!_disposed)
                {
                    try { await CheckForUpdateAsync(); }
                    catch { }
                }
            }, null, interval, interval);
        }

        public async Task<string?> DownloadInstallerAsync(
            UpdateCheckResult result,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(result.DownloadUrl)) return null;

            Directory.CreateDirectory(_opts.DownloadDirectory);
            string fileName = Path.GetFileName(new Uri(result.DownloadUrl!).AbsolutePath);
            string dest     = Path.Combine(_opts.DownloadDirectory, fileName);

            using var response = await _http.GetAsync(
                result.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long total    = response.Content.Headers.ContentLength ?? -1;
            long received = 0;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file   = File.Create(dest);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await file.WriteAsync(buffer, 0, read, ct);
                received += read;
                if (total > 0) progress?.Report((double)received / total);
            }

            return dest;
        }

        public void LaunchInstaller(string installerPath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = installerPath,
                UseShellExecute = true
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
            _http.Dispose();
        }
    }
}