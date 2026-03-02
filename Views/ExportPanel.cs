using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace McStudDesktop.Views;

/// <summary>
/// Slim export panel with dropdown for target selection and Clip It button.
/// Reusable across all tabs.
/// </summary>
public class ExportPanel : UserControl
{
    private ComboBox _targetDropdown = null!;
    private ComboBox _speedDropdown = null!;
    private Button _clipItButton = null!;
    private Button _typeItButton = null!;
    private Button _resumeButton = null!;
    private Button _restartButton = null!;
    private Button _cancelButton = null!;
    private TextBlock _statusText = null!;

    public event EventHandler<ExportEventArgs>? ClipItClicked;
    public event EventHandler<ExportEventArgs>? TypeItClicked;
    public event EventHandler<int>? SpeedChanged;
    public event EventHandler? ResumeClicked;
    public event EventHandler? RestartClicked;
    public event EventHandler? CancelExportClicked;

    public enum ExportTarget
    {
        CCCDesktop,
        CCCWeb,
        Mitchell
    }

    public ExportTarget SelectedTarget
    {
        get
        {
            return _targetDropdown.SelectedIndex switch
            {
                0 => ExportTarget.CCCDesktop,
                1 => ExportTarget.CCCWeb,
                2 => ExportTarget.Mitchell,
                _ => ExportTarget.CCCDesktop
            };
        }
    }

    /// <summary>
    /// Gets the selected speed level (0=Slow, 1=Normal, 2=Fast, 3=Turbo)
    /// </summary>
    public int SelectedSpeedLevel => _speedDropdown?.SelectedIndex ?? 2;

    public int LineCount
    {
        set => _statusText.Text = value > 0 ? $"{value} lines ready" : "No lines";
    }

    public string Status
    {
        set => _statusText.Text = value;
    }

    public ExportPanel()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var rootBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(15, 8, 15, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Instruction
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Target dropdown
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Speed dropdown
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Clip It
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Type It
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Status

        // Instruction text
        var instructionText = new TextBlock
        {
            Text = "Click in CCC Desktop first →",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 100)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(instructionText, 0);
        contentGrid.Children.Add(instructionText);

        // Target dropdown
        _targetDropdown = new ComboBox
        {
            Width = 140,
            Height = 32,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _targetDropdown.Items.Add("CCC Desktop");
        _targetDropdown.Items.Add("CCC Web");
        _targetDropdown.Items.Add("Mitchell");
        _targetDropdown.SelectedIndex = 0;
        Grid.SetColumn(_targetDropdown, 1);
        contentGrid.Children.Add(_targetDropdown);

        // Speed dropdown
        _speedDropdown = new ComboBox
        {
            Width = 90,
            Height = 32,
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        _speedDropdown.Items.Add("Slow");
        _speedDropdown.Items.Add("Normal");
        _speedDropdown.Items.Add("Fast");
        _speedDropdown.Items.Add("Turbo");
        _speedDropdown.Items.Add("Insane");
        _speedDropdown.SelectedIndex = 4; // Default to Insane (fastest)
        _speedDropdown.SelectionChanged += (s, e) =>
        {
            SpeedChanged?.Invoke(this, _speedDropdown.SelectedIndex);
        };
        Grid.SetColumn(_speedDropdown, 2);
        contentGrid.Children.Add(_speedDropdown);

        // Clip It button (copy to clipboard)
        _clipItButton = new Button
        {
            Content = "📋 Clip It",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 6, 15, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _clipItButton.Click += ClipItButton_Click;
        _clipItButton.PointerEntered += (s, e) =>
        {
            _clipItButton.Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
        };
        _clipItButton.PointerExited += (s, e) =>
        {
            _clipItButton.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
        };
        Grid.SetColumn(_clipItButton, 3);
        contentGrid.Children.Add(_clipItButton);

        // Type It button (UIA automation - only for CCC Desktop)
        _typeItButton = new Button
        {
            Content = "⌨️ Type It",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(15, 6, 15, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 160)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 100, 200)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _typeItButton.Click += TypeItButton_Click;
        _typeItButton.PointerEntered += (s, e) =>
        {
            _typeItButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 200));
        };
        _typeItButton.PointerExited += (s, e) =>
        {
            _typeItButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 160));
        };
        Grid.SetColumn(_typeItButton, 4);
        contentGrid.Children.Add(_typeItButton);

