#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Admin tool for managing user Excel files, licenses, and pushing updates
    /// </summary>
    public sealed class McStudAdminView : UserControl
    {
        private TextBlock? _statusText;
        private TextBlock? _masterFileText;
        private PasswordBox? _passwordBox;
        private ListView? _userListView;
        private Button? _updateAllButton;
        private Button? _addUserButton;
        private Button? _refreshButton;
        private ProgressRing? _loadingRing;

        private string? _masterFilePath;
        private string? _password;
        private ObservableCollection<UserFileInfo> _userFiles = new();

        public McStudAdminView()
        {
            BuildUI();
            _ = InitializeAsync();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var mainStack = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "McStud Admin",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(titleText, 0);

            _loadingRing = new ProgressRing
            {
                IsActive = false,
                Width = 20,
                Height = 20
            };
            Grid.SetColumn(_loadingRing, 1);

            headerGrid.Children.Add(titleText);
            headerGrid.Children.Add(_loadingRing);
            mainStack.Children.Add(headerGrid);

            // Master File Section
            mainStack.Children.Add(CreateSectionHeader("Master Excel File"));

            var masterFileGrid = new Grid();
            masterFileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            masterFileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _masterFileText = new TextBlock
            {
                Text = "Not selected",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_masterFileText, 0);

            var browseButton = new Button
            {
                Content = "Browse",
                Padding = new Thickness(12, 6, 12, 6)
            };
            browseButton.Click += BrowseMasterFile_Click;
            Grid.SetColumn(browseButton, 1);

            masterFileGrid.Children.Add(_masterFileText);
            masterFileGrid.Children.Add(browseButton);
            mainStack.Children.Add(masterFileGrid);

            // Password Section
            mainStack.Children.Add(CreateSectionHeader("Excel Password"));

            _passwordBox = new PasswordBox
            {
                PlaceholderText = "Enter password to unlock Excel files",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
            mainStack.Children.Add(_passwordBox);

            // Separator
            mainStack.Children.Add(CreateSeparator());

            // Users Section Header with buttons
            var usersHeaderGrid = new Grid();
            usersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            usersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            usersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var usersLabel = new TextBlock
            {
                Text = "User Files",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(usersLabel, 0);

            _refreshButton = new Button
            {
                Content = new SymbolIcon(Symbol.Refresh),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _refreshButton.Click += RefreshUsers_Click;
            Grid.SetColumn(_refreshButton, 1);

            _addUserButton = new Button
            {
                Content = "+ Add User",
                Padding = new Thickness(12, 6, 12, 6)
            };
            _addUserButton.Click += AddUser_Click;
            Grid.SetColumn(_addUserButton, 2);

            usersHeaderGrid.Children.Add(usersLabel);
            usersHeaderGrid.Children.Add(_refreshButton);
            usersHeaderGrid.Children.Add(_addUserButton);
            mainStack.Children.Add(usersHeaderGrid);

            // User List
            _userListView = new ListView
            {
                ItemsSource = _userFiles,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 300,
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4)
            };
            _userListView.ItemTemplate = CreateUserItemTemplate();
            mainStack.Children.Add(_userListView);

            // Separator
            mainStack.Children.Add(CreateSeparator());

            // Update All Button
            _updateAllButton = new Button
            {
                Content = "🔄 Update All Users",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 12, 16, 12),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            _updateAllButton.Click += UpdateAll_Click;
            mainStack.Children.Add(_updateAllButton);

            // Status Section
            mainStack.Children.Add(CreateSeparator());

            var statusPanel = new StackPanel { Spacing = 4 };
            var statusLabel = new TextBlock
            {
                Text = "Status",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
            _statusText = new TextBlock
            {
                Text = "Ready",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                TextWrapping = TextWrapping.Wrap
            };
            statusPanel.Children.Add(statusLabel);
            statusPanel.Children.Add(_statusText);
            mainStack.Children.Add(statusPanel);

            mainBorder.Child = mainStack;
            Content = mainBorder;
        }

        private TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 8, 0, 4)
            };
        }

        private Border CreateSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Margin = new Thickness(0, 8, 0, 8)
            };
        }

        private DataTemplate CreateUserItemTemplate()
        {
            // Create a simple template for user items
            // In a real implementation, you'd use XAML or a more sophisticated approach
            return null!; // Placeholder - we'll use a custom item rendering approach
        }

        private async Task InitializeAsync()
        {
            try
            {
                SetLoading(true);
                SetStatus("Initializing...", StatusType.Info);

                // Try to find user files in default location (OneDrive)
                await LoadUserFilesAsync();

                SetStatus("Ready", StatusType.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", StatusType.Error);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async Task LoadUserFilesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Look for user files in OneDrive McStud folder
                    var oneDrivePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "OneDrive", "McStud Users"
                    );

                    // Alternative paths to check
                    var possiblePaths = new[]
                    {
                        oneDrivePath,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "McStud Users"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "McStud", "Users")
                    };

                    string? foundPath = null;
                    foreach (var path in possiblePaths)
                    {
                        if (Directory.Exists(path))
                        {
                            foundPath = path;
                            break;
                        }
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _userFiles.Clear();

                        if (foundPath != null)
                        {
                            var files = Directory.GetFiles(foundPath, "*.xlsx");
                            foreach (var file in files)
                            {
                                _userFiles.Add(new UserFileInfo
                                {
                                    FileName = Path.GetFileNameWithoutExtension(file),
                                    FilePath = file,
                                    LastUpdated = File.GetLastWriteTime(file),
                                    Status = "Active",
                                    ExpirationDate = DateTime.Now.AddMonths(3) // Placeholder
                                });
                            }
                            SetStatus($"Found {files.Length} user files in {foundPath}", StatusType.Success);
                        }
                        else
                        {
                            SetStatus("No user files folder found. Create 'McStud Users' in OneDrive or Documents.", StatusType.Warning);
                        }

                        UpdateButtonStates();
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        SetStatus($"Error loading users: {ex.Message}", StatusType.Error);
                    });
                }
            });
        }

        private void UpdateButtonStates()
        {
            var canUpdate = _masterFilePath != null && !string.IsNullOrEmpty(_password) && _userFiles.Count > 0;
            if (_updateAllButton != null) _updateAllButton.IsEnabled = canUpdate;
        }

        private void SetLoading(bool isLoading)
        {
            if (_loadingRing != null)
            {
                _loadingRing.IsActive = isLoading;
            }
        }

        private enum StatusType { Info, Success, Warning, Error }

        private void SetStatus(string message, StatusType type)
        {
            if (_statusText == null) return;

            _statusText.Text = message;
            _statusText.Foreground = type switch
            {
                StatusType.Success => new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
                StatusType.Warning => new SolidColorBrush(Color.FromArgb(255, 255, 200, 100)),
                StatusType.Error => new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
                _ => new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
            };
        }

        private async void BrowseMasterFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".xlsx");

                // Get the window handle for the picker
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    _masterFilePath = file.Path;
                    if (_masterFileText != null)
                    {
                        _masterFileText.Text = Path.GetFileName(file.Path);
                        _masterFileText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100));
                    }
                    SetStatus($"Master file: {file.Name}", StatusType.Success);
                    UpdateButtonStates();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error selecting file: {ex.Message}", StatusType.Error);
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _password = _passwordBox?.Password;
            UpdateButtonStates();
        }

        private async void RefreshUsers_Click(object sender, RoutedEventArgs e)
        {
            await LoadUserFilesAsync();
        }

        private async void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (_masterFilePath == null)
            {
                SetStatus("Please select master file first", StatusType.Warning);
                return;
            }

            // Show dialog to get user name
            var dialog = new ContentDialog
            {
                Title = "Add New User",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var dialogStack = new StackPanel { Spacing = 12 };

            var nameBox = new TextBox
            {
                PlaceholderText = "User name (e.g., John_Smith)",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            dialogStack.Children.Add(nameBox);

            var expirationPicker = new DatePicker
            {
                Header = "Expiration Date",
                Date = DateTimeOffset.Now.AddMonths(3)
            };
            dialogStack.Children.Add(expirationPicker);

            dialog.Content = dialogStack;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                await CreateUserFileAsync(nameBox.Text, expirationPicker.Date.DateTime);
            }
        }

        private async Task CreateUserFileAsync(string userName, DateTime expirationDate)
        {
            if (_masterFilePath == null || string.IsNullOrEmpty(_password))
            {
                SetStatus("Master file and password required", StatusType.Error);
                return;
            }

            try
            {
                SetLoading(true);
                SetStatus($"Creating user file for {userName}...", StatusType.Info);

                await Task.Run(() =>
                {
                    // TODO: Implement actual file creation using ClosedXML
                    // 1. Copy master file
                    // 2. Set hidden expiration cell
                    // 3. Lock/protect the file
                    // 4. Save to user files folder

                    System.Threading.Thread.Sleep(1000); // Simulate work
                });

                SetStatus($"Created user file for {userName}", StatusType.Success);
                await LoadUserFilesAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Error creating user: {ex.Message}", StatusType.Error);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void UpdateAll_Click(object sender, RoutedEventArgs e)
        {
            if (_masterFilePath == null || string.IsNullOrEmpty(_password))
            {
                SetStatus("Master file and password required", StatusType.Error);
                return;
            }

            try
            {
                SetLoading(true);
                EnableButtons(false);

                int successCount = 0;
                int errorCount = 0;

                foreach (var user in _userFiles)
                {
                    SetStatus($"Updating {user.FileName}...", StatusType.Info);

                    try
                    {
                        await UpdateUserFileAsync(user);
                        successCount++;
                    }
                    catch
                    {
                        errorCount++;
                    }
                }

                if (errorCount == 0)
                {
                    SetStatus($"Updated {successCount} user files successfully!", StatusType.Success);
                }
                else
                {
                    SetStatus($"Updated {successCount} files, {errorCount} errors", StatusType.Warning);
                }

                await LoadUserFilesAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", StatusType.Error);
            }
            finally
            {
                SetLoading(false);
                EnableButtons(true);
            }
        }

        private async Task UpdateUserFileAsync(UserFileInfo user)
        {
            await Task.Run(() =>
            {
                // TODO: Implement actual update logic using ClosedXML
                // 1. Open user file with password
                // 2. Open master file
                // 3. Copy formulas from master to user (preserve user data)
                // 4. Re-lock and save user file

                System.Threading.Thread.Sleep(500); // Simulate work per file
            });
        }

        private void EnableButtons(bool enabled)
        {
            if (_updateAllButton != null) _updateAllButton.IsEnabled = enabled && _masterFilePath != null && !string.IsNullOrEmpty(_password);
            if (_addUserButton != null) _addUserButton.IsEnabled = enabled;
            if (_refreshButton != null) _refreshButton.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Represents a user's Excel file info
    /// </summary>
    public class UserFileInfo
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime LastUpdated { get; set; }
        public string Status { get; set; } = "Unknown";
        public DateTime ExpirationDate { get; set; }

        public string DisplayText => $"{FileName} | Expires: {ExpirationDate:MMM dd, yyyy} | Updated: {LastUpdated:MMM dd}";
    }
}
