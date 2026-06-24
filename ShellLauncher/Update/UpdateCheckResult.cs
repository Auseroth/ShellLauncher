namespace ShellLauncher
{
    public class UpdateCheckResult
    {
        public bool     UpdateAvailable { get; set; }
        public string?  TagName         { get; set; }
        public string?  DownloadUrl     { get; set; }
        public string?  ReleaseNotes    { get; set; }
        public Version? LatestVersion   { get; set; }
    }
}