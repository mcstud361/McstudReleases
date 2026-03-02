#nullable enable
using Microsoft.UI;
using McstudDesktop.Services.Legacy;
using Windows.UI;

namespace McstudDesktop.Views.Legacy;

public class LoginPage : Grid
{
    private TextBox? _usernameBox;
    private PasswordBox? _passwordBox;
    private CheckBox? _rememberMeCheckbox;
    private TextBlock? _errorMessageText;

    public event EventHandler? LoginSuccessful;
    public event EventHandler? NavigateToRegister;

    public LoginPage()
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

        // Load credentials asynchronously to not block UI
        _ = LoadRememberedCredentialsAsync();

        // AuthService initializes synchronously in its constructor (it's fast for small user files)
        // Just access the Instance to trigger initialization
        _ = AuthenticationService.Instance;
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

        // Logo/Title
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
            Text = "Estimating Tool Login",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            Margin = new Thickness(0, 0, 0, 30)
        };

        // Username field
        var usernameLabel = new TextBlock
        {
            Text = "Username",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        _usernameBox = new TextBox
        {
            PlaceholderText = "Enter your username",
            Height = 45,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 15),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        // Password field
        var passwordLabel = new TextBlock
        {
            Text = "Password",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        _passwordBox = new PasswordBox
        {
            PlaceholderText = "Enter your password",
            Height = 45,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        _passwordBox.KeyDown += PasswordBox_KeyDown;

        // Remember me checkbox
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

        // Login button
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

        var loginButton = new Button
        {
            Content = "Sign In",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        loginButton.Click += LoginButton_Click;
        loginButton.PointerEntered += (s, e) =>
        {
            loginButtonBorder.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1);
        };
        loginButton.PointerExited += (s, e) =>
        {
            loginButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
        };

        loginButtonBorder.Child = loginButton;

        // Forgot password and register links
        var linksPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 20
        };

        var forgotPasswordButton = new HyperlinkButton
        {
            Content = "Forgot Password?",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 13
        };
        forgotPasswordButton.Click += ForgotPasswordButton_Click;

        var registerButton = new HyperlinkButton
        {
            Content = "Create Account",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 13
        };
        registerButton.Click += RegisterButton_Click;

        linksPanel.Children.Add(forgotPasswordButton);
        linksPanel.Children.Add(registerButton);

        // TESTING ONLY - Skip Login Button
        var skipLoginButton = new Button
        {
            Content = "⚡ Skip Login (Testing)",
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

        // Add all elements to main stack
        mainStack.Children.Add(titleText);
        mainStack.Children.Add(subtitleText);
        mainStack.Children.Add(usernameLabel);
        mainStack.Children.Add(_usernameBox);
        mainStack.Children.Add(passwordLabel);
        mainStack.Children.Add(_passwordBox);
        mainStack.Children.Add(_rememberMeCheckbox);
        mainStack.Children.Add(_errorMessageText);
        mainStack.Children.Add(loginButtonBorder);
        mainStack.Children.Add(linksPanel);
        mainStack.Children.Add(skipLoginButton);

        Children.Add(mainStack);
    }

    private void PasswordBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PerformLogin();
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        PerformLogin();
    }

    private void PerformLogin()
    {
        if (_usernameBox == null || _passwordBox == null || _errorMessageText == null)
            return;

        var username = _usernameBox.Text.Trim();
        var password = _passwordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Please enter both username and password.");
            return;
        }

        var authService = AuthenticationService.Instance;
        if (authService.ValidateCredentials(username, password))
        {
            // Save credentials if remember me is checked
            if (_rememberMeCheckbox?.IsChecked == true)
            {
                SaveCredentials(username, password);
            }
            else
            {
                ClearSavedCredentials();
            }

            System.Diagnostics.Debug.WriteLine($"[Login] User logged in successfully: {username}");
            LoginSuccessful?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowError("Invalid username or password. If you're a new user, your account may be pending approval.");
        }
    }

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_usernameBox == null || _errorMessageText == null)
            return;

        var username = _usernameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Please enter your username first.");
            return;
        }

        // Send email notification to admin
        var emailService = EmailNotificationService.Instance;
        var emailTask = emailService.SendPasswordResetNotificationAsync(username);

        emailTask.ContinueWith(task =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (task.Result)
                {
                    ShowSuccess("Password reset request sent to administrator.");
                }
                else
                {
                    ShowError("Could not send password reset request. Please contact the administrator directly.");
                }
            });
        });
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToRegister?.Invoke(this, EventArgs.Empty);
    }

    private void SkipLoginButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Login] Skipping login for testing");
        LoginSuccessful?.Invoke(this, EventArgs.Empty);
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

    private void ShowSuccess(string message)
    {
        if (_errorMessageText != null)
        {
            _errorMessageText.Text = message;
            _errorMessageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100));
            _errorMessageText.Visibility = Visibility.Visible;
        }
    }

    private void SaveCredentials(string username, string password)
    {
        try
        {
            var credentials = new { Username = username, Password = password };
            var json = System.Text.Json.JsonSerializer.Serialize(credentials);
            var encrypted = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            File.WriteAllText("remember.dat", encrypted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error saving credentials: {ex.Message}");
        }
    }

    private void LoadRememberedCredentials()
    {
        try
        {
            if (File.Exists("remember.dat"))
            {
                var encrypted = File.ReadAllText("remember.dat");
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
                var credentials = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (credentials != null && _usernameBox != null && _passwordBox != null && _rememberMeCheckbox != null)
                {
                    if (credentials.TryGetValue("Username", out var username))
                        _usernameBox.Text = username;
                    if (credentials.TryGetValue("Password", out var password))
                        _passwordBox.Password = password;

                    _rememberMeCheckbox.IsChecked = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error loading credentials: {ex.Message}");
        }
    }

    private async Task LoadRememberedCredentialsAsync()
    {
        try
        {
            if (File.Exists("remember.dat"))
            {
                var encrypted = await File.ReadAllTextAsync("remember.dat");
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
                var credentials = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                // Update UI on main thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (credentials != null && _usernameBox != null && _passwordBox != null && _rememberMeCheckbox != null)
                    {
                        if (credentials.TryGetValue("Username", out var username))
                            _usernameBox.Text = username;
                        if (credentials.TryGetValue("Password", out var password))
                            _passwordBox.Password = password;

                        _rememberMeCheckbox.IsChecked = true;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error loading credentials: {ex.Message}");
        }
    }

    private void ClearSavedCredentials()
    {
        try
        {
            if (File.Exists("remember.dat"))
            {
                File.Delete("remember.dat");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Error clearing credentials: {ex.Message}");
        }
    }
}
