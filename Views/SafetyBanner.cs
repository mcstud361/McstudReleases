#nullable enable
using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Always-on-top red banner shown while a CCC Web insert is running, so the user (and anyone
    /// nearby) knows the automation has control and how to stop it. Sits above the browser.
    /// </summary>
    public sealed class SafetyBannerWindow : Window
    {
        private readonly TextBlock _statusText;

        public SafetyBannerWindow()
        {
            var presenter = OverlappedPresenter.CreateForToolWindow();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.IsShownInSwitchers = false;

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 200, 30, 30)),
                Padding = new Thickness(18, 10, 18, 10)
            };

            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };

            stack.Children.Add(new TextBlock
            {
                Text = "⚠  CCC WEB INSERT RUNNING — DO NOT TOUCH",
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = "To stop the program: move the mouse, click, or press any keystroke.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 225, 225)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(_statusText);

            root.Child = stack;
            Content = root;
        }

        /// <summary>Position at top-center of the primary display and show it.</summary>
        public void Present()
        {
            try
            {
                var work = DisplayArea.Primary.WorkArea;
                int w = 760;
                int h = 96;
                int x = work.X + (work.Width - w) / 2;
                int y = work.Y + 20;
                AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
            }
            catch { /* fall back to default placement */ }

            Activate();
        }

        public void SetStatus(string text) => _statusText.Text = text ?? "";
    }

    /// <summary>
    /// Singleton owner of the safety banner. Subscribes once to the CCC Web insert service and
    /// shows/updates/hides the banner on the UI thread as inserts start, progress, and finish.
    /// </summary>
    public static class SafetyBanner
    {
        private static SafetyBannerWindow? _window;
        private static bool _attached;

        private static Microsoft.UI.Dispatching.DispatcherQueue? Dq => McstudDesktop.App.MainDispatcherQueue;

        /// <summary>Wire the banner to the CCC Web insert service. Safe to call more than once.</summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            var svc = McStudDesktop.Services.CccWebInsertService.Instance;
            svc.InsertStarting += (s, e) => Show();
            svc.StatusChanged += (s, txt) => SetStatus(txt);
            svc.InsertCompleted += (s, ok) => Hide();
        }

        private static void Show()
        {
            Dq?.TryEnqueue(() =>
            {
                try
                {
                    _window ??= new SafetyBannerWindow();
                    _window.Present();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SafetyBanner] Show error: {ex.Message}");
                }
            });
        }

        private static void SetStatus(string text)
        {
            Dq?.TryEnqueue(() =>
            {
                try { _window?.SetStatus(text); } catch { /* ignore */ }
            });
        }

        private static void Hide()
        {
            Dq?.TryEnqueue(() =>
            {
                try { _window?.Close(); } catch { /* ignore */ }
                _window = null;
            });
        }
    }
}
