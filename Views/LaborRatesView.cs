#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Dealer Information View - Searchable dealer database
/// Manages contacts, labor rates, parts discount, delivery/returns info
/// </summary>
public sealed class LaborRatesView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 212);
    private static readonly Color AccentOrange = Color.FromArgb(255, 220, 140, 30);
    private static readonly Color AccentPurple = Color.FromArgb(255, 140, 80, 200);

    private readonly LaborRatesService _laborService = LaborRatesService.Instance;
    private TextBox? _searchBox;
    private ComboBox? _manufacturerFilter;
    private StackPanel? _dealerList;
    private ScrollViewer? _scrollViewer;
    private InfoBar? _infoBar;
    private string _currentSearch = "";
    private string _currentManufacturer = "";
    private bool _isUpdatingFilter = false;

    public LaborRatesView()
    {
        _laborService.DataChanged += (s, e) => RefreshList();
        BuildUI();
        RefreshList();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var header = CreateHeader();
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        _scrollViewer = new ScrollViewer { Padding = new Thickness(16) };
        _dealerList = new StackPanel { Spacing = 8 };
        _scrollViewer.Content = _dealerList;
        Grid.SetRow(_scrollViewer, 1);
        mainGrid.Children.Add(_scrollViewer);

        var footer = CreateFooter();
        Grid.SetRow(footer, 2);
        mainGrid.Children.Add(footer);

        _infoBar = new InfoBar
        {
            IsOpen = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0)
        };
        mainGrid.Children.Add(_infoBar);

        Content = mainGrid;
    }

    private Border CreateHeader()
    {
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerContent = new StackPanel { Spacing = 12 };

        headerContent.Children.Add(new TextBlock
        {
            Text = "Dealer Information",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        headerContent.Children.Add(new TextBlock
        {
            Text = "Manage dealer contacts, labor rates, and parts information. Search by name, manufacturer, contact, or address.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            TextWrapping = TextWrapping.Wrap
        });

        var searchRow = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        _searchBox = new TextBox
        {
            PlaceholderText = "Search dealers... (name, manufacturer, contact, address, phone)",
            Margin = new Thickness(0, 0, 12, 0)
        };
        _searchBox.TextChanged += (s, e) =>
        {
            _currentSearch = _searchBox.Text;
            RefreshList();
        };
        Grid.SetColumn(_searchBox, 0);
        searchRow.Children.Add(_searchBox);

        _manufacturerFilter = new ComboBox
        {
            PlaceholderText = "All Manufacturers",
            MinWidth = 180
        };
        _manufacturerFilter.SelectionChanged += (s, e) =>
        {
            if (_isUpdatingFilter) return;

            if (_manufacturerFilter.SelectedItem is ComboBoxItem item)
            {
                _currentManufacturer = item.Tag?.ToString() ?? "";
            }
            else
            {
                _currentManufacturer = "";
            }
            RefreshList();
        };
        Grid.SetColumn(_manufacturerFilter, 1);
        searchRow.Children.Add(_manufacturerFilter);

        headerContent.Children.Add(searchRow);
        header.Child = headerContent;
        return header;
    }

    private Border CreateFooter()
    {
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var footerContent = new Grid();
        footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var countText = new TextBlock
        {
            Text = "0 dealers",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            VerticalAlignment = VerticalAlignment.Center
        };
        countText.Name = "CountText";
        Grid.SetColumn(countText, 0);
        footerContent.Children.Add(countText);

        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 16 });
        addContent.Children.Add(new TextBlock { Text = "Add Dealer", VerticalAlignment = VerticalAlignment.Center });

        var addButton = new Button
        {
            Content = addContent,
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush(AccentGreen),
            Foreground = new SolidColorBrush(Colors.White)
        };
        addButton.Click += OnAddDealerClick;
        Grid.SetColumn(addButton, 1);
        footerContent.Children.Add(addButton);

        footer.Child = footerContent;
        return footer;
    }

    private void RefreshList()
    {
        if (_dealerList == null) return;
        _dealerList.Children.Clear();

        UpdateManufacturerFilter();

        List<DealerLaborRate> dealers;
        if (!string.IsNullOrWhiteSpace(_currentSearch))
        {
            dealers = _laborService.Search(_currentSearch);
        }
        else if (!string.IsNullOrWhiteSpace(_currentManufacturer))
        {
            dealers = _laborService.GetByManufacturer(_currentManufacturer);
        }
        else
        {
            dealers = _laborService.GetAllDealers();
        }

        if (!string.IsNullOrWhiteSpace(_currentSearch) && !string.IsNullOrWhiteSpace(_currentManufacturer))
        {
            dealers = dealers.Where(d =>
                d.Manufacturer?.Equals(_currentManufacturer, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (dealers.Count == 0)
        {
            _dealerList.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_currentSearch) && string.IsNullOrWhiteSpace(_currentManufacturer)
                    ? "No dealers added yet. Click 'Add Dealer' to get started."
                    : "No dealers match your search.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
        }
        else
        {
            foreach (var dealer in dealers)
            {
                _dealerList.Children.Add(CreateDealerCard(dealer));
            }
        }

        UpdateCount(dealers.Count);
    }

    private void UpdateManufacturerFilter()
    {
        if (_manufacturerFilter == null || _isUpdatingFilter) return;
        _isUpdatingFilter = true;

        try
        {
            var currentSelection = _currentManufacturer;
            _manufacturerFilter.Items.Clear();

            var allItem = new ComboBoxItem { Content = "All Manufacturers", Tag = "" };
            _manufacturerFilter.Items.Add(allItem);

            foreach (var mfr in _laborService.GetManufacturers())
            {
                var item = new ComboBoxItem { Content = mfr, Tag = mfr };
                _manufacturerFilter.Items.Add(item);

                if (mfr == currentSelection)
                {
                    _manufacturerFilter.SelectedItem = item;
                }
            }

            if (string.IsNullOrEmpty(currentSelection))
            {
                _manufacturerFilter.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingFilter = false;
        }
    }

    private void UpdateCount(int count)
    {
        if (Content is Grid mainGrid)
        {
            foreach (var child in mainGrid.Children)
            {
                if (child is Border border && Grid.GetRow(border) == 2)
                {
                    if (border.Child is Grid footerGrid)
                    {
                        foreach (var footerChild in footerGrid.Children)
                        {
                            if (footerChild is TextBlock tb && tb.Name == "CountText")
                            {
                                tb.Text = $"{count} dealer{(count != 1 ? "s" : "")}";
                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    #region Dealer Card

    private Border CreateDealerCard(DealerLaborRate dealer)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var cardContent = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Left side - dealer info
        var infoPanel = new StackPanel { Spacing = 6 };

        // Row 1: Name + Manufacturer badge + Example badge
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        nameRow.Children.Add(new TextBlock
        {
            Text = dealer.DealerName ?? "Unknown Dealer",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        if (!string.IsNullOrEmpty(dealer.Manufacturer))
        {
            nameRow.Children.Add(CreateBadge(dealer.Manufacturer, AccentBlue));
        }

        if (dealer.IsExample)
        {
            nameRow.Children.Add(CreateBadge("Example", AccentOrange));
        }
        infoPanel.Children.Add(nameRow);

        // Row 2: Address
        var addressStr = FormatAddress(dealer);
        if (!string.IsNullOrEmpty(addressStr))
        {
            var addressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            addressRow.Children.Add(new FontIcon
            {
                Glyph = "\uE81D",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            addressRow.Children.Add(new TextBlock
            {
                Text = addressStr,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170))
            });
            infoPanel.Children.Add(addressRow);
        }

        // Row 3: Contact people
        if (dealer.Contacts.Count > 0)
        {
            var contactsPanel = new StackPanel { Spacing = 2 };
            foreach (var contact in dealer.Contacts)
            {
                var contactRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                contactRow.Children.Add(new FontIcon
                {
                    Glyph = "\uE77B",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                });

                var contactText = "";
                if (!string.IsNullOrEmpty(contact.Role))
                    contactText += $"[{contact.Role}] ";
                contactText += contact.Name ?? "Unknown";
                if (contact.PhoneNumbers.Count > 0)
                    contactText += $" - {contact.PhoneNumbers[0]}";
                if (contact.Emails.Count > 0)
                    contactText += $" - {contact.Emails[0]}";

                contactRow.Children.Add(new TextBlock
                {
                    Text = contactText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170))
                });
                contactsPanel.Children.Add(contactRow);
            }
            infoPanel.Children.Add(contactsPanel);
        }

        // Row 4: Dealer-level phones + emails
        var dealerContactInfo = new List<string>();
        foreach (var ph in dealer.PhoneNumbers)
        {
            if (!string.IsNullOrWhiteSpace(ph)) dealerContactInfo.Add(ph);
        }
        foreach (var em in dealer.Emails)
        {
            if (!string.IsNullOrWhiteSpace(em)) dealerContactInfo.Add(em);
        }
        if (dealerContactInfo.Count > 0)
        {
            var contactInfoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            contactInfoRow.Children.Add(new FontIcon
            {
                Glyph = "\uE717",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
            contactInfoRow.Children.Add(new TextBlock
            {
                Text = string.Join("  |  ", dealerContactInfo),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 600
            });
            infoPanel.Children.Add(contactInfoRow);
        }

        // Row 5: Parts / Delivery / Returns chips
        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bool hasChips = false;

        if (dealer.PartsDiscountPercent > 0)
        {
            chipsPanel.Children.Add(CreateChip($"{dealer.PartsDiscountPercent}% Parts Discount", AccentGreen));
            hasChips = true;
        }

        if (dealer.Returns.AcceptsReturns)
        {
            var returnLabel = "Accepts Returns";
            if (!string.IsNullOrEmpty(dealer.Returns.ReturnsNote))
                returnLabel += $" ({dealer.Returns.ReturnsNote})";
            chipsPanel.Children.Add(CreateChip(returnLabel, AccentBlue));
            hasChips = true;
        }

        if (dealer.Delivery.DeliversParts)
        {
            var deliveryLabel = "Delivers Parts";
            var deliveryDetails = new List<string>();
            if (!string.IsNullOrEmpty(dealer.Delivery.DeliveryTime))
                deliveryDetails.Add(dealer.Delivery.DeliveryTime);
            if (dealer.Delivery.RunsPerDay > 0)
                deliveryDetails.Add($"{dealer.Delivery.RunsPerDay}x/day");
            if (deliveryDetails.Count > 0)
                deliveryLabel += $" ({string.Join(", ", deliveryDetails)})";
            chipsPanel.Children.Add(CreateChip(deliveryLabel, AccentPurple));
            hasChips = true;
        }

        if (hasChips)
            infoPanel.Children.Add(chipsPanel);

        // Row 6: Labor rate badges
        var ratesPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        bool hasRates = false;
        if (dealer.MechLaborRate > 0) { AddRateBadge(ratesPanel, "Mech", dealer.MechLaborRate); hasRates = true; }
        if (dealer.BodyLaborRate > 0) { AddRateBadge(ratesPanel, "Body", dealer.BodyLaborRate); hasRates = true; }
        if (dealer.PaintLaborRate > 0) { AddRateBadge(ratesPanel, "Paint", dealer.PaintLaborRate); hasRates = true; }
        if (dealer.FrameLaborRate > 0) { AddRateBadge(ratesPanel, "Frame", dealer.FrameLaborRate); hasRates = true; }
        if (dealer.GlassLaborRate > 0) { AddRateBadge(ratesPanel, "Glass", dealer.GlassLaborRate); hasRates = true; }
        if (hasRates)
            infoPanel.Children.Add(ratesPanel);

        // Row 7: Notes preview
        if (!string.IsNullOrEmpty(dealer.Notes))
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = dealer.Notes,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 500,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        }

        Grid.SetColumn(infoPanel, 0);
        cardContent.Children.Add(infoPanel);

        // Right side - actions
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 },
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        editBtn.Click += (s, e) => ShowEditDialog(dealer);
        actionsPanel.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        deleteBtn.Click += async (s, e) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Dealer",
                Content = $"Are you sure you want to delete {dealer.DealerName}?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _laborService.DeleteDealer(dealer.Id!);
                ShowNotification($"Deleted {dealer.DealerName}", InfoBarSeverity.Success);
            }
        };
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(actionsPanel, 1);
        cardContent.Children.Add(actionsPanel);

        card.Child = cardContent;
        return card;
    }

    private static string FormatAddress(DealerLaborRate dealer)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(dealer.Address)) parts.Add(dealer.Address);

        var cityStateZip = new List<string>();
        if (!string.IsNullOrWhiteSpace(dealer.City)) cityStateZip.Add(dealer.City);
        if (!string.IsNullOrWhiteSpace(dealer.State)) cityStateZip.Add(dealer.State);
        if (cityStateZip.Count > 0) parts.Add(string.Join(", ", cityStateZip));
        if (!string.IsNullOrWhiteSpace(dealer.Zip)) parts.Add(dealer.Zip);

        return string.Join(", ", parts);
    }

    private static Border CreateBadge(string text, Color color)
    {
        return new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            }
        };
    }

    private static Border CreateChip(string text, Color color)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B))
            }
        };
    }

    private void AddRateBadge(StackPanel panel, string label, decimal rate)
    {
        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        badge.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
        });
        badge.Children.Add(new TextBlock
        {
            Text = $"${rate:N2}",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentGreen)
        });
        panel.Children.Add(badge);
    }

    #endregion

    #region Add/Edit Dialog

    private async void OnAddDealerClick(object sender, RoutedEventArgs e)
    {
        await ShowDealerDialog(null);
    }

    private async void ShowEditDialog(DealerLaborRate dealer)
    {
        await ShowDealerDialog(dealer);
    }

    private async System.Threading.Tasks.Task ShowDealerDialog(DealerLaborRate? existing)
    {
        bool isEdit = existing != null;

        // Track dynamic lists
        var phoneRows = new List<TextBox>();
        var emailRows = new List<TextBox>();
        var contactCards = new List<(TextBox Name, ComboBox Role, TextBox Phone, TextBox Email, StackPanel Container)>();

        var dialogContent = new ScrollViewer
        {
            MaxHeight = 550,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var contentStack = new StackPanel { Spacing = 16, Width = 500 };

        // === Section 1: Basic Info ===
        contentStack.Children.Add(CreateSectionHeader("Basic Info"));

        var nameBox = new TextBox
        {
            Header = "Dealer Name",
            PlaceholderText = "e.g., AutoNation Ford",
            Text = existing?.DealerName ?? ""
        };
        contentStack.Children.Add(nameBox);

        var mfrBox = new TextBox
        {
            Header = "Manufacturer (for searching/filtering)",
            PlaceholderText = "e.g., Ford, Mercedes, BMW",
            Text = existing?.Manufacturer ?? ""
        };
        contentStack.Children.Add(mfrBox);

        // === Section 2: Location ===
        contentStack.Children.Add(CreateSectionHeader("Location"));

        var addressBox = new TextBox
        {
            Header = "Address",
            PlaceholderText = "1234 Motor Way",
            Text = existing?.Address ?? ""
        };
        contentStack.Children.Add(addressBox);

        var locationRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 8
        };

        var cityBox = new TextBox { Header = "City", PlaceholderText = "Springfield", Text = existing?.City ?? "" };
        Grid.SetColumn(cityBox, 0);
        locationRow.Children.Add(cityBox);

        var stateBox = new TextBox { Header = "State", PlaceholderText = "IL", Text = existing?.State ?? "" };
        Grid.SetColumn(stateBox, 1);
        locationRow.Children.Add(stateBox);

        var zipBox = new TextBox { Header = "Zip", PlaceholderText = "62701", Text = existing?.Zip ?? "" };
        Grid.SetColumn(zipBox, 2);
        locationRow.Children.Add(zipBox);

        contentStack.Children.Add(locationRow);

        // === Section 3: Dealer Phones & Emails ===
        contentStack.Children.Add(CreateSectionHeader("Dealer Phones & Emails"));

        var phonesContainer = new StackPanel { Spacing = 4 };
        contentStack.Children.Add(phonesContainer);

        // Seed existing phones
        if (existing != null && existing.PhoneNumbers.Count > 0)
        {
            foreach (var ph in existing.PhoneNumbers)
                AddDynamicRow(phonesContainer, phoneRows, "Phone", "(555) 123-4567", ph);
        }
        else
        {
            AddDynamicRow(phonesContainer, phoneRows, "Phone", "(555) 123-4567", "");
        }

        var addPhoneBtn = new HyperlinkButton { Content = "+ Add Phone" };
        addPhoneBtn.Click += (s, e) => AddDynamicRow(phonesContainer, phoneRows, "Phone", "(555) 123-4567", "");
        contentStack.Children.Add(addPhoneBtn);

        var emailsContainer = new StackPanel { Spacing = 4 };
        contentStack.Children.Add(emailsContainer);

        if (existing != null && existing.Emails.Count > 0)
        {
            foreach (var em in existing.Emails)
                AddDynamicRow(emailsContainer, emailRows, "Email", "parts@dealer.com", em);
        }
        else
        {
            AddDynamicRow(emailsContainer, emailRows, "Email", "parts@dealer.com", "");
        }

        var addEmailBtn = new HyperlinkButton { Content = "+ Add Email" };
        addEmailBtn.Click += (s, e) => AddDynamicRow(emailsContainer, emailRows, "Email", "parts@dealer.com", "");
        contentStack.Children.Add(addEmailBtn);

        // === Section 4: Contact People ===
        contentStack.Children.Add(CreateSectionHeader("Contact People"));

        var contactsContainer = new StackPanel { Spacing = 8 };
        contentStack.Children.Add(contactsContainer);

        if (existing != null && existing.Contacts.Count > 0)
        {
            foreach (var c in existing.Contacts)
                AddContactCard(contactsContainer, contactCards, c.Name ?? "", c.Role ?? "",
                    c.PhoneNumbers.FirstOrDefault() ?? "", c.Emails.FirstOrDefault() ?? "");
        }

        var addContactBtn = new HyperlinkButton { Content = "+ Add Contact Person" };
        addContactBtn.Click += (s, e) => AddContactCard(contactsContainer, contactCards, "", "", "", "");
        contentStack.Children.Add(addContactBtn);

        // === Section 5: Parts & Delivery ===
        contentStack.Children.Add(CreateSectionHeader("Parts & Delivery"));

        var discountBox = new NumberBox
        {
            Header = "Parts Discount %",
            Value = (double)(existing?.PartsDiscountPercent ?? 0),
            Minimum = 0,
            Maximum = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        contentStack.Children.Add(discountBox);

        // Returns
        var returnsCheck = new CheckBox
        {
            Content = "Accepts Returns",
            IsChecked = existing?.Returns.AcceptsReturns ?? false
        };
        contentStack.Children.Add(returnsCheck);

        var returnsNoteBox = new TextBox
        {
            PlaceholderText = "Returns note (e.g., Within 30 days with receipt)",
            Text = existing?.Returns.ReturnsNote ?? "",
            Margin = new Thickness(28, 0, 0, 0)
        };
        contentStack.Children.Add(returnsNoteBox);

        // Delivery
        var deliveryCheck = new CheckBox
        {
            Content = "Delivers Parts",
            IsChecked = existing?.Delivery.DeliversParts ?? false
        };
        contentStack.Children.Add(deliveryCheck);

        var deliveryNoteBox = new TextBox
        {
            PlaceholderText = "Delivery note (e.g., Mon-Fri only)",
            Text = existing?.Delivery.DeliveryNote ?? "",
            Margin = new Thickness(28, 0, 0, 0)
        };
        contentStack.Children.Add(deliveryNoteBox);

        var deliveryDetailsRow = new Grid
        {
            Margin = new Thickness(28, 0, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 8
        };

        var deliveryTimeBox = new TextBox
        {
            Header = "Delivery Time",
            PlaceholderText = "10:00 AM",
            Text = existing?.Delivery.DeliveryTime ?? ""
        };
        Grid.SetColumn(deliveryTimeBox, 0);
        deliveryDetailsRow.Children.Add(deliveryTimeBox);

        var runsPerDayBox = new NumberBox
        {
            Header = "Runs/Day",
            Value = existing?.Delivery.RunsPerDay ?? 0,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        Grid.SetColumn(runsPerDayBox, 1);
        deliveryDetailsRow.Children.Add(runsPerDayBox);

        contentStack.Children.Add(deliveryDetailsRow);

        // === Section 6: Labor Rates ===
        contentStack.Children.Add(CreateSectionHeader("Labor Rates ($/hr)"));

        var ratesGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnSpacing = 12,
            RowSpacing = 8
        };

        var mechRate = new NumberBox { Header = "Mechanical", Value = (double)(existing?.MechLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(mechRate, 0); Grid.SetColumn(mechRate, 0);
        ratesGrid.Children.Add(mechRate);

        var bodyRate = new NumberBox { Header = "Body", Value = (double)(existing?.BodyLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(bodyRate, 0); Grid.SetColumn(bodyRate, 1);
        ratesGrid.Children.Add(bodyRate);

        var paintRate = new NumberBox { Header = "Paint", Value = (double)(existing?.PaintLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(paintRate, 1); Grid.SetColumn(paintRate, 0);
        ratesGrid.Children.Add(paintRate);

        var frameRate = new NumberBox { Header = "Frame", Value = (double)(existing?.FrameLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(frameRate, 1); Grid.SetColumn(frameRate, 1);
        ratesGrid.Children.Add(frameRate);

        var glassRate = new NumberBox { Header = "Glass", Value = (double)(existing?.GlassLaborRate ?? 0), Minimum = 0 };
        Grid.SetRow(glassRate, 2); Grid.SetColumn(glassRate, 0);
        ratesGrid.Children.Add(glassRate);

        contentStack.Children.Add(ratesGrid);

        // === Section 7: Notes ===
        contentStack.Children.Add(CreateSectionHeader("Notes"));

        var notesBox = new TextBox
        {
            PlaceholderText = "Any additional info...",
            Text = existing?.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 60
        };
        contentStack.Children.Add(notesBox);

        dialogContent.Content = contentStack;

        var dialog = new ContentDialog
        {
            Title = isEdit ? "Edit Dealer" : "Add New Dealer",
            Content = dialogContent,
            PrimaryButtonText = isEdit ? "Save" : "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                ShowNotification("Please enter a dealer name", InfoBarSeverity.Warning);
                return;
            }

            var dealer = existing ?? new DealerLaborRate();
            dealer.DealerName = nameBox.Text.Trim();
            dealer.Manufacturer = mfrBox.Text.Trim();
            dealer.Address = addressBox.Text.Trim();
            dealer.City = cityBox.Text.Trim();
            dealer.State = stateBox.Text.Trim();
            dealer.Zip = zipBox.Text.Trim();

            // Collect phones
            dealer.PhoneNumbers = phoneRows
                .Select(tb => tb.Text.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            // Collect emails
            dealer.Emails = emailRows
                .Select(tb => tb.Text.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            // Collect contacts
            dealer.Contacts = contactCards
                .Where(c => !string.IsNullOrWhiteSpace(c.Name.Text))
                .Select(c =>
                {
                    var role = (c.Role.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                    var contact = new DealerContact
                    {
                        Name = c.Name.Text.Trim(),
                        Role = role,
                        PhoneNumbers = new List<string>(),
                        Emails = new List<string>()
                    };
                    if (!string.IsNullOrWhiteSpace(c.Phone.Text))
                        contact.PhoneNumbers.Add(c.Phone.Text.Trim());
                    if (!string.IsNullOrWhiteSpace(c.Email.Text))
                        contact.Emails.Add(c.Email.Text.Trim());
                    return contact;
                })
                .ToList();

            dealer.PartsDiscountPercent = (decimal)discountBox.Value;
            dealer.Returns = new ReturnsInfo
            {
                AcceptsReturns = returnsCheck.IsChecked == true,
                ReturnsNote = returnsNoteBox.Text.Trim()
            };
            dealer.Delivery = new DeliveryInfo
            {
                DeliversParts = deliveryCheck.IsChecked == true,
                DeliveryNote = deliveryNoteBox.Text.Trim(),
                DeliveryTime = deliveryTimeBox.Text.Trim(),
                RunsPerDay = (int)runsPerDayBox.Value
            };

            dealer.MechLaborRate = (decimal)mechRate.Value;
            dealer.BodyLaborRate = (decimal)bodyRate.Value;
            dealer.PaintLaborRate = (decimal)paintRate.Value;
            dealer.FrameLaborRate = (decimal)frameRate.Value;
            dealer.GlassLaborRate = (decimal)glassRate.Value;
            dealer.Notes = notesBox.Text.Trim();

            // Clear IsExample flag when user edits the example dealer
            if (isEdit && dealer.IsExample)
                dealer.IsExample = false;

            if (isEdit)
            {
                _laborService.UpdateDealer(dealer);
                ShowNotification($"Updated {dealer.DealerName}", InfoBarSeverity.Success);
            }
            else
            {
                _laborService.AddDealer(dealer);
                ShowNotification($"Added {dealer.DealerName}", InfoBarSeverity.Success);
            }
        }
    }

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    private void AddDynamicRow(StackPanel container, List<TextBox> trackedList, string label, string placeholder, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 4
        };

        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = value
        };
        Grid.SetColumn(textBox, 0);
        row.Children.Add(textBox);
        trackedList.Add(textBox);

        var removeBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center
        };
        removeBtn.Click += (s, e) =>
        {
            container.Children.Remove(row);
            trackedList.Remove(textBox);
        };
        Grid.SetColumn(removeBtn, 1);
        row.Children.Add(removeBtn);

        container.Children.Add(row);
    }

    private void AddContactCard(StackPanel container,
        List<(TextBox Name, ComboBox Role, TextBox Phone, TextBox Email, StackPanel Container)> trackedList,
        string name, string role, string phone, string email)
    {
        var card = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            CornerRadius = new CornerRadius(6)
        };

        // Name + Role row
        var topRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var nameBox = new TextBox { PlaceholderText = "Contact name", Text = name };
        Grid.SetColumn(nameBox, 0);
        topRow.Children.Add(nameBox);

        var roleBox = new ComboBox { PlaceholderText = "Role", MinWidth = 140 };
        var roles = new[] { "Manager", "Parts Person", "Delivery Driver", "Other" };
        foreach (var r in roles)
        {
            var item = new ComboBoxItem { Content = r };
            roleBox.Items.Add(item);
            if (r.Equals(role, StringComparison.OrdinalIgnoreCase))
                roleBox.SelectedItem = item;
        }
        Grid.SetColumn(roleBox, 1);
        topRow.Children.Add(roleBox);

        var removeBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(removeBtn, 2);
        topRow.Children.Add(removeBtn);

        card.Children.Add(topRow);

        // Phone + Email row
        var bottomRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 8
        };

        var phoneBox = new TextBox { PlaceholderText = "Phone", Text = phone };
        Grid.SetColumn(phoneBox, 0);
        bottomRow.Children.Add(phoneBox);

        var emailBox = new TextBox { PlaceholderText = "Email", Text = email };
        Grid.SetColumn(emailBox, 1);
        bottomRow.Children.Add(emailBox);

        card.Children.Add(bottomRow);

        var entry = (nameBox, roleBox, phoneBox, emailBox, card);
        trackedList.Add(entry);

        removeBtn.Click += (s, e) =>
        {
            container.Children.Remove(card);
            trackedList.Remove(entry);
        };

        container.Children.Add(card);
    }

    #endregion

    private void ShowNotification(string message, InfoBarSeverity severity)
    {
        if (_infoBar == null) return;

        _infoBar.Message = message;
        _infoBar.Severity = severity;
        _infoBar.IsOpen = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            _infoBar.IsOpen = false;
        };
        timer.Start();
    }
}
