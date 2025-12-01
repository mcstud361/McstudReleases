# SOP List Tab - Complete Implementation Specification

## Overview
Build the complete SOP List tab with all 171 rows, matching master.xlsx exactly.

## Sections
1. **Electrical** (Rows 27-75)
2. **Vehicle Diagnostics** (Rows 76-90+)
3. **Misc** (Remaining rows)

## Input Cells (All in Column A unless noted)

### Electrical Section
- **A29**: Battery Type (Dropdown: "", "Single", "Dual")
- **A31**: Test Battery (Dropdown: "", "Yes", "No")
- **A33**: Battery Support (Dropdown: "", "Yes", "No")
- **A35**: Vehicle Type (Dropdown: "", "Gas", "Hybrid", "EV")
- **C29**: ADAS (Dropdown: "", "Yes", "No")

### Vehicle Diagnostics Section
- **A79**: Scan Tool Type (Dropdown: "", "Gas", "Rivian", "Tesla")
- **A81**: Setup Scan Tool (Dropdown: "", "Yes", "No")
- **A83**: Custom Price (TextBox: numeric)
- **A85**: Custom Labor (TextBox: numeric)
- **A87**: Gateway Unlock (Dropdown: "", "Yes", "No")
- **B79**: ADAS Diagnostic (Dropdown: "", "Yes", "No")
- **B81**: Simulate Fluids (Dropdown: "", "Yes", "No")
- **B83**: Check Tire Pressure (Dropdown: "", "Yes", "No")
- **B85**: Remove Belongings (Dropdown: "", "Yes", "No")
- **B87**: Drive Cycle (Dropdown: "", "Yes", "No")

## Operations to Implement

### Row 29 - Battery Disconnect
- **Input**: A29 (Single/Dual)
- **Outputs**:
  - M29: "Rpr"
  - O29: IF(A29="Single", "Disconnect and Reconnect Battery", IF(A29="Dual", "Disconnect and Reconnect 2x Battery", ""))
  - Q29: 1
  - R29: 0
  - V29: IF(A29="Single", 0.4, IF(A29="Dual", 0.8, ""))
  - W29: "M"
  - X29: 0

### Row 30 - Header
- **Static**: "▼ Test Battery"

### Row 31 - Test Battery
- **Input**: A31 (Yes/No)
- **Outputs**:
  - M31: IF(A31="Yes", "Rpr", "")
  - O31: IF(A31="Yes", "Test Battery Condition", "")
  - Q31: IF(A31="Yes", 1, "")
  - V31: IF(A31="Yes", 0.2, "")
  - W31: IF(A31="Yes", "M", "")

### Row 32 - Electronic Reset (Static when Test Battery = Yes)
- **Static Outputs** (shown when A31="Yes"):
  - M32: "Rpr"
  - O32: "Electronic Reset"
  - Q32: 1
  - V32: 0.5
  - W32: "M"

### Row 33 - Cover and Protect Electrical
- **Static Outputs** (shown when A31="Yes"):
  - M33: "Rpr"
  - O33: "Cover and Protect Electrical Connections"
  - Q33: 1
  - R33: 5
  - V33: 0.3
  - W33: "M"

### Row 34 - Header
- **Static**: "▼ Battery Support"

### Row 35 - Battery Support
- **Input**: A33 (Yes/No)
- **Outputs**:
  - M35: IF(A33="Yes", "Rpr", "")
  - O35: IF(A33="Yes", "Battery Support", "")
  - Q35: IF(A33="Yes", 1, "")
  - V35: IF(A33="Yes", 0.2, "")
  - W35: IF(A33="Yes", "M", "")

### Row 36 - Header
- **Static**: "▼ Vehicle Type"

### Row 37 - Vehicle Type Input
- **Input**: A35 (Gas/Hybrid/EV)

### Row 38 - Charge and Maintain Battery
- **Inputs**: A35, C29
- **Outputs**:
  - M38: IF(OR(A35="EV", AND(C29="Yes", OR(A35="Gas", A35="Hybrid"))), "Rpr", "")
  - O38: IF(A35="EV", "Charge and Maintain Battery", IF(AND(C29="Yes", OR(A35="Gas", A35="Hybrid")), "Charge and Maintain Battery during ADAS", ""))
  - Q38: IF(OR(A35="EV", AND(C29="Yes", OR(A35="Gas", A35="Hybrid"))), 1, "")
  - V38: IF(OR(A35="EV", AND(C29="Yes", OR(A35="Gas", A35="Hybrid"))), 0.6, "")
  - W38: IF(OR(A35="EV", AND(C29="Yes", OR(A35="Gas", A35="Hybrid"))), "M", "")

### Row 39 - Mobile Cart
- **Input**: A35
- **Outputs**:
  - M39: IF(OR(A35="EV", A35="Hybrid"), "Rpr", "")
  - O39: IF(A35="Hybrid", "Mobile Cart for Hybrid", IF(A35="EV", "Mobile Cart for EV", ""))
  - Q39: IF(OR(A35="EV", A35="Hybrid"), 1, "")
  - R39: IF(OR(A35="EV", A35="Hybrid"), 50, "")
  - V39: IF(OR(A35="EV", A35="Hybrid"), 0.5, "")
  - W39: IF(OR(A35="EV", A35="Hybrid"), "M", "")

### Row 40 - Verify No High Voltage
- **Input**: A35
- **Outputs**:
  - M40: IF(OR(A35="EV", A35="Hybrid"), "Rpr", "")
  - O40: IF(OR(A35="EV", A35="Hybrid"), "Verify No High Voltage Present", "")
  - Q40: IF(OR(A35="EV", A35="Hybrid"), 1, "")
  - V40: IF(OR(A35="EV", A35="Hybrid"), 0.2, "")
  - W40: IF(OR(A35="EV", A35="Hybrid"), "M", "")

### Row 41 - Service Mode
- **Input**: A35
- **Outputs**:
  - M41: IF(OR(A35="EV", A35="Hybrid"), "Rpr", "")
  - O41: IF(OR(A35="EV", A35="Hybrid"), "Activate and Deactivate Service Mode", "")
  - Q41: IF(OR(A35="EV", A35="Hybrid"), 1, "")
  - V41: IF(OR(A35="EV", A35="Hybrid"), 0.3, "")
  - W41: IF(OR(A35="EV", A35="Hybrid"), "M", "")

## Implementation Strategy

Given the 171 rows with complex formulas, I'll:

1. Create helper methods for each operation type
2. Store references to all input controls
3. Wire up change events to update outputs
4. Implement the exact formula logic from Excel
5. Build incrementally and test each section

## Next Steps

1. Extend CreateSOPListGrid() to include ALL rows
2. Create input controls for ALL input cells
3. Implement ALL formula logic
4. Wire up section navigation
5. Test thoroughly

This will be approximately 50-60 operations with inputs and calculated outputs.
