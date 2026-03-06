#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for managing Shop Docs settings and custom document folders
    /// </summary>
    public class ShopDocsSettingsService
    {
        private static ShopDocsSettingsService? _instance;
        public static ShopDocsSettingsService Instance => _instance ??= new ShopDocsSettingsService();

        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private ShopDocsSettings _settings;

        // Event for when settings change
        public event Action? SettingsChanged;

        private ShopDocsSettingsService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            _settingsFile = Path.Combine(_settingsFolder, "ShopDocsSettings.json");
            _settings = LoadSettings();
        }

        public ShopDocsSettings GetSettings() => _settings;

        public void SaveSettings(ShopDocsSettings settings)
        {
            _settings = settings;
            try
            {
                if (!Directory.Exists(_settingsFolder))
                    Directory.CreateDirectory(_settingsFolder);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Settings saved");
                SettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error saving settings: {ex.Message}");
            }
        }

        private ShopDocsSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var settings = JsonSerializer.Deserialize<ShopDocsSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (settings != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[ShopDocs] Settings loaded");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error loading settings: {ex.Message}");
            }

            return GetDefaultSettings();
        }

        private ShopDocsSettings GetDefaultSettings()
        {
            return new ShopDocsSettings
            {
                CustomDocsFolder = "",
                UseCustomFolder = false,
                DocumentSources = new List<DocumentSource>()
            };
        }

        /// <summary>
        /// Get the app's built-in documents folder
        /// </summary>
        public string GetBaseDocsFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        }

        /// <summary>
        /// Get the user's custom documents folder (or default if not set)
        /// </summary>
        public string GetUserDocsFolder()
        {
            if (_settings.UseCustomFolder && !string.IsNullOrEmpty(_settings.CustomDocsFolder) &&
                Directory.Exists(_settings.CustomDocsFolder))
            {
                return _settings.CustomDocsFolder;
            }

            // Default to Documents\McStudDocs
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "McStudDocs");

            if (!Directory.Exists(defaultFolder))
            {
                try { Directory.CreateDirectory(defaultFolder); }
                catch { }
            }

            return defaultFolder;
        }

        /// <summary>
        /// Get all checklist files from both base and user folders
        /// </summary>
        public List<DocumentInfo> GetAllChecklists()
        {
            var docs = new List<DocumentInfo>();

            // Base checklists (from app)
            var baseChecklistsFolder = Path.Combine(GetBaseDocsFolder(), "Checklists");
            if (Directory.Exists(baseChecklistsFolder))
            {
                foreach (var file in Directory.GetFiles(baseChecklistsFolder, "*.json"))
                {
                    docs.Add(new DocumentInfo
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        DisplayName = GetChecklistTitle(file) ?? Path.GetFileNameWithoutExtension(file),
                        IsBase = true,
                        DocumentType = "Checklist"
                    });
                }
            }

            // User checklists
            var userChecklistsFolder = Path.Combine(GetUserDocsFolder(), "Checklists");
            if (Directory.Exists(userChecklistsFolder))
            {
                foreach (var file in Directory.GetFiles(userChecklistsFolder, "*.json"))
                {
                    var fileName = Path.GetFileName(file);
                    // Skip if it's a duplicate of a base doc (user override)
                    if (!docs.Any(d => d.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        docs.Add(new DocumentInfo
                        {
                            FilePath = file,
                            FileName = fileName,
                            DisplayName = GetChecklistTitle(file) ?? Path.GetFileNameWithoutExtension(file),
                            IsBase = false,
                            DocumentType = "Checklist"
                        });
                    }
                    else
                    {
                        // Mark the base one as having a user override
                        var baseDoc = docs.First(d => d.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                        baseDoc.HasUserOverride = true;
                        baseDoc.UserOverridePath = file;
                    }
                }
            }

            return docs;
        }

        /// <summary>
        /// Get all document files from user folder (PDFs, Word docs, etc.)
        /// </summary>
        public List<DocumentInfo> GetUserDocuments()
        {
            var docs = new List<DocumentInfo>();
            var userFolder = GetUserDocsFolder();

            if (!Directory.Exists(userFolder)) return docs;

            // Look for common document types
            var extensions = new[] { "*.pdf", "*.docx", "*.doc", "*.xlsx", "*.xls", "*.txt" };

            foreach (var ext in extensions)
            {
                foreach (var file in Directory.GetFiles(userFolder, ext, SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(userFolder, file);
                    docs.Add(new DocumentInfo
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        DisplayName = Path.GetFileNameWithoutExtension(file),
                        RelativePath = relativePath,
                        IsBase = false,
                        DocumentType = GetDocumentType(file)
                    });
                }
            }

            return docs.OrderBy(d => d.RelativePath).ToList();
        }

        private string GetDocumentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "PDF",
                ".docx" or ".doc" => "Word",
                ".xlsx" or ".xls" => "Excel",
                ".txt" => "Text",
                ".json" => "JSON",
                _ => "Document"
            };
        }

        private string? GetChecklistTitle(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("title", out var titleProp))
                {
                    return titleProp.GetString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Copy a base checklist to user folder for customization
        /// </summary>
        public string? CreateUserCopy(DocumentInfo baseDoc)
        {
            if (!baseDoc.IsBase) return null;

            try
            {
                var userFolder = Path.Combine(GetUserDocsFolder(), "Checklists");
                if (!Directory.Exists(userFolder))
                    Directory.CreateDirectory(userFolder);

                var destPath = Path.Combine(userFolder, baseDoc.FileName);

                // If file exists, create with "_custom" suffix
                if (File.Exists(destPath))
                {
                    var name = Path.GetFileNameWithoutExtension(baseDoc.FileName);
                    var ext = Path.GetExtension(baseDoc.FileName);
                    destPath = Path.Combine(userFolder, $"{name}_custom{ext}");
                }

                File.Copy(baseDoc.FilePath, destPath);
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Created user copy: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error creating user copy: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Import a document to the user folder
        /// </summary>
        public string? ImportDocument(string sourcePath, string? subFolder = null)
        {
            try
            {
                var destFolder = GetUserDocsFolder();
                if (!string.IsNullOrEmpty(subFolder))
                {
                    destFolder = Path.Combine(destFolder, subFolder);
                }

                if (!Directory.Exists(destFolder))
                    Directory.CreateDirectory(destFolder);

                var destPath = Path.Combine(destFolder, Path.GetFileName(sourcePath));

                // Handle duplicates
                if (File.Exists(destPath))
                {
                    var name = Path.GetFileNameWithoutExtension(sourcePath);
                    var ext = Path.GetExtension(sourcePath);
                    var counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(destFolder, $"{name}_{counter++}{ext}");
                    }
                }

                File.Copy(sourcePath, destPath);
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Imported: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error importing: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Delete a user document (not base documents)
        /// </summary>
        public bool DeleteUserDocument(DocumentInfo doc)
        {
            if (doc.IsBase) return false; // Can't delete base docs

            try
            {
                if (File.Exists(doc.FilePath))
                {
                    File.Delete(doc.FilePath);
                    System.Diagnostics.Debug.WriteLine($"[ShopDocs] Deleted: {doc.FilePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error deleting: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Open folder in Explorer
        /// </summary>
        public void OpenFolder(string folderPath)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] Error opening folder: {ex.Message}");
            }
        }
    }

    #region Data Models

    public class ShopDocsSettings
    {
        public string ShopName { get; set; } = "";
        public string CustomDocsFolder { get; set; } = "";
        public bool UseCustomFolder { get; set; }
        public List<DocumentSource> DocumentSources { get; set; } = new();
    }

    public class DocumentSource
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public class DocumentInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public bool IsBase { get; set; }
        public bool HasUserOverride { get; set; }
        public string? UserOverridePath { get; set; }
    }

    #endregion
}
