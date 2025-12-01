# Master.xlsx Logic & Design Documentation

## Document Overview

This document provides a comprehensive analysis of the **master.xlsx** spreadsheet, detailing all UI elements, formulas, logic flows, and interconnections between components. The spreadsheet serves as a comprehensive auto body shop estimating tool for McStud Estimating Tool (MET).

**Last Updated**: January 2025
**Total Sheets**: 12
**Total Formulas**: 5,505
**Total Data Cells**: 7,434

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Sheet Architecture](#sheet-architecture)
3. [Data Flow & Dependencies](#data-flow--dependencies)
4. [UI Input Elements](#ui-input-elements)
5. [Formula Logic Patterns](#formula-logic-patterns)
6. [Calculation Engine](#calculation-engine)
7. [Sheet-by-Sheet Analysis](#sheet-by-sheet-analysis)
8. [Integration Points](#integration-points)

---

## 1. System Overview

### Purpose

The master.xlsx spreadsheet is an **Auto Body Shop Estimating Tool** that calculates costs, labor hours, and materials for vehicle collision repair operations. It covers:

- **Standard Operating Procedures (SOPs)**: Electrical, diagnostics, and misc operations
- **Part Operations**: Blend, repair, and replacement procedures for body parts
- **Specialized Operations**: Cover car, body work, refinish, mechanical, SRS (airbag safety)
- **Special Scenarios**: Total loss charges, body-on-frame repairs, stolen recovery, post-repair inspections

### Key Features

1. **Conditional Logic**: Operations appear/disappear based on user selections
2. **Dynamic Calculations**: Real-time price, labor, and refinish hour calculations
3. **Navigation System**: Hyperlinked home page menu for quick access to all sections
4. **Aggregation Summaries**: Each section totals operations, prices, labor, and refinish hours
5. **Multi-scenario Support**: Handles gas, hybrid, EV, Tesla, Rivian vehicles with unique workflows

### User Workflow

```
Home Page (Navigation)
    ↓
Select Operation Category (12 tabs)
    ↓
Configure User Inputs (dropdowns, yes/no, text fields)
    ↓
System Generates Operations (conditional formulas)
    ↓
View Summary (total ops, price, labor, refinish)
    ↓
Export/Use for Estimating
```

---

## 2. Sheet Architecture

### Sheet Summary

| Sheet Name | Dimensions | Data Cells | Formulas | Purpose |
|-----------|------------|------------|----------|---------|
| **Home Page** | 174×25 | 48 | 2 | Navigation hub with hyperlinks to all sections |
| **SOP List** | 171×25 | 1,007 | 679 | Standard operating procedures (electrical, diagnostics, misc) |
| **Part Operations** | 521×24 | 3,079 | 2,205 | Part-specific operations (blend, repair, replace) |
| **Cover Car Operations** | 99×24 | 170 | 105 | Masking and covering procedures |
| **Body Operations** | 121×25 | 612 | 544 | Body equipment and structural repair operations |
| **Refinish Operations** | 99×24 | 521 | 418 | Paint and refinish procedures |
| **Mechanical Operations** | 221×24 | 867 | 659 | AC, cooling, suspension, and mechanical work |
| **SRS Operations** | 122×25 | 238 | 192 | Safety restraint system (airbag) operations |
| **Total Loss Charges** | 99×24 | 374 | 330 | Admin fees, coordination, handling charges |
| **Body On Frame** | 121×25 | 163 | 114 | Body-on-frame specific operations |
| **Stolen Recovery** | 121×25 | 308 | 258 | Stolen vehicle recovery operations |
| **Post Repair Inspection** | - | 47 | 0 | Final inspection checklist |

### Common Sheet Structure

Most operational sheets follow this standard layout:

**Rows 1-25**: Header, navigation menu, user input section
**Rows 26+**: Operation rows with conditional formulas

**Column Layout** (operational sheets):
- **Column A-F**: User input fields (dropdowns, yes/no, vehicle type, etc.)
- **Column G-L**: Reserved/placeholder columns (often set to "0" when operation is active)
- **Column M**: Operation type (Rpr, Replace, R&I, Refinish, Blend)
- **Column N**: Placeholder/reserved
- **Column O**: Operation description (human-readable text)
- **Column P**: Additional placeholder
- **Column Q**: Quantity (typically "1")
- **Column R**: Price ($)
- **Column S-U**: Additional cost fields
- **Column V**: Labor hours
- **Column W**: Category (M=Mechanical, 0=None, etc.)
- **Column X**: Refinish hours

---

## 3. Data Flow & Dependencies

### Primary Input → Output Flow

```
USER INPUTS (Columns A-F)
    ↓
CONDITIONAL FORMULAS (Columns G-X)
    ↓
OPERATION ROW (Generated if conditions met)
    ↓
AGGREGATION FORMULAS (Summary row)
    ↓
DISPLAY SUMMARY (Ops count, total $, total labor, total refinish)
```

### Cross-Sheet Dependencies

1. **Home Page** ← Links to all other sheets (navigation only)
2. **SOP List** → Influences Part Operations (e.g., ADAS calibration triggers additional operations)
3. **Part Operations** ↔ **Refinish Operations** (parts needing refinish trigger calculations)
4. **Mechanical Operations** → Linked to vehicle type (EV, Hybrid, Gas)

### Key Input Variables

| Input Cell | Sheet | Description | Values | Impacts |
|-----------|-------|-------------|--------|---------|
| **A29** | SOP List | 12V Battery type | Single, Dual | Labor hours (0.4 vs 0.8) |
| **C29** | SOP List | ADAS system | Yes, No | Triggers ADAS operations |
| **A35** | SOP List | Vehicle fuel type | Gas, Hybrid, EV | EV/Hybrid-specific operations |
| **A79** | SOP List | Scan tool type | Gas, Rivian, Tesla | Pre-scan, post-scan pricing |
| **B33** | Part Operations | Adhesion promoter | Yes, No | Adds adhesion operation |
| **B35** | Part Operations | Part size | First Large, Additional Large, Additional Small | Refinish time (0.3, 0.3, 0.2 hrs) |
| **D29** | Cover Car | Labor type | Refinish Labor, $ and Body Labor | Determines operation category |
| **C32** | Cover Car | Two-tone paint | Yes, No | Affects cover car price/labor |
| **A29** | Refinish | Paint stage | 2-Stage, 3-Stage, 4-Stage | Color tint time (0.5, 1.0, 1.5 hrs) |
| **A29** | Mechanical | Refrigerant type | R134a, R1234yf, R744 | Pricing ($85, $485, $600) |

---

## 4. UI Input Elements

### Input Types

1. **Dropdown Menus** (Data Validation Lists)
   - Vehicle types: Gas, Hybrid, EV, Tesla, Rivian
   - Operation types: Single, Dual, Yes, No
   - Part sizes: First Large Part, Additional Large Part, Additional Small Part
   - Paint stages: 2-Stage, 3-Stage, 4-Stage
   - Refrigerant types: R134a, R1234yf, R744

2. **Yes/No Fields**
   - ADAS system present (C29 in SOP List)
   - Test battery (A31 in SOP List)
   - Adhesion promoter needed (B33 in Part Operations)
   - Two-tone paint (C32 in Cover Car)
   - Safety inspections (A29 in SRS Operations)

3. **Numeric Inputs**
   - Dollar amounts (custom fees, charges)
   - Quantities
   - Custom labor hours

4. **Text Inputs**
   - Custom operation descriptions
   - Notes and additional information

### Input Location Pattern

**Primary inputs** are typically located in:
- **Column A**: Main selection (Yes/No, vehicle type, operation trigger)
- **Column B-C**: Secondary selections (ADAS, part specifics)
- **Column D-E**: Tertiary options (labor type, additional details)

Example from **SOP List**:
- **A29**: Battery type (Single/Dual)
- **A31**: Test battery (Yes/No)
- **A33**: Battery support (Yes/No)
- **A35**: Vehicle type (Gas/Hybrid/EV)
- **C29**: ADAS system (Yes/No)

---

## 5. Formula Logic Patterns

### Pattern 1: Simple Conditional Operation

**Purpose**: Show/hide an operation based on a single Yes/No input

**Formula Pattern**:
```excel
=IF(A31="Yes", [VALUE], "")
```

**Example** (SOP List, Row 30 - Test Battery):
```excel
O30: =IF(A31="Yes", "Test Battery Condition", "")
V30: =IF(A31="Yes", "0.2", "")
Q30: =IF(A31="Yes", "1", "")
```

**Logic**: If A31="Yes", populate operation description, labor hours, and quantity. Otherwise, leave blank (operation hidden).

---

### Pattern 2: Multi-Condition OR Logic

**Purpose**: Show operation if ANY of multiple conditions are true

**Formula Pattern**:
```excel
=IF(OR(condition1, condition2), [VALUE], "")
```

**Example** (SOP List, Row 34 - Charge and Maintain Battery):
```excel
H34: =IF(OR(A35="EV", C29="Yes"), "0", "")
V34: =IF(OR(A35="EV", C29="Yes"), "0.6", "")
O34: =IF(A35="EV", "Charge and Maintain Battery",
         IF(C29="Yes",
            IF(OR(A35="Gas", A35="Hybrid"),
               "Charge and Maintain Battery during ADAS",
               ""),
            ""))
```

**Logic**:
- If vehicle is EV **OR** ADAS is enabled → show operation
- Description varies: EV gets "Charge and Maintain Battery", Gas/Hybrid with ADAS gets "...during ADAS"

---

### Pattern 3: Nested IF for Multiple Options

**Purpose**: Different values based on multiple distinct conditions

**Formula Pattern**:
```excel
=IF(condition1, value1,
   IF(condition2, value2,
      IF(condition3, value3, default)))
```

**Example** (SOP List, Row 79-81 - Scan Types):
```excel
O79: =IF(OR(A79="Gas", A79="Rivian"), "Pre-Scan",
         IF(A79="Tesla", "Trim to Access Scanner", ""))

R79: =IF(A79="Gas", 150,
         IF(OR(A79="Rivian", A79="Tesla"), 0, ""))

V79: =IF(A79="Gas", 0,
         IF(A79="Rivian", 1,
            IF(A79="Tesla", 0.2, "")))
```

**Logic**:
- Gas vehicles: "Pre-Scan", $150, 0 labor hrs
- Rivian: "Pre-Scan", $0, 1 labor hr
- Tesla: "Trim to Access Scanner", $0, 0.2 labor hrs

---

### Pattern 4: Value-Based Cascading Logic

**Purpose**: Show operation only if a previous cell has a value

**Formula Pattern**:
```excel
=IF(A29<>"", [VALUE], "")
```

**Example** (Total Loss Charges, Row 29):
```excel
G29: =IF(A29<>"","0","")
M29: =IF(A29<>"","Replace","")
O29: =IF(A29<>"","Administration Fee","")
R29: =IF(A29<>"",A29,"")
```

**Logic**: If user enters a dollar amount in A29, populate the operation row with that amount as the price.

---

### Pattern 5: Complex Multi-Input Dependencies

**Purpose**: Operation depends on multiple inputs with complex AND/OR logic

**Formula Pattern**:
```excel
=IF(AND(condition1, OR(condition2, condition3)), value, "")
```

**Example** (SOP List, Row 88 - Simulate Full Fluids):
```excel
O88: =IF(OR(B79="No", B79=""), "",
         IF(B81="Yes", "Simulate Full Fluids for ADAS Calibrations", ""))
```

**Logic**: Only show operation if ADAS diagnostic is enabled (B79≠No) AND simulate fluids is Yes (B81=Yes).

---

### Pattern 6: Part Size-Based Refinish Time

**Purpose**: Different refinish times based on part size category

**Formula Pattern**:
```excel
=IF(B33="Yes",
   IF(OR(B35="First Large Part", B35="Additional Large Part"), 0.3,
      IF(B35="Additional Small Part", 0.2, "")),
   "")
```

**Example** (Part Operations, Row 33):
```excel
X33: =IF(B33="Yes",
        IF(OR(B35="First Large Part", B35="Additional Large Part"), 0.3,
           IF(B35="Additional Small Part", 0.2, "")),
        "")
```

**Logic**:
- If adhesion promoter = Yes
  - Large parts: 0.3 refinish hours
  - Small parts: 0.2 refinish hours

---

### Pattern 7: Price Tier Based on Paint Complexity

**Purpose**: Price/time scales with paint complexity

**Formula Pattern**:
```excel
=IF(A29="2-Stage", value1,
   IF(A29="3-Stage", value2,
      IF(A29="4-Stage", value3, "")))
```

**Example** (Refinish Operations, Row 30-31):
```excel
O30: =IF(A29="2-Stage", "Color Tint (2-Stage)",
        IF(A29="3-Stage", "Color Tint (3-Stage)",
           IF(A29="4-Stage", "Color Tint (4-Stage)", "")))

X30: =IF(A29="2-Stage", "0.5",
        IF(A29="3-Stage", "1",
           IF(A29="4-Stage", "1.5", "")))
```

**Logic**:
- 2-Stage paint: 0.5 refinish hours
- 3-Stage paint: 1.0 refinish hours
- 4-Stage paint: 1.5 refinish hours

---

### Pattern 8: Aggregation Summaries

**Purpose**: Calculate totals for operations, prices, labor, refinish hours

**Formula Pattern** (Array Formulas):
```excel
=SUM(range)
=SUMIF(range, criteria, sum_range)
```

**Example** (SOP List, Row 26-27 Summary):
```excel
O26: ="📊 " & Q27 & " Ops  |  💲" & R27 & "  |  🛠 " & V27 & " Labor  |  🎨 " & X27 & " Refinish"
Q27: =SUM(Q29:Q170)  [Array formula - sums all quantities]
R27: =SUM(R29:R170)  [Array formula - sums all prices]
V27: =SUM(V29:V170)  [Array formula - sums all labor hours]
X27: =SUM(X29:X170)  [Array formula - sums all refinish hours]
```

**Logic**: Summary row displays emoji indicators with totals for operations count, total price, labor hours, and refinish hours.

---

### Pattern 9: MAX Function for Minimum Values

**Purpose**: Ensure a minimum value is used (e.g., minimum charge)

**Formula Pattern**:
```excel
=MAX(input_cell, minimum_value)
```

**Example** (SOP List, Row 84):
```excel
R84: =MAX(A83, 50)
V84: =MAX(A85, 1)
```

**Logic**: If user enters less than $50, use $50. If user enters less than 1 hour, use 1 hour.

---

### Pattern 10: Refrigerant Type Pricing

**Purpose**: Different pricing based on refrigerant type

**Example** (Mechanical Operations, Row 29):
```excel
O29: =IF(A29="R134a", "R134a and Refrigerant Oil",
        IF(A29="R1234yf", "R1234yf and Refrigerant Oil",
           IF(A29="R744", "R744 and Refrigerant Oil", "")))

R29: =IF(A29="R134a", "85",
        IF(A29="R1234yf", "485",
           IF(A29="R744", "600", "")))
```

**Logic**:
- R134a (older refrigerant): $85
- R1234yf (modern refrigerant): $485
- R744 (CO2 refrigerant): $600

---

## 6. Calculation Engine

### Operation Row Structure

Each operation row follows this standard calculation pattern:

| Column | Purpose | Formula Type | Example |
|--------|---------|--------------|---------|
| **G-L** | Placeholder/Reserved | Conditional | `=IF(A29="Yes", "0", "")` |
| **M** | Operation Type | Conditional | `=IF(A29="Yes", "Rpr", "")` |
| **N** | Reserved | Conditional | `=IF(A29="Yes", "0", "")` |
| **O** | Description | Conditional String | `=IF(A29="Yes", "Test Battery Condition", "")` |
| **P** | Reserved | Conditional | `=IF(A29="Yes", "0", "")` |
| **Q** | Quantity | Conditional | `=IF(A29="Yes", "1", "")` |
| **R** | Price ($) | Conditional Numeric | `=IF(A29="Yes", "50", "")` |
| **S-U** | Additional Costs | Conditional | `=IF(A29="Yes", "0", "")` |
| **V** | Labor Hours | Conditional Numeric | `=IF(A29="Yes", "0.2", "")` |
| **W** | Category | Conditional | `=IF(A29="Yes", "M", "")` |
| **X** | Refinish Hours | Conditional Numeric | `=IF(A29="Yes", "0.5", "")` |

### Operation Types

| Code | Meaning | Usage Context |
|------|---------|---------------|
| **Rpr** | Repair | General repair operations, diagnostics, testing |
| **Replace** | Replace | Part replacement, equipment installation |
| **R&I** | Remove & Install | Temporary removal and reinstallation |
| **Refinish** | Refinish | Paint and refinish operations |
| **Blend** | Blend | Paint blending for adjacent panels |

### Category Codes

| Code | Meaning | Purpose |
|------|---------|---------|
| **M** | Mechanical | Mechanical labor operations |
| **0** | None/Not Applicable | Non-categorized or refinish-only operations |
| **(blank)** | Conditional/Variable | Category assigned based on inputs |

---

## 7. Sheet-by-Sheet Analysis

### 7.1 Home Page

**Purpose**: Navigation hub with hyperlinks to all operational sheets

**Key Elements**:
- **Row 3-6**: Menu with hyperlinks to Getting Started, MET Tabs, Support
- **Row 25-74**: "Getting Started" section with tool instructions
- **Row 75-124**: "MET Tabs" section listing all 11 operational sheets
- **Row 125+**: Support section with contact hyperlinks

**Formulas** (2 total):
```excel
B127: =HYPERLINK("https://mail.google.com/mail/?view=cm&fs=1&to=mcstudestimating@gmail.com&su=MET%20Support%20Request&body=Hi%20there%2C%20I%20need%20help%20with", "📧 Contact Support (Gmail)")

B128: =HYPERLINK("mailto:mcstudestimating@gmail.com?subject=MET%20Support%20Request&body=Hi%2C%20I%20need%20help%20with", "📧 Contact Support (Outlook)")
```

**Navigation Links**:
- SOP List (Row 78)
- Part Operations (Row 79)
- Cover Car Operations (Row 80)
- Body Operations (Row 81)
- Refinish Operations (Row 82)
- Mechanical Operations (Row 83)
- SRS Operations (Row 84)
- Total Loss Charges (Row 85)
- Body on Frame (Row 86)
- Stolen Recovery (Row 87)
- Post Repair Inspection (Row 88)

---

### 7.2 SOP List (Standard Operating Procedures)

**Purpose**: Electrical, vehicle diagnostics, and miscellaneous operations

**Dimensions**: 171 rows × 25 columns
**Formulas**: 679
**Sections**:
1. Electrical (Row 25+)
2. Vehicle Diagnostics (Row 76+)
3. Misc (additional operations)

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | 12V Battery type | Single, Dual | Labor: 0.4 hrs (Single) vs 0.8 hrs (Dual) |
| **A31** | Test battery | Yes, No | Adds "Test Battery Condition" operation (0.2 hrs) |
| **A33** | Battery support | Yes, No | Adds "Battery Support" operation (0.2 hrs) |
| **C29** | ADAS system | Yes, No | Triggers ADAS-related operations |
| **A35** | Vehicle type | Gas, Hybrid, EV | EV/Hybrid-specific operations |
| **A79** | Scan tool type | Gas, Rivian, Tesla | Pre-scan, in-process, post-scan pricing |
| **A81** | Setup scan tool | Yes, No | Adds "Setup Scan Tool" operation (0.2 hrs) |
| **A83** | Custom price input | Numeric | Minimum $50 applied via MAX() |
| **A85** | Custom labor input | Numeric | Minimum 1 hr applied via MAX() |
| **A87** | Gateway unlock | Yes, No | Adds "Gateway (Unlock)" operation (0.1 hrs) |
| **B79** | ADAS diagnostic | Yes, No | Triggers ADAS diagnostic report |
| **B81** | Simulate fluids | Yes, No | Adds "Simulate Full Fluids for ADAS" (0.2 hrs) |
| **B83** | Check tire pressure | Yes, No | Adds "Check and Adjust Tire Pressure for ADAS" (0.2 hrs) |
| **B85** | Remove belongings | Yes, No | Adds "Remove Customer Belongings for ADAS" (0.2 hrs) |
| **B87** | Drive cycle | Yes, No | Adds "Drive Cycle Operational Verification" (0.7 hrs) |

#### Electrical Section Logic

**12V Battery Disconnect** (Row 29):
```excel
O29: =IF(A29="Single", "Disconnect and Reconnect Battery",
         IF(A29="Dual", "Disconnect and Reconnect 2x Battery", ""))
V29: =IF(A29="Single", 0.4, IF(A29="Dual", 0.8, ""))
```

**EV/Hybrid Charge and Maintain** (Row 34):
```excel
O34: =IF(A35="EV", "Charge and Maintain Battery",
         IF(C29="Yes",
            IF(OR(A35="Gas", A35="Hybrid"),
               "Charge and Maintain Battery during ADAS", ""), ""))
V34: =IF(OR(A35="EV", C29="Yes"), "0.6", "")
```

**Mobile Cart for EV/Hybrid** (Row 35):
```excel
O35: =IF(A35="Hybrid", "Mobile Cart for Hybrid",
         IF(A35="EV", "Mobile Cart for EV", ""))
R35: =IF(OR(A35="EV", A35="Hybrid"), "50", "")
V35: =IF(OR(A35="EV", A35="Hybrid"), "0.5", "")
```

#### Vehicle Diagnostics Section Logic

**Scan Tool Operations** (Rows 79-81):
```excel
# Pre-Scan (Row 79)
O79: =IF(OR(A79="Gas", A79="Rivian"), "Pre-Scan",
         IF(A79="Tesla", "Trim to Access Scanner", ""))
R79: =IF(A79="Gas", 150, IF(OR(A79="Rivian", A79="Tesla"), 0, ""))
V79: =IF(A79="Gas", 0, IF(A79="Rivian", 1, IF(A79="Tesla", 0.2, "")))

# In-Process Scan (Row 80)
O80: =IF(OR(A79="Gas", A79="Rivian"), "In-Process Scan",
         IF(A79="Tesla", "Tesla Toolbox Scan", ""))
R80: =IF(A79="Gas", 150, IF(OR(A79="Rivian", A79="Tesla"), 0, ""))
V80: =IF(A79="Gas", 0, IF(A79="Rivian", 1, IF(A79="Tesla", 1, "")))

# Post Scan (Row 81)
O81: =IF(OR(A79="Gas", A79="Rivian"), "Post Scan",
         IF(A79="Tesla", "Tesla Software Script Programming", ""))
R81: =IF(A79="Gas", 150, IF(OR(A79="Rivian", A79="Tesla"), 0, ""))
V81: =IF(A79="Gas", 0, IF(A79="Rivian", 1, IF(A79="Tesla", 0, "")))
```

**ADAS Preparation Operations** (Rows 88-90):
```excel
# Simulate Full Fluids (Row 88)
O88: =IF(OR(B79="No", B79=""), "",
         IF(B81="Yes", "Simulate Full Fluids for ADAS Calibrations", ""))
V88: =IF(OR(B79="No", B79=""), "", IF(B81="Yes", "0.2", ""))

# Check Tire Pressure (Row 89)
O89: =IF(OR(B79="No", B79=""), "",
         IF(B83="Yes", "Check and Adjust Tire Pressure for ADAS Calibrations", ""))
V89: =IF(OR(B79="No", B79=""), "", IF(B83="Yes", "0.2", ""))

# Remove Customer Belongings (Row 90)
O90: =IF(OR(B79="No", B79=""), "",
         IF(B85="Yes", "Remove Customer Belongings for ADAS Calibrations", ""))
V90: =IF(OR(B79="No", B79=""), "", IF(B85="Yes", "0.2", ""))
```

#### Summary Calculations

**Row 26-27**: Summary display with emoji indicators
```excel
O26: ="📊 " & Q27 & " Ops  |  💲" & R27 & "  |  🛠 " & V27 & " Labor  |  🎨 " & X27 & " Refinish"
Q27: =SUM(Q29:Q170)  # Total operations count
R27: =SUM(R29:R170)  # Total price
V27: =SUM(V29:V170)  # Total labor hours
X27: =SUM(X29:X170)  # Total refinish hours
```

---

### 7.3 Part Operations

**Purpose**: Part-specific operations including blend, repair, and replacement procedures

**Dimensions**: 521 rows × 24 columns
**Formulas**: 2,205
**Operation Count**: Highly dynamic based on part selections

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **B26** | Part name | Text | Used in operation descriptions |
| **B33** | Adhesion promoter | Yes, No | Adds adhesion promoter operation |
| **B35** | Part size | First Large Part, Additional Large Part, Additional Small Part | Affects refinish time and pricing |

#### Part Size-Based Logic

**Adhesion Promoter** (Row 33):
```excel
O33: =IF(B33="Yes", B26 & " Adhesion Promoter", "")
X33: =IF(B33="Yes",
        IF(OR(B35="First Large Part", B35="Additional Large Part"), 0.3,
           IF(B35="Additional Small Part", 0.2, "")),
        "")
```

**Pricing Based on Part Size** (Row 34):
```excel
R34: =IF(B35="First Large Part", 15,
        IF(B35="Additional Large Part", 10,
           IF(B35="Additional Small Part", 5,
              IF(B35="", 15, ""))))
```

**Logic Summary**:
- First Large Part: $15, 0.3 refinish hrs
- Additional Large Part: $10, 0.3 refinish hrs
- Additional Small Part: $5, 0.2 refinish hrs
- Default (if B35 empty): $15

---

### 7.4 Cover Car Operations

**Purpose**: Masking and covering procedures for overspray protection

**Dimensions**: 99 rows × 24 columns
**Formulas**: 105

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **B29** | Cover area | Front, Rear, etc. | Determines which cover operations appear |
| **C32** | Two-tone paint | Yes, No | Doubles price/labor for two-tone work |
| **D29** | Labor type | Refinish Labor, $ and Body Labor | Determines if operation is refinish or body labor |

#### Labor Type Logic

**Cover Car for Overspray** (Row 29):
```excel
M29: =IF(D29="Refinish Labor", "Refinish",
         IF(D29="$ and Body Labor", "Replace", ""))

O29: =IF(C32="Yes", "Cover Car for Overspray for Two Tone Paint",
         "Cover Car for Overspray")

R29: =IF(D29="Refinish Labor", 0,
         IF(D29="$ and Body Labor",
            IF(C32="Yes", 10, 5), ""))

V29: =IF(D29="Refinish Labor", 0,
         IF(D29="$ and Body Labor",
            IF(C32="Yes", 0.4, 0.2), ""))

X29: =IF(D29="$ and Body Labor", 0,
         IF(D29="Refinish Labor",
            IF(C32="Yes", 0.4, 0.2), ""))
```

**Logic**:
- If "Refinish Labor": Operation category = Refinish, charges go to refinish hours (X), no price/labor
- If "$ and Body Labor": Operation category = Replace, charges go to price (R) and labor (V)
- Two-tone paint doubles the charges

---

### 7.5 Body Operations

**Purpose**: Body equipment and structural repair operations

**Dimensions**: 121 rows × 25 columns
**Formulas**: 544

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Collision damage access | Yes, No | Adds "Collision Damage Access" operation (0.5 hrs) |
| **A31** | Additional operation | Yes, No | Conditional operation based on A29 state |

#### Cascading Logic Example

**Collision Damage Access** (Row 29):
```excel
O29: =IF(A29="Yes", "Collision Damage Access", "")
V29: =IF(A29="Yes", "0.5", "")
```

**Conditional Follow-Up Operation** (Row 30):
```excel
G30: =IF(AND(A31="Yes", OR(A29="No", A29="")), "0", "")
H30: =IF(A31="Yes", "0", "")
```

**Logic**: Row 30 operation only appears if A31=Yes, and conditionally checks if A29 is disabled.

---

### 7.6 Refinish Operations

**Purpose**: Paint and refinish procedures

**Dimensions**: 99 rows × 24 columns
**Formulas**: 418

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Paint stage | 2-Stage, 3-Stage, 4-Stage | Determines color tint time |
| **A31** | Radar formula | Yes, No | Adds radar-specific color tint operation |
| **A33** | Additional refinish | Yes, No | Enables additional operations |

#### Paint Stage Logic

**Color Tint** (Row 30):
```excel
O30: =IF(A29="2-Stage", "Color Tint (2-Stage)",
        IF(A29="3-Stage", "Color Tint (3-Stage)",
           IF(A29="4-Stage", "Color Tint (4-Stage)", "")))

X30: =IF(A29="2-Stage", "0.5",
        IF(A29="3-Stage", "1",
           IF(A29="4-Stage", "1.5", "")))
```

**Spray Out Cards** (Row 31):
```excel
O31: =IF(A29="2-Stage", "Spray Out Cards (2-Stage)",
        IF(A29="3-Stage", "Spray Out Cards (3-Stage)",
           IF(A29="4-Stage", "Spray Out Cards (4-Stage)", "")))

X31: =IF(A29="2-Stage", "0.5",
        IF(A29="3-Stage", "1",
           IF(A29="4-Stage", "1.5", "")))
```

**Radar Formula Color Tint** (Row 32):
```excel
O32: =IF(A31="Yes",
        IF(A29="2-Stage", "Color Tint (2-Stage) Radar Formula",
           IF(A29="3-Stage", "Color Tint (3-Stage) Radar Formula",
              IF(A29="4-Stage", "Color Tint (4-Stage) Radar Formula", ""))),
        "")
```

#### Summary with Aggregation

**4-Stage Multiplier** (Row 34):
```excel
X34: =IF(AND(A29="4-Stage", A33="Yes"), SUM(A37:A55)*0.25, "")
```

**Logic**: If 4-stage paint AND A33=Yes, sum range A37:A55 and multiply by 0.25 for additional refinish calculation.

---

### 7.7 Mechanical Operations

**Purpose**: AC, cooling, suspension, and general mechanical work

**Dimensions**: 221 rows × 24 columns
**Formulas**: 659

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Refrigerant type | R134a, R1234yf, R744 | Pricing: $85, $485, $600 |
| **B29** | Cover AC lines | Yes, No | Adds "Cover and Protect AC Lines" operation |
| **B31** | Additional operation | Yes, No | Triggers conditional operation |

#### Refrigerant Type Logic

**Refrigerant and Oil** (Row 29):
```excel
O29: =IF(A29="R134a", "R134a and Refrigerant Oil",
        IF(A29="R1234yf", "R1234yf and Refrigerant Oil",
           IF(A29="R744", "R744 and Refrigerant Oil", "")))

R29: =IF(A29="R134a", "85",
        IF(A29="R1234yf", "485",
           IF(A29="R744", "600", "")))
```

**Cover and Protect AC Lines** (Row 30):
```excel
O30: =IF(B29="Yes", "Cover and Protect AC Lines", "")
R30: =IF(B29="Yes", "3", "")
V30: =IF(B29="Yes", "0.2", "")
```

**Logic**:
- R134a (older): $85
- R1234yf (modern/HFO): $485
- R744 (CO2): $600

---

### 7.8 SRS Operations

**Purpose**: Safety restraint system (airbag) operations

**Dimensions**: 122 rows × 25 columns
**Formulas**: 192

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Safety inspections | Yes, No | Adds "Safety Inspections" operation (4 hrs) |
| **A31** | Additional SRS operation | Yes, No | Conditional based on A29 |

#### Safety Inspections Logic

**Safety Inspections** (Row 29):
```excel
O29: =IF(A29="Yes", "Safety Inspections", "")
V29: =IF(A29="Yes", "4", "")
W29: =IF(A29="Yes", "M", "")
```

**Conditional Follow-Up** (Row 30):
```excel
G30: =IF(AND(OR(A29="No", A29=""), A31="Yes"), "0", "")
H30: =IF(A31="Yes", "0", "")
```

**Logic**: High labor hours (4.0) for comprehensive SRS safety inspections.

---

### 7.9 Total Loss Charges

**Purpose**: Administrative fees, coordination charges, and handling fees

**Dimensions**: 99 rows × 24 columns
**Formulas**: 330

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Admin fee amount | Numeric | Dollar amount for administration fee |
| **A31** | Coordination fee | Numeric | Dollar amount for coordination charge |

#### Value-Based Operations

**Administration Fee** (Row 29):
```excel
O29: =IF(A29<>"", "Administration Fee", "")
R29: =IF(A29<>"", A29, "")
```

**Coordination Charge** (Row 30):
```excel
O30: =IF(A31<>"", "Coordination Charge", "")
R30: =IF(A31<>"", A31, "")
```

**Logic**: If user enters a dollar amount, create operation row with that amount as the price. No labor involved (charges only).

---

### 7.10 Body On Frame

**Purpose**: Body-on-frame specific operations (truck/SUV frames)

**Dimensions**: 121 rows × 25 columns
**Formulas**: 114

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Frame disposal cost | Numeric | Adds "Frame Disposal" operation with custom price |
| **A31** | Additional frame operation | Yes, No | Triggers conditional operation |

#### Frame Operations Logic

**Frame Disposal** (Row 29):
```excel
O29: =IF(A29<>"", "Frame Disposal", "")
R29: =IF(A29<>"", A29, "")
```

**Conditional Frame Operation** (Row 30):
```excel
G30: =IF(AND(A29="", A31="Yes"), "0", "")
H30: =IF(A31="Yes", "0", "")
```

**Logic**: Similar to Total Loss Charges - user inputs custom prices for frame-related operations.

---

### 7.11 Stolen Recovery

**Purpose**: Stolen vehicle recovery operations

**Dimensions**: 121 rows × 25 columns
**Formulas**: 258

#### Key Input Variables

| Cell | Description | Values | Impact |
|------|-------------|--------|--------|
| **A29** | Vehicle inspection trigger | Numeric or Yes | Adds "Inspect Vehicle Inside and Out" operation |

#### Inspection Logic

**Vehicle Inspection** (Row 29):
```excel
O29: =IF(A29<>"", "Inspect Vehicle Inside and Out", "")
V29: =IF(A29<>"", [LABOR_VALUE], "")
```

**Logic**: Stolen recovery requires comprehensive vehicle inspection. Operations triggered by user input.

---

### 7.12 Post Repair Inspection

**Purpose**: Final inspection checklist

**Dimensions**: Variable
**Formulas**: 0 (no formulas - static checklist)

**Content**: Simple checklist for final quality assurance before vehicle delivery. No dynamic calculations.

---

## 8. Integration Points

### Navigation Integration

**Home Page → All Sheets**:
- Hyperlinks in rows 78-88 link to each operational sheet
- "Back to top" links on each sheet return to top of page (not cross-sheet)

### Data Integration

**Cross-Sheet Dependencies**:
1. **SOP List → Part Operations**: ADAS calibration status could influence part operations (though not explicitly linked via formulas)
2. **Part Operations → Refinish Operations**: Part size selections determine refinish hours
3. **All Sheets → Summary Row**: Each sheet has internal aggregation but no cross-sheet totals

### User Workflow Integration

**Typical Estimation Workflow**:
```
1. Home Page: Review tool information
    ↓
2. SOP List: Configure vehicle type (EV, Hybrid, Gas), ADAS, battery
    ↓
3. Part Operations: Select parts and sizes for blend/repair/replace
    ↓
4. Cover Car Operations: Determine masking needs
    ↓
5. Body Operations: Add structural repair operations
    ↓
6. Refinish Operations: Configure paint stages and refinish needs
    ↓
7. Mechanical Operations: Add AC, cooling, suspension work
    ↓
8. SRS Operations: Include airbag safety inspections if needed
    ↓
9. Total Loss Charges: Add admin fees if applicable
    ↓
10. Specialized Sheets: Body-on-frame, stolen recovery as needed
    ↓
11. Post Repair Inspection: Review checklist
    ↓
12. Export/Compile: Aggregate all operations for final estimate
```

---

## Appendix: Formula Statistics

### Total Formula Count by Type

| Formula Type | Count | Percentage |
|-------------|-------|------------|
| **IF Statements** | ~4,800 | 87% |
| **OR/AND Logic** | ~800 | 15% |
| **SUM/Aggregation** | ~50 | 1% |
| **MAX** | ~10 | <1% |
| **HYPERLINK** | 2 | <1% |
| **Array Formulas** | ~60 | 1% |

### Complexity Metrics

- **Average formulas per operational sheet**: 460
- **Longest formula chain**: 4-5 nested IF statements
- **Most complex sheet**: Part Operations (2,205 formulas)
- **Simplest sheet**: Home Page (2 formulas - hyperlinks only)

---

## Document Maintenance

**When to Update This Document**:
1. New sheets added to master.xlsx
2. New input variables created
3. Formula logic patterns change
4. New operation types introduced
5. Cross-sheet dependencies added

**Version Control**:
- Document version should match master.xlsx version
- Major changes: Increment major version (e.g., 2.0)
- Minor changes: Increment minor version (e.g., 1.1)

---

**End of Logic Documentation**
