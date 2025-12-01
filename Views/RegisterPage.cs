#nullable enable
using Microsoft.UI;
using McStudDesktop.Services;
using Windows.UI;

namespace McStudDesktop.Views;

public class RegisterPage : Grid
{
    private TextBox? _usernameBox;
    private PasswordBox? _passwordBox;
    private PasswordBox? _confirmPasswordBox;
    private TextBlock? _errorMessageText;

    public event EventHandler? RegistrationSuccessful;
    public event EventHandler? NavigateToLogin;

    public RegisterPage()
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

        CreateRegisterUI();
    }

    private void CreateRegisterUI()
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
            Text = "Create Account",
            FontSize = 42,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var subtitleText = new TextBlock
        {
            Text = "Register for McStud Desktop",
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
            PlaceholderText = "Choose a username (min 3 characters)",
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
            PlaceholderText = "Choose a password (min 6 characters)",
            Height = 45,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 15),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        // Confirm password field
        var confirmPasswordLabel = new TextBlock
        {
            Text = "Confirm Password",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        _confirmPasswordBox = new PasswordBox
        {
            PlaceholderText = "Re-enter your password",
            Height = 45,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 15),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        _confirmPasswordBox.KeyDown += ConfirmPasswordBox_KeyDown;

        // Error/Info message
        _errorMessageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };

        // Register button
        var registerButtonBorder = new Border
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

        var registerButton = new Button
        {
            Content = "Register",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        registerButton.Click += RegisterButton_Click;
        registerButton.PointerEntered += (s, e) =>
        {
            registerButtonBorder.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1);
        };
        registerButton.PointerExited += (s, e) =>
        {
            registerButtonBorder.Scale = new System.Numerics.Vector3(1, 1, 1);
        };

        registerButtonBorder.Child = registerButton;

        // Back to login link
        var backToLoginButton = new HyperlinkButton
        {
            Content = "Already have an account? Sign In",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        backToLoginButton.Click += BackToLoginButton_Click;

        // Add all elements to main stack
        mainStack.Children.Add(titleText);
        mainStack.Children.Add(subtitleText);
        mainStack.Children.Add(usernameLabel);
        mainStack.Children.Add(_usernameBox);
        mainStack.Children.Add(passwordLabel);
        mainStack.Children.Add(_passwordBox);
        mainStack.Children.Add(confirmPasswordLabel);
        mainStack.Children.Add(_confirmPasswordBox);
        mainStack.Children.Add(_errorMessageText);
        mainStack.Children.Add(registerButtonBorder);
        mainStack.Children.Add(backToLoginButton);

        Children.Add(mainStack);
    }

    private void ConfirmPasswordBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PerformRegistration();
        }
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        PerformRegistration();
    }

    private void PerformRegistration()
    {
        if (_usernameBox == null || _passwordBox == null || _confirmPasswordBox == null || _errorMessageText == null)
            return;

        var username = _usernameBox.Text.Trim();
        var password = _passwordBox.Password;
        var confirmPassword = _confirmPasswordBox.Password;

        // Validate input
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Please enter a username.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Please enter a password.");
            return;
        }

        if (password != confirmPassword)
        {
            ShowError("Passwords do not match.");
            return;
        }

        // Attempt registration
        var authService = AuthenticationService.Instance;
        if (authService.RegisterUser(username, password, out string errorMessage))
        {
            // Send email notification to admin
            var emailService = EmailNotificationService.Instance;
            _ = emailService.SendRegistrationNotificationAsync(username);

            ShowSuccess("Registration successful! Your account is pending approval by the administrator.");

            System.Diagnostics.Debug.WriteLine($"[Register] User registered successfully: {username}");

            // Wait 2 seconds then navigate to login
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RegistrationSuccessful?.Invoke(this, EventArgs.Empty);
            };
            timer.Start();
        }
        else
        {
            ShowError(errorMessage);
        }
    }

    private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToLogin?.Invoke(this, EventArgs.Empty);
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
}
