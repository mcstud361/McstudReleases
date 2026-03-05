#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Learning service that improves over time by analyzing estimates.
    ///
    /// How it works:
    /// 1. User feeds estimates in "training mode"
    /// 2. User maps estimate lines to operations they generate
    /// 3. System stores patterns: "Front Bumper + Repair" → [list of operations]
    /// 4. Future estimates auto-match based on learned patterns
    ///
    /// Storage: JSON file in AppData for persistence
    ///
    /// License Restrictions:
    /// - Client tier: Can parse and use suggestions, CANNOT train/learn
    /// - Shop/Admin tier: Full access including training
    /// </summary>
    public class EstimateLearningService
    {
        private readonly string _dataFilePath;
        private LearnedPatternDatabase _database;

        /// <summary>Read-only access to the current database for cleanup/analysis</summary>
        public LearnedPatternDatabase CurrentDatabase => _database;

        // License state - set by UI when checking license
        private LicenseTier _currentTier = LicenseTier.Shop; // Default to Shop for dev

        /// <summary>
        /// Set the current license tier (called by UI after license check)
        /// </summary>
        public void SetLicenseTier(LicenseTier tier)
        {
            _currentTier = tier;
            System.Diagnostics.Debug.WriteLine($"[Learning] License tier set to: {tier}");
        }

        /// <summary>
        /// Check if current license allows training/learning
        /// </summary>
        public bool CanTrain => _currentTier == LicenseTier.Shop || _currentTier == LicenseTier.Admin;

        /// <summary>
        /// Get current license tier
        /// </summary>
        public LicenseTier CurrentTier => _currentTier;

        // Singleton instance
        private static EstimateLearningService? _instance;
        public static EstimateLearningService Instance => _instance ??= new EstimateLearningService();

        // Base knowledge file (distributed with app) - read-only baseline
        private readonly string _baseKnowledgePath;
        // User knowledge file (in AppData) - user's additional learning
        private readonly string _userKnowledgePath;

        public EstimateLearningService()
        {
            // Base knowledge in app's Data folder (distributed with app)
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _baseKnowledgePath = Path.Combine(appDir, "Data", "LearnedPatterns.json");

            // User's additional learning in AppData
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _userKnowledgePath = Path.Combine(appDataPath, "learned_patterns.json");

            // Legacy support - keep _dataFilePath pointing to user knowledge
            _dataFilePath = _userKnowledgePath;

            _database = LoadDatabase();
        }

        /// <summary>
        /// Path to the base knowledge file (for publishing)
        /// </summary>
        public string BaseKnowledgePath => _baseKnowledgePath;

        #region Database Operations

        private LearnedPatternDatabase LoadDatabase()
        {
            var db = new LearnedPatternDatabase();

            // 1. Load base knowledge (distributed with app) - this is the baseline
            try
            {
                if (File.Exists(_baseKnowledgePath))
                {
                    var json = File.ReadAllText(_baseKnowledgePath);
                    var baseDb = JsonSerializer.Deserialize<LearnedPatternDatabase>(json);
                    if (baseDb != null)
                    {
                        db = baseDb;
                        System.Diagnostics.Debug.WriteLine($"[Learning] Loaded BASE knowledge: {db.Patterns.Count} patterns, {db.TrainingExamples.Count} examples");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] Error loading base knowledge: {ex.Message}");
            }

            // 2. Merge user's additional learning on top
            try
            {
                if (File.Exists(_userKnowledgePath))
                {
                    var json = File.ReadAllText(_userKnowledgePath);

                    // Check for corrupted file (just "null" or empty)
                    if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                    {
                        System.Diagnostics.Debug.WriteLine("[Learning] WARNING: User knowledge file is corrupted (null/empty)");

                        // Try to recover from backup
                        var backupPath = _userKnowledgePath + ".backup";
                        if (File.Exists(backupPath))
                        {
                            var backupJson = File.ReadAllText(backupPath);
                            if (!string.IsNullOrWhiteSpace(backupJson) && backupJson.Trim() != "null")
                            {
                                System.Diagnostics.Debug.WriteLine("[Learning] Recovering from backup file...");
                                json = backupJson;
                                // Restore the main file from backup
                                File.WriteAllText(_userKnowledgePath, backupJson);
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "null")
                    {
                        var userDb = JsonSerializer.Deserialize<LearnedPatternDatabase>(json);
                        if (userDb != null)
                        {
                            MergeDatabases(db, userDb);
                            System.Diagnostics.Debug.WriteLine($"[Learning] Merged USER knowledge: now {db.Patterns.Count} patterns, {db.TrainingExamples.Count} examples, {db.EstimatesImported} estimates");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] Error loading user knowledge: {ex.Message}");
            }

            // 3. Migrate to Smart Learning format if needed
            db = MigrateToSmartLearning(db);

            return db;
        }

        /// <summary>
        /// Migrates older database format to include Smart Learning fields.
        /// All new fields are nullable for backward compatibility.
        /// </summary>
        private LearnedPatternDatabase MigrateToSmartLearning(LearnedPatternDatabase db)
        {
            bool needsSave = false;

            // Initialize Smart Metadata if missing
            if (db.SmartMetadata == null)
            {
                db.SmartMetadata = new SmartLearningMetadata
                {
                    SchemaVersion = 1,
                    SmartFeaturesEnabledDate = DateTime.Now
                };
                needsSave = true;
                System.Diagnostics.Debug.WriteLine("[Learning] Initialized SmartLearningMetadata");
            }

            // Initialize feedback dictionary if missing
            if (db.PatternFeedbacks == null)
            {
                db.PatternFeedbacks = new Dictionary<string, PatternFeedback>();
                needsSave = true;
            }

            // Initialize quality records if missing
            if (db.QualityRecords == null)
            {
                db.QualityRecords = new List<EstimateQualityRecord>();
                needsSave = true;
            }

            // Initialize health metrics if missing
            if (db.HealthMetrics == null)
            {
                db.HealthMetrics = new LearningHealthMetrics();
                needsSave = true;
            }

            // Initialize baselines if missing
            if (db.Baselines == null)
            {
                db.Baselines = new Dictionary<string, OperationBaseline>();
                needsSave = true;

                // If we have existing training data, calculate initial baselines
                if (db.TrainingExamples.Count >= 5)
                {
                    CalculateBaselinesFromExamples(db);
                    System.Diagnostics.Debug.WriteLine($"[Learning] Calculated initial baselines from {db.TrainingExamples.Count} examples");
                }
            }

            // Initialize co-occurrences if missing
            if (db.CoOccurrences == null)
            {
                db.CoOccurrences = new Dictionary<string, CoOccurrenceRecord>();
                needsSave = true;
            }

            // Migrate patterns to include PatternVersion if not set
            foreach (var pattern in db.Patterns.Values)
            {
                if (pattern.PatternVersion == 0)
                {
                    pattern.PatternVersion = 1;
                    needsSave = true;
                }
            }

            // Update version if older
            if (db.Version < 4)
            {
                db.Version = 4;
                needsSave = true;
                System.Diagnostics.Debug.WriteLine("[Learning] Upgraded database to version 4 (Smart Learning)");
            }

            // Save if we made changes
            if (needsSave)
            {
                try
                {
                    SaveDatabase();
                    System.Diagnostics.Debug.WriteLine("[Learning] Saved migrated database");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Learning] Warning: Could not save migrated database: {ex.Message}");
                }
            }

            // Log bootstrap mode status
            if (db.IsBootstrapMode)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] BOOTSTRAP MODE active ({db.EstimatesImported}/20 estimates). Quality checks relaxed.");
            }

            return db;
        }

        /// <summary>
        /// Calculate statistical baselines from existing training examples.
        /// Called during migration or when baselines need refresh.
        /// </summary>
        private void CalculateBaselinesFromExamples(LearnedPatternDatabase db)
        {
            // Group examples by part|operation
            var groups = db.TrainingExamples
                .Where(e => !string.IsNullOrEmpty(e.PartName))
                .GroupBy(e => $"{e.PartName.ToLowerInvariant()}|{e.OperationType?.ToLowerInvariant() ?? "unknown"}")
                .Where(g => g.Count() >= 3);  // Need at least 3 samples for meaningful stats

            foreach (var group in groups)
            {
                var laborValues = group.Select(e => e.RepairHours).Where(v => v > 0).ToList();
                var refinishValues = group.Select(e => e.RefinishHours).Where(v => v > 0).ToList();
                var priceValues = group.Select(e => e.Price).Where(v => v > 0).ToList();

                var baseline = new OperationBaseline
                {
                    PartOperation = group.Key,
                    SampleCount = group.Count(),
                    LastUpdated = DateTime.Now
                };

                if (laborValues.Count >= 2)
                {
                    baseline.MeanLaborHours = laborValues.Average();
                    baseline.StdDevLaborHours = CalculateStdDev(laborValues);
                    baseline.MinLaborHours = laborValues.Min();
                    baseline.MaxLaborHours = laborValues.Max();
                }

                if (refinishValues.Count >= 2)
                {
                    baseline.MeanRefinishHours = refinishValues.Average();
                    baseline.StdDevRefinishHours = CalculateStdDev(refinishValues);
                    baseline.MinRefinishHours = refinishValues.Min();
                    baseline.MaxRefinishHours = refinishValues.Max();
                }

                if (priceValues.Count >= 2)
                {
                    baseline.MeanPrice = priceValues.Average();
                    baseline.StdDevPrice = CalculateStdDev(priceValues);
                    baseline.MinPrice = priceValues.Min();
                    baseline.MaxPrice = priceValues.Max();
                }

                db.Baselines![group.Key] = baseline;
            }
        }

        /// <summary>
        /// Calculate standard deviation for a list of decimal values
        /// </summary>
        private static decimal CalculateStdDev(List<decimal> values)
        {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
            return (decimal)Math.Sqrt((double)(sumOfSquares / (values.Count - 1)));
        }

        /// <summary>
        /// Merge user's database into base database (user data takes precedence)
        /// </summary>
        private void MergeDatabases(LearnedPatternDatabase baseDb, LearnedPatternDatabase userDb)
        {
            // Merge patterns (user patterns override base patterns with same key)
            foreach (var kvp in userDb.Patterns)
            {
                baseDb.Patterns[kvp.Key] = kvp.Value;
            }

            // Merge manual line patterns
            foreach (var kvp in userDb.ManualLinePatterns)
            {
                baseDb.ManualLinePatterns[kvp.Key] = kvp.Value;
            }

            // Add user training examples (avoid duplicates by checking normalized key)
            var existingKeys = baseDb.TrainingExamples.Select(e => e.NormalizedKey).ToHashSet();
            foreach (var example in userDb.TrainingExamples)
            {
                if (!existingKeys.Contains(example.NormalizedKey))
                {
                    baseDb.TrainingExamples.Add(example);
                    existingKeys.Add(example.NormalizedKey);
                }
            }

            // Add user trained estimates (avoid duplicates)
            var existingEstimates = baseDb.TrainedEstimates.Select(e => e.Id).ToHashSet();
            foreach (var estimate in userDb.TrainedEstimates)
            {
                if (!existingEstimates.Contains(estimate.Id))
                {
                    baseDb.TrainedEstimates.Add(estimate);
                }
            }

            // Update statistics (sum them)
            baseDb.EstimatesImported += userDb.EstimatesImported;
            baseDb.TotalEstimateValue += userDb.TotalEstimateValue;
            // Note: AverageEstimateValue is a computed property, no need to assign

            // Use the most recent LastUpdated
            if (userDb.LastUpdated > baseDb.LastUpdated)
            {
                baseDb.LastUpdated = userDb.LastUpdated;
            }

            // Merge co-occurrence data
            if (userDb.CoOccurrences != null)
            {
                baseDb.CoOccurrences ??= new Dictionary<string, CoOccurrenceRecord>();
                foreach (var (key, userRecord) in userDb.CoOccurrences)
                {
                    if (baseDb.CoOccurrences.TryGetValue(key, out var baseRecord))
                    {
                        baseRecord.TotalEstimateCount += userRecord.TotalEstimateCount;
                        foreach (var (entryKey, userEntry) in userRecord.CoOccurringOperations)
                        {
                            if (baseRecord.CoOccurringOperations.TryGetValue(entryKey, out var baseEntry))
                            {
                                int total = baseEntry.TimesSeenTogether + userEntry.TimesSeenTogether;
                                baseEntry.AvgLaborHours = (baseEntry.AvgLaborHours * baseEntry.TimesSeenTogether + userEntry.AvgLaborHours * userEntry.TimesSeenTogether) / total;
                                baseEntry.AvgRefinishHours = (baseEntry.AvgRefinishHours * baseEntry.TimesSeenTogether + userEntry.AvgRefinishHours * userEntry.TimesSeenTogether) / total;
                                baseEntry.AvgPrice = (baseEntry.AvgPrice * baseEntry.TimesSeenTogether + userEntry.AvgPrice * userEntry.TimesSeenTogether) / total;
                                baseEntry.TimesSeenTogether = total;
                            }
                            else
                            {
                                baseRecord.CoOccurringOperations[entryKey] = userEntry;
                            }
                        }
                    }
                    else
                    {
                        baseDb.CoOccurrences[key] = userRecord;
                    }
                }
            }
        }

        public void SaveDatabase()
        {
            try
            {
                // SAFEGUARD: Never save null or effectively empty database
                if (_database == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Learning] WARNING: Attempted to save null database - SKIPPED");
                    return;
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_database, options);

                // SAFEGUARD: Don't save if serialization produced null/empty
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                {
                    System.Diagnostics.Debug.WriteLine("[Learning] WARNING: Serialization produced null/empty - SKIPPED save");
                    return;
                }

                // Create backup before overwriting if file has data
                if (File.Exists(_dataFilePath))
                {
                    var existingContent = File.ReadAllText(_dataFilePath);
                    if (!string.IsNullOrWhiteSpace(existingContent) && existingContent != "null" && existingContent.Length > 10)
                    {
                        var backupPath = _dataFilePath + ".backup";
                        File.WriteAllText(backupPath, existingContent);
                    }
                }

                File.WriteAllText(_dataFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[Learning] Saved {_database.Patterns.Count} patterns, {_database.EstimatesImported} estimates imported");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] Error saving database: {ex.Message}");
            }
        }

        /// <summary>
        /// Replace the current database with a cleaned/rebuilt version.
        /// Creates a timestamped backup before replacing.
        /// </summary>
        public void ReplaceDatabase(LearnedPatternDatabase newDb)
        {
            // Create timestamped backup
            if (File.Exists(_dataFilePath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = _dataFilePath + $".pre_cleanup_{timestamp}";
                File.Copy(_dataFilePath, backupPath, overwrite: true);
                System.Diagnostics.Debug.WriteLine($"[Learning] Backup saved to {backupPath}");
            }

            _database = newDb;
            SaveDatabase();
            System.Diagnostics.Debug.WriteLine($"[Learning] Database replaced: {newDb.Patterns.Count} patterns, {newDb.TrainingExamples.Count} examples");
        }

        /// <summary>
        /// Publish the current learned database to the app's Data folder.
        /// This "bakes" the learning into the app so new users get it.
        /// Call this before distributing the app to share knowledge.
        /// </summary>
        /// <returns>True if successful, error message otherwise</returns>
        public (bool Success, string Message) PublishLearning()
        {
            try
            {
                // Ensure Data folder exists
                var dataDir = Path.GetDirectoryName(_baseKnowledgePath);
                if (!string.IsNullOrEmpty(dataDir) && !Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                // Serialize and save to base knowledge path
                var json = JsonSerializer.Serialize(_database, options);
                File.WriteAllText(_baseKnowledgePath, json);

                var stats = GetStatistics();
                System.Diagnostics.Debug.WriteLine($"[Learning] PUBLISHED to base knowledge: {_baseKnowledgePath}");
                System.Diagnostics.Debug.WriteLine($"[Learning] Published: {stats.TotalPatterns} patterns, {stats.TotalTrainingExamples} examples, {stats.TotalTrainedEstimates} estimates");

                return (true, $"Published {stats.TotalPatterns} patterns, {stats.TotalTrainingExamples} training examples.\nPath: {_baseKnowledgePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] Error publishing: {ex.Message}");
                return (false, $"Error publishing: {ex.Message}");
            }
        }

        #endregion

        #region Training Mode

        /// <summary>
        /// Add a training example: an estimate line and what operations it should generate
        /// </summary>
        public void AddTrainingExample(TrainingExample example)
        {
            // Skip junk lines that pass the strong header/footer filter
            if (!string.IsNullOrWhiteSpace(example.EstimateLine) &&
                EstimatePdfParser.IsHeaderOrFooter(example.EstimateLine))
                return;

            // Skip lines with no meaningful data
            if (string.IsNullOrWhiteSpace(example.PartName) &&
                string.IsNullOrWhiteSpace(example.OperationType) &&
                example.RepairHours == 0 && example.RefinishHours == 0 && example.Price == 0)
                return;

            // Normalize the line item for pattern matching
            var normalizedKey = NormalizeLineItem(example.EstimateLine);
            example.NormalizedKey = normalizedKey;
            example.DateAdded = DateTime.Now;

            _database.TrainingExamples.Add(example);

            // Update or create pattern
            UpdatePatternFromExample(example);

            SaveDatabase();
        }

        /// <summary>
        /// Learn from a complete estimate with mapped operations.
        /// Now includes full context linking to P-Pages, DEG, and IncludedNotIncluded data.
        /// REQUIRES Shop or Admin license tier.
        /// </summary>
        /// <returns>True if learning was successful, false if blocked by license</returns>
        public bool LearnFromEstimate(EstimateTrainingData trainingData)
        {
            // Check license tier
            if (!CanTrain)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] BLOCKED - Client license cannot train. Tier: {_currentTier}");
                return false;
            }

            trainingData.DateTrained = DateTime.Now;
            trainingData.Id = Guid.NewGuid().ToString();

            foreach (var mapping in trainingData.LineMappings)
            {
                var example = new TrainingExample
                {
                    EstimateLine = mapping.RawLine,
                    PartName = mapping.PartName,
                    OperationType = mapping.OperationType,
                    RepairHours = mapping.RepairHours,
                    RefinishHours = mapping.RefinishHours,
                    Price = mapping.Price,
                    GeneratedOperations = mapping.GeneratedOperations,
                    Source = trainingData.Source,
                    VehicleInfo = trainingData.VehicleInfo,
                    // Add full context references
                    LinkedPPageRef = FindPPageReference(mapping.PartName, mapping.OperationType),
                    LinkedIncludedNotIncludedRef = FindIncludedNotIncludedReference(mapping.PartName, mapping.OperationType),
                    LinkedDEGRef = FindDEGReference(mapping.PartName, mapping.OperationType)
                };

                AddTrainingExample(example);
            }

            // Build co-occurrence data from all operations in this estimate
            var opsList = trainingData.LineMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.PartName))
                .Select(m =>
                {
                    var normPart = NormalizePartName(m.PartName);
                    var normOp = NormalizeOperationType(m.OperationType);
                    var patternKey = string.IsNullOrEmpty(normOp) ? normPart : $"{normPart}|{normOp}";
                    return (PatternKey: patternKey, PartName: m.PartName, OperationType: m.OperationType,
                            LaborHours: m.RepairHours, RefinishHours: m.RefinishHours, Price: m.Price);
                })
                .ToList();
            UpdateCoOccurrences(opsList);

            _database.TrainedEstimates.Add(trainingData);
            SaveDatabase();

            System.Diagnostics.Debug.WriteLine($"[Learning] Learned estimate with full context linking ({opsList.Count} ops for co-occurrence)");
            return true;
        }

        #region Full Context Linking

        /// <summary>
        /// Find P-Page reference that matches this part/operation
        /// </summary>
        private string? FindPPageReference(string partName, string operationType)
        {
            if (string.IsNullOrWhiteSpace(partName)) return null;

            // Common P-Page references based on part name
            var pPageMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "bumper", "P-17: Bumper Operations" },
                { "bumper cover", "P-17: Bumper Cover R&I/Replace" },
                { "fender", "P-23: Fender Operations" },
                { "door", "P-25: Door Operations" },
                { "hood", "P-21: Hood Operations" },
                { "trunk", "P-31: Trunk/Decklid Operations" },
                { "quarter panel", "P-29: Quarter Panel Operations" },
                { "roof", "P-27: Roof Operations" },
                { "windshield", "P-33: Glass Operations" },
                { "headlight", "P-35: Lighting Operations" },
                { "taillight", "P-35: Lighting Operations" },
                { "mirror", "P-37: Mirror Operations" },
                { "wheel", "P-41: Wheel/Suspension" },
                { "airbag", "P-45: SRS Operations" },
                { "refinish", "P-50+: Refinish Operations" },
                { "blend", "P-52: Blend Operations" },
                { "clear coat", "P-53: Clear Coat Operations" },
                { "scan", "P-47: Diagnostic Scanning" },
                { "calibration", "P-48: ADAS Calibration" }
            };

            // Try to find a matching P-Page reference
            foreach (var kvp in pPageMappings)
            {
                if (partName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // Check operation type for refinish operations
            if (operationType.Contains("Refn", StringComparison.OrdinalIgnoreCase) ||
                operationType.Contains("Blend", StringComparison.OrdinalIgnoreCase))
            {
                return "P-50+: Refinish Operations";
            }

            return null;
        }

        /// <summary>
        /// Find IncludedNotIncluded reference that matches this part/operation
        /// </summary>
        private string? FindIncludedNotIncludedReference(string partName, string operationType)
        {
            if (string.IsNullOrWhiteSpace(partName)) return null;

            // Map part names to IncludedNotIncluded entry IDs
            var normalizedPart = partName.ToLowerInvariant().Replace(" ", "-");
            var normalizedOp = operationType?.ToLowerInvariant() ?? "replace";

            // Return a reference ID that can be used to look up the entry
            return $"incl-{normalizedPart}-{normalizedOp}";
        }

        /// <summary>
        /// Find DEG inquiry reference that matches this part/operation
        /// </summary>
        private string? FindDEGReference(string partName, string operationType)
        {
            if (string.IsNullOrWhiteSpace(partName)) return null;

            // Common DEG inquiry patterns - these could be loaded from data
            var degMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "bumper", new[] { "DEG 16044", "DEG 18527", "DEG 21334" } },
                { "fender", new[] { "DEG 15892", "DEG 19456" } },
                { "blend", new[] { "DEG 10923", "DEG 14567" } },
                { "scan", new[] { "DEG 21892", "DEG 22456", "DEG 23891" } },
                { "calibration", new[] { "DEG 21234", "DEG 22567" } },
                { "adhesion", new[] { "DEG 12456" } },
                { "flex", new[] { "DEG 12789" } },
                { "clear coat", new[] { "DEG 13456", "DEG 14789" } },
                { "denib", new[] { "DEG 15123" } },
                { "wet sand", new[] { "DEG 15456" } }
            };

            foreach (var kvp in degMappings)
            {
                if (partName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                    (operationType?.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return string.Join(", ", kvp.Value);
                }
            }

            return null;
        }

        #endregion

        /// <summary>
        /// Update pattern database based on a training example
        /// </summary>
        private void UpdatePatternFromExample(TrainingExample example)
        {
            var patternKey = GeneratePatternKey(example);

            if (_database.Patterns.TryGetValue(patternKey, out var existing))
            {
                // Update existing pattern with new example
                existing.ExampleCount++;
                existing.LastUpdated = DateTime.Now;

                // Merge operations (keep unique ones)
                foreach (var op in example.GeneratedOperations)
                {
                    var existingOp = existing.Operations.FirstOrDefault(o =>
                        o.Description.Equals(op.Description, StringComparison.OrdinalIgnoreCase));

                    if (existingOp != null)
                    {
                        existingOp.TimesUsed++;
                        // Running average: new_avg = ((old_avg * (n-1)) + new_value) / n
                        if (op.LaborHours > 0)
                            existingOp.LaborHours = ((existingOp.LaborHours * (existingOp.TimesUsed - 1)) + op.LaborHours) / existingOp.TimesUsed;
                        if (op.RepairHours > 0)
                            existingOp.RepairHours = ((existingOp.RepairHours * (existingOp.TimesUsed - 1)) + op.RepairHours) / existingOp.TimesUsed;
                        if (op.RefinishHours > 0)
                            existingOp.RefinishHours = ((existingOp.RefinishHours * (existingOp.TimesUsed - 1)) + op.RefinishHours) / existingOp.TimesUsed;
                        if (op.Price > 0)
                            existingOp.Price = ((existingOp.Price * (existingOp.TimesUsed - 1)) + op.Price) / existingOp.TimesUsed;
                    }
                    else
                    {
                        op.TimesUsed = 1;
                        existing.Operations.Add(op);
                    }
                }

                // Recalculate confidence
                existing.Confidence = CalculateConfidence(existing);
            }
            else
            {
                // Create new pattern
                var newPattern = new LearnedPattern
                {
                    PatternKey = patternKey,
                    PartName = example.PartName,
                    OperationType = example.OperationType,
                    Operations = example.GeneratedOperations.ToList(),
                    ExampleCount = 1,
                    DateCreated = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    Confidence = 0.5 // Start at 50% confidence
                };

                foreach (var op in newPattern.Operations)
                {
                    op.TimesUsed = 1;
                }

                _database.Patterns[patternKey] = newPattern;
            }
        }

        #endregion

        #region Pattern Matching

        /// <summary>
        /// Analyze an estimate line and find matching patterns
        /// </summary>
        public List<PatternMatch> FindMatches(string estimateLine)
        {
            var matches = new List<PatternMatch>();
            var normalizedLine = NormalizeLineItem(estimateLine);
            var extracted = ExtractLineComponents(estimateLine);

            foreach (var pattern in _database.Patterns.Values)
            {
                var score = CalculateMatchScore(normalizedLine, extracted, pattern);
                if (score > 0.3) // Minimum 30% match
                {
                    matches.Add(new PatternMatch
                    {
                        Pattern = pattern,
                        MatchScore = score,
                        SourceLine = estimateLine,
                        ExtractedData = extracted
                    });
                }
            }

            return matches.OrderByDescending(m => m.MatchScore).ToList();
        }

        /// <summary>
        /// Generate operations for an estimate based on learned patterns
        /// </summary>
        public List<GeneratedOperation> GenerateOperations(ParsedEstimateLine line)
        {
            var operations = new List<GeneratedOperation>();
            var matches = FindMatches(line.RawLine);

            if (matches.Count == 0)
            {
                // No learned pattern - return basic suggestion
                return GenerateDefaultOperations(line);
            }

            // Use best match
            var bestMatch = matches[0];

            foreach (var patternOp in bestMatch.Pattern.Operations)
            {
                var generated = new GeneratedOperation
                {
                    OperationType = patternOp.OperationType,
                    Description = patternOp.Description,
                    Category = patternOp.Category,
                    Confidence = bestMatch.MatchScore * bestMatch.Pattern.Confidence,
                    Source = $"Learned from {bestMatch.Pattern.ExampleCount} examples"
                };

                // Scale hours based on input if available
                if (line.RepairHours > 0 && patternOp.RepairHours > 0)
                {
                    var ratio = line.RepairHours / patternOp.RepairHours;
                    generated.RepairHours = line.RepairHours;
                    generated.LaborHours = patternOp.LaborHours * ratio;
                }
                else
                {
                    generated.RepairHours = patternOp.RepairHours;
                    generated.LaborHours = patternOp.LaborHours;
                }

                if (line.RefinishHours > 0)
                {
                    generated.RefinishHours = line.RefinishHours;
                }
                else
                {
                    generated.RefinishHours = patternOp.RefinishHours;
                }

                generated.Price = line.Price > 0 ? line.Price : patternOp.Price;

                operations.Add(generated);
            }

            return operations;
        }

        /// <summary>
        /// Generate default operations when no pattern matches
        /// </summary>
        private List<GeneratedOperation> GenerateDefaultOperations(ParsedEstimateLine line)
        {
            var operations = new List<GeneratedOperation>();

            // Basic operation based on type
            var op = new GeneratedOperation
            {
                OperationType = line.OperationType,
                Description = line.Description,
                Category = DetectCategory(line.Description),
                RepairHours = line.RepairHours,
                RefinishHours = line.RefinishHours,
                LaborHours = line.LaborHours,
                Price = line.Price,
                Confidence = 0.3,
                Source = "Default (no learned pattern)"
            };

            operations.Add(op);
            return operations;
        }

        #endregion

        #region Normalization & Extraction

        /// <summary>
        /// Comprehensive part name and abbreviation mappings for normalization
        /// </summary>
        private static readonly Dictionary<string, string> _normalizations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Position/Side abbreviations
            { "frt", "front" }, { "rr", "rear" }, { "lh", "left" }, { "rh", "right" },
            { "lt", "left" }, { "rt", "right" }, { "lf", "left_front" }, { "rf", "right_front" },
            { "lr", "left_rear" }, { "ctr", "center" }, { "upr", "upper" }, { "lwr", "lower" },
            { "inr", "inner" }, { "otr", "outer" }, { "fwd", "forward" },
            // Part abbreviations
            { "bpr", "bumper" }, { "cvr", "cover" }, { "pnl", "panel" }, { "qtr", "quarter" },
            { "assy", "assembly" }, { "brkt", "bracket" }, { "reinf", "reinforcement" },
            { "mldg", "molding" }, { "hndl", "handle" }, { "mirr", "mirror" },
            { "hdlmp", "headlamp" }, { "tllmp", "taillamp" }, { "flr", "floor" },
            { "whl", "wheel" }, { "wndsld", "windshield" }, { "w/s", "windshield" },
            { "abs", "absorber" }, { "supp", "support" }, { "mtg", "mounting" },
            // Operation abbreviations
            { "r&i", "remove_install" }, { "r+i", "remove_install" }, { "r/i", "remove_install" },
            { "o/h", "overhaul" }, { "oh", "overhaul" },
            { "rpr", "repair" }, { "repl", "replace" },
            { "ref", "refinish" }, { "rfn", "refinish" }, { "refn", "refinish" },
            { "blnd", "blend" }, { "algn", "alignment" }, { "subl", "sublet" },
            // Other
            { "w/", "with_" }, { "w/o", "without_" },
            { "incl", "included" }, { "excl", "excluded" },
        };

        /// <summary>
        /// Normalize a line item for pattern matching
        /// Removes noise, standardizes terminology
        /// </summary>
        public string NormalizeLineItem(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";

            var normalized = line.ToLowerInvariant();

            // Remove prices and numbers
            normalized = Regex.Replace(normalized, @"\$[\d,]+\.?\d*", "");
            normalized = Regex.Replace(normalized, @"\b\d+\.?\d*\s*(hrs?|hours?|units?)\b", "");
            normalized = Regex.Replace(normalized, @"\b\d{5,}\b", ""); // Part numbers

            // Apply comprehensive normalizations with word boundaries
            foreach (var kvp in _normalizations)
            {
                var pattern = $@"\b{Regex.Escape(kvp.Key)}\b";
                normalized = Regex.Replace(normalized, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }

            // Remove extra whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Normalize a part name for consistent matching
        /// </summary>
        public string NormalizePartNameForMatching(string partName)
        {
            if (string.IsNullOrWhiteSpace(partName)) return "";

            var normalized = partName.ToLowerInvariant();

            // Apply normalizations
            foreach (var kvp in _normalizations)
            {
                var pattern = $@"\b{Regex.Escape(kvp.Key)}\b";
                normalized = Regex.Replace(normalized, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }

            // Remove common noise words
            var noiseWords = new[] { "the", "a", "an", "for", "and", "&" };
            foreach (var noise in noiseWords)
            {
                normalized = Regex.Replace(normalized, $@"\b{noise}\b", " ", RegexOptions.IgnoreCase);
            }

            // Clean up
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            normalized = Regex.Replace(normalized, @"\s+", "_").Trim('_');

            return normalized;
        }

        /// <summary>
        /// Extract components from an estimate line
        /// </summary>
        public ExtractedLineData ExtractLineComponents(string line)
        {
            var data = new ExtractedLineData { RawLine = line };
            var lower = line.ToLowerInvariant();

            // Extract operation type
            if (lower.Contains("replace") || lower.Contains("repl"))
                data.OperationType = "Replace";
            else if (lower.Contains("repair") || lower.Contains("rpr"))
                data.OperationType = "Repair";
            else if (lower.Contains("r&i") || lower.Contains("r+i") || lower.Contains("remove"))
                data.OperationType = "R&I";
            else if (lower.Contains("refinish") || lower.Contains("rfn") || lower.Contains("paint"))
                data.OperationType = "Refinish";
            else if (lower.Contains("blend"))
                data.OperationType = "Blend";
            else if (lower.Contains("o/h") || lower.Contains("overhaul"))
                data.OperationType = "Overhaul";

            // Extract part name
            data.PartName = ExtractPartName(line);

            // Extract position (front/rear/left/right)
            if (lower.Contains("front") || lower.Contains("frt"))
                data.Position = "Front";
            else if (lower.Contains("rear") || lower.Contains("rr"))
                data.Position = "Rear";

            if (lower.Contains("left") || lower.Contains("lh") || lower.Contains("driver"))
                data.Side = "Left";
            else if (lower.Contains("right") || lower.Contains("rh") || lower.Contains("passenger"))
                data.Side = "Right";

            // Extract hours
            var hoursMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(hrs?|hours?|labor)", RegexOptions.IgnoreCase);
            if (hoursMatch.Success && decimal.TryParse(hoursMatch.Groups[1].Value, out var hours))
            {
                data.LaborHours = hours;
            }

            var refinishMatch = Regex.Match(line, @"(\d+\.?\d*)\s*(ref|rfn|refinish|paint)", RegexOptions.IgnoreCase);
            if (refinishMatch.Success && decimal.TryParse(refinishMatch.Groups[1].Value, out var refHours))
            {
                data.RefinishHours = refHours;
            }

            // Extract price
            var priceMatch = Regex.Match(line, @"\$\s*([\d,]+\.?\d*)");
            if (priceMatch.Success)
            {
                var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(priceStr, out var price))
                {
                    data.Price = price;
                }
            }

            return data;
        }

        /// <summary>
        /// Extract the part name from a line
        /// </summary>
        private string ExtractPartName(string line)
        {
            var knownParts = new[]
            {
                "bumper cover", "bumper fascia", "bumper reinforcement", "bumper absorber", "bumper",
                "fender liner", "fender flare", "fender", "inner fender",
                "hood hinge", "hood latch", "hood strut", "hood insulator", "hood",
                "door shell", "door skin", "door handle", "door mirror", "door glass", "door trim", "door",
                "quarter panel", "quarter glass", "quarter",
                "roof rail", "roof panel", "sunroof", "headliner", "roof",
                "trunk lid", "decklid", "trunk floor", "liftgate", "tailgate", "trunk",
                "grille", "radiator grille",
                "headlight", "headlamp", "head light",
                "taillight", "taillamp", "tail light", "brake light",
                "mirror", "side mirror", "door mirror",
                "windshield", "front glass", "back glass", "rear glass",
                "a-pillar", "b-pillar", "c-pillar", "pillar",
                "rocker panel", "rocker", "side sill",
                "radiator support", "core support", "radiator",
                "frame rail", "subframe", "frame",
                "control arm", "ball joint", "tie rod", "strut", "shock", "suspension",
                "condenser", "evaporator", "compressor", "ac", "a/c",
                "airbag", "air bag", "seatbelt", "seat belt"
            };

            var lower = line.ToLowerInvariant();

            // Find the longest matching part name
            string? bestMatch = null;
            foreach (var part in knownParts)
            {
                if (lower.Contains(part))
                {
                    if (bestMatch == null || part.Length > bestMatch.Length)
                    {
                        bestMatch = part;
                    }
                }
            }

            return bestMatch ?? "";
        }

        /// <summary>
        /// Detect category from description
        /// </summary>
        private string DetectCategory(string description)
        {
            var lower = description.ToLowerInvariant();

            if (lower.Contains("bumper") || lower.Contains("fender") || lower.Contains("hood") ||
                lower.Contains("door") || lower.Contains("quarter") || lower.Contains("trunk") ||
                lower.Contains("grille") || lower.Contains("light") || lower.Contains("mirror"))
                return "Part Operations";

            if (lower.Contains("frame") || lower.Contains("pillar") || lower.Contains("rocker") ||
                lower.Contains("radiator support") || lower.Contains("structural"))
                return "Body Operations";

            if (lower.Contains("refinish") || lower.Contains("paint") || lower.Contains("blend") ||
                lower.Contains("clear") || lower.Contains("base"))
                return "Refinish Operations";

            if (lower.Contains("ac") || lower.Contains("a/c") || lower.Contains("suspension") ||
                lower.Contains("wheel") || lower.Contains("brake") || lower.Contains("alignment"))
                return "Mechanical Operations";

            if (lower.Contains("airbag") || lower.Contains("srs") || lower.Contains("seatbelt"))
                return "SRS Operations";

            return "SOP List";
        }

        #endregion

        #region Scoring

        /// <summary>
        /// Generate a unique key for a pattern
        /// </summary>
        private string GeneratePatternKey(TrainingExample example)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(example.PartName))
                parts.Add(example.PartName.ToLowerInvariant().Replace(" ", "_"));

            if (!string.IsNullOrEmpty(example.OperationType))
                parts.Add(example.OperationType.ToLowerInvariant());

            return string.Join("|", parts);
        }

        /// <summary>
        /// Calculate match score between a line and a pattern using SMART matching
        /// </summary>
        private double CalculateMatchScore(string normalizedLine, ExtractedLineData extracted, LearnedPattern pattern)
        {
            double score = 0;
            int factors = 0;

            // Normalize both part names for comparison
            var extractedPartNorm = NormalizePartNameForMatching(extracted.PartName);
            var patternPartNorm = NormalizePartNameForMatching(pattern.PartName);

            // Part name match (most important - 50% of score)
            if (!string.IsNullOrEmpty(extractedPartNorm) && !string.IsNullOrEmpty(patternPartNorm))
            {
                // Exact match after normalization
                if (extractedPartNorm.Equals(patternPartNorm, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.5;
                }
                // Contains match
                else if (extractedPartNorm.Contains(patternPartNorm, StringComparison.OrdinalIgnoreCase) ||
                         patternPartNorm.Contains(extractedPartNorm, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.35;
                }
                // Fuzzy word overlap match
                else
                {
                    var extractedWords = extractedPartNorm.Split('_').Where(w => w.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var patternWords = patternPartNorm.Split('_').Where(w => w.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (extractedWords.Count > 0 && patternWords.Count > 0)
                    {
                        var overlap = extractedWords.Intersect(patternWords).Count();
                        var maxPossible = Math.Max(extractedWords.Count, patternWords.Count);
                        var overlapRatio = (double)overlap / maxPossible;

                        if (overlapRatio >= 0.5)
                        {
                            score += 0.25 * overlapRatio;
                        }
                    }
                }
                factors++;
            }

            // Operation type match (30% of score)
            if (!string.IsNullOrEmpty(extracted.OperationType) && !string.IsNullOrEmpty(pattern.OperationType))
            {
                var extractedOp = NormalizeLineItem(extracted.OperationType);
                var patternOp = NormalizeLineItem(pattern.OperationType);

                if (extractedOp.Equals(patternOp, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.3;
                }
                else if (AreOperationsSimilar(extracted.OperationType, pattern.OperationType))
                {
                    score += 0.15;
                }
                factors++;
            }

            // Keyword overlap from full line (20% of score)
            var patternKeyWords = pattern.PatternKey.Split('|', '_').Where(w => w.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lineWords = normalizedLine.Split(' ', '_').Where(w => w.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var keywordOverlap = patternKeyWords.Intersect(lineWords).Count();
            if (patternKeyWords.Count > 0)
            {
                score += 0.2 * ((double)keywordOverlap / patternKeyWords.Count);
                factors++;
            }

            return factors > 0 ? score : 0;
        }

        /// <summary>
        /// Check if two operation types are similar (e.g., Repl and Replace)
        /// </summary>
        private bool AreOperationsSimilar(string op1, string op2)
        {
            var operationGroups = new[]
            {
                new[] { "replace", "repl", "new" },
                new[] { "repair", "rpr" },
                new[] { "refinish", "refn", "ref", "rfn", "paint" },
                new[] { "r&i", "r+i", "r/i", "remove_install", "remove and install" },
                new[] { "blend", "blnd" },
                new[] { "overhaul", "o/h", "oh" },
                new[] { "alignment", "algn", "align" },
                new[] { "sublet", "subl" }
            };

            var norm1 = op1.ToLowerInvariant().Trim();
            var norm2 = op2.ToLowerInvariant().Trim();

            foreach (var group in operationGroups)
            {
                if (group.Contains(norm1) && group.Contains(norm2))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Calculate confidence for a pattern based on example count
        /// </summary>
        private double CalculateConfidence(LearnedPattern pattern)
        {
            // More examples = higher confidence (asymptotic to 1.0)
            // 1 example = 0.5, 5 examples = 0.83, 10 examples = 0.91
            return 1.0 - (1.0 / (1.0 + pattern.ExampleCount * 0.5));
        }

        #endregion

        #region Manual Line Learning

        /// <summary>
        /// Parse estimate lines from CCC PDF format.
        /// CCC format: Line# | # | Oper | Description | PartNum | Qty | Price | Labor | Paint
        /// Section headers are ALL CAPS with no # (e.g., "ELECTRICAL", "FRONT DOOR")
        /// </summary>
        public List<ParsedEstimateLine> ParseWithManualLineDetection(string estimateText)
        {
            var lines = estimateText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var parsedLines = new List<ParsedEstimateLine>();
            string? currentSection = null;
            string? currentPartName = null;

            System.Diagnostics.Debug.WriteLine($"[PDF Parse] Starting parse of {lines.Length} lines");

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3) continue;

                // Skip header rows and non-data lines
                if (IsHeaderOrFooterLine(trimmed)) continue;

                // Check if this is a section header (ALL CAPS, no numbers at start except line#)
                if (IsSectionHeader(trimmed))
                {
                    currentSection = ExtractSectionName(trimmed);
                    System.Diagnostics.Debug.WriteLine($"[PDF Parse] Section: {currentSection}");
                    continue;
                }

                // Try to parse as CCC line format
                var parsed = ParseCCCLine(trimmed, currentSection);
                if (parsed != null)
                {
                    // Track current part for linking related operations
                    if (!string.IsNullOrEmpty(parsed.PartName) && !parsed.IsManualLine)
                    {
                        currentPartName = parsed.PartName;
                    }
                    else if (parsed.IsManualLine && !string.IsNullOrEmpty(currentPartName))
                    {
                        parsed.ParentPartName = currentPartName;
                    }

                    parsedLines.Add(parsed);
                    System.Diagnostics.Debug.WriteLine($"[PDF Parse] Line: {parsed.OperationType} | {parsed.Description} | L:{parsed.LaborHours} P:{parsed.RefinishHours} | Manual:{parsed.IsManualLine}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[PDF Parse] Parsed {parsedLines.Count} lines");
            return parsedLines;
        }

        /// <summary>
        /// Parse a CCC estimate line: Line# | #/* | Oper | Description | PartNum | Qty | Price | Labor | Paint
        /// </summary>
        private ParsedEstimateLine? ParseCCCLine(string line, string? currentSection)
        {
            var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count < 2) return null;

            // Skip if this is a section header that got through
            if (words.Count <= 3 && words.All(w => w == w.ToUpper() && !char.IsDigit(w[0])))
                return null;

            // Detect if line starts with line number
            int startIndex = 0;
            if (int.TryParse(words[0], out _))
            {
                startIndex = 1; // Skip line number
            }

            // Check for # or * marker
            bool hasHashMarker = false;
            if (startIndex < words.Count && (words[startIndex] == "#" || words[startIndex] == "*" || words[startIndex] == "**"))
            {
                hasHashMarker = true;
                startIndex++;
            }

            if (startIndex >= words.Count) return null;

            // Get operation type
            string? operationType = null;
            var operationCodes = new[] { "Repl", "Rpr", "Refn", "R&I", "Blnd", "Algn", "Subl", "Add" };
            if (operationCodes.Any(op => words[startIndex].Equals(op, StringComparison.OrdinalIgnoreCase)))
            {
                operationType = words[startIndex];
                startIndex++;
            }

            if (startIndex >= words.Count) return null;

            // Extract description - everything until we hit numbers/part numbers
            var descriptionParts = new List<string>();
            decimal laborHours = 0;
            decimal refinishHours = 0;
            decimal price = 0;
            string? partNumber = null;
            int qty = 0;

            for (int i = startIndex; i < words.Count; i++)
            {
                var word = words[i];

                // Check if this looks like a part number (letters + numbers, typically 6+ chars)
                if (word.Length >= 6 && word.Any(char.IsDigit) && word.Any(char.IsLetter) && !word.Contains("."))
                {
                    partNumber = word;
                    continue;
                }

                // Check if this is a quantity (single digit)
                if ((word == "1" || word == "2" || word == "3" || word == "4" || word == "5" || word == "6" ||
                     word == "7" || word == "8" || word == "9" || word == "10" || word == "11" || word == "12" || word == "13")
                    && qty == 0)
                {
                    qty = int.Parse(word);
                    continue;
                }

                // Check if this is a price (has comma or decimal with value > 10)
                if (decimal.TryParse(word.Replace(",", ""), out var possiblePrice) && possiblePrice > 10 && word.Contains("."))
                {
                    price = possiblePrice;
                    continue;
                }
                // Also handle prices with commas like "2,498.85"
                if (word.Contains(",") && decimal.TryParse(word.Replace(",", ""), out var commaPrice))
                {
                    price = commaPrice;
                    continue;
                }

                // Check if this looks like labor/paint hours (decimal number < 50)
                if (decimal.TryParse(word, out var hours) && hours > 0 && hours < 50)
                {
                    // First number is usually labor, second is paint
                    if (laborHours == 0)
                        laborHours = hours;
                    else if (refinishHours == 0)
                        refinishHours = hours;
                    continue;
                }

                // Check for "M" suffix (mechanical labor indicator)
                if (word == "M" || word == "m")
                {
                    continue;
                }

                // Check for "Incl." (included)
                if (word.Equals("Incl.", StringComparison.OrdinalIgnoreCase) || word.Equals("Incl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Otherwise it's part of the description
                descriptionParts.Add(word);
            }

            var description = string.Join(" ", descriptionParts);
            if (string.IsNullOrWhiteSpace(description)) return null;

            // Determine if this is a MAIN PART line or an ADDITIONAL OPERATION
            // Main parts have: Repl/Blnd + price OR part number
            // Additional operations: DE-NIB, Clear Coat, Backtape, Wet/Dry Sand, etc.
            // Also, lines with # marker are always additional operations
            bool isAdditionalOperation = hasHashMarker || IsAdditionalOperation(description, operationType, price, partNumber);

            // Extract the core part name (e.g., "LT Rear Door" from "LT Rear Door DE-NIB")
            var partName = ExtractCorePartName(description);

            return new ParsedEstimateLine
            {
                RawLine = line,
                Description = description,
                PartName = partName,
                OperationType = operationType ?? "",
                LaborHours = laborHours,
                RepairHours = laborHours,
                RefinishHours = refinishHours,
                Price = price,
                Category = currentSection ?? "",
                IsManualLine = isAdditionalOperation // "Manual line" = additional operation (includes # marked lines)
            };
        }

        /// <summary>
        /// Determine if this is an additional operation (not a main part replacement)
        /// </summary>
        private bool IsAdditionalOperation(string description, string? operationType, decimal price, string? partNumber)
        {
            // If it has a significant price or part number, it's likely a main part
            if (price > 50 || !string.IsNullOrEmpty(partNumber))
                return false;

            // Common additional operation keywords
            var additionalKeywords = new[] {
                "DE-NIB", "DENIB", "Wet/Dry", "Wet Dry", "Rub-Out", "Rub Out", "Buff",
                "Backtape", "Back tape", "Clear Coat", "Clearcoat",
                "Adhesion Promoter", "Flex Additive", "Flex Agent",
                "Stage and Secure", "Mask and Tape", "Mask for",
                "Cover Car", "Cover Interior", "Cover Trunk", "Cover and Protect",
                "Disconnect and Reconnect", "Battery", "Pre-Scan", "Post-Scan", "In-Process Scan",
                "Trial Fit", "Replicate seam sealer", "Electronic Reset",
                "Color Tint", "Spray Out Cards", "Touch Up", "Monitor Flash",
                "Clean for Delivery", "Pre wash", "Collision wrap",
                "Central Paint", "Refinish Material", "Hazardous Waste", "Parts Disposal",
                "Sound Proofing", "Hinge", "Add for"
            };

            var descLower = description.ToLowerInvariant();
            foreach (var keyword in additionalKeywords)
            {
                if (descLower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }

            // Refinish operations without prices are usually additional
            if ((operationType == "Refn" || operationType == "Rpr") && price < 10)
                return true;

            return false;
        }

        /// <summary>
        /// Extract the core part name from a description
        /// e.g., "LT Rear Door DE-NIB" → "LT Rear Door"
        /// </summary>
        private string ExtractCorePartName(string description)
        {
            // Common part names
            var partPatterns = new[] {
                "Front Bumper", "Rear Bumper", "Bumper Cover",
                "LT Front Door", "RT Front Door", "LT Rear Door", "RT Rear Door",
                "Front Door", "Rear Door",
                "LT Fender", "RT Fender", "Fender",
                "Hood", "Trunk Lid", "Liftgate", "Tailgate",
                "LT Quarter Panel", "RT Quarter Panel", "Quarter Panel", "Quarter panel",
                "LT Roof Rail", "RT Roof Rail", "Roof Rail", "Roof",
                "LT Wheel Flare", "RT Wheel Flare", "Wheel Flare",
                "Windshield", "Rear Window", "Quarter Glass",
                "LT Mirror", "RT Mirror", "Mirror",
                "LT Headlamp", "RT Headlamp", "Headlamp",
                "LT Tail Lamp", "RT Tail Lamp", "Tail Lamp",
                "Grille", "Radiator Support", "Apron",
                "Rocker Panel", "A Pillar", "B Pillar", "C Pillar",
                "Fuel Door"
            };

            foreach (var pattern in partPatterns)
            {
                if (description.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return pattern;
                }
            }

            // If no known part found, return the first 2-3 words (likely the part name)
            var words = description.Split(' ');
            if (words.Length >= 3 && (words[0] == "LT" || words[0] == "RT"))
            {
                return string.Join(" ", words.Take(3));
            }
            if (words.Length >= 2)
            {
                return string.Join(" ", words.Take(2));
            }

            return description;
        }

        /// <summary>
        /// Check if line is a header/footer that should be skipped
        /// </summary>
        private bool IsHeaderOrFooterLine(string line)
        {
            return EstimatePdfParser.IsHeaderOrFooter(line);
        }

        /// <summary>
        /// Check if line is a section header (ELECTRICAL, FRONT DOOR, etc.)
        /// </summary>
        private bool IsSectionHeader(string line)
        {
            var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 5) return false;

            // Skip line number if present
            int startIdx = 0;
            if (int.TryParse(words[0], out _))
                startIdx = 1;

            if (startIdx >= words.Length) return false;

            // Check if remaining words are all caps and alphabetic
            for (int i = startIdx; i < words.Length; i++)
            {
                var word = words[i];
                // Skip "&" in section names like "PILLARS, ROCKER & FLOOR"
                if (word == "&" || word == ",") continue;

                // Must be all caps and mostly letters
                if (word != word.ToUpper()) return false;
                if (word.Any(char.IsDigit)) return false;
            }

            // Known section headers
            var knownSections = new[] { "ELECTRICAL", "WINDSHIELD", "ROOF", "FRONT", "REAR", "DOOR", "QUARTER",
                                        "BUMPER", "PILLARS", "ROCKER", "FLOOR", "LAMPS", "VEHICLE", "MISCELLANEOUS",
                                        "DIAGNOSTICS", "OPERATIONS", "PANEL", "HOOD", "FENDER", "GRILLE" };

            var sectionText = string.Join(" ", words.Skip(startIdx)).Replace(",", "").ToUpper();
            return knownSections.Any(s => sectionText.Contains(s));
        }

        /// <summary>
        /// Extract section name from header line
        /// </summary>
        private string ExtractSectionName(string line)
        {
            var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int startIdx = 0;
            if (words.Length > 0 && int.TryParse(words[0], out _))
                startIdx = 1;

            return string.Join(" ", words.Skip(startIdx)).Trim(',', ' ');
        }

        /// <summary>
        /// Extract part name from description
        /// </summary>
        private string ExtractPartNameFromDescription(string description, string? section)
        {
            // Common suffixes that indicate operations, not part of the part name
            var operationSuffixes = new[] {
                "Adhesion Promoter", "Flex Additive", "Flex Agent", "DE-NIB", "Wet/Dry Sand",
                "Rub-Out & Buff", "Backtape Jambs", "Stage and Secure", "Mask and Tape",
                "Clear Coat", "Trial Fit", "Replicate seam sealer", "Cover and Protect",
                "Pre-Scan", "Post-Scan", "In-Process Scan", "Cover Car", "for Refinish",
                "for Overspray", "for Edging", "for Buffing", "for Primer"
            };

            var partName = description;
            foreach (var suffix in operationSuffixes)
            {
                if (partName.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    partName = partName.Replace(suffix, "", StringComparison.OrdinalIgnoreCase).Trim();
                }
            }

            // If nothing meaningful left, use section as context
            if (string.IsNullOrWhiteSpace(partName) && !string.IsNullOrEmpty(section))
            {
                return section;
            }

            return partName.Trim();
        }

        /// <summary>
        /// Detect if a line is a manual entry (marked with # in CCC)
        /// </summary>
        private bool IsManualLine(string line)
        {
            // CCC manual lines typically have:
            // 1. "#" character in the line (often in 3rd column as category marker)
            // 2. Tab-separated format where 3rd field is "#"
            // 3. Or the line description starts with "#"

            // Check for tab-separated format
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                // Check if 3rd column (index 2) contains "#" or is exactly "#"
                var thirdCol = parts[2].Trim();
                if (thirdCol == "#" || thirdCol.Contains("#"))
                    return true;
            }

            // Check for space-separated format with # as a field
            var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(5, words.Length); i++)
            {
                if (words[i] == "#")
                    return true;
            }

            // Check if line has common manual line indicators
            var lower = line.ToLowerInvariant();
            var manualIndicators = new[]
            {
                "adhesion promoter", "flex additive", "flex agent", "plastic prep",
                "seam sealer", "corrosion protection", "cavity wax", "undercoating",
                "sound deadener", "primer surfacer", "wet sand", "color sand",
                "single stage", "add'l", "additional"
            };

            // Only consider manual indicators if they appear to be supplementary lines
            // (no strong part keywords at the beginning)
            if (!HasPartKeywordAtStart(line))
            {
                foreach (var indicator in manualIndicators)
                {
                    if (lower.Contains(indicator))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if line starts with a known part keyword
        /// </summary>
        private bool HasPartKeywordAtStart(string line)
        {
            var lower = line.ToLowerInvariant().TrimStart();
            var partStarters = new[]
            {
                "front bumper", "rear bumper", "bumper cover", "fender", "hood", "door",
                "quarter panel", "trunk", "liftgate", "grille", "headlight", "taillight",
                "mirror", "windshield", "roof", "pillar", "rocker"
            };

            return partStarters.Any(p => lower.StartsWith(p));
        }

        /// <summary>
        /// Parse a single estimate line
        /// </summary>
        private ParsedEstimateLine ParseEstimateLine(string line)
        {
            var extracted = ExtractLineComponents(line);
            return new ParsedEstimateLine
            {
                RawLine = line,
                Description = line,
                PartName = extracted.PartName,
                OperationType = extracted.OperationType ?? "",
                Position = extracted.Position ?? "",
                Side = extracted.Side ?? "",
                LaborHours = extracted.LaborHours,
                RepairHours = extracted.LaborHours,
                RefinishHours = extracted.RefinishHours,
                Price = extracted.Price,
                Category = DetectCategory(line)
            };
        }

        /// <summary>
        /// Learn manual line patterns from parsed estimate lines.
        /// Groups manual lines with their parent parts and stores the relationships.
        /// REQUIRES Shop or Admin license tier.
        /// </summary>
        /// <returns>True if learning was successful, false if blocked by license</returns>
        public bool LearnManualLinePatterns(List<ParsedEstimateLine> parsedLines)
        {
            // Check license tier
            if (!CanTrain)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] BLOCKED - Client license cannot train manual patterns. Tier: {_currentTier}");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[Learning] ========== LEARNING FROM {parsedLines.Count} LINES ==========");

            // Count totals for logging
            int parentCount = 0;
            int manualCount = 0;
            int patternsCreated = 0;

            // Group lines: find parent parts and their associated manual lines
            ParsedEstimateLine? currentParent = null;
            var manualLinesForParent = new List<ParsedEstimateLine>();

            foreach (var line in parsedLines)
            {
                if (line.IsManualLine)
                {
                    manualCount++;
                    // This is a manual line - add to current parent's list
                    if (currentParent != null)
                    {
                        manualLinesForParent.Add(line);
                        System.Diagnostics.Debug.WriteLine($"[Learning]   # Manual: {line.Description} | Labor: {line.LaborHours} | Refn: {line.RefinishHours} | $: {line.Price}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Learning]   ! ORPHAN Manual (no parent): {line.Description}");
                    }
                }
                else if (!string.IsNullOrEmpty(line.PartName))
                {
                    parentCount++;
                    // This is a new parent part
                    // First, save the previous parent's manual lines
                    if (currentParent != null && manualLinesForParent.Count > 0)
                    {
                        SaveManualLinePattern(currentParent, manualLinesForParent);
                        patternsCreated++;
                    }

                    // Start tracking new parent
                    currentParent = line;
                    manualLinesForParent = new List<ParsedEstimateLine>();
                    System.Diagnostics.Debug.WriteLine($"[Learning] PARENT: {line.PartName} | {line.OperationType} | $: {line.Price}");
                }
            }

            // Don't forget the last parent's manual lines
            if (currentParent != null && manualLinesForParent.Count > 0)
            {
                SaveManualLinePattern(currentParent, manualLinesForParent);
                patternsCreated++;
            }

            System.Diagnostics.Debug.WriteLine($"[Learning] ========== SUMMARY: {parentCount} parents, {manualCount} manual lines, {patternsCreated} patterns ==========");

            SaveDatabase();
            return true;
        }

        /// <summary>
        /// Save a manual line pattern linking a parent part to its manual operations
        /// </summary>
        private void SaveManualLinePattern(ParsedEstimateLine parentPart, List<ParsedEstimateLine> manualLines)
        {
            var patternKey = GenerateManualLinePatternKey(parentPart.PartName, parentPart.OperationType);

            if (_database.ManualLinePatterns.TryGetValue(patternKey, out var existingPattern))
            {
                // Update existing pattern
                existingPattern.ExampleCount++;
                existingPattern.LastUpdated = DateTime.Now;

                foreach (var manualLine in manualLines)
                {
                    var manualType = ExtractManualLineType(manualLine.Description, parentPart.PartName);

                    var existingEntry = existingPattern.ManualLines
                        .FirstOrDefault(m => m.ManualLineType.Equals(manualType, StringComparison.OrdinalIgnoreCase));

                    if (existingEntry != null)
                    {
                        existingEntry.TimesUsed++;
                        existingEntry.LastSeen = DateTime.Now;

                        // Update running average labor units
                        var oldCount = existingEntry.TimesUsed - 1;
                        existingEntry.LaborUnits = ((existingEntry.LaborUnits * oldCount) + manualLine.LaborHours) / existingEntry.TimesUsed;
                        existingEntry.RefinishUnits = ((existingEntry.RefinishUnits * oldCount) + manualLine.RefinishHours) / existingEntry.TimesUsed;

                        // Track min/max labor
                        if (manualLine.LaborHours > 0)
                        {
                            existingEntry.MinLaborUnits = existingEntry.MinLaborUnits == 0 ? manualLine.LaborHours : Math.Min(existingEntry.MinLaborUnits, manualLine.LaborHours);
                            existingEntry.MaxLaborUnits = Math.Max(existingEntry.MaxLaborUnits, manualLine.LaborHours);
                        }
                        if (manualLine.RefinishHours > 0)
                        {
                            existingEntry.MinRefinishUnits = existingEntry.MinRefinishUnits == 0 ? manualLine.RefinishHours : Math.Min(existingEntry.MinRefinishUnits, manualLine.RefinishHours);
                            existingEntry.MaxRefinishUnits = Math.Max(existingEntry.MaxRefinishUnits, manualLine.RefinishHours);
                        }

                        // Update price tracking
                        if (manualLine.Price > 0)
                        {
                            existingEntry.MinPrice = existingEntry.MinPrice == 0 ? manualLine.Price : Math.Min(existingEntry.MinPrice, manualLine.Price);
                            existingEntry.MaxPrice = Math.Max(existingEntry.MaxPrice, manualLine.Price);
                            existingEntry.AvgPrice = ((existingEntry.AvgPrice * oldCount) + manualLine.Price) / existingEntry.TimesUsed;
                            existingEntry.Price = manualLine.Price;
                        }

                        // Track labor type
                        if (!string.IsNullOrEmpty(manualLine.LaborType) && string.IsNullOrEmpty(existingEntry.LaborType))
                            existingEntry.LaborType = manualLine.LaborType;

                        // Track wording variations
                        if (!string.IsNullOrEmpty(manualLine.Description))
                        {
                            var normalizedDesc = manualLine.Description.Trim();
                            if (!existingEntry.WordingVariations.Contains(normalizedDesc, StringComparer.OrdinalIgnoreCase))
                            {
                                existingEntry.WordingVariations.Add(normalizedDesc);
                                if (existingEntry.WordingVariations.Count > 10) // Keep max 10 variations
                                    existingEntry.WordingVariations.RemoveAt(0);
                            }
                        }

                        // Track source estimates
                        if (!string.IsNullOrEmpty(manualLine.SourceFile) && !existingEntry.SourceEstimates.Contains(manualLine.SourceFile))
                        {
                            existingEntry.SourceEstimates.Add(manualLine.SourceFile);
                            if (existingEntry.SourceEstimates.Count > 20) // Keep max 20 sources
                                existingEntry.SourceEstimates.RemoveAt(0);
                        }
                    }
                    else
                    {
                        existingPattern.ManualLines.Add(new ManualLineEntry
                        {
                            Description = manualLine.Description,
                            ParentPartName = parentPart.PartName,
                            ManualLineType = manualType,
                            LaborUnits = manualLine.LaborHours,
                            RefinishUnits = manualLine.RefinishHours,
                            MinLaborUnits = manualLine.LaborHours,
                            MaxLaborUnits = manualLine.LaborHours,
                            MinRefinishUnits = manualLine.RefinishHours,
                            MaxRefinishUnits = manualLine.RefinishHours,
                            TimesUsed = 1,
                            Price = manualLine.Price,
                            MinPrice = manualLine.Price,
                            MaxPrice = manualLine.Price,
                            AvgPrice = manualLine.Price,
                            LaborType = manualLine.LaborType,
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            WordingVariations = string.IsNullOrEmpty(manualLine.Description) ? new() : new List<string> { manualLine.Description },
                            SourceEstimates = string.IsNullOrEmpty(manualLine.SourceFile) ? new() : new List<string> { manualLine.SourceFile }
                        });
                    }
                }

                // Recalculate confidence
                existingPattern.Confidence = CalculateManualLineConfidence(existingPattern);

                System.Diagnostics.Debug.WriteLine($"[Learning] Updated manual line pattern: {patternKey} ({existingPattern.ManualLines.Count} manual lines)");
            }
            else
            {
                // Create new pattern
                var newPattern = new ManualLinePattern
                {
                    ParentPartName = parentPart.PartName,
                    ParentOperationType = parentPart.OperationType,
                    ManualLines = manualLines.Select(ml => new ManualLineEntry
                    {
                        Description = ml.Description,
                        ParentPartName = parentPart.PartName,
                        ManualLineType = ExtractManualLineType(ml.Description, parentPart.PartName),
                        LaborUnits = ml.LaborHours,
                        RefinishUnits = ml.RefinishHours,
                        MinLaborUnits = ml.LaborHours,
                        MaxLaborUnits = ml.LaborHours,
                        MinRefinishUnits = ml.RefinishHours,
                        MaxRefinishUnits = ml.RefinishHours,
                        TimesUsed = 1,
                        Price = ml.Price,
                        MinPrice = ml.Price,
                        MaxPrice = ml.Price,
                        AvgPrice = ml.Price,
                        LaborType = ml.LaborType,
                        FirstSeen = DateTime.Now,
                        LastSeen = DateTime.Now,
                        WordingVariations = string.IsNullOrEmpty(ml.Description) ? new() : new List<string> { ml.Description },
                        SourceEstimates = string.IsNullOrEmpty(ml.SourceFile) ? new() : new List<string> { ml.SourceFile }
                    }).ToList(),
                    ExampleCount = 1,
                    DateCreated = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    Confidence = 0.5
                };

                _database.ManualLinePatterns[patternKey] = newPattern;

                System.Diagnostics.Debug.WriteLine($"[Learning] Created new manual line pattern: {patternKey} ({newPattern.ManualLines.Count} manual lines)");
            }
        }

        /// <summary>
        /// Extract the manual line type from description by removing parent part name
        /// e.g., "Bumper Cover Adhesion Promoter" → "Adhesion Promoter"
        /// </summary>
        private string ExtractManualLineType(string description, string parentPartName)
        {
            if (string.IsNullOrEmpty(parentPartName))
                return description.Trim();

            // Remove parent part name from description
            var result = description;
            foreach (var variation in GetPartNameVariations(parentPartName))
            {
                result = Regex.Replace(result, Regex.Escape(variation), "", RegexOptions.IgnoreCase);
            }

            return result.Trim().TrimStart('-', ' ', ',');
        }

        /// <summary>
        /// Get variations of a part name for matching
        /// </summary>
        private IEnumerable<string> GetPartNameVariations(string partName)
        {
            yield return partName;
            yield return partName.Replace(" ", "");

            // Common abbreviations
            if (partName.Contains("front", StringComparison.OrdinalIgnoreCase))
                yield return partName.Replace("front", "frt", StringComparison.OrdinalIgnoreCase);
            if (partName.Contains("rear", StringComparison.OrdinalIgnoreCase))
                yield return partName.Replace("rear", "rr", StringComparison.OrdinalIgnoreCase);
            if (partName.Contains("bumper cover", StringComparison.OrdinalIgnoreCase))
                yield return "bpr cvr";
        }

        /// <summary>
        /// Generate a key for manual line patterns
        /// </summary>
        private string GenerateManualLinePatternKey(string partName, string operationType)
        {
            var normalizedPart = partName.ToLowerInvariant().Replace(" ", "_");
            var normalizedOp = (operationType ?? "any").ToLowerInvariant();
            return $"{normalizedPart}|{normalizedOp}";
        }

        /// <summary>
        /// Calculate confidence for a manual line pattern
        /// </summary>
        private double CalculateManualLineConfidence(ManualLinePattern pattern)
        {
            // Base confidence on example count and consistency of manual lines
            var baseConfidence = 1.0 - (1.0 / (1.0 + pattern.ExampleCount * 0.5));

            // Boost confidence if manual lines are consistently used
            var avgTimesUsed = pattern.ManualLines.Average(m => m.TimesUsed);
            var consistencyBoost = Math.Min(0.2, avgTimesUsed * 0.05);

            return Math.Min(1.0, baseConfidence + consistencyBoost);
        }

        /// <summary>
        /// Query: What manual lines should I expect for a given part?
        /// Only returns a pattern if there's a genuine match for the queried part.
        /// </summary>
        public ManualLinePattern? GetManualLinesForPart(string partName, string? operationType = null)
        {
            if (string.IsNullOrWhiteSpace(partName))
                return null;

            // Try exact match first
            var key = GenerateManualLinePatternKey(partName, operationType ?? "any");
            if (_database.ManualLinePatterns.TryGetValue(key, out var pattern))
                return pattern;

            // Try without operation type
            if (operationType != null)
            {
                key = GenerateManualLinePatternKey(partName, "any");
                if (_database.ManualLinePatterns.TryGetValue(key, out pattern))
                    return pattern;
            }

            // Try partial match - but only if part names are actually similar
            var normalizedPart = partName.ToLowerInvariant().Replace(" ", "_");
            var partWords = partName.ToLowerInvariant().Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3)
                .ToHashSet();

            if (partWords.Count == 0)
                return null;

            ManualLinePattern? bestMatch = null;
            int bestScore = 0;

            foreach (var kvp in _database.ManualLinePatterns)
            {
                var storedPartName = kvp.Value.ParentPartName?.ToLowerInvariant() ?? "";
                var storedWords = storedPartName.Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 3)
                    .ToHashSet();

                // Count overlapping significant words
                var overlap = partWords.Intersect(storedWords, StringComparer.OrdinalIgnoreCase).Count();

                // Require at least 1 significant word match, and at least 50% overlap
                if (overlap >= 1 && (overlap >= partWords.Count * 0.5 || overlap >= storedWords.Count * 0.5))
                {
                    if (overlap > bestScore)
                    {
                        bestScore = overlap;
                        bestMatch = kvp.Value;
                    }
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Get all manual line patterns
        /// </summary>
        public IReadOnlyList<ManualLinePattern> GetAllManualLinePatterns()
        {
            return _database.ManualLinePatterns.Values.ToList();
        }

        /// <summary>
        /// Get count of manual line patterns
        /// </summary>
        public int ManualLinePatternCount => _database.ManualLinePatterns.Count;

        /// <summary>
        /// Search for manual operations by keyword (e.g., "scan", "weld", "DE-NIB", "cavity wax")
        /// Returns all matching operations across all patterns with their parent context
        /// </summary>
        public List<OperationSearchResult> SearchOperationsByKeyword(string keyword)
        {
            var results = new List<OperationSearchResult>();
            if (string.IsNullOrWhiteSpace(keyword)) return results;

            var lower = keyword.ToLowerInvariant();

            foreach (var pattern in _database.ManualLinePatterns.Values)
            {
                foreach (var manualLine in pattern.ManualLines)
                {
                    var descLower = manualLine.Description?.ToLowerInvariant() ?? "";
                    var typeLower = manualLine.ManualLineType?.ToLowerInvariant() ?? "";

                    if (descLower.Contains(lower) || typeLower.Contains(lower))
                    {
                        results.Add(new OperationSearchResult
                        {
                            OperationName = manualLine.ManualLineType.Length > 0 ? manualLine.ManualLineType : manualLine.Description,
                            Description = manualLine.Description,
                            ParentPartName = pattern.ParentPartName,
                            ParentOperationType = pattern.ParentOperationType,
                            LaborHours = manualLine.LaborUnits,
                            RefinishHours = manualLine.RefinishUnits,
                            TimesUsed = manualLine.TimesUsed,
                            PatternExampleCount = pattern.ExampleCount,
                            Confidence = pattern.Confidence,
                            // Include price data
                            Price = manualLine.Price,
                            MinPrice = manualLine.MinPrice,
                            MaxPrice = manualLine.MaxPrice,
                            AvgPrice = manualLine.AvgPrice,
                            // Enhanced data
                            LaborType = manualLine.LaborType,
                            MinLaborHours = manualLine.MinLaborUnits,
                            MaxLaborHours = manualLine.MaxLaborUnits,
                            MinRefinishHours = manualLine.MinRefinishUnits,
                            MaxRefinishHours = manualLine.MaxRefinishUnits,
                            WordingVariations = manualLine.WordingVariations ?? new(),
                            FirstSeen = manualLine.FirstSeen,
                            LastSeen = manualLine.LastSeen
                        });
                    }
                }
            }

            return results.OrderByDescending(r => r.TimesUsed).ToList();
        }

        /// <summary>
        /// Search learned patterns by part name keyword.
        /// Returns matching patterns from the main Patterns dictionary.
        /// </summary>
        public List<LearnedPattern> SearchPatterns(string keyword, int maxResults = 20)
        {
            var results = new List<LearnedPattern>();
            if (string.IsNullOrWhiteSpace(keyword)) return results;

            var lower = keyword.ToLowerInvariant();

            foreach (var pattern in _database.Patterns.Values)
            {
                var partNameLower = pattern.PartName?.ToLowerInvariant() ?? "";
                var keyLower = pattern.PatternKey?.ToLowerInvariant() ?? "";

                if (partNameLower.Contains(lower) || keyLower.Contains(lower))
                {
                    results.Add(pattern);
                }
            }

            return results
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.ExampleCount)
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// Get aggregated stats for an operation type across all patterns
        /// </summary>
        public OperationAggregateStats GetOperationStats(string operationKeyword)
        {
            var matches = SearchOperationsByKeyword(operationKeyword);
            if (matches.Count == 0) return new OperationAggregateStats { OperationName = operationKeyword };

            var grouped = matches.GroupBy(m => m.OperationName, StringComparer.OrdinalIgnoreCase);

            // Calculate price stats
            var priceMatches = matches.Where(m => m.AvgPrice > 0).ToList();

            return new OperationAggregateStats
            {
                OperationName = operationKeyword,
                TotalOccurrences = matches.Sum(m => m.TimesUsed),
                UniqueParentParts = matches.Select(m => m.ParentPartName).Distinct().Count(),
                AvgLaborHours = matches.Where(m => m.LaborHours > 0).Select(m => m.LaborHours).DefaultIfEmpty(0).Average(),
                AvgRefinishHours = matches.Where(m => m.RefinishHours > 0).Select(m => m.RefinishHours).DefaultIfEmpty(0).Average(),
                MinLaborHours = matches.Where(m => m.LaborHours > 0).Select(m => m.LaborHours).DefaultIfEmpty(0).Min(),
                MaxLaborHours = matches.Where(m => m.LaborHours > 0).Select(m => m.LaborHours).DefaultIfEmpty(0).Max(),
                Occurrences = matches,
                // Price stats
                AvgPrice = priceMatches.Select(m => m.AvgPrice).DefaultIfEmpty(0).Average(),
                MinPrice = priceMatches.Select(m => m.MinPrice).DefaultIfEmpty(0).Min(),
                MaxPrice = priceMatches.Select(m => m.MaxPrice).DefaultIfEmpty(0).Max(),
                TotalPriceValue = priceMatches.Sum(m => m.AvgPrice * m.TimesUsed)
            };
        }

        #endregion

        #region Vehicle-Based Suggestions

        /// <summary>
        /// Store a vehicle estimate summary for YMM-based suggestions
        /// </summary>
        public void StoreVehicleEstimateSummary(VehicleEstimateSummary summary)
        {
            _database.VehicleEstimates ??= new Dictionary<string, List<VehicleEstimateSummary>>();

            var normalizedKey = NormalizeVehicleInfo(summary.VehicleInfo);
            summary.NormalizedVehicle = normalizedKey;

            if (!_database.VehicleEstimates.TryGetValue(normalizedKey, out var list))
            {
                list = new List<VehicleEstimateSummary>();
                _database.VehicleEstimates[normalizedKey] = list;
            }

            // Don't duplicate same estimate
            if (!list.Any(e => e.EstimateId == summary.EstimateId))
            {
                list.Add(summary);
                System.Diagnostics.Debug.WriteLine($"[Learning] Stored vehicle estimate: {summary.VehicleInfo} ({summary.Operations.Count} operations)");
            }
        }

        /// <summary>
        /// Find similar estimates by Year/Make/Model and operation type
        /// </summary>
        public SimilarEstimatesResult FindSimilarEstimates(string vehicleInfo, string partName, string operationType)
        {
            var result = new SimilarEstimatesResult
            {
                VehicleInfo = vehicleInfo,
                PartName = partName,
                OperationType = operationType
            };

            if (_database.VehicleEstimates == null || _database.VehicleEstimates.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[Learning] No vehicle estimates stored yet");
                return result;
            }

            var normalizedVehicle = NormalizeVehicleInfo(vehicleInfo);
            var normalizedPart = NormalizePartName(partName);
            var normalizedOp = NormalizeOperationType(operationType);

            // Strategy 1: Exact YMM match
            if (_database.VehicleEstimates.TryGetValue(normalizedVehicle, out var exactMatches))
            {
                result.MatchType = "Exact YMM";
                FindOperationMatches(exactMatches, normalizedPart, normalizedOp, result);
            }

            // Strategy 2: Same Make/Model (any year) if exact didn't find enough
            if (result.MatchingEstimates.Count < 2)
            {
                var makeModel = GetMakeModel(normalizedVehicle);
                if (!string.IsNullOrEmpty(makeModel))
                {
                    var similarVehicles = _database.VehicleEstimates
                        .Where(kvp => kvp.Key.Contains(makeModel) && kvp.Key != normalizedVehicle)
                        .SelectMany(kvp => kvp.Value)
                        .ToList();

                    if (similarVehicles.Any())
                    {
                        result.MatchType = "Similar Vehicle";
                        FindOperationMatches(similarVehicles, normalizedPart, normalizedOp, result);
                    }
                }
            }

            // Strategy 3: Any vehicle with same operation (fallback)
            if (result.MatchingEstimates.Count == 0)
            {
                var allEstimates = _database.VehicleEstimates.Values.SelectMany(v => v).ToList();
                result.MatchType = "Any Vehicle";
                FindOperationMatches(allEstimates, normalizedPart, normalizedOp, result);
            }

            // Aggregate the manual lines across all matching estimates
            AggregateManualLines(result);

            return result;
        }

        private void FindOperationMatches(List<VehicleEstimateSummary> estimates, string normalizedPart, string normalizedOp, SimilarEstimatesResult result)
        {
            foreach (var estimate in estimates)
            {
                foreach (var op in estimate.Operations)
                {
                    var opPartNorm = NormalizePartName(op.PartName);
                    var opTypeNorm = NormalizeOperationType(op.OperationType);

                    // Check if this operation matches
                    if (opPartNorm.Contains(normalizedPart) || normalizedPart.Contains(opPartNorm))
                    {
                        if (string.IsNullOrEmpty(normalizedOp) || opTypeNorm.Contains(normalizedOp) || normalizedOp.Contains(opTypeNorm))
                        {
                            result.MatchingEstimates.Add(new MatchedEstimate
                            {
                                EstimateId = estimate.EstimateId,
                                VehicleInfo = estimate.VehicleInfo,
                                DateImported = estimate.DateImported,
                                Operation = op
                            });
                        }
                    }
                }
            }
        }

        private void AggregateManualLines(SimilarEstimatesResult result)
        {
            var totalEstimates = result.MatchingEstimates.Count;
            if (totalEstimates == 0) return;

            // Group all manual lines by normalized description
            var allManualLines = result.MatchingEstimates
                .SelectMany(e => e.Operation.ManualLines)
                .ToList();

            var grouped = allManualLines
                .GroupBy(m => NormalizeManualLine(m.Description))
                .Select(g => new AggregatedManualLine
                {
                    Description = g.First().Description, // Original description
                    NormalizedDescription = g.Key,
                    Count = g.Count(),
                    TotalEstimates = totalEstimates,
                    Frequency = (double)g.Count() / totalEstimates,
                    AvgLaborHours = g.Average(m => m.LaborHours),
                    AvgRefinishHours = g.Average(m => m.RefinishHours),
                    AvgPrice = g.Average(m => m.Price),
                    MinPrice = g.Min(m => m.Price),
                    MaxPrice = g.Max(m => m.Price)
                })
                .OrderByDescending(a => a.Frequency)
                .ThenByDescending(a => a.Count)
                .ToList();

            result.AggregatedManualLines = grouped;
        }

        private string NormalizeVehicleInfo(string vehicleInfo)
        {
            if (string.IsNullOrEmpty(vehicleInfo)) return "";
            return vehicleInfo.ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "")
                .Replace(",", "");
        }

        private string GetMakeModel(string normalizedVehicle)
        {
            // Extract make and model from "2022_toyota_camry" -> "toyota_camry"
            var parts = normalizedVehicle.Split('_');
            if (parts.Length >= 3)
            {
                // Skip year (first part if numeric)
                if (int.TryParse(parts[0], out _))
                    return string.Join("_", parts.Skip(1));
            }
            return normalizedVehicle;
        }

        private string NormalizePartName(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return "";
            return partName.ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace("cover", "")
                .Replace("panel", "")
                .Trim('_');
        }

        private string NormalizeOperationType(string opType)
        {
            if (string.IsNullOrEmpty(opType)) return "";
            var lower = opType.ToLowerInvariant();
            if (lower.Contains("repl")) return "replace";
            if (lower.Contains("rpr") || lower.Contains("repair")) return "repair";
            if (lower.Contains("r&i") || lower.Contains("r/i")) return "ri";
            if (lower.Contains("refn") || lower.Contains("refinish")) return "refinish";
            return lower.Replace(" ", "_");
        }

        private string NormalizeManualLine(string description)
        {
            if (string.IsNullOrEmpty(description)) return "";
            return description.ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "")
                .Trim('_');
        }

        /// <summary>
        /// Get count of stored vehicle estimates
        /// </summary>
        public int VehicleEstimateCount => _database.VehicleEstimates?.Values.Sum(v => v.Count) ?? 0;

        #endregion

        #region Statistics & Info

        public int PatternCount => _database.Patterns.Count;
        public int ExampleCount => _database.TrainingExamples.Count;
        public int TrainedEstimateCount => _database.TrainedEstimates.Count;

        // New stats properties
        public int EstimatesImported => _database.EstimatesImported;
        public decimal TotalEstimateValue => _database.TotalEstimateValue;
        public decimal AverageEstimateValue => _database.AverageEstimateValue;

        /// <summary>
        /// Record that an estimate was imported for stats tracking.
        /// REQUIRES Shop or Admin license tier.
        /// </summary>
        /// <returns>True if recorded, false if blocked by license</returns>
        public bool RecordEstimateImport(decimal estimateTotal)
        {
            if (!CanTrain)
            {
                System.Diagnostics.Debug.WriteLine($"[Learning] BLOCKED - Client license cannot record imports. Tier: {_currentTier}");
                return false;
            }

            _database.EstimatesImported++;
            _database.TotalEstimateValue += estimateTotal;
            _database.LastUpdated = DateTime.Now;
            SaveDatabase();
            System.Diagnostics.Debug.WriteLine($"[Learning] Recorded estimate #{_database.EstimatesImported}, Total: ${estimateTotal:N2}, Avg: ${_database.AverageEstimateValue:N2}");
            return true;
        }

        public IReadOnlyList<LearnedPattern> GetAllPatterns() => _database.Patterns.Values.ToList();

        /// <summary>
        /// Expose the patterns dictionary keyed by pattern key for the intelligence service.
        /// </summary>
        public IReadOnlyDictionary<string, LearnedPattern> GetAllPatternsDict() => _database.Patterns;

        public IReadOnlyList<TrainingExample> GetRecentExamples(int count = 20) =>
            _database.TrainingExamples
                .OrderByDescending(e => e.DateAdded)
                .Take(count)
                .ToList();

        /// <summary>
        /// Get all training examples (for analysis like Retirement Fund Finder)
        /// </summary>
        public IReadOnlyList<TrainingExample> GetTrainingExamples() =>
            _database.TrainingExamples.ToList();

        /// <summary>
        /// Get summary statistics
        /// </summary>
        public LearningStatistics GetStatistics()
        {
            return new LearningStatistics
            {
                TotalPatterns = _database.Patterns.Count,
                TotalExamples = _database.TrainingExamples.Count,
                TotalEstimatesTrained = _database.TrainedEstimates.Count,
                TotalManualLinePatterns = _database.ManualLinePatterns.Count,
                AverageConfidence = _database.Patterns.Values.Any()
                    ? _database.Patterns.Values.Average(p => p.Confidence)
                    : 0,
                TopPatterns = _database.Patterns.Values
                    .OrderByDescending(p => p.ExampleCount)
                    .Take(10)
                    .Select(p => $"{p.PartName} {p.OperationType} ({p.ExampleCount}x)")
                    .ToList(),
                // Extended stats for publishing
                TotalTrainingExamples = _database.TrainingExamples.Count,
                TotalTrainedEstimates = _database.TrainedEstimates.Count,
                EstimatesImported = _database.EstimatesImported,
                TotalEstimateValue = _database.TotalEstimateValue,
                AverageEstimateValue = _database.AverageEstimateValue,
                LastUpdated = _database.LastUpdated,
                BaseKnowledgePath = _baseKnowledgePath,
                UserKnowledgePath = _userKnowledgePath,
                HasBaseKnowledge = File.Exists(_baseKnowledgePath),
                HasUserKnowledge = File.Exists(_userKnowledgePath)
            };
        }

        /// <summary>
        /// Clear all learned data (reset)
        /// </summary>
        public void ClearAllData()
        {
            _database = new LearnedPatternDatabase();
            SaveDatabase();
        }

        #endregion

        #region Query Operations by Part

        /// <summary>
        /// Query: "I'm replacing a bumper with 3.0 refinish units, what operations do I need?"
        /// Returns operations with calculations scaled to the provided labor/refinish units.
        /// </summary>
        public ScaledQueryResult QueryOperationsWithUnits(
            string partName,
            string operationType,
            decimal? laborUnits = null,
            decimal? refinishUnits = null,
            string? vehicleInfo = null)
        {
            var result = new ScaledQueryResult
            {
                QueryPartName = partName,
                QueryOperationType = operationType,
                InputLaborUnits = laborUnits,
                InputRefinishUnits = refinishUnits,
                QueryVehicle = vehicleInfo
            };

            var normalizedPart = NormalizePartName(partName);

            // Find matching patterns
            var matchingPatterns = _database.Patterns.Values
                .Where(p => PartMatches(p.PartName, normalizedPart))
                .Where(p => string.IsNullOrEmpty(operationType) ||
                            p.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Confidence)
                .ToList();

            // Find training examples for this part to calculate typical ratios
            var relevantExamples = _database.TrainingExamples
                .Where(e => PartMatches(e.PartName, normalizedPart))
                .Where(e => string.IsNullOrEmpty(operationType) ||
                            e.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Vehicle-specific filtering
            if (!string.IsNullOrEmpty(vehicleInfo))
            {
                var vehicleSpecific = relevantExamples
                    .Where(e => !string.IsNullOrEmpty(e.VehicleInfo) &&
                                e.VehicleInfo.Contains(vehicleInfo, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (vehicleSpecific.Any())
                {
                    result.HasVehicleSpecificData = true;
                    relevantExamples = vehicleSpecific;
                }
            }

            // Calculate typical ratios from examples
            decimal avgRefinishPerLabor = 1.0m;
            if (relevantExamples.Any(e => e.RepairHours > 0))
            {
                var examplesWithBoth = relevantExamples.Where(e => e.RepairHours > 0 && e.RefinishHours > 0).ToList();
                if (examplesWithBoth.Any())
                {
                    avgRefinishPerLabor = examplesWithBoth.Average(e => e.RefinishHours / e.RepairHours);
                }
            }

            // Get typical values for scaling
            decimal typicalLabor = relevantExamples.Any() ? relevantExamples.Average(e => e.RepairHours) : 1.0m;
            decimal typicalRefinish = relevantExamples.Any() ? relevantExamples.Average(e => e.RefinishHours) : 2.0m;

            // Calculate scale factor based on input
            decimal scaleFactor = 1.0m;
            if (refinishUnits.HasValue && typicalRefinish > 0)
            {
                scaleFactor = refinishUnits.Value / typicalRefinish;
            }
            else if (laborUnits.HasValue && typicalLabor > 0)
            {
                scaleFactor = laborUnits.Value / typicalLabor;
            }

            result.ScaleFactor = scaleFactor;
            result.TypicalLaborUnits = typicalLabor;
            result.TypicalRefinishUnits = typicalRefinish;

            // Build scaled operations from patterns
            foreach (var pattern in matchingPatterns)
            {
                foreach (var op in pattern.Operations)
                {
                    var scaledOp = new ScaledOperation
                    {
                        OperationType = op.OperationType,
                        Description = op.Description,
                        Category = op.Category,
                        OriginalLaborHours = op.LaborHours,
                        OriginalRefinishHours = op.RefinishHours,
                        ScaledLaborHours = Math.Round(op.LaborHours * scaleFactor, 2),
                        ScaledRefinishHours = Math.Round(op.RefinishHours * scaleFactor, 2),
                        Confidence = pattern.Confidence,
                        ExampleCount = pattern.ExampleCount,
                        Source = $"Learned from {pattern.ExampleCount} estimates"
                    };

                    result.Operations.Add(scaledOp);
                }
            }

            // Add manual line suggestions
            var manualPattern = GetManualLinesForPart(partName, operationType);
            if (manualPattern != null)
            {
                foreach (var manualLine in manualPattern.ManualLines)
                {
                    var scaledManual = new ScaledOperation
                    {
                        OperationType = "Add",
                        Description = $"{partName} {manualLine.ManualLineType}",
                        Category = "Manual Lines",
                        OriginalLaborHours = manualLine.LaborUnits,
                        OriginalRefinishHours = manualLine.RefinishUnits,
                        ScaledLaborHours = Math.Round(manualLine.LaborUnits * scaleFactor, 2),
                        ScaledRefinishHours = Math.Round(manualLine.RefinishUnits * scaleFactor, 2),
                        Confidence = manualPattern.Confidence,
                        ExampleCount = manualLine.TimesUsed,
                        Source = $"Manual line - used {manualLine.TimesUsed}x",
                        IsManualLine = true
                    };

                    result.Operations.Add(scaledManual);
                }
            }

            // Remove duplicates
            result.Operations = result.Operations
                .GroupBy(o => $"{o.OperationType}|{o.Description}")
                .Select(g => g.OrderByDescending(o => o.Confidence).First())
                .OrderByDescending(o => o.Confidence)
                .ToList();

            // Build explanation
            result.Explanation = BuildScaledQueryExplanation(result, matchingPatterns.Count, relevantExamples.Count, manualPattern);

            return result;
        }

        /// <summary>
        /// Build explanation for scaled query result
        /// </summary>
        private string BuildScaledQueryExplanation(ScaledQueryResult result, int patternCount, int exampleCount, ManualLinePattern? manualPattern)
        {
            var sb = new System.Text.StringBuilder();

            if (result.Operations.Count == 0)
            {
                return $"No learned data for '{result.QueryPartName}' ({result.QueryOperationType}). Import estimates to train.";
            }

            sb.Append($"Found {result.Operations.Count} operations for '{result.QueryPartName}' ({result.QueryOperationType})");

            if (result.InputRefinishUnits.HasValue)
                sb.Append($" with {result.InputRefinishUnits:F1} refinish units");
            if (result.InputLaborUnits.HasValue)
                sb.Append($" with {result.InputLaborUnits:F1} labor units");

            sb.AppendLine(".");

            if (result.ScaleFactor != 1.0m)
            {
                sb.AppendLine($"Values scaled by {result.ScaleFactor:F2}x based on typical {result.TypicalRefinishUnits:F1} refinish / {result.TypicalLaborUnits:F1} labor.");
            }

            if (manualPattern != null)
            {
                sb.AppendLine($"Includes {manualPattern.ManualLines.Count} manual line(s): {string.Join(", ", manualPattern.ManualLines.Select(m => m.ManualLineType).Take(3))}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Query: "I'm repairing a door, what operations do I need?"
        /// Returns operations learned from historical estimates for a given part and operation type.
        /// </summary>
        public QueryResult QueryOperationsForPart(string partName, string? operationType = null, string? vehicleInfo = null)
        {
            var result = new QueryResult
            {
                QueryPartName = partName,
                QueryOperationType = operationType,
                QueryVehicle = vehicleInfo
            };

            var normalizedPart = NormalizePartName(partName);

            // Find all patterns matching this part
            var matchingPatterns = _database.Patterns.Values
                .Where(p => PartMatches(p.PartName, normalizedPart))
                .ToList();

            // Filter by operation type if specified
            if (!string.IsNullOrEmpty(operationType))
            {
                matchingPatterns = matchingPatterns
                    .Where(p => p.OperationType.Equals(operationType, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Find relevant training examples for vehicle-specific data
            var relevantExamples = _database.TrainingExamples
                .Where(e => PartMatches(e.PartName, normalizedPart))
                .ToList();

            if (!string.IsNullOrEmpty(vehicleInfo))
            {
                var vehicleSpecific = relevantExamples
                    .Where(e => !string.IsNullOrEmpty(e.VehicleInfo) &&
                                e.VehicleInfo.Contains(vehicleInfo, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (vehicleSpecific.Any())
                {
                    result.HasVehicleSpecificData = true;
                    relevantExamples = vehicleSpecific;
                }
            }

            // Build suggested operations
            foreach (var pattern in matchingPatterns.OrderByDescending(p => p.Confidence))
            {
                foreach (var op in pattern.Operations)
                {
                    var suggested = new SuggestedOperation
                    {
                        PartName = pattern.PartName,
                        OperationType = pattern.OperationType,
                        Description = op.Description,
                        Category = op.Category,
                        TypicalLaborHours = op.LaborHours,
                        TypicalRefinishHours = op.RefinishHours,
                        TypicalPrice = op.Price,
                        Confidence = pattern.Confidence,
                        TimesUsed = op.TimesUsed,
                        ExampleCount = pattern.ExampleCount,
                        Source = $"Learned from {pattern.ExampleCount} estimates"
                    };

                    // Calculate averages from training examples if available
                    var opExamples = relevantExamples
                        .Where(e => e.OperationType.Equals(pattern.OperationType, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (opExamples.Any())
                    {
                        suggested.AverageLaborHours = opExamples.Average(e => e.RepairHours);
                        suggested.AverageRefinishHours = opExamples.Average(e => e.RefinishHours);
                        suggested.AveragePrice = opExamples.Average(e => e.Price);
                        suggested.MinLaborHours = opExamples.Min(e => e.RepairHours);
                        suggested.MaxLaborHours = opExamples.Max(e => e.RepairHours);
                    }

                    result.SuggestedOperations.Add(suggested);
                }
            }

            // Remove duplicates (same operation type + description)
            result.SuggestedOperations = result.SuggestedOperations
                .GroupBy(o => $"{o.OperationType}|{o.Description}")
                .Select(g => g.OrderByDescending(o => o.Confidence).First())
                .OrderByDescending(o => o.Confidence)
                .ToList();

            // Add related parts — prefer learned co-occurrence data, fall back to hardcoded
            var coOccurrences = GetRelatedOperations(partName, operationType ?? "");
            if (coOccurrences.Count > 0)
            {
                result.RelatedParts = coOccurrences
                    .Select(c => $"{c.PartName} {c.OperationType} ({c.CoOccurrenceRate:P0})")
                    .ToList();
            }
            else
            {
                result.RelatedParts = FindRelatedParts(normalizedPart);
            }

            // Build explanation
            result.Explanation = BuildQueryExplanation(result, matchingPatterns.Count, relevantExamples.Count);

            return result;
        }

        /// <summary>
        /// Quick query: Get all common operations for a part
        /// </summary>
        public List<string> GetCommonOperationsForPart(string partName)
        {
            var normalizedPart = NormalizePartName(partName);

            return _database.Patterns.Values
                .Where(p => PartMatches(p.PartName, normalizedPart))
                .OrderByDescending(p => p.ExampleCount)
                .Select(p => $"{p.OperationType}: {p.Operations.FirstOrDefault()?.Description ?? p.PartName}")
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Get operation suggestions for natural language query
        /// Example: "repairing a front bumper" or "replacing a fender"
        /// </summary>
        public QueryResult QueryFromNaturalLanguage(string query)
        {
            var lower = query.ToLowerInvariant();

            // Extract part name
            string? partName = null;
            var knownParts = new[]
            {
                "bumper cover", "bumper", "fender", "hood", "door", "quarter panel", "quarter",
                "trunk", "liftgate", "tailgate", "grille", "headlight", "taillight",
                "mirror", "windshield", "roof", "pillar", "rocker", "radiator support",
                "frame", "wheel", "control arm", "strut", "airbag"
            };

            foreach (var part in knownParts.OrderByDescending(p => p.Length))
            {
                if (lower.Contains(part))
                {
                    partName = part;
                    break;
                }
            }

            if (string.IsNullOrEmpty(partName))
            {
                return new QueryResult
                {
                    Explanation = "Could not identify a part name in your query. Try asking about a specific part like 'door', 'fender', or 'bumper'."
                };
            }

            // Extract operation type
            string? operationType = null;
            if (lower.Contains("repair"))
                operationType = "Repair";
            else if (lower.Contains("replace") || lower.Contains("replacing"))
                operationType = "Replace";
            else if (lower.Contains("remove") || lower.Contains("r&i"))
                operationType = "R&I";
            else if (lower.Contains("refinish") || lower.Contains("paint"))
                operationType = "Refinish";
            else if (lower.Contains("blend"))
                operationType = "Blend";

            return QueryOperationsForPart(partName, operationType);
        }

        /// <summary>
        /// Normalize part name for matching (abbreviates common prefixes)
        /// </summary>
        private string NormalizePartNameForMatch(string partName)
        {
            var normalized = partName.ToLowerInvariant()
                .Replace("front ", "frt ")
                .Replace("rear ", "rr ")
                .Replace("left ", "lh ")
                .Replace("right ", "rh ")
                .Replace("  ", " ")
                .Trim();
            return normalized;
        }

        /// <summary>
        /// Check if two part names match (fuzzy match)
        /// </summary>
        private bool PartMatches(string storedPart, string queryPart)
        {
            if (string.IsNullOrEmpty(storedPart) || string.IsNullOrEmpty(queryPart))
                return false;

            var stored = NormalizePartNameForMatch(storedPart);
            var query = queryPart.ToLowerInvariant();

            // Exact match
            if (stored.Equals(query, StringComparison.OrdinalIgnoreCase))
                return true;

            // Contains match
            if (stored.Contains(query) || query.Contains(stored))
                return true;

            // Word overlap
            var storedWords = stored.Split(' ', '_').Where(w => w.Length > 2).ToHashSet();
            var queryWords = query.Split(' ', '_').Where(w => w.Length > 2).ToHashSet();
            var overlap = storedWords.Intersect(queryWords, StringComparer.OrdinalIgnoreCase).Count();

            return overlap >= 1 && (overlap >= storedWords.Count * 0.5 || overlap >= queryWords.Count * 0.5);
        }

        /// <summary>
        /// Record co-occurrences for all operations from a single estimate.
        /// Each operation pair is recorded bidirectionally with running-averaged values.
        /// </summary>
        public void UpdateCoOccurrences(List<(string PatternKey, string PartName, string OperationType, decimal LaborHours, decimal RefinishHours, decimal Price)> estimateOperations)
        {
            if (estimateOperations.Count < 2) return;
            _database.CoOccurrences ??= new Dictionary<string, CoOccurrenceRecord>();

            foreach (var op in estimateOperations)
            {
                if (string.IsNullOrWhiteSpace(op.PatternKey)) continue;

                if (!_database.CoOccurrences.TryGetValue(op.PatternKey, out var record))
                {
                    record = new CoOccurrenceRecord
                    {
                        PatternKey = op.PatternKey,
                        PartName = op.PartName,
                        OperationType = op.OperationType
                    };
                    _database.CoOccurrences[op.PatternKey] = record;
                }

                record.TotalEstimateCount++;

                foreach (var other in estimateOperations)
                {
                    if (string.IsNullOrWhiteSpace(other.PatternKey) || other.PatternKey == op.PatternKey) continue;

                    if (!record.CoOccurringOperations.TryGetValue(other.PatternKey, out var entry))
                    {
                        entry = new CoOccurrenceEntry
                        {
                            PatternKey = other.PatternKey,
                            PartName = other.PartName,
                            OperationType = other.OperationType
                        };
                        record.CoOccurringOperations[other.PatternKey] = entry;
                    }

                    // Running average
                    int n = entry.TimesSeenTogether;
                    entry.AvgLaborHours = (entry.AvgLaborHours * n + other.LaborHours) / (n + 1);
                    entry.AvgRefinishHours = (entry.AvgRefinishHours * n + other.RefinishHours) / (n + 1);
                    entry.AvgPrice = (entry.AvgPrice * n + other.Price) / (n + 1);
                    entry.TimesSeenTogether++;
                }
            }
        }

        /// <summary>
        /// Get operations that frequently co-occur with the given operation, sorted by rate descending.
        /// </summary>
        public List<CoOccurrenceEntry> GetRelatedOperations(string partName, string operationType, int maxResults = 10)
        {
            _database.CoOccurrences ??= new Dictionary<string, CoOccurrenceRecord>();

            var normalizedPart = NormalizePartName(partName);
            var normalizedOp = NormalizeOperationType(operationType);
            var key = string.IsNullOrEmpty(normalizedOp) ? normalizedPart : $"{normalizedPart}|{normalizedOp}";

            if (!_database.CoOccurrences.TryGetValue(key, out var record))
            {
                // Try without operation type
                key = normalizedPart;
                if (!_database.CoOccurrences.TryGetValue(key, out record))
                    return new List<CoOccurrenceEntry>();
            }

            if (record.TotalEstimateCount == 0)
                return new List<CoOccurrenceEntry>();

            var results = new List<CoOccurrenceEntry>();
            foreach (var entry in record.CoOccurringOperations.Values)
            {
                entry.CoOccurrenceRate = (double)entry.TimesSeenTogether / record.TotalEstimateCount;
                results.Add(entry);
            }

            return results
                .OrderByDescending(e => e.CoOccurrenceRate)
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// Find parts that are often repaired together
        /// </summary>
        private List<string> FindRelatedParts(string partName)
        {
            var relatedParts = new Dictionary<string, List<string>>
            {
                { "bumper", new List<string> { "bumper cover", "bumper reinforcement", "bumper absorber", "grille", "fog light" } },
                { "fender", new List<string> { "fender liner", "headlight", "door", "bumper" } },
                { "door", new List<string> { "door shell", "door skin", "door handle", "door mirror", "fender" } },
                { "hood", new List<string> { "hood hinge", "hood latch", "hood strut", "fender", "grille" } },
                { "quarter", new List<string> { "quarter panel", "taillight", "trunk", "rocker panel" } },
                { "headlight", new List<string> { "bumper", "fender", "grille" } },
                { "taillight", new List<string> { "bumper", "quarter panel", "trunk" } },
            };

            var lower = partName.ToLowerInvariant();
            foreach (var kvp in relatedParts)
            {
                if (lower.Contains(kvp.Key))
                    return kvp.Value;
            }

            return new List<string>();
        }

        /// <summary>
        /// Build human-readable explanation of query results
        /// </summary>
        private string BuildQueryExplanation(QueryResult result, int patternCount, int exampleCount)
        {
            if (result.SuggestedOperations.Count == 0)
            {
                return $"No learned data found for '{result.QueryPartName}'. " +
                       "Import more estimates to build the learning database.";
            }

            var sb = new System.Text.StringBuilder();

            sb.Append($"Found {result.SuggestedOperations.Count} operation(s) for '{result.QueryPartName}'");

            if (!string.IsNullOrEmpty(result.QueryOperationType))
                sb.Append($" ({result.QueryOperationType})");

            sb.Append($" based on {exampleCount} training examples.");

            if (result.HasVehicleSpecificData)
                sb.Append($" Using vehicle-specific data for '{result.QueryVehicle}'.");

            if (result.RelatedParts.Any())
                sb.Append($" Related parts often done together: {string.Join(", ", result.RelatedParts.Take(3))}.");

            return sb.ToString();
        }

        #endregion
    }

    #region Data Models

    public class LearnedPatternDatabase
    {
        public Dictionary<string, LearnedPattern> Patterns { get; set; } = new();
        public List<TrainingExample> TrainingExamples { get; set; } = new();
        public List<EstimateTrainingData> TrainedEstimates { get; set; } = new();

        /// <summary>
        /// Patterns for manual lines (marked with # in CCC).
        /// Key format: "{partname}|{operationtype}" e.g. "bumper_cover|replace"
        /// </summary>
        public Dictionary<string, ManualLinePattern> ManualLinePatterns { get; set; } = new();

        // Statistics tracking
        public int EstimatesImported { get; set; } = 0;
        public decimal TotalEstimateValue { get; set; } = 0;
        public decimal AverageEstimateValue => EstimatesImported > 0 ? TotalEstimateValue / EstimatesImported : 0;

        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int Version { get; set; } = 4; // v4: Smart Learning features

        // ==================== SMART LEARNING EXTENSIONS ====================
        // All nullable for backward compatibility with existing data files

        /// <summary>
        /// Metadata for smart learning features (versioning, configuration)
        /// </summary>
        public SmartLearningMetadata? SmartMetadata { get; set; }

        /// <summary>
        /// Tracks user feedback (accept/reject) for each pattern
        /// Key: PatternKey
        /// </summary>
        public Dictionary<string, PatternFeedback>? PatternFeedbacks { get; set; }

        /// <summary>
        /// Quality assessment records for imported estimates
        /// </summary>
        public List<EstimateQualityRecord>? QualityRecords { get; set; }

        /// <summary>
        /// Cached health metrics for the learning system
        /// </summary>
        public LearningHealthMetrics? HealthMetrics { get; set; }

        /// <summary>
        /// Statistical baselines for outlier detection
        /// Key: "{partname}|{operationtype}"
        /// </summary>
        public Dictionary<string, OperationBaseline>? Baselines { get; set; }

        /// <summary>
        /// Vehicle-indexed estimate summaries for YMM-based suggestions
        /// Key: Normalized vehicle info (e.g., "2022_toyota_camry")
        /// </summary>
        public Dictionary<string, List<VehicleEstimateSummary>>? VehicleEstimates { get; set; }

        /// <summary>
        /// Co-occurrence data: which operations appear together on the same estimate.
        /// Key: PatternKey (e.g., "lt_fender|replace")
        /// </summary>
        public Dictionary<string, CoOccurrenceRecord>? CoOccurrences { get; set; }

        /// <summary>
        /// Returns true if system is in bootstrap mode (< 20 estimates imported)
        /// During bootstrap, quality checks are relaxed to build initial baselines
        /// </summary>
        [JsonIgnore]
        public bool IsBootstrapMode => EstimatesImported < 20;
    }

    public class LearnedPattern
    {
        public string PatternKey { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public List<GeneratedOperation> Operations { get; set; } = new();
        public int ExampleCount { get; set; }
        public double Confidence { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastUpdated { get; set; }

        // ==================== SMART LEARNING EXTENSIONS ====================

        /// <summary>
        /// Vehicle type this pattern is specific to (null = applies to all)
        /// Values: "truck", "suv", "car", or null
        /// </summary>
        public string? VehicleType { get; set; }

        /// <summary>
        /// Version number for pattern versioning/rollback
        /// </summary>
        public int PatternVersion { get; set; } = 1;

        /// <summary>
        /// List of pattern keys that conflict with this one
        /// </summary>
        public List<string>? ConflictingPatterns { get; set; }

        /// <summary>
        /// Confidence adjusted for pattern age decay (reduces over time without updates)
        /// </summary>
        [JsonIgnore]
        public double DecayedConfidence
        {
            get
            {
                var daysSinceUpdate = (DateTime.Now - LastUpdated).TotalDays;
                if (daysSinceUpdate <= 90) return Confidence;
                // After 90 days, decay 10% per 90-day period
                var decayPeriods = (daysSinceUpdate - 90) / 90;
                return Math.Max(0.3, Confidence * Math.Pow(0.9, decayPeriods));
            }
        }

        /// <summary>
        /// True if pattern hasn't been updated in 180+ days
        /// </summary>
        [JsonIgnore]
        public bool IsStale => (DateTime.Now - LastUpdated).TotalDays > 180;
    }

    public class TrainingExample
    {
        public string EstimateLine { get; set; } = "";
        public string NormalizedKey { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal RepairHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public List<GeneratedOperation> GeneratedOperations { get; set; } = new();
        public string Source { get; set; } = "";
        public string? VehicleInfo { get; set; }
        public DateTime DateAdded { get; set; }

        // Full context linking (when learning with full context enabled)
        /// <summary>
        /// Reference to matching P-Page entry (e.g., "P-17: Bumper Operations")
        /// </summary>
        public string? LinkedPPageRef { get; set; }

        /// <summary>
        /// Reference to IncludedNotIncluded entry ID
        /// </summary>
        public string? LinkedIncludedNotIncludedRef { get; set; }

        /// <summary>
        /// Reference to related DEG inquiries (e.g., "DEG 16044, DEG 18527")
        /// </summary>
        public string? LinkedDEGRef { get; set; }
    }

    public class EstimateTrainingData
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = ""; // "CCC", "Mitchell", "Manual"
        public string? VehicleInfo { get; set; }
        public string? VIN { get; set; }
        public List<LineMapping> LineMappings { get; set; } = new();
        public DateTime DateTrained { get; set; }
    }

    public class LineMapping
    {
        public string RawLine { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal RepairHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public List<GeneratedOperation> GeneratedOperations { get; set; } = new();
    }

    public class GeneratedOperation
    {
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RepairHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;
        public double Confidence { get; set; }
        public string Source { get; set; } = "";
        public int TimesUsed { get; set; }
    }

    public class PatternMatch
    {
        public LearnedPattern Pattern { get; set; } = new();
        public double MatchScore { get; set; }
        public string SourceLine { get; set; } = "";
        public ExtractedLineData ExtractedData { get; set; } = new();
    }

    public class ExtractedLineData
    {
        public string RawLine { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string? Position { get; set; } // Front/Rear
        public string? Side { get; set; } // Left/Right
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
    }

    public class ParsedEstimateLine
    {
        public string RawLine { get; set; } = "";
        public string Description { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Category { get; set; } = "";
        public string Position { get; set; } = "";
        public string Side { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RepairHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Labor type: Body, Refinish, Mechanical, Structural
        /// </summary>
        public string LaborType { get; set; } = "";

        /// <summary>
        /// True if this is a manual line (marked with # in CCC estimates)
        /// </summary>
        public bool IsManualLine { get; set; }

        /// <summary>
        /// Reference to the parent part this manual line is associated with
        /// </summary>
        public string? ParentPartName { get; set; }

        /// <summary>
        /// Source estimate file name for tracking
        /// </summary>
        public string? SourceFile { get; set; }
    }

    /// <summary>
    /// Represents a manual line entry linked to a parent part.
    /// Manual lines in CCC are marked with "#" in the 3rd column (category).
    /// Examples: "Bumper Cover Adhesion Promoter", "Bumper Cover Flex Additive"
    /// </summary>
    public class ManualLineEntry
    {
        public string Description { get; set; } = "";
        public string ParentPartName { get; set; } = "";
        public decimal LaborUnits { get; set; }
        public decimal RefinishUnits { get; set; }
        public string ManualLineType { get; set; } = ""; // Adhesion Promoter, Flex Additive, etc.
        public int TimesUsed { get; set; } = 1;

        // Price tracking - dollar amounts from estimates
        public decimal Price { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AvgPrice { get; set; }

        // Enhanced tracking
        public string LaborType { get; set; } = ""; // Body, Refinish, Mechanical, Structural
        public decimal MinLaborUnits { get; set; }
        public decimal MaxLaborUnits { get; set; }
        public decimal MinRefinishUnits { get; set; }
        public decimal MaxRefinishUnits { get; set; }

        // Wording variations - track different phrasings used
        public List<string> WordingVariations { get; set; } = new();

        // Timestamps
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }

        // Source estimate tracking
        public List<string> SourceEstimates { get; set; } = new();
    }

    /// <summary>
    /// Pattern for manual lines associated with a part.
    /// Stores what manual operations typically accompany a parent part.
    /// </summary>
    public class ManualLinePattern
    {
        public string ParentPartName { get; set; } = "";
        public string ParentOperationType { get; set; } = ""; // Replace, Repair, etc.
        public List<ManualLineEntry> ManualLines { get; set; } = new();
        public int ExampleCount { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastUpdated { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Summary of a single uploaded estimate - for YMM-based suggestions
    /// </summary>
    public class VehicleEstimateSummary
    {
        public string EstimateId { get; set; } = "";
        public string VehicleInfo { get; set; } = ""; // "2022 Toyota Camry"
        public string NormalizedVehicle { get; set; } = ""; // "2022_toyota_camry"
        public DateTime DateImported { get; set; }
        public string SourceFile { get; set; } = "";

        /// <summary>
        /// All operations in this estimate: Part + Operation + Manual Lines
        /// </summary>
        public List<EstimateOperationSummary> Operations { get; set; } = new();
    }

    /// <summary>
    /// A single operation from an estimate (e.g., "Bumper Cover Replace" with its manual lines)
    /// </summary>
    public class EstimateOperationSummary
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = ""; // Replace, Repair, R&I, etc.
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }

        /// <summary>
        /// Manual lines (# lines) that followed this operation
        /// </summary>
        public List<ManualLineSummary> ManualLines { get; set; } = new();
    }

    /// <summary>
    /// A single manual line from an estimate
    /// </summary>
    public class ManualLineSummary
    {
        public string Description { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public decimal Price { get; set; }
    }

    /// <summary>
    /// Result from finding similar estimates by YMM + operation
    /// </summary>
    public class SimilarEstimatesResult
    {
        public string VehicleInfo { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string MatchType { get; set; } = "None"; // "Exact YMM", "Similar Vehicle", "Any Vehicle"

        public List<MatchedEstimate> MatchingEstimates { get; set; } = new();
        public List<AggregatedManualLine> AggregatedManualLines { get; set; } = new();

        public int TotalEstimatesFound => MatchingEstimates.Count;
        public bool HasData => MatchingEstimates.Count > 0;
    }

    /// <summary>
    /// A single matched estimate with its operation
    /// </summary>
    public class MatchedEstimate
    {
        public string EstimateId { get; set; } = "";
        public string VehicleInfo { get; set; } = "";
        public DateTime DateImported { get; set; }
        public EstimateOperationSummary Operation { get; set; } = new();
    }

    /// <summary>
    /// Aggregated manual line across multiple estimates
    /// </summary>
    public class AggregatedManualLine
    {
        public string Description { get; set; } = "";
        public string NormalizedDescription { get; set; } = "";
        public int Count { get; set; }
        public int TotalEstimates { get; set; }
        public double Frequency { get; set; } // 0.0 to 1.0
        public decimal AvgLaborHours { get; set; }
        public decimal AvgRefinishHours { get; set; }
        public decimal AvgPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }

        /// <summary>
        /// Display format: "3/4" means 3 out of 4 estimates had this
        /// </summary>
        public string FrequencyDisplay => $"{Count}/{TotalEstimates}";
    }

    /// <summary>
    /// Result from searching operations by keyword
    /// </summary>
    public class OperationSearchResult
    {
        public string OperationName { get; set; } = "";
        public string Description { get; set; } = "";
        public string ParentPartName { get; set; } = "";
        public string ParentOperationType { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public int TimesUsed { get; set; }
        public int PatternExampleCount { get; set; }
        public double Confidence { get; set; }

        // Price data
        public decimal Price { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AvgPrice { get; set; }

        // Enhanced data
        public string LaborType { get; set; } = "";
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }
        public decimal MinRefinishHours { get; set; }
        public decimal MaxRefinishHours { get; set; }
        public List<string> WordingVariations { get; set; } = new();
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// Aggregated stats for an operation type
    /// </summary>
    public class OperationAggregateStats
    {
        public string OperationName { get; set; } = "";
        public int TotalOccurrences { get; set; }
        public int UniqueParentParts { get; set; }
        public decimal AvgLaborHours { get; set; }
        public decimal AvgRefinishHours { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }
        public List<OperationSearchResult> Occurrences { get; set; } = new();

        // Price aggregates
        public decimal AvgPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal TotalPriceValue { get; set; }
    }

    public class LearningStatistics
    {
        public int TotalPatterns { get; set; }
        public int TotalExamples { get; set; }
        public int TotalEstimatesTrained { get; set; }
        public int TotalManualLinePatterns { get; set; }
        public double AverageConfidence { get; set; }
        public List<string> TopPatterns { get; set; } = new();

        // Extended stats for publishing
        public int TotalTrainingExamples { get; set; }
        public int TotalTrainedEstimates { get; set; }
        public int EstimatesImported { get; set; }
        public decimal TotalEstimateValue { get; set; }
        public decimal AverageEstimateValue { get; set; }
        public DateTime LastUpdated { get; set; }
        public string BaseKnowledgePath { get; set; } = "";
        public string UserKnowledgePath { get; set; } = "";
        public bool HasBaseKnowledge { get; set; }
        public bool HasUserKnowledge { get; set; }
    }

    /// <summary>
    /// Result of a query for operations by part
    /// </summary>
    public class QueryResult
    {
        public string QueryPartName { get; set; } = "";
        public string? QueryOperationType { get; set; }
        public string? QueryVehicle { get; set; }
        public bool HasVehicleSpecificData { get; set; }
        public List<SuggestedOperation> SuggestedOperations { get; set; } = new();
        public List<string> RelatedParts { get; set; } = new();
        public string Explanation { get; set; } = "";
    }

    /// <summary>
    /// A suggested operation based on learned data
    /// </summary>
    public class SuggestedOperation
    {
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";

        // Direct labor/refinish hours (aliases for Typical values)
        public decimal LaborHours { get => TypicalLaborHours; set => TypicalLaborHours = value; }
        public decimal RefinishHours { get => TypicalRefinishHours; set => TypicalRefinishHours = value; }
        public decimal Price { get => TypicalPrice; set => TypicalPrice = value; }

        // Typical values from pattern
        public decimal TypicalLaborHours { get; set; }
        public decimal TypicalRefinishHours { get; set; }
        public decimal TypicalPrice { get; set; }

        // Statistics from training examples
        public decimal AverageLaborHours { get; set; }
        public decimal AverageRefinishHours { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }

        public double Confidence { get; set; }
        public int TimesUsed { get; set; }
        public int ExampleCount { get; set; }
        public string Source { get; set; } = "";
    }

    /// <summary>
    /// Result of a query for operations with scaled values.
    /// Used when user provides labor/refinish units and wants calculated operations.
    /// </summary>
    public class ScaledQueryResult
    {
        public string QueryPartName { get; set; } = "";
        public string QueryOperationType { get; set; } = "";
        public string? QueryVehicle { get; set; }
        public decimal? InputLaborUnits { get; set; }
        public decimal? InputRefinishUnits { get; set; }
        public bool HasVehicleSpecificData { get; set; }

        /// <summary>
        /// Factor by which values were scaled (input / typical)
        /// </summary>
        public decimal ScaleFactor { get; set; } = 1.0m;

        /// <summary>
        /// Typical labor units learned from examples
        /// </summary>
        public decimal TypicalLaborUnits { get; set; }

        /// <summary>
        /// Typical refinish units learned from examples
        /// </summary>
        public decimal TypicalRefinishUnits { get; set; }

        public List<ScaledOperation> Operations { get; set; } = new();
        public string Explanation { get; set; } = "";
    }

    /// <summary>
    /// An operation with both original and scaled values
    /// </summary>
    public class ScaledOperation
    {
        public string OperationType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";

        /// <summary>
        /// Original labor hours from learned pattern
        /// </summary>
        public decimal OriginalLaborHours { get; set; }

        /// <summary>
        /// Original refinish hours from learned pattern
        /// </summary>
        public decimal OriginalRefinishHours { get; set; }

        /// <summary>
        /// Scaled labor hours based on user input
        /// </summary>
        public decimal ScaledLaborHours { get; set; }

        /// <summary>
        /// Scaled refinish hours based on user input
        /// </summary>
        public decimal ScaledRefinishHours { get; set; }

        public double Confidence { get; set; }
        public int ExampleCount { get; set; }
        public string Source { get; set; } = "";

        /// <summary>
        /// True if this is a manual line entry (adhesion promoter, flex additive, etc.)
        /// </summary>
        public bool IsManualLine { get; set; }
    }

    // ==================== SMART LEARNING DATA MODELS ====================

    /// <summary>
    /// Metadata for smart learning features
    /// </summary>
    public class SmartLearningMetadata
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime SmartFeaturesEnabledDate { get; set; } = DateTime.Now;
        public Dictionary<string, PatternVersionHistory> PatternVersions { get; set; } = new();
        public LearningConfiguration Configuration { get; set; } = new();
    }

    /// <summary>
    /// Configuration for learning behavior
    /// </summary>
    public class LearningConfiguration
    {
        /// <summary>Minimum confidence to show a suggestion (0-1)</summary>
        public double MinConfidenceForSuggestion { get; set; } = 0.5;

        /// <summary>Minimum quality score to use estimate for training (0-100)</summary>
        public double MinQualityScoreForTraining { get; set; } = 60;

        /// <summary>Days after which pattern confidence starts to decay</summary>
        public int PatternDecayStartDays { get; set; } = 90;

        /// <summary>Confidence decay rate per period (0.1 = 10% reduction)</summary>
        public double PatternDecayRate { get; set; } = 0.1;

        /// <summary>Enable outlier detection during quality assessment</summary>
        public bool EnableOutlierDetection { get; set; } = true;

        /// <summary>Z-score threshold for outlier detection (2.5 = ~1% of values)</summary>
        public double OutlierZScoreThreshold { get; set; } = 2.5;

        /// <summary>Enable vehicle-type-specific patterns</summary>
        public bool EnableVehicleTypePatterns { get; set; } = true;

        /// <summary>Maximum number of pattern version snapshots to keep</summary>
        public int MaxPatternSnapshots { get; set; } = 5;

        /// <summary>Number of estimates required before quality checks activate</summary>
        public int BootstrapEstimateCount { get; set; } = 20;
    }

    /// <summary>
    /// Tracks user feedback (accept/reject) for patterns and suggestions
    /// </summary>
    public class PatternFeedback
    {
        public string PatternKey { get; set; } = "";

        /// <summary>How many times this pattern was suggested</summary>
        public int TimesGenerated { get; set; }

        /// <summary>How many times user accepted the suggestion</summary>
        public int TimesAccepted { get; set; }

        /// <summary>How many times user rejected the suggestion</summary>
        public int TimesRejected { get; set; }

        /// <summary>How many times user accepted but modified the values</summary>
        public int TimesModified { get; set; }

        public DateTime FirstUsed { get; set; } = DateTime.Now;
        public DateTime LastUsed { get; set; } = DateTime.Now;

        /// <summary>Last 50 feedback events for trend analysis</summary>
        public List<FeedbackEvent> RecentEvents { get; set; } = new();

        /// <summary>Acceptance rate (0-1)</summary>
        [JsonIgnore]
        public double AcceptanceRate => TimesGenerated > 0 ? (double)TimesAccepted / TimesGenerated : 0;

        /// <summary>Rejection rate (0-1)</summary>
        [JsonIgnore]
        public double RejectionRate => TimesGenerated > 0 ? (double)TimesRejected / TimesGenerated : 0;
    }

    /// <summary>
    /// Individual feedback event for a pattern
    /// </summary>
    public class FeedbackEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public FeedbackAction Action { get; set; }
        public string? Context { get; set; }  // Vehicle type, estimate source, etc.
        public string? ModificationDetails { get; set; }  // What was changed if modified
    }

    /// <summary>
    /// Types of feedback actions
    /// </summary>
    public enum FeedbackAction
    {
        Generated,   // Pattern was suggested to user
        Accepted,    // User accepted the suggestion
        Rejected,    // User explicitly rejected
        Modified,    // User accepted but changed values
        Ignored      // Shown but user took no action
    }

    /// <summary>
    /// Quality assessment for an imported estimate
    /// </summary>
    public class EstimateQualityRecord
    {
        public string EstimateId { get; set; } = Guid.NewGuid().ToString();
        public DateTime AssessmentDate { get; set; } = DateTime.Now;

        /// <summary>Quality score 0-100</summary>
        public int QualityScore { get; set; }

        public QualityGrade Grade { get; set; }

        /// <summary>Issues found during quality assessment</summary>
        public List<QualityFlag> Flags { get; set; } = new();

        /// <summary>Detected outliers in the estimate</summary>
        public List<OutlierDetection> Outliers { get; set; } = new();

        /// <summary>True if estimate was used for training</summary>
        public bool WasUsedForTraining { get; set; }

        /// <summary>Weight applied during learning (reduced for low quality)</summary>
        public decimal LearningWeight { get; set; } = 1.0m;

        /// <summary>Vehicle info from the estimate</summary>
        public string? VehicleInfo { get; set; }

        /// <summary>Number of line items in the estimate</summary>
        public int LineItemCount { get; set; }
    }

    /// <summary>
    /// Quality grade based on score
    /// </summary>
    public enum QualityGrade
    {
        Excellent,  // 90-100
        Good,       // 75-89
        Fair,       // 60-74
        Poor,       // 40-59
        Rejected    // 0-39
    }

    /// <summary>
    /// A quality issue found during assessment
    /// </summary>
    public class QualityFlag
    {
        public QualityFlagType Type { get; set; }
        public string Description { get; set; } = "";
        public QualitySeverity Severity { get; set; }
        public string? AffectedItem { get; set; }
    }

    /// <summary>
    /// Types of quality issues
    /// </summary>
    public enum QualityFlagType
    {
        MissingCommonOperation,  // Expected operation not found
        UnusualHours,            // Labor/refinish hours outside normal range
        UnusualPrice,            // Price outside normal range
        IncompleteData,          // Missing required fields
        SuspiciousPattern,       // Pattern doesn't match typical structure
        VehicleMismatch,         // Vehicle-specific mismatch
        SourceInconsistency      // Mixed data sources
    }

    /// <summary>
    /// Severity of quality issues
    /// </summary>
    public enum QualitySeverity
    {
        Info,     // Informational only, no score penalty
        Warning,  // Minor issue, small score penalty
        Error     // Major issue, significant score penalty
    }

    /// <summary>
    /// Outlier detection result for a specific field
    /// </summary>
    public class OutlierDetection
    {
        public string Field { get; set; } = "";  // "LaborHours", "RefinishHours", "Price"
        public decimal Value { get; set; }
        public decimal ExpectedMin { get; set; }
        public decimal ExpectedMax { get; set; }
        public decimal ZScore { get; set; }
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
    }

    /// <summary>
    /// Pattern versioning for rollback capability
    /// </summary>
    public class PatternVersionHistory
    {
        public string PatternKey { get; set; } = "";
        public int CurrentVersion { get; set; } = 1;
        public List<PatternSnapshot> Snapshots { get; set; } = new();
    }

    /// <summary>
    /// Snapshot of a pattern at a point in time
    /// </summary>
    public class PatternSnapshot
    {
        public int Version { get; set; }
        public DateTime SnapshotDate { get; set; } = DateTime.Now;
        public string Reason { get; set; } = "";  // "learning_update", "manual_edit", "rollback"
        public string SerializedPattern { get; set; } = "";  // JSON of LearnedPattern
    }

    /// <summary>
    /// Statistical baseline for a part/operation combination
    /// Used for outlier detection
    /// </summary>
    public class OperationBaseline
    {
        public string PartOperation { get; set; } = "";  // "{partname}|{operationtype}"

        public decimal MeanLaborHours { get; set; }
        public decimal StdDevLaborHours { get; set; }
        public decimal MinLaborHours { get; set; }
        public decimal MaxLaborHours { get; set; }

        public decimal MeanRefinishHours { get; set; }
        public decimal StdDevRefinishHours { get; set; }
        public decimal MinRefinishHours { get; set; }
        public decimal MaxRefinishHours { get; set; }

        public decimal MeanPrice { get; set; }
        public decimal StdDevPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }

        public int SampleCount { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Overall learning system health metrics
    /// </summary>
    public class LearningHealthMetrics
    {
        public DateTime LastCalculated { get; set; } = DateTime.Now;

        public int TotalPatterns { get; set; }
        public int HighConfidencePatterns { get; set; }  // Confidence > 0.8
        public int MediumConfidencePatterns { get; set; }  // 0.5 - 0.8
        public int LowConfidencePatterns { get; set; }   // < 0.5
        public int StalePatterns { get; set; }           // Not updated in 180+ days

        public double OverallAcceptanceRate { get; set; }
        public double OverallRejectionRate { get; set; }

        public List<string> TopRejectedPatterns { get; set; } = new();  // Top 10
        public List<string> MostUsedPatterns { get; set; } = new();     // Top 10
        public List<string> StalePatternKeys { get; set; } = new();     // Patterns needing refresh

        public Dictionary<string, int> PatternsByVehicleType { get; set; } = new();
        public int ConflictingPatternsCount { get; set; }

        /// <summary>Overall health score 0-100</summary>
        public int HealthScore { get; set; }

        /// <summary>Recommended actions to improve health</summary>
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Learning effectiveness report for a time period
    /// </summary>
    public class LearningEffectivenessReport
    {
        public DateTime ReportDate { get; set; } = DateTime.Now;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public double OverallAcceptanceRate { get; set; }
        public double TrendVsPreviousPeriod { get; set; }  // +/- percentage

        public int TotalSuggestionsMade { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalRejected { get; set; }
        public int TotalModified { get; set; }

        public List<string> ImprovingPatterns { get; set; } = new();  // Acceptance trending up
        public List<string> DecliningPatterns { get; set; } = new();  // Acceptance trending down
    }

    /// <summary>
    /// Pattern conflict information
    /// </summary>
    public class PatternConflict
    {
        public string PatternKey1 { get; set; } = "";
        public string PatternKey2 { get; set; } = "";
        public string ConflictType { get; set; } = "";  // "same_part_different_ops", "overlapping_hours"
        public double Severity { get; set; }  // 0-1
        public string Description { get; set; } = "";
    }

    // ==================== CO-OCCURRENCE DATA MODELS ====================

    /// <summary>
    /// Tracks which operations appear together on the same estimate.
    /// One record per unique operation (pattern key).
    /// </summary>
    public class CoOccurrenceRecord
    {
        public string PatternKey { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public int TotalEstimateCount { get; set; }
        public Dictionary<string, CoOccurrenceEntry> CoOccurringOperations { get; set; } = new();
    }

    /// <summary>
    /// A single co-occurring operation entry with running-averaged values.
    /// </summary>
    public class CoOccurrenceEntry
    {
        public string PatternKey { get; set; } = "";
        public string PartName { get; set; } = "";
        public string OperationType { get; set; } = "";
        public int TimesSeenTogether { get; set; }
        [JsonIgnore]
        public double CoOccurrenceRate { get; set; }
        public decimal AvgLaborHours { get; set; }
        public decimal AvgRefinishHours { get; set; }
        public decimal AvgPrice { get; set; }
    }

    #endregion
}
