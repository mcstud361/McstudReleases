#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for updating P-Pages, DEG Inquiries, Definitions, and other data files
    /// Downloads from a remote source (GitHub or custom server)
    /// </summary>
    public class DataUpdateService
    {
        // Base URL for data files - UPDATE THIS to your GitHub raw content URL
        // Example: https://raw.githubusercontent.com/yourusername/mcstud-data/main/
        private const string DataBaseUrl = "https://raw.githubusercontent.com/mcstud/mcstud-data/main/";

        // Manifest file that tracks versions
        private const string ManifestFileName = "data-manifest.json";

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _localDataPath;

        public event Action<string>? StatusChanged;
        public event Action<int, int>? ProgressChanged; // (current, total)
        public event Action<string>? ErrorOccurred;
        public event Action<DataUpdateResult>? UpdateCompleted;

        public DataUpdateService()
        {
            _localDataPath = Path.Combine(AppContext.BaseDirectory, "Data");
        }

        /// <summary>
        /// Check if updates are available
        /// </summary>
        public async Task<DataUpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                StatusChanged?.Invoke("Checking for data updates...");

                // Get remote manifest
                var remoteManifest = await GetRemoteManifestAsync();
                if (remoteManifest == null)
                {
                    return new DataUpdateCheckResult
                    {
                        Success = false,
                        Error = "Could not fetch update manifest. Check your internet connection."
                    };
                }

                // Get local manifest
                var localManifest = GetLocalManifest();

                // Compare versions
                var updatesAvailable = new List<DataFileInfo>();
                foreach (var remoteFile in remoteManifest.Files)
                {
                    var localFile = localManifest?.Files.Find(f => f.Name == remoteFile.Name);
                    if (localFile == null || remoteFile.Version > localFile.Version)
                    {
                        updatesAvailable.Add(remoteFile);
                    }
                }

                return new DataUpdateCheckResult
                {
                    Success = true,
                    UpdatesAvailable = updatesAvailable.Count > 0,
                    FilesToUpdate = updatesAvailable,
                    RemoteManifest = remoteManifest,
                    LocalManifest = localManifest
                };
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
                return new DataUpdateCheckResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Download and install all available updates
        /// </summary>
        public async Task<DataUpdateResult> DownloadUpdatesAsync(List<DataFileInfo> filesToUpdate)
        {
            var result = new DataUpdateResult();
            var total = filesToUpdate.Count;
            var current = 0;

            try
            {
                // Ensure Data directory exists
                Directory.CreateDirectory(_localDataPath);

                foreach (var file in filesToUpdate)
                {
                    current++;
                    StatusChanged?.Invoke($"Downloading {file.Name} ({current}/{total})...");
                    ProgressChanged?.Invoke(current, total);

                    try
                    {
                        var url = DataBaseUrl + file.Name;
                        var content = await _httpClient.GetStringAsync(url);

                        // Validate JSON
                        try
                        {
                            JsonDocument.Parse(content);
                        }
                        catch
                        {
                            result.FailedFiles.Add(file.Name);
                            result.Errors.Add($"{file.Name}: Invalid JSON format");
                            continue;
                        }

                        // Save to local Data folder
                        var localPath = Path.Combine(_localDataPath, file.Name);
                        await File.WriteAllTextAsync(localPath, content);

                        result.UpdatedFiles.Add(file.Name);
                        System.Diagnostics.Debug.WriteLine($"[DataUpdate] Updated {file.Name} to version {file.Version}");
                    }
                    catch (Exception ex)
                    {
                        result.FailedFiles.Add(file.Name);
                        result.Errors.Add($"{file.Name}: {ex.Message}");
                    }
                }

                // Update local manifest
                if (result.UpdatedFiles.Count > 0)
                {
                    await UpdateLocalManifestAsync(filesToUpdate);
                }

                result.Success = result.FailedFiles.Count == 0;
                StatusChanged?.Invoke(result.Success
                    ? $"Update complete! {result.UpdatedFiles.Count} file(s) updated."
                    : $"Update completed with errors. {result.UpdatedFiles.Count} updated, {result.FailedFiles.Count} failed.");

                UpdateCompleted?.Invoke(result);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add(ex.Message);
                ErrorOccurred?.Invoke(ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Quick update - check and download all in one call
        /// </summary>
        public async Task<DataUpdateResult> UpdateAllAsync()
        {
            var checkResult = await CheckForUpdatesAsync();

            if (!checkResult.Success)
            {
                return new DataUpdateResult
                {
                    Success = false,
                    Errors = new List<string> { checkResult.Error ?? "Unknown error" }
                };
            }

            if (!checkResult.UpdatesAvailable || checkResult.FilesToUpdate.Count == 0)
            {
                StatusChanged?.Invoke("All data is up to date!");
                return new DataUpdateResult
                {
                    Success = true,
                    Message = "All data is already up to date."
                };
            }

            return await DownloadUpdatesAsync(checkResult.FilesToUpdate);
        }

        private async Task<DataManifest?> GetRemoteManifestAsync()
        {
            try
            {
                var url = DataBaseUrl + ManifestFileName;
                var json = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<DataManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (HttpRequestException)
            {
                // No manifest found - try default file list
                return GetDefaultManifest();
            }
        }

        private DataManifest? GetLocalManifest()
        {
            try
            {
                var path = Path.Combine(_localDataPath, "local-manifest.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<DataManifest>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch { }

            return null;
        }

        private async Task UpdateLocalManifestAsync(List<DataFileInfo> updatedFiles)
        {
            var localManifest = GetLocalManifest() ?? new DataManifest();

            foreach (var file in updatedFiles)
            {
                var existing = localManifest.Files.Find(f => f.Name == file.Name);
                if (existing != null)
                {
                    existing.Version = file.Version;
                    existing.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    localManifest.Files.Add(new DataFileInfo
                    {
                        Name = file.Name,
                        Version = file.Version,
                        LastUpdated = DateTime.UtcNow
                    });
                }
            }

            localManifest.LastChecked = DateTime.UtcNow;

            var path = Path.Combine(_localDataPath, "local-manifest.json");
            var json = JsonSerializer.Serialize(localManifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        private DataManifest GetDefaultManifest()
        {
            // Default files to check for updates
            return new DataManifest
            {
                Files = new List<DataFileInfo>
                {
                    new DataFileInfo { Name = "PPages.json", Version = 1, Description = "CCC/MOTOR P-Pages" },
                    new DataFileInfo { Name = "DEGInquiries.json", Version = 1, Description = "DEG Inquiries" },
                    new DataFileInfo { Name = "Definitions.json", Version = 1, Description = "Estimating Definitions" },
                    new DataFileInfo { Name = "Procedures.json", Version = 1, Description = "OEM Procedures" }
                }
            };
        }

        /// <summary>
        /// Get current data version info
        /// </summary>
        public DataManifest? GetCurrentDataInfo()
        {
            return GetLocalManifest();
        }
    }

    #region Data Models

    public class DataManifest
    {
        public int ManifestVersion { get; set; } = 1;
        public DateTime LastChecked { get; set; }
        public List<DataFileInfo> Files { get; set; } = new List<DataFileInfo>();
    }

    public class DataFileInfo
    {
        public string Name { get; set; } = "";
        public int Version { get; set; }
        public string Description { get; set; } = "";
        public DateTime LastUpdated { get; set; }
        public long FileSize { get; set; }
    }

    public class DataUpdateCheckResult
    {
        public bool Success { get; set; }
        public bool UpdatesAvailable { get; set; }
        public List<DataFileInfo> FilesToUpdate { get; set; } = new List<DataFileInfo>();
        public DataManifest? RemoteManifest { get; set; }
        public DataManifest? LocalManifest { get; set; }
        public string? Error { get; set; }
    }

    public class DataUpdateResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public List<string> UpdatedFiles { get; set; } = new List<string>();
        public List<string> FailedFiles { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    #endregion
}
