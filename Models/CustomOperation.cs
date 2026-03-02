using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace McStudDesktop.Models;

/// <summary>
/// Represents a custom operation that can be added by shops/users
/// </summary>
public class CustomOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The operation description/name
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category this operation belongs to (e.g., "PlasticPartReplace", "BodyOperations", "Refinish")
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Sub-category within the main category (e.g., "Measurements", "AC", "Cooling")
    /// </summary>
    public string SubCategory { get; set; } = string.Empty;

    /// <summary>
    /// How this operation's labor is calculated
    /// </summary>
    public CalculationType CalculationType { get; set; } = CalculationType.FixedHours;

    /// <summary>
    /// For FixedHours: the fixed number of hours
    /// For percentage types: the percentage value (e.g., 30 for 30%)
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Optional material cost for this operation
    /// </summary>
    public decimal MaterialCost { get; set; }

    /// <summary>
    /// Whether this operation is enabled by default when the category is selected
    /// </summary>
    public bool EnabledByDefault { get; set; } = true;

    /// <summary>
    /// Operation type code for export (e.g., "Mat", "Rpr", "R&I", "Refinish")
    /// </summary>
    public string OperationTypeCode { get; set; } = "Mat";

    /// <summary>
    /// Optional notes/justification for this operation
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Order/position within the category
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// When this custom operation was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// When this custom operation was last modified
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Calculate the actual labor hours based on the calculation type and input values
    /// </summary>
    public decimal CalculateHours(decimal repairHours = 0, decimal refinishHours = 0, decimal riHours = 0, decimal totalHours = 0)
    {
        return CalculationType switch
        {
            CalculationType.FixedHours => Value,
            CalculationType.PercentOfRepairTime => Math.Round(repairHours * (Value / 100m), 1),
            CalculationType.PercentOfRefinishTime => Math.Round(refinishHours * (Value / 100m), 1),
            CalculationType.PercentOfRITime => Math.Round(riHours * (Value / 100m), 1),
            CalculationType.PercentOfTotalTime => Math.Round(totalHours * (Value / 100m), 1),
            _ => Value
        };
    }

    /// <summary>
    /// Get a display string for the calculation method
    /// </summary>
    public string GetCalculationDisplay()
    {
        return CalculationType switch
        {
            CalculationType.FixedHours => $"{Value} hrs",
            CalculationType.PercentOfRepairTime => $"{Value}% of Repair",
            CalculationType.PercentOfRefinishTime => $"{Value}% of Refinish",
            CalculationType.PercentOfRITime => $"{Value}% of R&I",
            CalculationType.PercentOfTotalTime => $"{Value}% of Total",
            _ => Value.ToString()
        };
    }
}

/// <summary>
/// How an operation's labor time is calculated
/// </summary>
public enum CalculationType
{
    /// <summary>Fixed number of hours</summary>
    FixedHours,

    /// <summary>Percentage of body/repair time</summary>
    PercentOfRepairTime,

    /// <summary>Percentage of paint/refinish time</summary>
    PercentOfRefinishTime,

    /// <summary>Percentage of R&I time</summary>
    PercentOfRITime,

    /// <summary>Percentage of total estimate time</summary>
    PercentOfTotalTime
}

/// <summary>
/// A profile/template containing multiple custom operations for a category
/// </summary>
public class CustomOperationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for this profile (e.g., "Plastic Part Replace - Premium")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The category this profile belongs to
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The operations in this profile
    /// </summary>
    public List<CustomOperation> Operations { get; set; } = new();

    /// <summary>
    /// Whether this is the default profile for the category
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Root container for all custom operations data
/// </summary>
public class CustomOperationsData
{
    /// <summary>
    /// All custom operation profiles organized by category
    /// </summary>
    public Dictionary<string, List<CustomOperationProfile>> ProfilesByCategory { get; set; } = new();

    /// <summary>
    /// Standalone custom operations not in a profile (per category)
    /// </summary>
    public Dictionary<string, List<CustomOperation>> OperationsByCategory { get; set; } = new();

    /// <summary>
    /// Built-in operations that have been hidden/disabled by the user
    /// </summary>
    public List<string> HiddenBuiltInOperations { get; set; } = new();

    /// <summary>
    /// Overrides for built-in operation descriptions (original description -> custom description)
    /// </summary>
    public Dictionary<string, string> DescriptionOverrides { get; set; } = new();

    /// <summary>
    /// Shop name for this configuration
    /// </summary>
    public string ShopName { get; set; } = string.Empty;

    public DateTime LastModified { get; set; } = DateTime.Now;
}
