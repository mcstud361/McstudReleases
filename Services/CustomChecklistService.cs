#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services;

/// <summary>
/// Service for managing user-created custom checklists.
/// Allows users to duplicate, edit, and create their own checklists.
/// </summary>
public class CustomChecklistService
{
    private static CustomChecklistService? _instance;
    public static CustomChecklistService Instance => _instance ??= new CustomChecklistService();

    private List<Checklist> _customChecklists = new();
    private readonly string _customFolder;

    public event EventHandler? ChecklistsChanged;

    private CustomChecklistService()
    {
        _customFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CustomChecklists");
        EnsureFolder();
        LoadCustomChecklists();
    }

    private void EnsureFolder()
    {
        if (!Directory.Exists(_customFolder))
        {
            Directory.CreateDirectory(_customFolder);
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Created folder: {_customFolder}");
        }
    }

    private void LoadCustomChecklists()
    {
        try
        {
            _customChecklists.Clear();
            var files = Directory.GetFiles(_customFolder, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var checklist = JsonSerializer.Deserialize<Checklist>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (checklist != null)
                    {
                        _customChecklists.Add(checklist);
                        System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Loaded: {checklist.Title}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Error loading {file}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Loaded {_customChecklists.Count} custom checklists");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all custom checklists
    /// </summary>
    public List<Checklist> GetCustomChecklists()
    {
        return _customChecklists.ToList();
    }

    /// <summary>
    /// Get a specific custom checklist by ID
    /// </summary>
    public Checklist? GetChecklist(string id)
    {
        return _customChecklists.FirstOrDefault(c =>
            c.Id?.Equals(id, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Duplicate an existing checklist (from either built-in or custom)
    /// </summary>
    public Checklist DuplicateChecklist(Checklist source, string? newTitle = null)
    {
        var newId = $"custom-{Guid.NewGuid():N}".Substring(0, 20);
        var newChecklist = new Checklist
        {
            Id = newId,
            Title = newTitle ?? $"{source.Title} (Copy)",
            Description = source.Description,
            Version = "1.0",
            ShopName = source.ShopName,
            Sections = source.Sections?.Select(s => new ChecklistSection
            {
                Id = $"sec-{Guid.NewGuid():N}".Substring(0, 12),
                Title = s.Title,
                Icon = s.Icon,
                Items = s.Items?.Select(i => new ChecklistItem
                {
                    Id = $"item-{Guid.NewGuid():N}".Substring(0, 14),
                    Text = i.Text,
                    Required = i.Required
                }).ToList()
            }).ToList()
        };

        SaveChecklist(newChecklist);
        return newChecklist;
    }

    /// <summary>
    /// Create a new blank checklist
    /// </summary>
    public Checklist CreateNewChecklist(string title)
    {
        var newChecklist = new Checklist
        {
            Id = $"custom-{Guid.NewGuid():N}".Substring(0, 20),
            Title = title,
            Description = "Custom checklist",
            Version = "1.0",
            ShopName = "My Shop",
            Sections = new List<ChecklistSection>
            {
                new ChecklistSection
                {
                    Id = "sec-default",
                    Title = "Section 1",
                    Icon = "E73E",
                    Items = new List<ChecklistItem>
                    {
                        new ChecklistItem
                        {
                            Id = "item-1",
                            Text = "First item",
                            Required = false
                        }
                    }
                }
            }
        };

        SaveChecklist(newChecklist);
        return newChecklist;
    }

    /// <summary>
    /// Save a checklist (create or update)
    /// </summary>
    public void SaveChecklist(Checklist checklist)
    {
        try
        {
            EnsureFolder();

            var filePath = Path.Combine(_customFolder, $"{checklist.Id}.json");
            var json = JsonSerializer.Serialize(checklist, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(filePath, json);

            // Update in-memory list
            var existing = _customChecklists.FindIndex(c =>
                c.Id?.Equals(checklist.Id, StringComparison.OrdinalIgnoreCase) == true);

            if (existing >= 0)
                _customChecklists[existing] = checklist;
            else
                _customChecklists.Add(checklist);

            ChecklistsChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Saved: {checklist.Title}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Error saving: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a custom checklist
    /// </summary>
    public void DeleteChecklist(string id)
    {
        try
        {
            var filePath = Path.Combine(_customFolder, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            _customChecklists.RemoveAll(c =>
                c.Id?.Equals(id, StringComparison.OrdinalIgnoreCase) == true);

            ChecklistsChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Deleted: {id}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomChecklist] Error deleting: {ex.Message}");
        }
    }

    /// <summary>
    /// Add a section to a checklist
    /// </summary>
    public void AddSection(Checklist checklist, string title)
    {
        checklist.Sections ??= new List<ChecklistSection>();
        checklist.Sections.Add(new ChecklistSection
        {
            Id = $"sec-{Guid.NewGuid():N}".Substring(0, 12),
            Title = title,
            Icon = "E73E",
            Items = new List<ChecklistItem>()
        });
        SaveChecklist(checklist);
    }

    /// <summary>
    /// Remove a section from a checklist
    /// </summary>
    public void RemoveSection(Checklist checklist, string sectionId)
    {
        checklist.Sections?.RemoveAll(s =>
            s.Id?.Equals(sectionId, StringComparison.OrdinalIgnoreCase) == true);
        SaveChecklist(checklist);
    }

    /// <summary>
    /// Add an item to a section
    /// </summary>
    public void AddItem(ChecklistSection section, string text, bool required = false)
    {
        section.Items ??= new List<ChecklistItem>();
        section.Items.Add(new ChecklistItem
        {
            Id = $"item-{Guid.NewGuid():N}".Substring(0, 14),
            Text = text,
            Required = required
        });
    }

    /// <summary>
    /// Remove an item from a section
    /// </summary>
    public void RemoveItem(ChecklistSection section, string itemId)
    {
        section.Items?.RemoveAll(i =>
            i.Id?.Equals(itemId, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Refresh from disk
    /// </summary>
    public void Refresh()
    {
        LoadCustomChecklists();
        ChecklistsChanged?.Invoke(this, EventArgs.Empty);
    }
}
