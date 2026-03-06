using Windows.Media.SpeechSynthesis;
using Windows.Media.Playback;
using Windows.Media.Core;
using McstudDesktop;

namespace McStudDesktop.Services;

public class TextToSpeechService
{
    private static readonly Lazy<TextToSpeechService> _instance = new(() => new TextToSpeechService());
    public static TextToSpeechService Instance => _instance.Value;

    private static readonly string SettingsPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "tts_settings.json");

    private readonly SpeechSynthesizer _synthesizer;
    private MediaPlayer? _player;
    private bool _isSpeaking;

    public bool IsSpeaking
    {
        get => _isSpeaking;
        private set
        {
            if (_isSpeaking != value)
            {
                _isSpeaking = value;
                SpeakingChanged?.Invoke(value);
            }
        }
    }

    public event Action<bool>? SpeakingChanged;

    /// <summary>
    /// Current voice display name.
    /// </summary>
    public string CurrentVoiceName => _synthesizer.Voice.DisplayName;

    private TextToSpeechService()
    {
        _synthesizer = new SpeechSynthesizer();
        LoadSavedVoice();
    }

    /// <summary>
    /// Returns all installed TTS voices.
    /// </summary>
    public static IReadOnlyList<VoiceInformation> GetAvailableVoices()
    {
        return SpeechSynthesizer.AllVoices;
    }

    /// <summary>
    /// Sets the voice by display name and persists the choice.
    /// </summary>
    public void SetVoice(string displayName)
    {
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == displayName);
        if (voice != null)
        {
            _synthesizer.Voice = voice;
            SaveVoiceSetting(displayName);
        }
    }

    private void LoadSavedVoice()
    {
        try
        {
            if (System.IO.File.Exists(SettingsPath))
            {
                var json = System.IO.File.ReadAllText(SettingsPath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("voice", out var voiceProp))
                {
                    var name = voiceProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == name);
                        if (voice != null)
                            _synthesizer.Voice = voice;
                    }
                }
            }
        }
        catch { }
    }

    private static void SaveVoiceSetting(string displayName)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { voice = displayName });
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    public async void SpeakAsync(string text)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var stream = await _synthesizer.SynthesizeTextToStreamAsync(text);
            _player = new MediaPlayer();
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.MediaEnded += (s, e) =>
            {
                App.MainDispatcherQueue?.TryEnqueue(() => IsSpeaking = false);
            };
            _player.MediaFailed += (s, e) =>
            {
                App.MainDispatcherQueue?.TryEnqueue(() => IsSpeaking = false);
            };
            _player.Play();
            IsSpeaking = true;
        }
        catch
        {
            IsSpeaking = false;
        }
    }

    public void Stop()
    {
        if (_player != null)
        {
            _player.Pause();
            _player.Source = null;
            _player.Dispose();
            _player = null;
        }
        IsSpeaking = false;
    }

    /// <summary>
    /// Creates a styled Read Aloud button that toggles speech on/off.
    /// </summary>
    public static Button CreateReadAloudButton(Func<string> getTextToRead)
    {
        var icon = new FontIcon { Glyph = "\uE767", FontSize = 12 };
        var label = new TextBlock { Text = "Read Aloud", FontSize = 11 };

        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { icon, label }
            },
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80))
        };

        btn.Click += (s, e) =>
        {
            var tts = Instance;
            if (tts.IsSpeaking)
            {
                tts.Stop();
            }
            else
            {
                tts.SpeakAsync(getTextToRead());
            }
        };

        // Update icon when speaking state changes
        void OnSpeakingChanged(bool speaking)
        {
            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                icon.Glyph = speaking ? "\uE71A" : "\uE767";
                label.Text = speaking ? "Stop" : "Read Aloud";
            });
        }

        Instance.SpeakingChanged += OnSpeakingChanged;

        // Clean up when button is unloaded
        btn.Unloaded += (s, e) =>
        {
            Instance.SpeakingChanged -= OnSpeakingChanged;
        };

        return btn;
    }

    /// <summary>
    /// Creates a small icon-only Read Aloud button (for chatbot messages).
    /// </summary>
    public static Button CreateSmallReadAloudButton(Func<string> getTextToRead)
    {
        var icon = new FontIcon { Glyph = "\uE767", FontSize = 11 };

        var btn = new Button
        {
            Content = icon,
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 24,
            MinHeight = 20,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Opacity = 0.6
        };

        btn.Click += (s, e) =>
        {
            var tts = Instance;
            if (tts.IsSpeaking)
            {
                tts.Stop();
            }
            else
            {
                tts.SpeakAsync(getTextToRead());
            }
        };

        void OnSpeakingChanged(bool speaking)
        {
            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                icon.Glyph = speaking ? "\uE71A" : "\uE767";
            });
        }

        Instance.SpeakingChanged += OnSpeakingChanged;

        btn.Unloaded += (s, e) =>
        {
            Instance.SpeakingChanged -= OnSpeakingChanged;
        };

        return btn;
    }
}
