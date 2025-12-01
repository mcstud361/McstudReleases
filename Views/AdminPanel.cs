#nullable enable
using Microsoft.UI;
using McStudDesktop.Services;
using Windows.UI;

namespace McStudDesktop.Views;

public class AdminPanel : Grid
{
    private ListView? _pendingUsersListView;
    private TextBox? _emailServerBox;
    private TextBox? _emailPortBox;
    private TextBox? _adminEmailBox;
    private TextBox? _senderEmailBox;
    private PasswordBox? _senderPasswordBox;
    private TextBlock? _statusMessageText;

    public event EventHandler? ClosePanel;

    public AdminPanel()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        Background = new SolidColorBrush(Color.FromArgb(230, 0, 0, 0));

        CreateAdminUI();
        LoadEmailSettings();
        LoadPendingUsers();
    }

    private void CreateAdminUI()
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var mainStack = new StackPanel
        {
            MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(40),
            Spacing = 25
        };

        // Header
        var headerStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var titleText = new TextBlock
        {
            Text = "Admin Panel",
            FontSize = 36,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var closeButton = new Button
        {
            Content = "✕",
            Width = 40,
            Height = 40,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(20)
        };
        closeButton.Click += (s, e) => ClosePanel?.Invoke(this, EventArgs.Empty);

        headerStack.Children.Add(titleText);

        var headerGrid = new Grid();
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(closeButton);
        Canvas.SetZIndex(closeButton, 1);
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;

        // Pending Users Section
        var pendingUsersSection = CreatePendingUsersSection();

        // Email Settings Section
        var emailSettingsSection = CreateEmailSettingsSection();

        // Status message
        _statusMessageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };

        mainStack.Children.Add(headerGrid);
        mainStack.Children.Add(pendingUsersSection);
        mainStack.Children.Add(emailSettingsSection);
        mainStack.Children.Add(_statusMessageText);

        scrollViewer.Content = mainStack;
        Children.Add(scrollViewer);
    }

    private Border CreatePendingUsersSection()
    {
        var sectionBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20)
        };

        var stack = new StackPanel { Spacing = 15 };

        var titleText = new TextBlock
        {
            Text = "Pending User Approvals",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 10)
        };

        _pendingUsersListView = new ListView
        {
            MaxHeight = 300,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var refreshButton = new Button
        {
            Content = "Refresh List",
            Height = 40,
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        refreshButton.Click += (s, e) => LoadPendingUsers();

        stack.Children.Add(titleText);
        stack.Children.Add(_pendingUsersListView);
        stack.Children.Add(refreshButton);

        sectionBorder.Child = stack;
        return sectionBorder;
    }

    private Border CreateEmailSettingsSection()
    {
        var sectionBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20)
        };

        var stack = new StackPanel { Spacing = 15 };

        var titleText = new TextBlock
        {
            Text = "Email Notification Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 10)
        };

        // SMTP Server
        stack.Children.Add(CreateLabel("SMTP Server (e.g., smtp.gmail.com)"));
        _emailServerBox = CreateTextBox("smtp.gmail.com");
        stack.Children.Add(_emailServerBox);

        // SMTP Port
        stack.Children.Add(CreateLabel("SMTP Port"));
        _emailPortBox = CreateTextBox("587");
        stack.Children.Add(_emailPortBox);

        // Admin Email
        stack.Children.Add(CreateLabel("Admin Email (where notifications are sent)"));
        _adminEmailBox = CreateTextBox("admin@example.com");
        stack.Children.Add(_adminEmailBox);

        // Sender Email
        stack.Children.Add(CreateLabel("Sender Email (Gmail address)"));
        _senderEmailBox = CreateTextBox("app@gmail.com");
        stack.Children.Add(_senderEmailBox);

        // Sender Password
        stack.Children.Add(CreateLabel("Sender Password (App Password for Gmail)"));
        _senderPasswordBox = new PasswordBox
        {
            PlaceholderText = "Enter app password",
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        stack.Children.Add(_senderPasswordBox);

        // Info text
        var infoText = new TextBlock
        {
            Text = "Note: For Gmail, you need to create an App Password in your Google Account settings (Security > 2-Step Verification > App Passwords).",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            Margin = new Thickness(0, 5, 0, 10)
        };
        stack.Children.Add(infoText);

        // Save button
        var saveButton = new Button
        {
            Content = "Save Email Settings",
            Height = 45,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        saveButton.Click += SaveEmailSettingsButton_Click;
        stack.Children.Add(saveButton);

        var titleStack = new StackPanel();
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(stack);

        sectionBorder.Child = titleStack;
        return sectionBorder;
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 5, 0, 5)
        };
    }

    private TextBox CreateTextBox(string placeholder)
    {
        return new TextBox
        {
            PlaceholderText = placeholder,
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
    }

    private void LoadPendingUsers()
    {
        if (_pendingUsersListView == null) return;

        var authService = AuthenticationService.Instance;
        var pendingUsers = authService.GetPendingUsers();

        _pendingUsersListView.Items.Clear();

        if (pendingUsers.Count == 0)
        {
            var noUsersText = new TextBlock
            {
                Text = "No pending user approvals",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(10),
                FontSize = 14
            };
            _pendingUsersListView.Items.Add(noUsersText);
        }
        else
        {
            foreach (var user in pendingUsers)
            {
                var userPanel = new Grid
                {
                    Margin = new Thickness(10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                var userInfo = new StackPanel();
                var usernameText = new TextBlock
                {
                    Text = user.Username,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                var dateText = new TextBlock
                {
                    Text = $"Registered: {user.CreatedDate:yyyy-MM-dd HH:mm}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                };
                userInfo.Children.Add(usernameText);
                userInfo.Children.Add(dateText);

                var approveButton = new Button
                {
                    Content = "Approve",
                    Width = 90,
                    Height = 35,
                    Margin = new Thickness(10, 0, 5, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 140, 40)),
                    Foreground = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(6)
                };
                approveButton.Click += (s, e) =>
                {
                    authService.ApproveUser(user.Username);
                    ShowStatus($"User '{user.Username}' approved successfully!");
                    LoadPendingUsers();
                };

                var rejectButton = new Button
                {
                    Content = "Reject",
                    Width = 90,
                    Height = 35,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 140, 40, 40)),
                    Foreground = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(6)
                };
                rejectButton.Click += (s, e) =>
                {
                    authService.RejectUser(user.Username);
                    ShowStatus($"User '{user.Username}' rejected and removed.");
                    LoadPendingUsers();
                };

                Grid.SetColumn(userInfo, 0);
                Grid.SetColumn(approveButton, 1);
                Grid.SetColumn(rejectButton, 2);

                userPanel.Children.Add(userInfo);
                userPanel.Children.Add(approveButton);
                userPanel.Children.Add(rejectButton);

                _pendingUsersListView.Items.Add(userPanel);
            }
        }
    }

    private void LoadEmailSettings()
    {
        var emailService = EmailNotificationService.Instance;
        var settings = emailService.GetSettings();

        if (_emailServerBox != null) _emailServerBox.Text = settings.SmtpServer;
        if (_emailPortBox != null) _emailPortBox.Text = settings.SmtpPort.ToString();
        if (_adminEmailBox != null) _adminEmailBox.Text = settings.AdminEmail;
        if (_senderEmailBox != null) _senderEmailBox.Text = settings.SenderEmail;
        if (_senderPasswordBox != null) _senderPasswordBox.Password = settings.SenderPassword;
    }

    private void SaveEmailSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_emailServerBox == null || _emailPortBox == null || _adminEmailBox == null ||
            _senderEmailBox == null || _senderPasswordBox == null)
            return;

        var settings = new EmailSettings
        {
            SmtpServer = _emailServerBox.Text.Trim(),
            SmtpPort = int.TryParse(_emailPortBox.Text, out int port) ? port : 587,
            AdminEmail = _adminEmailBox.Text.Trim(),
            SenderEmail = _senderEmailBox.Text.Trim(),
            SenderPassword = _senderPasswordBox.Password,
            EnableSsl = true
        };

        var emailService = EmailNotificationService.Instance;
        emailService.SaveSettings(settings);

        ShowStatus("Email settings saved successfully!");
    }

    private void ShowStatus(string message)
    {
        if (_statusMessageText != null)
        {
            _statusMessageText.Text = message;
            _statusMessageText.Visibility = Visibility.Visible;

            // Hide after 3 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (_statusMessageText != null)
                    _statusMessageText.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }
    }
}
