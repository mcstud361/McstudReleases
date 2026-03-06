#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace McStudDesktop.Services
{
    public enum LearningMode
    {
        Shop,
        Personal
    }

    public class LearningModeService
    {
        private static LearningModeService? _instance;
        public static LearningModeService Instance => _instance ??= new LearningModeService();

        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private LearningModeState _state;

        public event Action<LearningMode>? ModeChanged;

        public LearningMode CurrentMode => _state.Mode;

        private LearningModeService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            Directory.CreateDirectory(_settingsFolder);
            _settingsFile = Path.Combine(_settingsFolder, "learning_mode.json");
            _state = LoadState();
        }

        public void SetMode(LearningMode mode)
        {
            if (_state.Mode == mode) return;

            _state.Mode = mode;
            SaveState();
            System.Diagnostics.Debug.WriteLine($"[LearningMode] Mode changed to: {mode}");
            ModeChanged?.Invoke(mode);
        }

        private void SaveState()
        {
            try
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine("[LearningMode] State saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LearningMode] Error saving state: {ex.Message}");
            }
        }

        private LearningModeState LoadState()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var state = JsonSerializer.Deserialize<LearningModeState>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (state != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LearningMode] State loaded: {state.Mode}");
                        return state;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LearningMode] Error loading state: {ex.Message}");
            }

            return new LearningModeState();
        }
    }

    public class LearningModeState
    {
        public LearningMode Mode { get; set; } = LearningMode.Shop;
    }
}
