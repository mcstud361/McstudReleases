#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Auto-update service for McStud Tool
    /// Checks a remote server for new versions and downloads/installs updates
    /// </summary>
    public class UpdateService
    {
        // Current app version - UPDATE THIS WITH EACH RELEASE
        public static readonly Version CurrentVersion = new Version(2, 10, 0);

        // OneDrive sync folder path for update manifest
        // This will check the local OneDrive sync folder first
        private static string GetOneDriveUpdatePath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "OneDrive", "McStudUpdates", "update.json");
        }

        // Fallback web URL (if OneDrive sync not available)
        private const string FallbackUpdateUrl = "";

        private static readonly HttpClient _httpClient = new HttpClient();
        private UpdateManifest? _latestManifest;
        private string? _lastError;

        public event Action<UpdateManifest>? UpdateAvailable;
        public event Action<int>? DownloadProgress;
        public event Action<string>? UpdateError;

        /// <summary>
        /// Get the last error message
        /// </summary>
        public string? LastError => _lastError;

        /// <summary>
        /// Check for updates - tries OneDrive local path first, then fallback URL
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            _lastError = null;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Checking for updates... Current version: {CurrentVersion}");

                string? manifestJson = null;
                string source = "";

                // First try local OneDrive path
                var oneDrivePath = GetOneDriveUpdatePath();
                if (File.Exists(oneDrivePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Update] Found local OneDrive manifest: {oneDrivePath}");
                    manifestJson = await File.ReadAllTextAsync(oneDrivePath);
                    source = "OneDrive";
                }
                // Then try fallback URL if configured
                else if (!string.IsNullOrEmpty(FallbackUpdateUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"[Update] Using fallback URL: {FallbackUpdateUrl}");
                    manifestJson = await _httpClient.GetStringAsync(FallbackUpdateUrl);
                    source = "Web";
                }
                else
                {
                    _lastError = "OneDrive sync folder not found. Make sure OneDrive is syncing.";
                    System.Diagnostics.Debug.WriteLine($"[Update] No update source available. OneDrive path: {oneDrivePath}");
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Error = _lastError,
                        CurrentVersion = CurrentVersion.ToString()
                    };
                }

                _latestManifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_latestManifest == null)
                {
                    _lastError = "Invalid update manifest";
                    return new UpdateCheckResult { Success = false, Error = _lastError };
                }

                var latestVersion = Version.Parse(_latestManifest.Version);
                var updateAvailable = latestVersion > CurrentVersion;

                System.Diagnostics.Debug.WriteLine($"[Update] Latest version: {latestVersion} (from {source}), Update available: {updateAvailable}");

                if (updateAvailable)
                {
                    UpdateAvailable?.Invoke(_latestManifest);
                }

                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = updateAvailable,
                    CurrentVersion = CurrentVersion.ToString(),
                    LatestVersion = _latestManifest.Version,
                    ReleaseNotes = _latestManifest.ReleaseNotes,
                    DownloadUrl = _latestManifest.DownloadUrl
                };
            }
            catch (HttpRequestException ex)
            {
                _lastError = $"Network error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[Update] {_lastError}");
                return new UpdateCheckResult { Success = false, Error = _lastError, CurrentVersion = CurrentVersion.ToString() };
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[Update] Error checking for updates: {ex.Message}");
                UpdateError?.Invoke(ex.Message);
                return new UpdateCheckResult { Success = false, Error = _lastError, CurrentVersion = CurrentVersion.ToString() };
            }
        }

        /// <summary>
        /// Get the local OneDrive path for the update zip
        /// </summary>
        public string GetOneDriveZipPath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "OneDrive", "McStudUpdates", "McStudTool.zip");
        }

        /// <summary>
        /// Download and install the update (supports local OneDrive or web)
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdateAsync()
        {
            if (_latestManifest == null)
            {
                UpdateError?.Invoke("No update available to download");
                return false;
            }

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), "McStudUpdate");
                Directory.CreateDirectory(tempPath);

                var downloadPath = Path.Combine(tempPath, "McStudTool_Update.zip");
                string sourcePath = "";

                // First check OneDrive local path for the zip
                var oneDriveZip = GetOneDriveZipPath();
                if (File.Exists(oneDriveZip))
                {
                    System.Diagnostics.Debug.WriteLine($"[Update] Copying from OneDrive: {oneDriveZip}");
                    sourcePath = oneDriveZip;

                    // Copy file with progress
                    var sourceInfo = new FileInfo(oneDriveZip);
                    var totalBytes = sourceInfo.Length;
                    var copiedBytes = 0L;

                    using var sourceStream = new FileStream(oneDriveZip, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
                    using var destStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[81920]; // Larger buffer for local copy
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await destStream.WriteAsync(buffer, 0, bytesRead);
                        copiedBytes += bytesRead;
                        var progress = (int)((copiedBytes * 100) / totalBytes);
                        DownloadProgress?.Invoke(progress);
                    }
                }
                // Fall back to web download if URL provided
                else if (!string.IsNullOrEmpty(_latestManifest.DownloadUrl) &&
                         _latestManifest.DownloadUrl != "PASTE_YOUR_ZIP_SHARE_LINK_HERE")
                {
                    System.Diagnostics.Debug.WriteLine($"[Update] Downloading from web: {_latestManifest.DownloadUrl}");

                    using var response = await _httpClient.GetAsync(_latestManifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var downloadedBytes = 0L;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((downloadedBytes * 100) / totalBytes);
                            DownloadProgress?.Invoke(progress);
                        }
                    }
                }
                else
                {
                    UpdateError?.Invoke("No update source found. OneDrive sync folder not available.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[Update] Download/copy complete to: {downloadPath}");

                // Launch installer/updater and exit current app
                await LaunchUpdaterAsync(downloadPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Download error: {ex.Message}");
                UpdateError?.Invoke($"Download failed: {ex.Message}");
                return false;
            }
        }

        private async Task LaunchUpdaterAsync(string downloadPath)
        {
            // Create a simple batch script to update the app
            var appPath = AppContext.BaseDirectory;
            var exeName = "McstudDesktop.exe";
            var batchPath = Path.Combine(Path.GetTempPath(), "McStudUpdate", "update.bat");

            var batchContent = $@"@echo off
echo Updating McStud Tool...
timeout /t 2 /nobreak > nul

REM Extract if it's a zip file
if ""%~x1"" == "".zip"" (
    powershell -Command ""Expand-Archive -Path '{downloadPath}' -DestinationPath '{appPath}' -Force""
) else (
    REM Copy if it's an exe
    copy /Y ""{downloadPath}"" ""{Path.Combine(appPath, exeName)}""
)

echo Update complete! Starting McStud Tool...
start """" ""{Path.Combine(appPath, exeName)}""

REM Clean up
del ""{downloadPath}"" 2>nul
del ""%~f0"" 2>nul
";

            await File.WriteAllTextAsync(batchPath, batchContent);

            // Start the updater batch file
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            // Exit current application to allow update
            Environment.Exit(0);
        }

        /// <summary>
        /// Get current version string
        /// </summary>
        public static string GetVersionString() => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
    }

    #region Data Models

    public class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string MinimumVersion { get; set; } = "";
        public DateTime ReleaseDate { get; set; }
        public long FileSize { get; set; }
        public string Checksum { get; set; } = "";
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string? ReleaseNotes { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Error { get; set; }
    }

    #endregion
}
