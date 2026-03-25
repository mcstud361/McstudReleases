#nullable enable
using Microsoft.UI;
using McStudDesktop.Services;
using Windows.UI;

namespace McStudDesktop.Views;

public class LoginView : Grid
{
    private TextBox? _emailBox;
    private CheckBox? _rememberMeCheckbox;
    private TextBlock? _errorMessageText;
    private Button? _loginButton;
    private ProgressRing? _spinner;

    public event EventHandler? LoginSuccessful;

    /// <summary>
    /// Optional error message to display immediately (e.g. from failed session re-validation).
    /// </summary>
    public string? InitialError { get; set; }

    public LoginView()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(255, 0, 0, 0), Offset = 0.0 },
                new GradientStop { Color = Color.FromArgb(255, 20, 20, 20), Offset = 1.0 }
            }
        };

        CreateLoginUI();
        _ = LoadRememberedEmailAsync();
    }

    private void CreateLoginUI()
    {
        var mainStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 400,
            Spacing = 20
        };

        // Title
        var titleText = new TextBlock
        {
            Text = "Mcstud Desktop",
            FontSize = 42,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var subtitleText = new TextBlock
        {
            Text = "Enter your license email",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            Margin = new Thickness(0, 0, 0, 30)
        };

        // Email field
        var emailLabel = new TextBlock
        {
            Text = "Email",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        _emailBox = new TextBox
        {
            PlaceholderText = "Enter your email",
            Height = 45,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 15),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        _emailBox.KeyDown += EmailBox_KeyDown;

        // Remember me
        _rememberMeCheckbox = new CheckBox
        {
            Content = "Remember me",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 15)
        };

        // Error message
        _errorMessageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };

        // Loading spinner (hidden by default)
        _spinner = new ProgressRing
        {
            IsActive = false,
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };

        // Activate button
        var loginButtonBorder = new Border
        {
            Height = 50,
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 50, 50, 50), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 70, 70, 70), Offset = 1.0 }
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 5, 0, 15)
        };

        _loginButton = new Button
        {
            Content = "Activate",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        _loginButton.Click += LoginButton_Click;
        _loginButton.PointerEntered += (s, e) =>
        {
            loginButtonBorder.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1);
        };
        _loginButton.PointerExited += (s, e) =>
        {
            loginButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
        };

        loginButtonBorder.Child = _loginButton;

        // Skip Login button (testing)
        var skipLoginButton = new Button
        {
            Content = "Skip Login (Testing)",
            Height = 45,
            Margin = new Thickness(0, 20, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 100, 50, 150)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 150, 100, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        skipLoginButton.Click += SkipLoginButton_Click;
        skipLoginButton.PointerEntered += (s, e) =>
        {
            skipLoginButton.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1);
        };
        skipLoginButton.PointerExited += (s, e) =>
        {
            skipLoginButton.Scale = new System.Numerics.Vector3(1, 1, 1);
        };

        // Add elements
        mainStack.Children.Add(titleText);
        mainStack.Children.Add(subtitleText);
        mainStack.Children.Add(emailLabel);
        mainStack.Children.Add(_emailBox);
        mainStack.Children.Add(_rememberMeCheckbox);
        mainStack.Children.Add(_errorMessageText);
        mainStack.Children.Add(_spinner);
        mainStack.Children.Add(loginButtonBorder);
        mainStack.Children.Add(skipLoginButton);

        Children.Add(mainStack);

        // Show initial error if set (e.g. from failed session re-validation)
        Loaded += (s, e) =>
        {
            if (!string.IsNullOrEmpty(InitialError))
            {
                ShowError(InitialError);
            }
        };
    }

    private void EmailBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = PerformLoginAsync();
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformLoginAsync();
    }

    private void SkipLoginButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Login] Skipping login for testing");
        LoginSuccessful?.Invoke(this, EventArgs.Empty);
    }

    private async Task PerformLoginAsync()
    {
        if (_emailBox == null || _errorMessageText == null ||
            _loginButton == null || _spinner == null)
            return;

        var email = _emailBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            ShowError("Please enter your license email.");
            return;
        }

        // Show spinner, disable button
        _loginButton.IsEnabled = false;
        _spinner.IsActive = true;
        _spinner.Visibility = Visibility.Visible;
        _errorMessageText.Visibility = Visibility.Collapsed;

        try
        {
            var result = await LoginAuthService.LoginAsync(email);

            if (result.Success)
            {
                // Save email if remember me is checked
                if (_rememberMeCheckbox?.IsChecked == true)
                {
                    SaveEmail(email);
                }
                else
                {
                    ClearSavedEmail();
                }

                // Save session for auto-login
                LoginAuthService.SaveSession(email);

                System.Diagnostics.Debug.WriteLine($"[Login] License activated for: {email}");
                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ShowError(result.Message);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Activation failed: {ex.Message}");
        }
        finally
        {
            _loginButton.IsEnabled = true;
            _spinner.IsActive = false;
            _spinner.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string message)
    {
        if (_errorMessageText != null)
        {
            _errorMessageText.Text = message;
            _errorMessageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
            _errorMessageText.Visibility = Visibility.Visible;
        }
    }

    private void SaveEmail(string email)
    {
        try
        {
            var data = new { Email = email };
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            File.WriteAllText("remember.dat", encoded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error saving email: {ex.Message}");
        }
    }

    private async Task LoadRememberedEmailAsync()
    {
        try
        {
            if (File.Exists("remember.dat"))
            {
                var encoded = await File.ReadAllTextAsync("remember.dat");
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (data != null && _emailBox != null && _rememberMeCheckbox != null)
                    {
                        if (data.TryGetValue("Email", out var email) ||
                            data.TryGetValue("Username", out email))
                            _emailBox.Text = email;

                        _rememberMeCheckbox.IsChecked = true;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error loading email: {ex.Message}");
        }
    }

    private void ClearSavedEmail()
    {
        try
        {
            if (File.Exists("remember.dat"))
                File.Delete("remember.dat");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error clearing saved email: {ex.Message}");
        }
    }
}
