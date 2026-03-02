#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    /// <summary>
    /// MET Guide Service - Provides access to MET Excel guide content
    /// and contextual help for the desktop tool.
    /// </summary>
    public class METGuideService
    {
        private static METGuideService? _instance;
        public static METGuideService Instance => _instance ??= new METGuideService();

        private METGuideData? _guideData;
        private bool _isLoaded = false;

        private METGuideService()
        {
            LoadGuideData();
        }

        private void LoadGuideData()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataPath = Path.Combine(baseDir, "Data", "METGuide.json");

                if (File.Exists(dataPath))
                {
                    var json = File.ReadAllText(dataPath);
                    _guideData = JsonSerializer.Deserialize<METGuideData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    _isLoaded = true;
                    System.Diagnostics.Debug.WriteLine($"[METGuide] Loaded {_guideData?.Sections?.Count ?? 0} sections");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[METGuide] Guide file not found: {dataPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[METGuide] Error loading guide: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all guide sections
        /// </summary>
        public List<GuideSection> GetSections()
        {
            return _guideData?.Sections?.OrderBy(s => s.Order).ToList() ?? new List<GuideSection>();
        }

        /// <summary>
        /// Get a specific section by ID
        /// </summary>
        public GuideSection? GetSection(string sectionId)
        {
            return _guideData?.Sections?.FirstOrDefault(s =>
                s.Id?.Equals(sectionId, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Get a specific topic by ID
        /// </summary>
        public GuideTopic? GetTopic(string topicId)
        {
            if (_guideData?.Sections == null) return null;

            foreach (var section in _guideData.Sections)
            {
                var topic = section.Topics?.FirstOrDefault(t =>
                    t.Id?.Equals(topicId, StringComparison.OrdinalIgnoreCase) == true);
                if (topic != null) return topic;
            }
            return null;
        }

        /// <summary>
        /// Search guide content
        /// </summary>
        public List<GuideSearchResult> Search(string query)
        {
            var results = new List<GuideSearchResult>();
            if (string.IsNullOrWhiteSpace(query) || _guideData?.Sections == null)
                return results;

            var queryLower = query.ToLowerInvariant();
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var section in _guideData.Sections)
            {
                if (section.Topics == null) continue;

                foreach (var topic in section.Topics)
                {
                    int score = 0;

                    // Check title
                    if (topic.Title?.ToLowerInvariant().Contains(queryLower) == true)
                        score += 10;

                    // Check summary
                    if (topic.Summary?.ToLowerInvariant().Contains(queryLower) == true)
                        score += 5;

                    // Check keywords
                    if (topic.Keywords != null)
                    {
                        foreach (var keyword in topic.Keywords)
                        {
                            if (queryLower.Contains(keyword.ToLowerInvariant()) ||
                                keyword.ToLowerInvariant().Contains(queryLower))
                            {
                                score += 3;
                            }
                        }
                    }

                    // Check content
                    if (topic.Content != null)
                    {
                        foreach (var line in topic.Content)
                        {
                            if (line.ToLowerInvariant().Contains(queryLower))
                            {
                                score += 2;
                            }
                        }
                    }

                    if (score > 0)
                    {
                        results.Add(new GuideSearchResult
                        {
                            Topic = topic,
                            SectionTitle = section.Title ?? "",
                            SectionId = section.Id ?? "",
                            Score = score
                        });
                    }
                }
            }

            return results.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>
        /// Get contextual help for a specific view/tab
        /// </summary>
        public ContextualHelp? GetContextualHelp(string viewId)
        {
            if (_guideData?.ContextualHelp == null) return null;

            var key = viewId.ToLowerInvariant().Replace(" ", "-");
            if (_guideData.ContextualHelp.TryGetValue(key, out var help))
            {
                return help;
            }

            // Try alternate key formats
            var altKey = viewId.ToLowerInvariant();
            foreach (var kvp in _guideData.ContextualHelp)
            {
                if (kvp.Key.Replace("-", "").Contains(altKey.Replace("-", "")))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Get a quick answer for a common question
        /// </summary>
        public string? GetQuickAnswer(string question)
        {
            if (_guideData?.QuickAnswers == null) return null;

            var questionLower = question.ToLowerInvariant();

            // Direct match
            foreach (var kvp in _guideData.QuickAnswers)
            {
                if (questionLower.Contains(kvp.Key.ToLowerInvariant()))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all quick answers
        /// </summary>
        public Dictionary<string, string> GetQuickAnswers()
        {
            return _guideData?.QuickAnswers ?? new Dictionary<string, string>();
        }
    }

    #region Data Models

    public class METGuideData
    {
        public string? Version { get; set; }
        public string? LastUpdated { get; set; }
        public List<GuideSection>? Sections { get; set; }
        public Dictionary<string, ContextualHelp>? ContextualHelp { get; set; }
        public Dictionary<string, string>? QuickAnswers { get; set; }
    }

    public class GuideSection
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Icon { get; set; }
        public int Order { get; set; }
        public List<GuideTopic>? Topics { get; set; }
    }

    public class GuideTopic
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Images { get; set; }
        public List<string>? Content { get; set; }
        public List<string>? Tips { get; set; }
        public List<string>? Warnings { get; set; }
        public List<string>? Keywords { get; set; }
    }

    public class ContextualHelp
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<string>? Steps { get; set; }
        public List<string>? Tips { get; set; }
    }

    public class GuideSearchResult
    {
        public GuideTopic? Topic { get; set; }
        public string SectionTitle { get; set; } = "";
        public string SectionId { get; set; } = "";
        public int Score { get; set; }
    }

    #endregion
}
