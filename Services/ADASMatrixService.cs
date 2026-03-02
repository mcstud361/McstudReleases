#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// ADAS Matrix Service - Determines calibration requirements based on repair operations
    ///
    /// Key functionality:
    /// - Maps repair operations to required ADAS calibrations
    /// - Identifies which sensors are affected by specific repairs
    /// - Provides calibration type (static, dynamic, or both)
    /// - Includes OEM-specific notes and warnings
    /// </summary>
    public class ADASMatrixService
    {
        private readonly string _dataFilePath;
        private ADASRequirementsData? _adasData;

        private static ADASMatrixService? _instance;
        public static ADASMatrixService Instance => _instance ??= new ADASMatrixService();

        public ADASMatrixService()
        {
            _dataFilePath = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "ADASRequirements.json"
            );
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _adasData = JsonSerializer.Deserialize<ADASRequirementsData>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ADASMatrix] Load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyze operations and return required ADAS calibrations
        /// </summary>
        public ADASAnalysisResult AnalyzeForADAS(List<EstimateOperation> operations, VehicleInfo? vehicle = null)
        {
            var result = new ADASAnalysisResult();

            if (_adasData == null)
            {
                result.Warnings.Add("Could not load ADAS requirements database");
                return result;
            }

            // Check each operation against calibration triggers
            foreach (var operation in operations)
            {
                var calibrations = CheckOperationForADAS(operation);
                result.RequiredCalibrations.AddRange(calibrations);
            }

            // Deduplicate calibrations (same sensor shouldn't be listed twice)
            result.RequiredCalibrations = DeduplicateCalibrations(result.RequiredCalibrations);

            // Add vehicle-specific notes if available
            if (vehicle != null && !string.IsNullOrEmpty(vehicle.Make))
            {
                var vehicleNotes = GetVehicleSpecificNotes(vehicle.Make);
                if (vehicleNotes != null)
                {
                    result.VehicleSpecificNotes = vehicleNotes;
                }
            }

            // Calculate totals
            result.TotalCalibrationCost = result.RequiredCalibrations.Sum(c => c.EstimatedCost);
            result.TotalCalibrationTime = result.RequiredCalibrations.Sum(c => c.EstimatedTime);
            result.CalibrationsRequired = result.RequiredCalibrations.Count;

            // Check for painting considerations
            var paintingConsiderations = CheckPaintingConsiderations(operations);
            result.PaintingConsiderations.AddRange(paintingConsiderations);

            return result;
        }

        /// <summary>
        /// Check a single operation for ADAS calibration requirements
        /// </summary>
        private List<ADASCalibration> CheckOperationForADAS(EstimateOperation operation)
        {
            var calibrations = new List<ADASCalibration>();

            if (_adasData?.CalibrationTriggers == null) return calibrations;

            var opLower = operation.OperationType.ToLowerInvariant();
            var partLower = operation.PartName.ToLowerInvariant();
            var descLower = operation.Description?.ToLowerInvariant() ?? "";
            var combinedText = $"{partLower} {descLower} {opLower}";

            foreach (var triggerKvp in _adasData.CalibrationTriggers)
            {
                var trigger = triggerKvp.Value;

                // Check if operation matches this trigger
                bool matches = trigger.TriggerKeywords.Any(kw => combinedText.Contains(kw.ToLower()));

                if (matches)
                {
                    foreach (var sensorType in trigger.AffectedSensors)
                    {
                        var sensorInfo = GetSensorInfo(sensorType);

                        var calibration = new ADASCalibration
                        {
                            SensorType = sensorType,
                            SensorName = sensorInfo?.Name ?? sensorType,
                            CalibrationRequired = trigger.CalibrationRequired?.ToString() ?? "true",
                            CalibrationType = trigger.CalibrationType,
                            Explanation = trigger.Explanation,
                            OEMNotes = trigger.OemNotes,
                            TriggerOperation = $"{operation.OperationType} {operation.PartName}",
                            EstimatedCost = trigger.TypicalCost,
                            EstimatedTime = trigger.TypicalTime,
                            SensorLocation = sensorInfo?.Location ?? ""
                        };

                        calibrations.Add(calibration);
                    }
                }
            }

            return calibrations;
        }

        /// <summary>
        /// Get sensor information by type
        /// </summary>
        private SensorTypeInfo? GetSensorInfo(string sensorType)
        {
            if (_adasData?.SensorTypes == null) return null;

            return _adasData.SensorTypes.TryGetValue(sensorType, out var info) ? info : null;
        }

        /// <summary>
        /// Get vehicle-specific ADAS notes
        /// </summary>
        private VehicleADASNotes? GetVehicleSpecificNotes(string make)
        {
            if (_adasData?.VehicleSpecificNotes == null) return null;

            var makeLower = make.ToLowerInvariant();

            // Try direct match first
            foreach (var kvp in _adasData.VehicleSpecificNotes)
            {
                if (makeLower.Contains(kvp.Key.ToLower()) || kvp.Key.ToLower().Contains(makeLower))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Check for painting considerations in radar zones
        /// </summary>
        private List<string> CheckPaintingConsiderations(List<EstimateOperation> operations)
        {
            var considerations = new List<string>();

            // Check for bumper refinish with radar zone concerns
            var bumperRefinish = operations.Any(op =>
            {
                var combined = $"{op.PartName} {op.OperationType}".ToLowerInvariant();
                return (combined.Contains("bumper") || combined.Contains("fascia")) &&
                       (combined.Contains("refinish") || combined.Contains("paint") || combined.Contains("rfn"));
            });

            if (bumperRefinish && _adasData?.PaintingConsiderations?.RadarZones != null)
            {
                var radar = _adasData.PaintingConsiderations.RadarZones;
                considerations.Add($"RADAR ZONE WARNING: {radar.Requirement}");
                considerations.Add($"Max film thickness: {radar.MaxFilmThickness}");
                considerations.Add($"Masking: {radar.Masking}");
                if (!string.IsNullOrEmpty(radar.DegReference))
                {
                    considerations.Add($"Reference: {radar.DegReference}");
                }
            }

            return considerations;
        }

        /// <summary>
        /// Deduplicate calibrations by sensor type
        /// </summary>
        private List<ADASCalibration> DeduplicateCalibrations(List<ADASCalibration> calibrations)
        {
            return calibrations
                .GroupBy(c => c.SensorType)
                .Select(g =>
                {
                    // If multiple triggers for same sensor, combine info
                    var first = g.First();
                    if (g.Count() > 1)
                    {
                        first.TriggerOperation = string.Join("; ", g.Select(c => c.TriggerOperation).Distinct());
                    }
                    return first;
                })
                .ToList();
        }

        /// <summary>
        /// Quick check - does this estimate need any ADAS calibrations?
        /// </summary>
        public bool RequiresADASCalibration(List<EstimateOperation> operations)
        {
            var result = AnalyzeForADAS(operations);
            return result.RequiredCalibrations.Any();
        }

        /// <summary>
        /// Get a list of all sensor types and their common names
        /// </summary>
        public List<SensorTypeInfo> GetAllSensorTypes()
        {
            if (_adasData?.SensorTypes == null) return new List<SensorTypeInfo>();

            return _adasData.SensorTypes.Values.ToList();
        }

        /// <summary>
        /// Get calibration type descriptions
        /// </summary>
        public CalibrationTypeInfo? GetCalibrationTypeInfo(string calibrationType)
        {
            if (_adasData?.CalibrationTypes == null) return null;

            return _adasData.CalibrationTypes.TryGetValue(calibrationType, out var info) ? info : null;
        }
    }

    #region ADAS Data Models

    public class ADASRequirementsData
    {
        public string Version { get; set; } = "";
        public string LastUpdated { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, SensorTypeInfo> SensorTypes { get; set; } = new();
        public Dictionary<string, CalibrationTrigger> CalibrationTriggers { get; set; } = new();
        public PaintingConsiderationsData? PaintingConsiderations { get; set; }
        public Dictionary<string, VehicleADASNotes> VehicleSpecificNotes { get; set; } = new();
        public Dictionary<string, CalibrationTypeInfo> CalibrationTypes { get; set; } = new();
    }

    public class SensorTypeInfo
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string[] CommonNames { get; set; } = Array.Empty<string>();
        public string CalibrationType { get; set; } = "";
    }

    public class CalibrationTrigger
    {
        public string Operation { get; set; } = "";
        public string[] TriggerKeywords { get; set; } = Array.Empty<string>();
        public string[] AffectedSensors { get; set; } = Array.Empty<string>();
        public object? CalibrationRequired { get; set; }
        public string CalibrationType { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string? OemNotes { get; set; }
        public decimal TypicalCost { get; set; }
        public decimal TypicalTime { get; set; }
    }

    public class PaintingConsiderationsData
    {
        public RadarZoneInfo? RadarZones { get; set; }
        public CameraZoneInfo? CameraZones { get; set; }
    }

    public class RadarZoneInfo
    {
        public string Description { get; set; } = "";
        public string Requirement { get; set; } = "";
        public string MaxFilmThickness { get; set; } = "";
        public string Masking { get; set; } = "";
        public string? DegReference { get; set; }
    }

    public class CameraZoneInfo
    {
        public string Description { get; set; } = "";
        public string Requirement { get; set; } = "";
    }

    public class VehicleADASNotes
    {
        public string Notes { get; set; } = "";
        public string[] SpecialRequirements { get; set; } = Array.Empty<string>();
    }

    public class CalibrationTypeInfo
    {
        public string Description { get; set; } = "";
        public string[] Requirements { get; set; } = Array.Empty<string>();
        public string TypicalTime { get; set; } = "";
    }

    public class VehicleInfo
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }
        public List<string> EquippedSensors { get; set; } = new();
    }

    // Result models
    public class ADASAnalysisResult
    {
        public List<ADASCalibration> RequiredCalibrations { get; set; } = new();
        public int CalibrationsRequired { get; set; }
        public decimal TotalCalibrationCost { get; set; }
        public decimal TotalCalibrationTime { get; set; }
        public VehicleADASNotes? VehicleSpecificNotes { get; set; }
        public List<string> PaintingConsiderations { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class ADASCalibration
    {
        public string SensorType { get; set; } = "";
        public string SensorName { get; set; } = "";
        public string SensorLocation { get; set; } = "";
        public string CalibrationRequired { get; set; } = "";
        public string CalibrationType { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string? OEMNotes { get; set; }
        public string TriggerOperation { get; set; } = "";
        public decimal EstimatedCost { get; set; }
        public decimal EstimatedTime { get; set; }
    }

    #endregion
}
