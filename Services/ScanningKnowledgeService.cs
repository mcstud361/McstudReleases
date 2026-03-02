#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for vehicle scanning and calibration knowledge.
    /// Source: Collision Advise LLC (https://oemonestop.com/)
    ///
    /// Features:
    /// 1. Health check procedure with included/not included breakdown
    /// 2. Battery support requirements and best practices
    /// 3. OEM vs Aftermarket scan tool comparison
    /// 4. Questions to ask aftermarket scan tool vendors
    /// 5. Four key questions for billing determination
    /// 6. DTC code color meanings
    /// </summary>
    public class ScanningKnowledgeService
    {
        private static ScanningKnowledgeService? _instance;
        public static ScanningKnowledgeService Instance => _instance ??= new ScanningKnowledgeService();

        private ScanningCalibrationData? _data;
        private readonly string _dataPath;

        private ScanningKnowledgeService()
        {
            _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ScanningCalibration.json");
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath);
                    _data = JsonSerializer.Deserialize<ScanningCalibrationData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    System.Diagnostics.Debug.WriteLine("[ScanningKnowledge] Data loaded successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ScanningKnowledge] Data file not found: {_dataPath}");
                    _data = CreateDefaultData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanningKnowledge] Error loading data: {ex.Message}");
                _data = CreateDefaultData();
            }
        }

        #region Health Check Procedure

        /// <summary>
        /// Get the complete 20-step health check procedure
        /// </summary>
        public HealthCheckProcedure GetHealthCheckProcedure()
        {
            return _data?.HealthCheckProcedure ?? new HealthCheckProcedure();
        }

        /// <summary>
        /// Get only the steps INCLUDED in the base 0.5m labor time
        /// </summary>
        public List<HealthCheckStep> GetIncludedSteps()
        {
            return _data?.HealthCheckProcedure?.Steps?
                .Where(s => s.Included)
                .ToList() ?? new List<HealthCheckStep>();
        }

        /// <summary>
        /// Get only the steps NOT INCLUDED (bill separately)
        /// </summary>
        public List<HealthCheckStep> GetNotIncludedSteps()
        {
            return _data?.HealthCheckProcedure?.Steps?
                .Where(s => !s.Included)
                .ToList() ?? new List<HealthCheckStep>();
        }

        #endregion

        #region Battery Support

        /// <summary>
        /// Get battery support requirements and best practices
        /// </summary>
        public BatterySupportInfo GetBatterySupportInfo()
        {
            return _data?.BatterySupport ?? new BatterySupportInfo();
        }

        /// <summary>
        /// Get what is NOT acceptable as battery support
        /// </summary>
        public List<string> GetNotBatterySupport()
        {
            return _data?.BatterySupport?.CriticalInfo?.NotBatterySupport ?? new List<string>();
        }

        /// <summary>
        /// Get why battery support matters
        /// </summary>
        public List<string> GetWhyBatterySupportMatters()
        {
            return _data?.BatterySupport?.WhyBatterySupportMatters ?? new List<string>();
        }

        #endregion

        #region OEM vs Aftermarket

        /// <summary>
        /// Get the full OEM vs Aftermarket comparison table
        /// </summary>
        public List<ScanToolComparison> GetOemVsAftermarketComparison()
        {
            return _data?.OemVsAftermarket?.Comparison ?? new List<ScanToolComparison>();
        }

        /// <summary>
        /// Get questions to ask aftermarket scan tool companies
        /// </summary>
        public List<string> GetAftermarketQuestions()
        {
            return _data?.AftermarketScanToolQuestions?.Questions ?? new List<string>();
        }

        #endregion

        #region Four Key Questions

        /// <summary>
        /// Get the four questions to determine if an operation should be billed
        /// </summary>
        public List<KeyQuestion> GetFourKeyQuestions()
        {
            return _data?.FourKeyQuestions?.Questions ?? new List<KeyQuestion>();
        }

        /// <summary>
        /// Evaluate an operation against the four key questions
        /// Returns guidance for each question
        /// </summary>
        public OperationEvaluation EvaluateOperation(string operationName)
        {
            var questions = GetFourKeyQuestions();
            var evaluation = new OperationEvaluation
            {
                OperationName = operationName,
                Evaluations = new List<QuestionEvaluation>()
            };

            foreach (var q in questions)
            {
                evaluation.Evaluations.Add(new QuestionEvaluation
                {
                    QuestionNumber = q.Number,
                    Question = q.Question,
                    QuestionType = q.Type,
                    Guidance = GetGuidanceForQuestion(operationName, q.Type)
                });
            }

            return evaluation;
        }

        private string GetGuidanceForQuestion(string operationName, string questionType)
        {
            return questionType switch
            {
                "required" => "Document OEM position statement or repair procedure requirement",
                "included" => "Check P-Pages to determine if included in other labor operations",
                "predetermined" => "Search CCC/Mitchell/Motor databases for published time",
                "worth" => "If no predetermined time, minimum of 0.1 hours or $1.00",
                _ => ""
            };
        }

        #endregion

        #region DTC Code Colors

        /// <summary>
        /// Get DTC code color meanings (OEM tools)
        /// </summary>
        public List<DtcCodeColor> GetDtcCodeColors()
        {
            return _data?.DtcCodeColors?.Colors ?? new List<DtcCodeColor>();
        }

        #endregion

        #region Scanning Myths

        /// <summary>
        /// Get responses to the "$50 scan" myth
        /// </summary>
        public ScanningMythsInfo GetScanningMyths()
        {
            return _data?.ScanningMyths ?? new ScanningMythsInfo();
        }

        /// <summary>
        /// Get itemization steps for responding to insurance scan pricing
        /// </summary>
        public List<MythResponse> GetMythResponses()
        {
            return _data?.ScanningMyths?.Responses ?? new List<MythResponse>();
        }

        #endregion

        #region Calibration Types

        /// <summary>
        /// Get common calibration types
        /// </summary>
        public List<CalibrationType> GetCalibrationTypes()
        {
            return _data?.CalibrationTypes?.Common ?? new List<CalibrationType>();
        }

        #endregion

        #region Resources

        /// <summary>
        /// Get OEM position statement resource info
        /// </summary>
        public ResourceInfo GetOemResource()
        {
            return _data?.Resources?.Oem ?? new ResourceInfo();
        }

        #endregion

        private ScanningCalibrationData CreateDefaultData()
        {
            // Create minimal default data if file not found
            return new ScanningCalibrationData
            {
                Version = "1.0",
                Source = "Collision Advise LLC",
                HealthCheckProcedure = new HealthCheckProcedure
                {
                    Title = "Vehicle Health Check",
                    BaseLabor = "0.5m",
                    Steps = new List<HealthCheckStep>()
                },
                BatterySupport = new BatterySupportInfo
                {
                    Title = "Battery Support",
                    CriticalInfo = new BatterySupportCriticalInfo
                    {
                        NotBatterySupport = new List<string>
                        {
                            "Jump box is NOT battery support",
                            "Trickle charger is NOT battery support"
                        },
                        BestChoice = "Battery voltage maintainer (Midtronics DCA-8000)"
                    }
                },
                FourKeyQuestions = new FourKeyQuestionsData
                {
                    Questions = new List<KeyQuestion>
                    {
                        new() { Number = 1, Question = "Is it required to restore the vehicle back to pre-accident condition?", Type = "required" },
                        new() { Number = 2, Question = "Is it included in any other labor operation or is it a separate operation?", Type = "included" },
                        new() { Number = 3, Question = "Is there a pre-determined time in the database?", Type = "predetermined" },
                        new() { Number = 4, Question = "If not, what is it worth? (minimum of 0.1 or $1.00)", Type = "worth" }
                    }
                }
            };
        }
    }

    #region Data Models

    public class ScanningCalibrationData
    {
        public string? Version { get; set; }
        public string? LastUpdated { get; set; }
        public string? Source { get; set; }
        public string? ReferenceUrl { get; set; }
        public HealthCheckProcedure? HealthCheckProcedure { get; set; }
        public BatterySupportInfo? BatterySupport { get; set; }
        public OemVsAftermarketData? OemVsAftermarket { get; set; }
        public AftermarketQuestionsData? AftermarketScanToolQuestions { get; set; }
        public ScanningMythsInfo? ScanningMyths { get; set; }
        public FourKeyQuestionsData? FourKeyQuestions { get; set; }
        public DtcCodeColorsData? DtcCodeColors { get; set; }
        public CalibrationTypesData? CalibrationTypes { get; set; }
        public ResourcesData? Resources { get; set; }
    }

    public class HealthCheckProcedure
    {
        public string? Title { get; set; }
        public string? BaseLabor { get; set; }
        public string? BaseLaborDescription { get; set; }
        public string? NotIncludedNote { get; set; }
        public List<HealthCheckStep>? Steps { get; set; }
    }

    public class HealthCheckStep
    {
        public int Step { get; set; }
        public string? Description { get; set; }
        public bool Included { get; set; }
        public string? Notes { get; set; }
        public List<string>? Examples { get; set; }
        public List<string>? Conditions { get; set; }
    }

    public class BatterySupportInfo
    {
        public string? Title { get; set; }
        public BatterySupportCriticalInfo? CriticalInfo { get; set; }
        public List<string>? WhyBatterySupportMatters { get; set; }
    }

    public class BatterySupportCriticalInfo
    {
        public List<string>? NotBatterySupport { get; set; }
        public List<string>? Reasons { get; set; }
        public string? BestChoice { get; set; }
        public string? OemRecommended { get; set; }
    }

    public class OemVsAftermarketData
    {
        public string? Title { get; set; }
        public List<ScanToolComparison>? Comparison { get; set; }
    }

    public class ScanToolComparison
    {
        public string? Category { get; set; }
        public string? Oem { get; set; }
        public string? Aftermarket { get; set; }
    }

    public class AftermarketQuestionsData
    {
        public string? Title { get; set; }
        public List<string>? Questions { get; set; }
    }

    public class ScanningMythsInfo
    {
        public string? Title { get; set; }
        public string? Myth { get; set; }
        public List<MythResponse>? Responses { get; set; }
    }

    public class MythResponse
    {
        public int Step { get; set; }
        public string? Action { get; set; }
        public List<string>? Examples { get; set; }
    }

    public class FourKeyQuestionsData
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<KeyQuestion>? Questions { get; set; }
    }

    public class KeyQuestion
    {
        public int Number { get; set; }
        public string? Question { get; set; }
        public string? Type { get; set; }
    }

    public class DtcCodeColorsData
    {
        public string? Title { get; set; }
        public List<DtcCodeColor>? Colors { get; set; }
    }

    public class DtcCodeColor
    {
        public string? Color { get; set; }
        public string? Meaning { get; set; }
        public string? Action { get; set; }
    }

    public class CalibrationTypesData
    {
        public List<CalibrationType>? Common { get; set; }
    }

    public class CalibrationType
    {
        public string? Type { get; set; }
        public string? Description { get; set; }
    }

    public class ResourcesData
    {
        public ResourceInfo? Oem { get; set; }
    }

    public class ResourceInfo
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
    }

    public class OperationEvaluation
    {
        public string? OperationName { get; set; }
        public List<QuestionEvaluation>? Evaluations { get; set; }
    }

    public class QuestionEvaluation
    {
        public int QuestionNumber { get; set; }
        public string? Question { get; set; }
        public string? QuestionType { get; set; }
        public string? Guidance { get; set; }
    }

    #endregion
}
