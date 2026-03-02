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
/// Editor view for customizing checklists.
/// Allows adding/removing sections, items, and editing all properties.
/// </summary>
public class ChecklistEditorView : UserControl
{
    private readonly CustomChecklistService _customService;
    private Checklist? _checklist;
    private bool _isCustom;

    // Header fields
    private TextBox? _titleBox;
    private TextBox? _shopNameBox;
    private TextBox? _descriptionBox;

    // Sections container
    private StackPanel? _sectionsPanel;

    // Events
    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    public ChecklistEditorView()
    {
        _customService = CustomChecklistService.Instance;
        BuildUI();
    }

    public void LoadChecklist(Checklist checklist, bool isCustom)
    {
        _checklist = checklist;
        _isCustom = isCustom;
        RefreshUI();
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

        // Header
        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Content - scrollable sections
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20)
        };

        _sectionsPanel = new StackPanel { Spacing = 15 };
        scroll.Content = _sectionsPanel;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // Footer with actions
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

        // Title row
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        titleRow.Children.Add(new TextBlock
        {
            Text = "Title:",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        _titleBox = new TextBox
        {
            PlaceholderText = "Checklist title...",
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _titleBox.TextChanged += (s, e) => OnFieldChanged();
        Grid.SetColumn(_titleBox, 1);
        titleRow.Children.Add(_titleBox);
        stack.Children.Add(titleRow);

        // Shop name row
        var shopRow = new Grid();
        shopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        shopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        shopRow.Children.Add(new TextBlock
        {
            Text = "Shop Name:",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        _shopNameBox = new TextBox
        {
            PlaceholderText = "Your shop name...",
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _shopNameBox.TextChanged += (s, e) => OnFieldChanged();
        Grid.SetColumn(_shopNameBox, 1);
        shopRow.Children.Add(_shopNameBox);
        stack.Children.Add(shopRow);

        // Description row
        var descRow = new Grid();
        descRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        descRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        descRow.Children.Add(new TextBlock
        {
            Text = "Description:",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        _descriptionBox = new TextBox
        {
            PlaceholderText = "Brief description...",
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _descriptionBox.TextChanged += (s, e) => OnFieldChanged();
        Grid.SetColumn(_descriptionBox, 1);
        descRow.Children.Add(_descriptionBox);
        stack.Children.Add(descRow);

        header.Child = stack;
        return header;
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

        // Add Section button
        var addSectionBtn = new Button
        {
            Content = "+ Add Section",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        addSectionBtn.Click += OnAddSection;
        row.Children.Add(addSectionBtn);

        // Spacer
        row.Children.Add(new Border { Width = 30 });

        // Cancel button
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        cancelBtn.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
        row.Children.Add(cancelBtn);

        // Save button
        var saveBtn = new Button
        {
            Content = "Save Changes",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 140, 80)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        saveBtn.Click += OnSave;
        row.Children.Add(saveBtn);

        footer.Child = row;
        return footer;
    }

    private void RefreshUI()
    {
        if (_checklist == null) return;

        _titleBox!.Text = _checklist.Title ?? "";
        _shopNameBox!.Text = _checklist.ShopName ?? "";
        _descriptionBox!.Text = _checklist.Description ?? "";

        RefreshSections();
    }

    private void RefreshSections()
    {
        if (_sectionsPanel == null || _checklist == null) return;

        _sectionsPanel.Children.Clear();

        foreach (var section in _checklist.Sections ?? new List<ChecklistSection>())
        {
            _sectionsPanel.Children.Add(BuildSectionEditor(section));
        }

        // Empty state
        if ((_checklist.Sections?.Count ?? 0) == 0)
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

    private FrameworkElement BuildSectionEditor(ChecklistSection section)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var stack = new StackPanel { Spacing = 10 };

        // Section header row
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
        titleBox.TextChanged += (s, e) => section.Title = titleBox.Text;
        headerRow.Children.Add(titleBox);

        var removeBtn = new Button
        {
            Content = "\uE74D", // Delete icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromArgb(255, 150, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(10, 0, 0, 0)
        };
        removeBtn.Click += (s, e) => OnRemoveSection(section);
        Grid.SetColumn(removeBtn, 1);
        headerRow.Children.Add(removeBtn);

        stack.Children.Add(headerRow);

        // Items container
        var itemsPanel = new StackPanel { Spacing = 5, Margin = new Thickness(10, 5, 0, 5) };

        foreach (var item in section.Items ?? new List<ChecklistItem>())
        {
            itemsPanel.Children.Add(BuildItemEditor(section, item, itemsPanel));
        }

        stack.Children.Add(itemsPanel);

        // Add item button
        var addItemBtn = new Button
        {
            Content = "+ Add Item",
            FontSize = 12,
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 80, 100)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(10, 5, 0, 0)
        };
        addItemBtn.Click += (s, e) =>
        {
            section.Items ??= new List<ChecklistItem>();
            var newItem = new ChecklistItem
            {
                Id = $"item-{Guid.NewGuid():N}".Substring(0, 14),
                Text = "New item",
                Required = false
            };
            section.Items.Add(newItem);
            itemsPanel.Children.Add(BuildItemEditor(section, newItem, itemsPanel));
        };
        stack.Children.Add(addItemBtn);

        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildItemEditor(ChecklistSection section, ChecklistItem item, StackPanel parentPanel)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Required checkbox
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Text
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Delete button

        // Required checkbox
        var reqCheck = new CheckBox
        {
            IsChecked = item.Required,
            Content = "",
            Margin = new Thickness(0, 0, 5, 0)
        };
        ToolTipService.SetToolTip(reqCheck, "Required item");
        reqCheck.Checked += (s, e) => item.Required = true;
        reqCheck.Unchecked += (s, e) => item.Required = false;
        row.Children.Add(reqCheck);

        // Text box
        var textBox = new TextBox
        {
            Text = item.Text ?? "",
            PlaceholderText = "Item text...",
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 5, 0)
        };
        textBox.TextChanged += (s, e) => item.Text = textBox.Text;
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);

        // Delete button
        var deleteBtn = new Button
        {
            Content = "\uE711", // X icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        deleteBtn.Click += (s, e) =>
        {
            section.Items?.Remove(item);
            parentPanel.Children.Remove(row);
        };
        Grid.SetColumn(deleteBtn, 2);
        row.Children.Add(deleteBtn);

        return row;
    }

    private void OnFieldChanged()
    {
        if (_checklist == null) return;

        _checklist.Title = _titleBox?.Text ?? "";
        _checklist.ShopName = _shopNameBox?.Text ?? "";
        _checklist.Description = _descriptionBox?.Text ?? "";
    }

    private async void OnAddSection(object sender, RoutedEventArgs e)
    {
        if (_checklist == null) return;

        var dialog = new ContentDialog
        {
            Title = "Add Section",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var textBox = new TextBox
        {
            PlaceholderText = "Section title...",
            Width = 300
        };
        dialog.Content = textBox;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            _checklist.Sections ??= new List<ChecklistSection>();
            _checklist.Sections.Add(new ChecklistSection
            {
                Id = $"sec-{Guid.NewGuid():N}".Substring(0, 12),
                Title = textBox.Text.Trim(),
                Icon = "E73E",
                Items = new List<ChecklistItem>()
            });
            RefreshSections();
        }
    }

    private void OnRemoveSection(ChecklistSection section)
    {
        _checklist?.Sections?.Remove(section);
        RefreshSections();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_checklist == null) return;

        // Ensure it's saved as a custom checklist
        if (!_isCustom)
        {
            // This was a built-in checklist that was duplicated - save as custom
            _customService.SaveChecklist(_checklist);
        }
        else
        {
            _customService.SaveChecklist(_checklist);
        }

        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    public Checklist? GetChecklist() => _checklist;
}
