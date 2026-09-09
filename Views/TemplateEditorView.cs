#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Views;

/// <summary>
/// Dedicated editor for customizing a Shop-Doc template (invoices, tow bill, custom docs, etc.).
///
/// Deliberately mirrors <see cref="ChecklistEditorView"/> so ALL shop docs get the same,
/// simple customization experience: a header (name/description), a scrollable list of
/// section cards (each with its own delete button and add-item button), and a footer with
/// "+ Add Section", a live "Saved" indicator, Cancel and Done. Every change auto-saves.
///
/// This edits the template STRUCTURE only (sections, fields, charge items) — it never touches
/// the fill-in/export form, which continues to live in TemplateFormBuilder.
/// </summary>
public class TemplateEditorView : UserControl
{
    private readonly ShopDocTemplateService _service;
    private ShopDocTemplate? _template;
    private bool _suppressAutoSave;                 // suppress during LoadTemplate / RefreshUI
    private string? _originalSnapshotJson;          // snapshot taken when the editor opens, for Cancel/discard

    // Header fields
    private TextBox? _nameBox;
    private TextBox? _descriptionBox;

    // Status indicator
    private TextBlock? _saveStatus;

    // Sections container
    private StackPanel? _sectionsPanel;

    // Events
    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    public TemplateEditorView()
    {
        _service = ShopDocTemplateService.Instance;
        BuildUI();
    }

    public ShopDocTemplate? GetTemplate() => _template;

    public void LoadTemplate(ShopDocTemplate template)
    {
        _suppressAutoSave = true;
        _template = template;
        // Snapshot the template as-opened so Cancel can discard this session's edits
        // (auto-save writes changes to disk as you go, so Cancel restores this snapshot).
        try { _originalSnapshotJson = System.Text.Json.JsonSerializer.Serialize(template); }
        catch { _originalSnapshotJson = null; }
        RefreshUI();
        _suppressAutoSave = false;
    }

