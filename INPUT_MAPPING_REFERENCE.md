# Complete Input Mapping Reference
**Generated from: Unlocked Mcstud Estimating Tool Master.xlsx**

This document provides a complete reference of all 290 input controls found in your Excel workbook.

---

## Sheet-by-Sheet Breakdown

### 1. SOP List (17 Inputs)

| Mapping Key | Excel Cell | Control Type | Options | Current Value | Purpose |
|-------------|-----------|--------------|---------|---------------|---------|
| `SOPList_A35` | A35 | Dropdown | Gas, Hybrid, EV | Gas | Vehicle fuel type |
| `SOPList_C29` | C29 | Toggle | Yes, No | Yes | ADAS system present |
| `SOPList_A29` | A29 | Dropdown | Single, Dual | Single | Battery configuration |
| `SOPList_A79` | A79 | Dropdown | Dollar Amount, Labor Unit, Tesla | Dollar Amount | Labor rate type |
| `SOPList_A31` | A31 | Toggle | Yes, No | No | Test battery condition |
| `SOPList_B83` | B83 | Toggle | Yes, No | Yes | Unknown (check Excel) |
| `SOPList_A87` | A87 | Toggle | Yes, No | No | Unknown |
| `SOPList_B87` | B87 | Toggle | Yes, No | No | Unknown |
| `SOPList_A81` | A81 | Toggle | Yes, No | Yes | Unknown |
| `SOPList_A129` | A129 | Toggle | Yes, No | No | Unknown |
| `SOPList_B81` | B81 | Toggle | Yes, No | Yes | Unknown |
| `SOPList_B85` | B85 | Toggle | Yes, No | No | Unknown |
| `SOPList_A33` | A33 | Toggle | Yes, No | No | Battery support required |
| `SOPList_C129` | C129 | Toggle | Yes, No | (empty) | Unknown |
| `SOPList_D129` | D129 | Toggle | Yes, No | (empty) | Unknown |
| `SOPList_A133` | A133 | Toggle | Yes, No | No | Unknown |
| `SOPList_B79` | B79 | Toggle | Yes, No | Yes | Unknown |

**Usage Example**:
```csharp
// Set battery type to dual
_excelEngine.SetInput("SOPList_A29", "Dual");

// Enable ADAS
_excelEngine.SetInput("SOPList_C29", "Yes");

// Set vehicle to EV
_excelEngine.SetInput("SOPList_A35", "EV");
```

---

### 2. Part Operations (161 Inputs)

This is the most complex sheet with 161 dropdown controls. They follow a repeating pattern for multiple part entries.

**Sample Inputs**:

| Mapping Key | Excel Cell | Options | Current Value | Pattern |
|-------------|-----------|---------|---------------|---------|
| `PartOp_A33` | A33 | First Panel, Additional Panel | Additional Panel | Panel pricing tier #1 |
| `PartOp_A83` | A83 | First Panel, Additional Panel | First Panel | Panel pricing tier #2 |
| `PartOp_A183` | A183 | First Panel, Additional Panel | Additional Panel | Panel pricing tier #3 |

**Pattern Analysis**:
- The sheet appears to have ~50 rows, each with 3-4 input cells
- Cells in column A repeat "First Panel, Additional Panel" options
- This suggests multiple part entry rows with similar controls

**Recommendation**: Group these in UI using:
1. **Expander controls** (collapsible sections)
2. **Data grid** with dropdowns in cells
3. **Repeating form pattern** with "Add Part" button

**Usage Example**:
```csharp
// Set first part as "First Panel" (higher rate)
_excelEngine.SetInput("PartOp_A33", "First Panel");

// Set second part as "Additional Panel" (lower rate)
_excelEngine.SetInput("PartOp_A83", "Additional Panel");
```

---

### 3. Cover Car Operations (8 Inputs)

| Mapping Key | Excel Cell | Options | Current Value |
|-------------|-----------|---------|---------------|
| `CoverCarOp_C32` | C32 | Yes, No | No |
| `CoverCarOp_A29` | A29 | Gas, EV | Gas |
| `CoverCarOp_B29` | B29 | Front | Front |
| `CoverCarOp_D29` | D29 | Yes, No | No |
| *(4 more inputs)* | ... | ... | ... |

**Usage Example**:
```csharp
_excelEngine.SetInput("CoverCarOp_A29", "EV");
_excelEngine.SetInput("CoverCarOp_B29", "Front");
```

---

### 4. Body Operations (25 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| `BodyOp_A79` | A79 | Yes, No | Toggle control |
| *(24 more inputs)* | ... | ... | ... |

**Usage Example**:
```csharp
_excelEngine.SetInput("BodyOp_A79", "Yes");
```

---

### 5. Refinish Operations (11 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(11 inputs)* | ... | ... | Toggle/dropdown |

---

### 6. Mechanical Operations (34 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(34 inputs)* | ... | ... | Toggle/dropdown |

---

### 7. SRS Operations (9 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(9 inputs)* | ... | ... | Toggle/dropdown |

---

### 8. Total Loss Charges (10 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(10 inputs)* | ... | ... | Toggle/dropdown |

