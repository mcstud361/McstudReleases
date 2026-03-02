using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using McStudDesktop.Models;

namespace McStudDesktop.Services;

/// <summary>
/// Service for managing shop-specific custom operations and profiles
/// </summary>
public class CustomOperationsService
{
    private static CustomOperationsService? _instance;
    public static CustomOperationsService Instance => _instance ??= new CustomOperationsService();

    private readonly string _settingsPath;
    private CustomOperationsData _data = new();
    private bool _isLoaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private CustomOperationsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "CustomOperations.json");
    }

    /// <summary>
    /// Load custom operations from disk
    /// </summary>
    public async Task LoadAsync()
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _data = JsonSerializer.Deserialize<CustomOperationsData>(json, JsonOptions) ?? new CustomOperationsData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading custom operations: {ex.Message}");
            _data = new CustomOperationsData();
        }

        _isLoaded = true;
    }

    /// <summary>
    /// Save custom operations to disk
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            _data.LastModified = DateTime.Now;
            var json = JsonSerializer.Serialize(_data, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving custom operations: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronous load for use during initialization
    /// </summary>
    public void Load()
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _data = JsonSerializer.Deserialize<CustomOperationsData>(json, JsonOptions) ?? new CustomOperationsData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading custom operations: {ex.Message}");
            _data = new CustomOperationsData();
        }

        _isLoaded = true;
    }

    // ==================== OPERATIONS MANAGEMENT ====================

    /// <summary>
    /// Get all custom operations for a category
    /// </summary>
    public List<CustomOperation> GetOperations(string category)
    {
        Load();
        return _data.OperationsByCategory.TryGetValue(category, out var ops)
            ? ops.OrderBy(o => o.SortOrder).ToList()
            : new List<CustomOperation>();
    }

    /// <summary>
    /// Add a custom operation to a category
    /// </summary>
    public async Task<CustomOperation> AddOperationAsync(string category, CustomOperation operation)
    {
        Load();

        if (!_data.OperationsByCategory.ContainsKey(category))
            _data.OperationsByCategory[category] = new List<CustomOperation>();

        operation.Category = category;
        operation.SortOrder = _data.OperationsByCategory[category].Count;
        operation.CreatedAt = DateTime.Now;
        operation.ModifiedAt = DateTime.Now;

        _data.OperationsByCategory[category].Add(operation);
        await SaveAsync();

        return operation;
    }

    /// <summary>
    /// Update an existing custom operation
    /// </summary>
    public async Task UpdateOperationAsync(CustomOperation operation)
    {
        Load();

        if (_data.OperationsByCategory.TryGetValue(operation.Category, out var ops))
        {
            var index = ops.FindIndex(o => o.Id == operation.Id);
            if (index >= 0)
            {
                operation.ModifiedAt = DateTime.Now;
                ops[index] = operation;
                await SaveAsync();
            }
        }
    }

    /// <summary>
    /// Delete a custom operation
    /// </summary>
    public async Task DeleteOperationAsync(string category, string operationId)
    {
        Load();

        if (_data.OperationsByCategory.TryGetValue(category, out var ops))
        {
            ops.RemoveAll(o => o.Id == operationId);
            await SaveAsync();
        }
    }

    /// <summary>
    /// Reorder operations within a category
    /// </summary>
    public async Task ReorderOperationsAsync(string category, List<string> operationIds)
    {
        Load();

        if (_data.OperationsByCategory.TryGetValue(category, out var ops))
        {
            for (int i = 0; i < operationIds.Count; i++)
            {
                var op = ops.FirstOrDefault(o => o.Id == operationIds[i]);
                if (op != null)
                    op.SortOrder = i;
            }
            await SaveAsync();
        }
    }

    // ==================== PROFILES MANAGEMENT ====================

    /// <summary>
    /// Get all profiles for a category
    /// </summary>
    public List<CustomOperationProfile> GetProfiles(string category)
    {
        Load();
        return _data.ProfilesByCategory.TryGetValue(category, out var profiles)
            ? profiles.OrderBy(p => p.Name).ToList()
            : new List<CustomOperationProfile>();
    }

    /// <summary>
    /// Get the default profile for a category
    /// </summary>
    public CustomOperationProfile? GetDefaultProfile(string category)
    {
        return GetProfiles(category).FirstOrDefault(p => p.IsDefault);
    }

    /// <summary>
    /// Create a new profile
    /// </summary>
    public async Task<CustomOperationProfile> CreateProfileAsync(string category, string name)
    {
        Load();

        if (!_data.ProfilesByCategory.ContainsKey(category))
            _data.ProfilesByCategory[category] = new List<CustomOperationProfile>();

        var profile = new CustomOperationProfile
        {
            Name = name,
            Category = category,
            IsDefault = !_data.ProfilesByCategory[category].Any()
        };

        _data.ProfilesByCategory[category].Add(profile);
        await SaveAsync();

        return profile;
    }

    /// <summary>
    /// Duplicate an existing profile
    /// </summary>
    public async Task<CustomOperationProfile> DuplicateProfileAsync(string profileId, string newName)
    {
        Load();

        foreach (var categoryProfiles in _data.ProfilesByCategory.Values)
        {
            var source = categoryProfiles.FirstOrDefault(p => p.Id == profileId);
            if (source != null)
            {
                var newProfile = new CustomOperationProfile
                {
                    Name = newName,
                    Category = source.Category,
                    IsDefault = false,
                    Operations = source.Operations.Select(o => new CustomOperation
                    {
                        Description = o.Description,
                        Category = o.Category,
                        SubCategory = o.SubCategory,
                        CalculationType = o.CalculationType,
                        Value = o.Value,
                        MaterialCost = o.MaterialCost,
                        EnabledByDefault = o.EnabledByDefault,
                        OperationTypeCode = o.OperationTypeCode,
                        Notes = o.Notes,
                        SortOrder = o.SortOrder
                    }).ToList()
                };

                categoryProfiles.Add(newProfile);
                await SaveAsync();
                return newProfile;
            }
        }

        throw new InvalidOperationException("Profile not found");
    }

    /// <summary>
    /// Delete a profile
    /// </summary>
    public async Task DeleteProfileAsync(string category, string profileId)
    {
        Load();

        if (_data.ProfilesByCategory.TryGetValue(category, out var profiles))
        {
            profiles.RemoveAll(p => p.Id == profileId);
            await SaveAsync();
        }
    }

    /// <summary>
    /// Set a profile as the default for its category
    /// </summary>
    public async Task SetDefaultProfileAsync(string category, string profileId)
    {
        Load();

        if (_data.ProfilesByCategory.TryGetValue(category, out var profiles))
        {
            foreach (var profile in profiles)
            {
                profile.IsDefault = profile.Id == profileId;
            }
            await SaveAsync();
        }
    }

    /// <summary>
    /// Add operation to a profile
    /// </summary>
    public async Task AddOperationToProfileAsync(string profileId, CustomOperation operation)
    {
        Load();

        foreach (var categoryProfiles in _data.ProfilesByCategory.Values)
        {
            var profile = categoryProfiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                operation.SortOrder = profile.Operations.Count;
                profile.Operations.Add(operation);
                profile.ModifiedAt = DateTime.Now;
                await SaveAsync();
                return;
            }
        }
    }

    // ==================== BUILT-IN OPERATION OVERRIDES ====================

    /// <summary>
    /// Hide a built-in operation
    /// </summary>
    public async Task HideBuiltInOperationAsync(string operationKey)
    {
        Load();

        if (!_data.HiddenBuiltInOperations.Contains(operationKey))
        {
            _data.HiddenBuiltInOperations.Add(operationKey);
            await SaveAsync();
        }
    }

    /// <summary>
    /// Show a previously hidden built-in operation
    /// </summary>
    public async Task ShowBuiltInOperationAsync(string operationKey)
    {
        Load();
        _data.HiddenBuiltInOperations.Remove(operationKey);
        await SaveAsync();
    }

    /// <summary>
    /// Check if a built-in operation is hidden
    /// </summary>
    public bool IsBuiltInOperationHidden(string operationKey)
    {
        Load();
        return _data.HiddenBuiltInOperations.Contains(operationKey);
    }

    /// <summary>
    /// Override a built-in operation's description
    /// </summary>
    public async Task SetDescriptionOverrideAsync(string originalDescription, string newDescription)
    {
        Load();
        _data.DescriptionOverrides[originalDescription] = newDescription;
        await SaveAsync();
    }

    /// <summary>
    /// Get overridden description for a built-in operation
    /// </summary>
    public string GetDescriptionOverride(string originalDescription)
    {
        Load();
        return _data.DescriptionOverrides.TryGetValue(originalDescription, out var @override)
            ? @override
            : originalDescription;
    }

    /// <summary>
    /// Remove a description override
    /// </summary>
    public async Task RemoveDescriptionOverrideAsync(string originalDescription)
    {
        Load();
        _data.DescriptionOverrides.Remove(originalDescription);
        await SaveAsync();
    }

    // ==================== SHOP INFO ====================

    /// <summary>
    /// Get/set the shop name
    /// </summary>
    public string ShopName
    {
        get
        {
            Load();
            return _data.ShopName;
        }
        set
        {
            Load();
            _data.ShopName = value;
            _ = SaveAsync();
        }
    }

    // ==================== IMPORT/EXPORT ====================

    /// <summary>
    /// Export all custom operations to JSON
    /// </summary>
    public string ExportToJson()
    {
        Load();
        return JsonSerializer.Serialize(_data, JsonOptions);
    }

    /// <summary>
    /// Import custom operations from JSON
    /// </summary>
    public async Task ImportFromJsonAsync(string json, bool merge = false)
    {
        var imported = JsonSerializer.Deserialize<CustomOperationsData>(json, JsonOptions);
        if (imported == null) return;

        if (merge)
        {
            // Merge with existing data
            foreach (var kvp in imported.OperationsByCategory)
            {
                if (!_data.OperationsByCategory.ContainsKey(kvp.Key))
                    _data.OperationsByCategory[kvp.Key] = new List<CustomOperation>();

                _data.OperationsByCategory[kvp.Key].AddRange(kvp.Value);
            }

            foreach (var kvp in imported.ProfilesByCategory)
            {
                if (!_data.ProfilesByCategory.ContainsKey(kvp.Key))
                    _data.ProfilesByCategory[kvp.Key] = new List<CustomOperationProfile>();

                _data.ProfilesByCategory[kvp.Key].AddRange(kvp.Value);
            }
        }
        else
        {
            // Replace all data
            _data = imported;
        }

        await SaveAsync();
    }

    /// <summary>
    /// Get all category names that have custom operations
    /// </summary>
    public List<string> GetCategoriesWithCustomOperations()
    {
        Load();
        return _data.OperationsByCategory.Keys
            .Concat(_data.ProfilesByCategory.Keys)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }
}
