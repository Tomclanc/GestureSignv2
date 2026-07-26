using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using GestureSign.Shared;

namespace GestureSign.Daemon
{
    internal sealed class UpdateAvailableEventArgs : EventArgs
    {
        public UpdateAvailableEventArgs(string version, string releaseUrl)
        {
            Version = version;
            ReleaseUrl = releaseUrl;
        }

        public string Version { get; private set; }
        public string ReleaseUrl { get; private set; }
    }

    internal sealed class GitHubUpdateChecker : IDisposable
    {
        private const string LatestReleaseUrl = "https://github.com/Tomclanc/GestureSignv2/releases/latest";
        private const int AppModelErrorNoPackage = 15700;
        private static readonly HttpClient Client = CreateClient();
        private readonly Timer _timer;
        private int _isChecking;
        private string _lastNotifiedVersion;

        public GitHubUpdateChecker()
        {
            _timer = new Timer(CheckTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler<UpdateAvailableEventArgs> UpdateAvailable;

        public void Configure()
        {
            if (IsPackagedProcess() || !AppConfig.CheckForUpdates)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            if (String.Equals(AppConfig.UpdateCheckInterval, "Manual", StringComparison.OrdinalIgnoreCase))
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            TimeSpan interval = GetInterval(AppConfig.UpdateCheckInterval);
            DateTimeOffset lastCheck;
            TimeSpan dueTime = TimeSpan.FromSeconds(2);
            if (DateTimeOffset.TryParse(AppConfig.LastUpdateCheckUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastCheck))
            {
                TimeSpan remaining = interval - (DateTimeOffset.UtcNow - lastCheck);
                if (remaining > TimeSpan.Zero)
                    dueTime = remaining;
            }

            if (dueTime > TimeSpan.FromMilliseconds(int.MaxValue - 1))
                dueTime = TimeSpan.FromMilliseconds(int.MaxValue - 1);
            _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private async void CheckTimerCallback(object state)
        {
            if (Interlocked.Exchange(ref _isChecking, 1) != 0)
                return;

            try
            {
                GitHubReleaseInfo latest = await GetLatestReleaseAsync();
                Version currentVersion;
                if (!TryParseVersion(ProductVersion.Current, out currentVersion))
                    currentVersion = new Version(0, 0);

                if (latest.Version > currentVersion && !String.Equals(_lastNotifiedVersion, latest.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    _lastNotifiedVersion = latest.TagName;
                    EventHandler<UpdateAvailableEventArgs> handler = UpdateAvailable;
                    if (handler != null)
                        handler(this, new UpdateAvailableEventArgs(latest.TagName.TrimStart('v', 'V'), latest.ReleaseUrl));
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
            finally
            {
                AppConfig.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                Interlocked.Exchange(ref _isChecking, 0);
                Configure();
            }
        }

        private static async Task<GitHubReleaseInfo> GetLatestReleaseAsync()
        {
            using (HttpResponseMessage response = await Client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                Uri releaseUri = response.RequestMessage == null ? null : response.RequestMessage.RequestUri;
                string tagName = releaseUri == null ? String.Empty : releaseUri.Segments[releaseUri.Segments.Length - 1].Trim('/').Trim();
                Version version;
                if (!TryParseVersion(tagName, out version) || releaseUri == null)
                    throw new InvalidOperationException("GitHub returned an invalid release response.");

                return new GitHubReleaseInfo(version, tagName, releaseUri.AbsoluteUri);
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = new Version(0, 0);
            if (String.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().TrimStart('v', 'V');
            int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
                normalized = normalized.Substring(0, suffixIndex);
            return Version.TryParse(normalized, out version);
        }

        private static TimeSpan GetInterval(string value)
        {
            switch ((value ?? String.Empty).Trim().ToLowerInvariant())
            {
                case "tenminutes":
                    return TimeSpan.FromMinutes(10);
                case "hour":
                    return TimeSpan.FromHours(1);
                case "month":
                    return TimeSpan.FromDays(30);
                default:
                    return TimeSpan.FromDays(1);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-V2-UpdateChecker");
            return client;
        }

        private static bool IsPackagedProcess()
        {
            string executablePath = Process.GetCurrentProcess().MainModule.FileName;
            if (!String.IsNullOrEmpty(executablePath) && executablePath.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            int length = 0;
            int result = GetCurrentPackageFullName(ref length, null);
            return result != AppModelErrorNoPackage;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        private sealed class GitHubReleaseInfo
        {
            public GitHubReleaseInfo(Version version, string tagName, string releaseUrl)
            {
                Version = version;
                TagName = tagName;
                ReleaseUrl = releaseUrl;
            }

            public Version Version { get; private set; }
            public string TagName { get; private set; }
            public string ReleaseUrl { get; private set; }
        }
    }
}