---

### 9. Body On Frame (4 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(4 inputs)* | ... | ... | Toggle/dropdown |

---

### 10. Stolen Recovery (11 Inputs)

| Mapping Key | Excel Cell | Options | Pattern |
|-------------|-----------|---------|---------|
| *(11 inputs)* | ... | ... | Toggle/dropdown |

---

## How to Use This Reference

### 1. In ViewModel Code

```csharp
// Using the mapping key from the reference
public partial class SOPListViewModel : ObservableObject
{
    [ObservableProperty]
    private string _batteryType = "Single";

    partial void OnBatteryTypeChanged(string value)
    {
        // Use the mapping key: SOPList_A29
        _excelEngine.SetInput("SOPList_A29", value);
        _excelEngine.Calculate();
    }
}
```

### 2. In XAML UI

```xml
<!-- Dropdown for SOPList_A29 -->
<ComboBox Header="Battery Type"
          ItemsSource="{x:Bind BatteryTypeOptions}"
          SelectedItem="{x:Bind ViewModel.BatteryType, Mode=TwoWay}"/>
```

Where `BatteryTypeOptions` is defined as:
```csharp
public string[] BatteryTypeOptions { get; } = { "Single", "Dual" };
```

### 3. Validation

Use the `ExcelMappings.InputControls` dictionary to validate user input:

```csharp
public bool IsValidInput(string mappingKey, string value)
{
    if (!ExcelMappings.InputControls.TryGetValue(mappingKey, out var mapping))
        return false;

    return mapping.Options.Contains(value);
}
```

---

## Complete List Access

The complete mapping of all 290 inputs is available in:

**File**: `Services/ExcelMappings.cs`

**Structure**:
```csharp
public static readonly Dictionary<string, InputMapping> InputControls = new()
{
    ["SOPList_A29"] = new InputMapping(
        Sheet: "SOP List",
        Cell: "A29",
        Options: new[] { "Single", "Dual" },
        DefaultValue: "Single"
    ),
    // ... 289 more entries
};
```

---

## Finding Input Purposes

Many inputs have generic "Yes/No" options. To understand their purpose:

1. **Open your Excel file**: `Unlocked Mcstud Estimating Tool Master.xlsx`
2. **Navigate to the sheet**: e.g., "SOP List"
3. **Look at the label cell** to the left or above the input cell
4. **Document the purpose** in your ViewModel comments

**Example**:
```csharp
// Cell A29 in Excel has label "Battery Configuration" in cell A28
[ObservableProperty]
private string _batteryType = "Single"; // SOPList_A29: Battery configuration
```

---

## UI Design Patterns

### Pattern 1: Simple Dropdown (For 2-5 Options)

```xml
<ComboBox Header="Battery Type"
          ItemsSource="{x:Bind BatteryOptions}"
          SelectedItem="{x:Bind ViewModel.BatteryType, Mode=TwoWay}"/>
```

### Pattern 2: Toggle Switch (For Yes/No)

```xml
<ToggleSwitch Header="ADAS Enabled"
              IsOn="{x:Bind ViewModel.AdasEnabled, Mode=TwoWay}"/>
```

### Pattern 3: Grouped Inputs (For Related Controls)

```xml
<Expander Header="Battery Configuration" IsExpanded="True">
    <StackPanel Spacing="12">
        <ComboBox Header="Battery Type" ... />
        <ToggleSwitch Header="Test Battery" ... />
        <ToggleSwitch Header="Battery Support" ... />
    </StackPanel>
</Expander>
```

### Pattern 4: Repeating Inputs (For Part Operations)

```xml
<ListView ItemsSource="{x:Bind ViewModel.Parts}">
    <ListView.ItemTemplate>
        <DataTemplate>
            <Grid ColumnDefinitions="*,*,*">
                <ComboBox Grid.Column="0" ... />
                <ComboBox Grid.Column="1" ... />
                <TextBox Grid.Column="2" ... />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

---

## Summary Statistics

| Sheet | Input Count | Percentage |
|-------|-------------|------------|
| Part Operations | 161 | 55.5% |
| Mechanical Operations | 34 | 11.7% |
| Body Operations | 25 | 8.6% |
| SOP List | 17 | 5.9% |
| Refinish Operations | 11 | 3.8% |
| Stolen Recovery | 11 | 3.8% |
| Total Loss Charges | 10 | 3.4% |
| SRS Operations | 9 | 3.1% |
| Cover Car Operations | 8 | 2.8% |
| Body On Frame | 4 | 1.4% |
| **Total** | **290** | **100%** |

**Key Insight**: Part Operations sheet contains over half of all inputs. Prioritize this sheet's UI design.

---

## Next Steps

1. **Review your Excel file** to document the purpose of each input (especially the generic Yes/No toggles)
2. **Group related inputs** in the UI using Expander controls
3. **Start with SOP List** (17 inputs, simple) to validate the approach
4. **Tackle Part Operations** (161 inputs) using a data grid or repeating template
5. **Complete remaining sheets** following the same pattern

---

**For complete implementation details, see `EXCEL_INTEGRATION_PLAN.md`**