    /// <summary>
    /// Cancel/discard: restore the template to the snapshot taken when the editor opened,
    /// undoing everything auto-saved during this session, then close.
    /// </summary>
    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_template == null || string.IsNullOrEmpty(_originalSnapshotJson))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Discard changes?",
            Content = "Discard the changes you made in this editing session? This restores the document to how it was when you opened it.",
            PrimaryButtonText = "Discard",
            CloseButtonText = "Keep editing",
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<ShopDocTemplate>(_originalSnapshotJson);
            if (restored != null)
            {
                // Copy restored fields back INTO the same object the parent holds, so the
                // revert shows everywhere, then persist it over the auto-saved edits.
                _suppressAutoSave = true;
                _template.Name = restored.Name;
                _template.Description = restored.Description;
                _template.Sections = restored.Sections;
                _template.Settings = restored.Settings;
                _suppressAutoSave = false;
                if (!_template.IsReadOnly && !_template.IsOriginal)
                    _service.SaveUserTemplate(_template);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateEditor] Cancel restore failed: {ex.Message}");
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Auto-save the current template to disk. Only saves user (non-read-only) templates.
    /// </summary>
    private void AutoSave()
    {
        if (_suppressAutoSave || _template == null) return;
        if (_template.IsReadOnly || _template.IsOriginal) return;

        try { _service.SaveUserTemplate(_template); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TemplateEditor] AutoSave failed: {ex.Message}"); }

        if (_saveStatus != null)
        {
            _saveStatus.Text = "Saved";
            _saveStatus.Opacity = 1;
            FadeSaveStatus();
        }
    }

    private async void FadeSaveStatus()
    {
        if (_saveStatus == null) return;
        await System.Threading.Tasks.Task.Delay(1500);
        _saveStatus.Opacity = 0;
    }

    private void BuildUI()
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25))
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20)
        };
        _sectionsPanel = new StackPanel { Spacing = 15 };
        scroll.Content = _sectionsPanel;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            Padding = new Thickness(20, 15, 20, 15),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var stack = new StackPanel { Spacing = 12 };

        // Title / "Customize Document" label
        stack.Children.Add(new TextBlock
        {
            Text = "Customize Document",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        // Name row
        stack.Children.Add(BuildLabeledRow("Name:", out _nameBox, "Document name..."));
        _nameBox!.TextChanged += (s, e) => OnFieldChanged();

        // Description row
        stack.Children.Add(BuildLabeledRow("Description:", out _descriptionBox, "Brief description..."));
        _descriptionBox!.TextChanged += (s, e) => OnFieldChanged();

        header.Child = stack;
        return header;
    }

    private Grid BuildLabeledRow(string label, out TextBox box, string placeholder)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        box = new TextBox
        {
            PlaceholderText = placeholder,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        return row;
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
            Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var addSectionBtn = new Button
        {
            Content = "+ Add Section",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        addSectionBtn.Click += OnAddSection;
        row.Children.Add(addSectionBtn);

        _saveStatus = new TextBlock
        {
            Text = "Saved",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 180, 80)),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Margin = new Thickness(10, 0, 0, 0)
        };
        row.Children.Add(_saveStatus);

        row.Children.Add(new Border { Width = 30 });

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(15, 8, 15, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 120, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        cancelBtn.Click += OnCancelClick;
        row.Children.Add(cancelBtn);

        var doneBtn = new Button
        {
            Content = "Done",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 140, 80)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        doneBtn.Click += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        row.Children.Add(doneBtn);

        footer.Child = row;
        return footer;
    }

    private void OnFieldChanged()
    {
        if (_template == null) return;
        _template.Name = _nameBox?.Text ?? "";
        _template.Description = _descriptionBox?.Text ?? "";
        AutoSave();
    }

    private void RefreshUI()
    {
        if (_template == null) return;
        _nameBox!.Text = _template.Name ?? "";
        _descriptionBox!.Text = _template.Description ?? "";
        RefreshSections();
    }

    private void RefreshSections()
    {
        if (_sectionsPanel == null || _template == null) return;

        _sectionsPanel.Children.Clear();

        foreach (var section in _template.Sections ?? new List<TemplateSection>())
        {
            _sectionsPanel.Children.Add(BuildSectionEditor(section));
        }

        if ((_template.Sections?.Count ?? 0) == 0)
        {
            _sectionsPanel.Children.Add(new TextBlock
            {
                Text = "No sections yet. Click '+ Add Section' to get started.",
                FontSize = 14,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 30)
            });
        }
    }

    private FrameworkElement BuildSectionEditor(TemplateSection section)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var stack = new StackPanel { Spacing = 10 };

        // Section header row: title box + delete
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBox = new TextBox
        {
            Text = section.Title ?? "",
            PlaceholderText = "Section title...",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        titleBox.TextChanged += (s, e) => { section.Title = titleBox.Text; AutoSave(); };
        headerRow.Children.Add(titleBox);

        var removeBtn = new Button
        {
            Content = "", // Delete icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromArgb(255, 150, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(10, 0, 0, 0)
        };
        ToolTipService.SetToolTip(removeBtn, "Delete section");
        removeBtn.Click += (s, e) => OnRemoveSection(section);
        Grid.SetColumn(removeBtn, 1);
        headerRow.Children.Add(removeBtn);

        stack.Children.Add(headerRow);

        // Items container + add button. Charge sections show priced charge items;
        // everything else shows fields.
        var itemsPanel = new StackPanel { Spacing = 5, Margin = new Thickness(10, 5, 0, 5) };

        if (section.IsChargeSection)
        {
            foreach (var item in section.ChargeItems ?? new List<TemplateChargeItem>())
                itemsPanel.Children.Add(BuildChargeEditor(section, item, itemsPanel));

            stack.Children.Add(itemsPanel);
            stack.Children.Add(BuildAddButton("+ Add Item", () =>
            {
                section.ChargeItems ??= new List<TemplateChargeItem>();
                var newItem = new TemplateChargeItem
                {
                    Id = $"charge_{Guid.NewGuid():N}".Substring(0, 20),
                    Name = "New Charge",
                    DefaultAmount = 0,
                    IsEnabled = true,
                    Order = section.ChargeItems.Count
                };
                section.ChargeItems.Add(newItem);
                itemsPanel.Children.Add(BuildChargeEditor(section, newItem, itemsPanel));
                AutoSave();
            }));
        }
        else
        {
            foreach (var field in section.Fields ?? new List<TemplateField>())
                itemsPanel.Children.Add(BuildFieldEditor(section, field, itemsPanel));

            stack.Children.Add(itemsPanel);
            stack.Children.Add(BuildAddButton("+ Add Field", () =>
            {
                section.Fields ??= new List<TemplateField>();
                var newField = new TemplateField
                {
                    Id = $"field_{Guid.NewGuid():N}".Substring(0, 20),
                    Label = "New Field",
                    FieldType = FieldType.Text,
                    Order = section.Fields.Count
                };
                section.Fields.Add(newField);
                itemsPanel.Children.Add(BuildFieldEditor(section, newField, itemsPanel));
                AutoSave();
            }));
        }

        card.Child = stack;
        return card;
    }

    private Button BuildAddButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 80, 100)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(10, 5, 0, 0)
        };
        btn.Click += (s, e) => onClick();
        return btn;
    }

    private FrameworkElement BuildFieldEditor(TemplateSection section, TemplateField field, StackPanel parentPanel)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Label
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });                 // Type
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // Required
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // Delete

        // Label
        var labelBox = new TextBox
        {
            Text = field.Label ?? "",
            PlaceholderText = "Field label...",
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 5, 0)
        };
        labelBox.TextChanged += (s, e) => { field.Label = labelBox.Text; AutoSave(); };
        row.Children.Add(labelBox);

        // Type dropdown
        var typeCombo = new ComboBox
        {
            FontSize = 12,
            Margin = new Thickness(0, 0, 5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var ft in Enum.GetNames(typeof(FieldType)))
            typeCombo.Items.Add(new ComboBoxItem { Content = FriendlyFieldType(ft), Tag = ft });
        typeCombo.SelectedIndex = Array.IndexOf(Enum.GetNames(typeof(FieldType)), field.FieldType.ToString());
        typeCombo.SelectionChanged += (s, e) =>
        {
            if (typeCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag &&
                Enum.TryParse<FieldType>(tag, out var ft))
            {
                field.FieldType = ft;
                AutoSave();
            }
        };
        ToolTipService.SetToolTip(typeCombo, "Field type");
        Grid.SetColumn(typeCombo, 1);
        row.Children.Add(typeCombo);

        // Required checkbox
        var reqCheck = new CheckBox
        {
            IsChecked = field.IsRequired,
            Content = "",
            Margin = new Thickness(0, 0, 5, 0),
            MinWidth = 0
        };
        ToolTipService.SetToolTip(reqCheck, "Required field");
        reqCheck.Checked += (s, e) => { field.IsRequired = true; AutoSave(); };
        reqCheck.Unchecked += (s, e) => { field.IsRequired = false; AutoSave(); };
        Grid.SetColumn(reqCheck, 2);
        row.Children.Add(reqCheck);

        // Delete
        var deleteBtn = BuildDeleteButton(() =>
        {
            section.Fields?.Remove(field);
            parentPanel.Children.Remove(row);
            AutoSave();
        });
        Grid.SetColumn(deleteBtn, 3);
        row.Children.Add(deleteBtn);

        return row;
    }

    private FrameworkElement BuildChargeEditor(TemplateSection section, TemplateChargeItem item, StackPanel parentPanel)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });                 // Amount
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // Delete

        // Name
        var nameBox = new TextBox
        {
            Text = item.Name ?? "",
            PlaceholderText = "Charge name...",
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 5, 0)
        };
        nameBox.TextChanged += (s, e) => { item.Name = nameBox.Text; AutoSave(); };
        row.Children.Add(nameBox);

        // Default amount
        var amountBox = new NumberBox
        {
            Value = (double)item.DefaultAmount,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 1,
            LargeChange = 10,
            Margin = new Thickness(0, 0, 5, 0)
        };
        ToolTipService.SetToolTip(amountBox, "Default price");
        amountBox.ValueChanged += (s, e) =>
        {
            item.DefaultAmount = double.IsNaN(amountBox.Value) ? 0 : (decimal)amountBox.Value;
            AutoSave();
        };
        Grid.SetColumn(amountBox, 1);
        row.Children.Add(amountBox);

        // Delete
        var deleteBtn = BuildDeleteButton(() =>
        {
            section.ChargeItems?.Remove(item);
            parentPanel.Children.Remove(row);
            AutoSave();
        });
        Grid.SetColumn(deleteBtn, 2);
        row.Children.Add(deleteBtn);

        return row;
    }

    private Button BuildDeleteButton(Action onClick)
    {
        var btn = new Button
        {
            Content = "", // X icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(btn, "Delete");
        btn.Click += (s, e) => onClick();
        return btn;
    }

    private static string FriendlyFieldType(string ft) => ft switch
    {
        "MultilineText" => "Multi-line Text",
        _ => ft
    };

    private async void OnAddSection(object sender, RoutedEventArgs e)
    {
        if (_template == null) return;

        var textBox = new TextBox { PlaceholderText = "Section title...", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "Add Section",
            Content = textBox,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            _template.Sections ??= new List<TemplateSection>();
            _template.Sections.Add(new TemplateSection
            {
                Id = $"section_{Guid.NewGuid():N}".Substring(0, 20),
                Title = textBox.Text.Trim(),
                Icon = "",
                Order = _template.Sections.Count,
                Fields = new List<TemplateField>(),
                ChargeItems = new List<TemplateChargeItem>()
            });
            RefreshSections();
            AutoSave();
        }
    }

    private void OnRemoveSection(TemplateSection section)
    {
        _template?.Sections?.Remove(section);
        RefreshSections();
        AutoSave();
    }
}
