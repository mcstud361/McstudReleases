#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    public class OnboardingAchievement
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string EarnedDate { get; set; } = "";
    }

    public class OnboardingStateService
    {
        private static OnboardingStateService? _instance;
        public static OnboardingStateService Instance => _instance ??= new OnboardingStateService();

        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private OnboardingState _state;

        private OnboardingStateService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            _settingsFile = Path.Combine(_settingsFolder, "onboarding_state.json");
            _state = LoadState();
        }

        public bool IsFirstLaunch => !_state.HasCompletedFirstLaunch;

        public bool HasNewVersionSinceLastSeen()
        {
            if (string.IsNullOrEmpty(_state.LastSeenVersion))
                return true;

            try
            {
                var lastSeen = new Version(_state.LastSeenVersion);
                return UpdateService.CurrentVersion > lastSeen;
            }
            catch
            {
                return true;
            }
        }

        public string GetLastSeenVersion() => _state.LastSeenVersion ?? "0.0.0";

        public void MarkFirstLaunchComplete()
        {
            _state.HasCompletedFirstLaunch = true;
            SaveState();
        }

        public void MarkVersionSeen()
        {
            _state.LastSeenVersion = $"{UpdateService.CurrentVersion.Major}.{UpdateService.CurrentVersion.Minor}.{UpdateService.CurrentVersion.Build}";
            SaveState();
        }

        public bool EarnAchievement(string id, string title)
        {
            if (_state.Achievements.Any(a => a.Id == id))
                return false;

            _state.Achievements.Add(new OnboardingAchievement
            {
                Id = id,
                Title = title,
                EarnedDate = DateTime.Now.ToString("o")
            });
            SaveState();
            return true;
        }

        public bool HasAchievement(string id) => _state.Achievements.Any(a => a.Id == id);

        public int GetAchievementCount() => _state.Achievements.Count;

        private void SaveState()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                    Directory.CreateDirectory(_settingsFolder);

                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine("[Onboarding] State saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Onboarding] Error saving state: {ex.Message}");
            }
        }

        private OnboardingState LoadState()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var state = JsonSerializer.Deserialize<OnboardingState>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (state != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[Onboarding] State loaded");
                        return state;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Onboarding] Error loading state: {ex.Message}");
            }

            return new OnboardingState();
        }
    }

    public class OnboardingState
    {
        public bool HasCompletedFirstLaunch { get; set; } = false;
        public string LastSeenVersion { get; set; } = "";
        public List<OnboardingAchievement> Achievements { get; set; } = new();
    }
}
