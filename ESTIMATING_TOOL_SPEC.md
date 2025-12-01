# McStud Estimating Tool - Excel-Like View Specification

## Overview
Create a spreadsheet-like interface that matches the master.xlsx structure exactly.

## Layout Structure

### SOP List Tab Example (from Excel Row 27-31)

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ HEADER ROW (Row 27)                                                                  │
│ Summary: 📊 X Ops | 💲 $XXX | 🛠 X.X Labor | 🎨 X.X Refinish                       │
└─────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────────────┐
│ COLUMN HEADERS (Row 28)                                                              │
│ A: 12V Battery | C: ADAS | M: Operation | O: Description | Q: Qty | R: Price |      │
│ V: Labor | W: Category | X: Refinish                                                 │
└──────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ROW 29 - Battery Disconnect                                                          │
│ [A29: Dropdown ▼ Single/Dual] [C29: Dropdown ▼ Yes/No] → OUTPUT CELLS →            │
│ M29: Rpr | O29: "Disconnect and Reconnect Battery" | Q29: 1 | R29: 0 | V29: 0.4 |  │
│ W29: M | X29: 0                                                                      │
└──────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ROW 30 - Test Battery Header                                                         │
│ [A30: Label "▼Test Battery"]                                                         │
└──────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ROW 31 - Test Battery Operation                                                      │
│ [A31: Dropdown ▼ Yes/No] → OUTPUT CELLS →                                           │
│ M31: Rpr | O31: "Test Battery Condition" | Q31: 1 | R31: 0 | V31: 0.2 | W31: M |   │
│ X31: 0                                                                                │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

## Key Requirements

### 1. Grid Layout
- Use WinUI **DataGrid** or custom **Grid** control
- Each ROW represents one operation or input section
- Each COLUMN represents a cell (A, B, C... through X)

### 2. Input Cells (Colored in Excel)
- **Column A**: Primary inputs (dropdowns, text)
- **Column B**: Secondary inputs
- **Column C**: ADAS and other boolean inputs
- **Column D-F**: Additional inputs
- **Background**: Light blue/yellow tint (matching Excel)
- **Editable**: User can type or select from dropdown

### 3. Output Cells (Formulas in Excel)
- **Columns G-X**: Calculated values
- **Background**: White/light gray
- **Read-only**: User cannot edit
- **Auto-update**: When input cells change, output cells recalculate

### 4. Cell Types

#### Input Cell Types:
1. **Dropdown (Data Validation)**
   - Example: A29 = Single/Dual
   - Implementation: ComboBox in cell

2. **Text Input**
   - Example: B26 = Part name
   - Implementation: TextBox in cell

3. **Label/Header**
   - Example: A30 = "▼Test Battery"
   - Implementation: TextBlock (read-only, bold)

#### Output Cell Types:
1. **Text**
   - Example: O29 = "Disconnect and Reconnect Battery"
   - Implementation: TextBlock (auto-calculated from formula)

2. **Number**
   - Example: V29 = 0.4
   - Implementation: TextBlock showing decimal

3. **Formula Result**
   - Example: O29 = IF(A29="Single", "Disconnect...", IF(A29="Dual", "Disconnect 2x...", ""))
   - Implementation: C# logic that updates when inputs change

## Implementation Plan

### Phase 1: Create Grid Structure
```csharp
public class SOPListGrid : Grid
{
    // Define columns: A through X (24 columns)
    // Define rows: 27-170 (143 rows for SOP List)

    // Column widths:
    // A-F: 100-150px (input columns)
    // G-L: 40px (spacer columns)
    // M-N: 60px (operation type)
    // O: 300px (description - wide)
    // P: 40px (spacer)
    // Q: 60px (quantity)
    // R: 80px (price)
    // S-U: 40px (spacers)
    // V: 80px (labor hours)
    // W: 60px (category)
    // X: 80px (refinish hours)
}
```

### Phase 2: Create Cell Controls
```csharp
// Input cell with dropdown
var cellA29 = new ComboBox
{
    Items = { "Single", "Dual" },
    Background = LightBlueBrush,
    BorderBrush = GrayBrush
};
Grid.SetRow(cellA29, 29);
Grid.SetColumn(cellA29, 0); // Column A

// Output cell with formula
var cellO29 = new TextBlock
{
    Background = WhiteBrush,
    Padding = new Thickness(5)
};
// Bind to formula: IF(A29="Single", "Disconnect...", ...)
cellA29.SelectionChanged += (s, e) => {
    cellO29.Text = cellA29.SelectedItem == "Single"
        ? "Disconnect and Reconnect Battery"
        : "Disconnect and Reconnect 2x Battery";
    cellV29.Text = cellA29.SelectedItem == "Single" ? "0.4" : "0.8";
};
Grid.SetRow(cellO29, 29);
Grid.SetColumn(cellO29, 14); // Column O
```

### Phase 3: Implement Formula Engine
```csharp
public class FormulaEngine
{
    public string Evaluate(string formula, Dictionary<string, object> cells)
    {
        // Parse Excel-like formula: =IF(A29="Single", "value1", "value2")
        // Return calculated result
    }
}
```

## Visual Design

### Colors (matching Excel):
- **Input cells**: `Color.FromArgb(255, 220, 235, 255)` (light blue)
- **Output cells**: `Color.FromArgb(255, 255, 255, 255)` (white)
- **Header cells**: `Color.FromArgb(255, 200, 200, 200)` (gray)
- **Borders**: 1px gray (#CCCCCC)

### Fonts:
- **Regular cells**: Segoe UI, 12px
- **Headers**: Segoe UI Semibold, 12px
- **Bold labels**: Segoe UI Bold, 12px

## Data Flow

```
User changes A29 from "Single" to "Dual"
    ↓
SelectionChanged event fires
    ↓
Update all dependent output cells:
    - O29: "Disconnect and Reconnect 2x Battery"
    - V29: 0.8
    - Q29: 1 (stays same)
    - R29: 0 (stays same)
    ↓
Update summary row:
    - Total operations count
    - Total price
    - Total labor hours
    - Total refinish hours
```

## Next Steps

1. ✅ Document Excel structure
2. ⏳ Create `SOPListGridView.cs` - custom Grid control with Excel-like cells
3. ⏳ Implement cell controls (ComboBox, TextBox, TextBlock)
4. ⏳ Implement formula logic for output cells
5. ⏳ Wire up change events
6. ⏳ Add summary calculations
7. ⏳ Apply styling to match Excel
8. ⏳ Repeat for all other tabs (Part Operations, Cover Car, etc.)

## File Structure

```
Views/
  ├── EstimatingToolView.cs          (main container with tabs)
  ├── Grids/
  │   ├── SOPListGrid.cs             (SOP List tab grid)
  │   ├── PartOperationsGrid.cs      (Part Operations tab grid)
  │   ├── CoverCarGrid.cs            (Cover Car tab grid)
  │   └── ... (other grids)
  └── Cells/
      ├── InputCell.cs               (base input cell)
      ├── DropdownCell.cs            (dropdown input cell)
      ├── TextInputCell.cs           (text input cell)
      ├── OutputCell.cs              (formula output cell)
      └── FormulaEngine.cs           (formula evaluation)
```
