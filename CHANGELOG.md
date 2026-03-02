# McStud Desktop - Version History

## Version 1.3.0 (February 2026)
### Interactive Shop Docs Template System
- **Template-based forms** for all Shop Docs (Tow Bill, Color Tint Invoice, Shop Stock Invoice, PPF Pricing, Labor Rates)
- **"Make a Copy" functionality** - Create editable copies of original templates
- **Edit Mode** - Customize field labels, add/remove charges in user templates
- **Original templates** remain read-only and preserved
- **User templates** saved locally in `%LOCALAPPDATA%\McStudDesktop\ShopDocTemplates\`
- **PDF Export** from all Shop Doc forms with filled data

### Estimate DNA & History Database
- **Estimate History Database** - Stores parsed estimates locally in JSON format
- **Estimate DNA Fingerprint** - Complexity scoring, damage zone analysis, risk flags
- **Insurance Payment Tracking** - Index operations by insurer for payment pattern analysis
- **Chat Integration** - Query estimate history through natural language
  - "How many times did Allstate pay for corrosion protection?"
  - "What estimates included pre-scan?"
  - "Show me State Farm payment patterns"

### Interactive Checklists
- **CheckBox controls** replace static indicators
- **Progress tracking** with visual progress bar
- **Batch operations** - Clear All, Check All, Check Required Only
- **PDF export** reflects checked items with checkmarks

---

## Version 1.2.0 (February 2026)
### Smart Learning System
- **EstimateLearningService** - Pattern recognition from uploaded estimates
- **Pattern Intelligence** - Learns from user corrections and preferences
- **Learned Patterns Panel** - View and manage learned patterns
- **Learning Health Dashboard** - Monitor learning system performance
- **Training Explanation Service** - Educational content for estimating concepts

### Estimate Analysis Tools
- **Estimate Upload View** - Parse PDF estimates
- **Estimate Scoring Panel** - Quality scoring for estimates
- **Estimate Accuracy Service** - Track estimation accuracy over time
- **Supplement Detector** - Identify potential supplement items
- **Ghost Estimate Service** - Generate preliminary estimates

### Chatbot & AI Assistant
- **ChatbotService** - Context-aware responses for estimating questions
- **Chatbot View** - Interactive chat interface
- **Estimator Assistant View** - Guided estimating help
- **Contextual Help Panel** - Context-sensitive guidance

---

## Version 1.1.0 (January 2026)
### Excel Integration (Core Estimating Engine)
- **ExcelEngineService** - ClosedXML-based Excel backend
- **290 Input Mappings** - Complete mapping of all Excel inputs
- **Real-time Calculation** - Instant formula recalculation
- **SOPListViewModel** - Full MVVM implementation
- **SOPListPage** - Professional WinUI 3 UI with:
  - Expandable sections
  - ComboBox dropdowns
  - ToggleSwitch controls
  - Real-time summary footer
  - Operations list view

### Operation Pages (XAML-based)
- **SOP List Page** - Main operations overview
- **Part Operations Page** - Panel-based operations
- **Body Operations Page** - Body work operations
- **Refinish Operations Page** - Paint and refinish
- **Mechanical Operations Page** - Mechanical operations
- **Cover Car Page** - Cover car operations

---

## Version 1.0.0 (January 2026)
### Core Application
- **WinUI 3 Desktop App** - Modern Windows UI framework
- **System Tray Integration** - H.NotifyIcon for tray functionality
- **Single Instance** - Mutex-based single instance enforcement
- **Dark Theme** - Monochrome black/gray UI theme

### Reference & Documentation Tools
- **MET Guide View** - Motor Estimating Training guide
- **Definitions View** - Industry definitions and terms
- **Included/Not Included View** - Coverage reference
- **Procedures View** - Standard procedures
- **P-Pages View** - P-Page reference
- **DEG Inquiries View** - DEG database inquiries

### Shop Documents
- **Tow Bill** - Tow service invoicing
- **Color Tint Invoice** - Paint tint pricing and invoicing
- **Shop Stock Invoice** - Stock parts invoicing
- **PPF Pricing** - Paint Protection Film quotes
- **Labor Rates** - Labor rate calculator
- **Checklists** - Pre-delivery, teardown, quality control

### Utility Services
- **PDF Export Service** - Generate PDFs from app data
- **Clipboard Services** - Smart paste and copy operations
- **Document Usage Tracking** - Track feature usage
- **Update Service** - Check for app updates
- **License Service** - License management

---

## Architecture

### Technology Stack
- **.NET 10.0** - Latest .NET framework
- **WinUI 3** - Modern Windows UI
- **H.NotifyIcon** - System tray support
- **ClosedXML** - Excel file operations
- **CommunityToolkit.Mvvm** - MVVM helpers

### Key Services
| Service | Purpose |
|---------|---------|
| ExcelEngineService | Excel backend for calculations |
| EstimateLearningService | Pattern learning from estimates |
| ChatbotService | AI-powered chat responses |
| ShopDocTemplateService | Template management for Shop Docs |
| EstimateHistoryDatabase | Local estimate storage |
| EstimateQueryService | Natural language estimate queries |

### Data Storage
- **Local AppData** - User settings, templates, estimate history
- **App Directory** - Default templates, knowledge bases, reference data

---

## Roadmap

### Planned Features
- [ ] Cloud sync for estimate history
- [ ] Multi-user template sharing
- [ ] Advanced reporting dashboard
- [ ] CCC/Mitchell integration
- [ ] Mobile companion app

---

## Build Information
- **Target**: Windows 10 version 2004+
- **Architecture**: x64
- **Deployment**: Self-contained, unpackaged