        // Enable/disable buttons based on target - only CCC Desktop is functional for now
        _targetDropdown.SelectionChanged += (s, e) =>
        {
            bool isCCCDesktop = _targetDropdown.SelectedIndex == 0;

            // Type It - only CCC Desktop
            _typeItButton.IsEnabled = isCCCDesktop;
            _typeItButton.Background = new SolidColorBrush(isCCCDesktop
                ? Color.FromArgb(255, 0, 80, 160)
                : Color.FromArgb(255, 50, 50, 50));

            // Clip It - only CCC Desktop (CCC Web and Mitchell not yet implemented)
            _clipItButton.IsEnabled = isCCCDesktop;
            _clipItButton.Background = new SolidColorBrush(isCCCDesktop
                ? Color.FromArgb(255, 60, 60, 60)
                : Color.FromArgb(255, 50, 50, 50));

            // Update instruction text based on target
            if (isCCCDesktop)
            {
                instructionText.Text = "Click in CCC Desktop first →";
                instructionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 100));
            }
            else
            {
                instructionText.Text = "Coming soon...";
                instructionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
            }
        };

        // Resume button (hidden by default)
        _resumeButton = new Button
        {
            Content = "▶ Resume",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 150, 100)),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _resumeButton.Click += (s, e) => ResumeClicked?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(_resumeButton, 4);
        contentGrid.Children.Add(_resumeButton);

        // Restart button (hidden by default)
        _restartButton = new Button
        {
            Content = "↺ Restart",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 120, 80, 0)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 150, 100, 0)),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _restartButton.Click += (s, e) => RestartClicked?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(_restartButton, 4);
        contentGrid.Children.Add(_restartButton);

        // Cancel button (hidden by default)
        _cancelButton = new Button
        {
            Content = "✕ Cancel",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(255, 100, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 130, 70, 70)),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _cancelButton.Click += (s, e) => CancelExportClicked?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(_cancelButton, 4);
        contentGrid.Children.Add(_cancelButton);

        // Status text (right-aligned)
        _statusText = new TextBlock
        {
            Text = "No lines",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(15, 0, 0, 0)
        };
        Grid.SetColumn(_statusText, 5);
        contentGrid.Children.Add(_statusText);

        rootBorder.Child = contentGrid;
        Content = rootBorder;
    }

    private void ClipItButton_Click(object sender, RoutedEventArgs e)
    {
        ClipItClicked?.Invoke(this, new ExportEventArgs
        {
            Target = SelectedTarget
        });
    }

    private void TypeItButton_Click(object sender, RoutedEventArgs e)
    {
        TypeItClicked?.Invoke(this, new ExportEventArgs
        {
            Target = SelectedTarget
        });
    }

    /// <summary>
    /// Updates status to show transfer in progress
    /// </summary>
    public void SetTransferring()
    {
        _statusText.Text = "Transferring...";
        _clipItButton.IsEnabled = false;
        _typeItButton.IsEnabled = false;
    }

    /// <summary>
    /// Updates status to show countdown before typing
    /// </summary>
    public void SetCountdown(int secondsRemaining)
    {
        _statusText.Text = $"Position cursor in CCC... {secondsRemaining}s";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));
        _clipItButton.IsEnabled = false;
        _typeItButton.IsEnabled = false;
    }

    /// <summary>
    /// Updates status to show typing in progress
    /// </summary>
    public void SetTyping(int current, int total)
    {
        _statusText.Text = $"Typing {current}/{total}...";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
        _clipItButton.IsEnabled = false;
        _typeItButton.IsEnabled = false;
    }

    /// <summary>
    /// Updates status to show transfer complete
    /// </summary>
    public void SetComplete(int linesTransferred)
    {
        _statusText.Text = $"✓ {linesTransferred} lines transferred";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100));
        _clipItButton.IsEnabled = true;
        _typeItButton.IsEnabled = _targetDropdown.SelectedIndex == 0;
    }

    /// <summary>
    /// Updates status to show error
    /// </summary>
    public void SetError(string message)
    {
        _statusText.Text = $"✗ {message}";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
        _clipItButton.IsEnabled = true;
        _typeItButton.IsEnabled = _targetDropdown.SelectedIndex == 0;
    }

    /// <summary>
    /// Resets status text color to normal
    /// </summary>
    public void ResetStatus()
    {
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
        HidePauseButtons();
    }

    /// <summary>
    /// Shows the paused state with Resume/Restart/Cancel options
    /// </summary>
    public void SetPaused(int completed, int total)
    {
        _statusText.Text = $"⏸ Paused at {completed}/{total} - User input detected";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 100));

        // Hide normal buttons
        _clipItButton.Visibility = Visibility.Collapsed;
        _typeItButton.Visibility = Visibility.Collapsed;
        _targetDropdown.Visibility = Visibility.Collapsed;

        // Show pause buttons
        _resumeButton.Visibility = Visibility.Visible;
        _restartButton.Visibility = Visibility.Visible;
        _cancelButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides the pause buttons and shows normal buttons
    /// </summary>
    public void HidePauseButtons()
    {
        // Hide pause buttons
        _resumeButton.Visibility = Visibility.Collapsed;
        _restartButton.Visibility = Visibility.Collapsed;
        _cancelButton.Visibility = Visibility.Collapsed;

        // Show normal buttons
        _clipItButton.Visibility = Visibility.Visible;
        _typeItButton.Visibility = Visibility.Visible;
        _targetDropdown.Visibility = Visibility.Visible;
        _clipItButton.IsEnabled = true;
        _typeItButton.IsEnabled = _targetDropdown.SelectedIndex == 0;
    }

    /// <summary>
    /// Shows the aiming mode status - click freely in CCC, press Enter to paste
    /// </summary>
    public void SetAiming()
    {
        _statusText.Text = "🎯 Click in CCC to position, press ENTER to paste (ESC cancel)";
        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
        _clipItButton.IsEnabled = false;
        _typeItButton.IsEnabled = false;
    }
}

public class ExportEventArgs : EventArgs
{
    public ExportPanel.ExportTarget Target { get; set; }
}
