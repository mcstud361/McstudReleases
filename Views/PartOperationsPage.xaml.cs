using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using McStudDesktop.ViewModels;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;

namespace McStudDesktop.Views
{
    public sealed partial class PartOperationsPage : Page
    {
        public PartOperationsViewModel ViewModel { get; }
        private string _currentCategory = "plasticblend";
        private bool _isInitialized = false;

        // Custom operation panels for each category
        private Dictionary<string, CustomOperationEditorPanel> _customPanels = new();

        public PartOperationsPage()
        {
            this.InitializeComponent();

            // Create ViewModel (in production, use dependency injection)
            ViewModel = new PartOperationsViewModel();

            this.DataContext = ViewModel;

            // Wire up export panel
            ExportPanel.ClipItClicked += ExportPanel_ClipItClicked;
            ExportPanel.TypeItClicked += ExportPanel_TypeItClicked;

            // Defer initial setup to Loaded event to prevent event cascade during construction
            this.Loaded += PartOperationsPage_Loaded;
        }

        private void PartOperationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Mark as initialized so event handlers can run
            _isInitialized = true;

            // Initialize custom operation panels for each category
            InitializeCustomOperationPanels();

            // Set Plastic Blend as default active category
            ShowSubCategory("plasticblend");
        }

        private void InitializeCustomOperationPanels()
        {
            // Create custom operation panels for each category
            var categories = new[]
            {
                ("plasticblend", "Plastic Part Blend", PlasticBlendPanel),
                ("plasticrepair", "Plastic Part Repair", PlasticRepairPanel),
                ("plasticreplace", "Plastic Part Replace", PlasticReplacePanel),
                ("smc", "Carbon Fiber / SMC", SmcPanel),
                ("metalblend", "Metal Part Blend", MetalBlendPanel),
                ("metalrepair", "Metal Part Repair", MetalRepairPanel),
                ("boltedmetal", "Bolted Metal Replace", MetalReplacePanel),
                ("weldedmetal", "Welded Metal Replace", WeldedMetalPanel),
                ("innerpanel", "Inner Panel", InnerPanelPanel),
                ("glass", "Glass", GlassPanel)
            };

            foreach (var (categoryId, categoryName, panel) in categories)
            {
                var customPanel = new CustomOperationEditorPanel($"PartOperations_{categoryId}");
                customPanel.OperationsChanged += (s, ops) =>
                {
                    if (_currentCategory == categoryId)
                    {
                        UpdateExportLineCount();
                        UpdateSummary();
                    }
                };

                // Add a header before the custom panel
                var headerBorder = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 16, 0, 8)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = "Custom Operations",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212)),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };

                panel.Children.Add(headerBorder);
                panel.Children.Add(customPanel);

                _customPanels[categoryId] = customPanel;
            }
        }

        private void ExportPanel_ClipItClicked(object? sender, ExportEventArgs e)
        {
            int lineCount = GetCurrentCategoryOperationCount();

            if (lineCount == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            ExportPanel.SetTransferring();

            var targetName = e.Target switch
            {
                ExportPanel.ExportTarget.CCCDesktop => "CCC Desktop",
                ExportPanel.ExportTarget.CCCWeb => "CCC Web",
                ExportPanel.ExportTarget.Mitchell => "Mitchell",
                _ => "Unknown"
            };

            // Get current operations
            var currentOps = GetCurrentCategoryOperations();

            // Update virtual clipboard first (so Export tab shows accurate summary)
            McStudDesktop.Services.VirtualClipboardService.Instance.SetOperations(currentOps, $"Part Operations - {_currentCategory}");

            // Copy to Windows clipboard with proper formatting
            McStudDesktop.Services.ClipboardExportService.CopyToClipboard(currentOps, targetName);

            System.Diagnostics.Debug.WriteLine($"[ExportPanel] Clipped {lineCount} lines from {_currentCategory} to {targetName}");

            ExportPanel.SetComplete(lineCount);
        }

        private async void ExportPanel_TypeItClicked(object? sender, ExportEventArgs e)
        {
            int lineCount = GetCurrentCategoryOperationCount();
            if (lineCount == 0)
            {
                ExportPanel.Status = "No lines to export";
                return;
            }

            using var pasteService = new AutoHotkeyPasteService();
            pasteService.StatusChanged += (s, status) =>
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.Status = status);
            pasteService.ProgressChanged += (s, p) =>
                ExportPanel.DispatcherQueue.TryEnqueue(() => ExportPanel.SetTyping(p.current, p.total));

            var currentOps = GetCurrentCategoryOperations();
            var rows = new System.Collections.Generic.List<string[]>();
            foreach (var op in currentOps)
            {
                string labor = op.Labor > 0 ? op.Labor.ToString("F1") : "";
                string refinish = op.Refinish > 0 ? op.Refinish.ToString("F1") : "";
                rows.Add(new[] { op.OperationType, op.Name, labor, refinish });
            }

            try
            {
                ExportPanel.SetTyping(0, lineCount);
                bool success = await pasteService.PasteToApp(rows.ToArray());
                if (success) ExportPanel.SetComplete(lineCount);
                else ExportPanel.SetError("Paste failed");
            }
            catch (System.Exception ex)
            {
                ExportPanel.SetError(ex.Message);
            }
        }

        private void ClipItButton_Click(object sender, RoutedEventArgs e)
        {
            int lineCount = GetCurrentCategoryOperationCount();

            if (lineCount == 0) return;

            System.Diagnostics.Debug.WriteLine($"[ClipIt] Clipping {lineCount} operations from {_currentCategory}");
        }

        private int GetCurrentCategoryOperationCount()
        {
            return GetCurrentCategoryOperations().Count;
        }

        private System.Collections.Generic.List<McStudDesktop.Services.OperationRow> GetCurrentCategoryOperations()
        {
            System.Collections.Generic.IEnumerable<McStudDesktop.Services.OperationRow> ops = _currentCategory switch
            {
                "plasticblend" => ViewModel.PlasticBlendOperations,
                "plasticrepair" => ViewModel.PlasticRepairOperations,
                "plasticreplace" => ViewModel.PlasticReplaceOperations,
                "smc" => ViewModel.SmcOperations,
                "metalblend" => ViewModel.MetalBlendOperations,
                "metalrepair" => ViewModel.MetalRepairOperations,
                "boltedmetal" => ViewModel.BoltedMetalOperations,
                "weldedmetal" => ViewModel.WeldedMetalOperations,
                "innerpanel" => ViewModel.InnerPanelOperations,
                "glass" => ViewModel.GlassOperations,
                _ => System.Linq.Enumerable.Empty<McStudDesktop.Services.OperationRow>()
            };

            var result = new System.Collections.Generic.List<McStudDesktop.Services.OperationRow>(ops);

            // Add custom operations from the panel
            if (_customPanels.TryGetValue(_currentCategory, out var customPanel))
            {
                // Calculate current totals for percentage calculations
                decimal repairHours = 0, refinishHours = 0;
                foreach (var op in result)
                {
                    repairHours += (decimal)op.Labor;
                    refinishHours += (decimal)op.Refinish;
                }

                // Get part name from current panel
                string partName = GetCurrentPartName();

                // Add custom operations
                var customOps = customPanel.GetOperationRows(repairHours, refinishHours, 0, partName);
                result.AddRange(customOps);
            }

            return result;
        }

        private string GetCurrentPartName()
        {
            return _currentCategory switch
            {
                "plasticblend" => PB_PartName?.Text ?? "",
                "plasticrepair" => PR_PartName?.Text ?? "",
                "metalblend" => MB_PartName?.Text ?? "",
                "metalrepair" => MR_PartName?.Text ?? "",
                "boltedmetal" => BMR_PartName?.Text ?? "",
                "weldedmetal" => WMR_PartName?.Text ?? "",
                "innerpanel" => IP_PartName?.Text ?? "",
                "glass" => GL_PartName?.Text ?? "",
                _ => ""
            };
        }

        private void SubCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string category)
            {
                ShowSubCategory(category);
            }
        }

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateViewModelFromUI();
            UpdateFooterTotals();
            UpdateExportLineCount();
            UpdateSummary();
        }

        private void OnInputChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateViewModelFromUI();
            UpdateFooterTotals();
            UpdateExportLineCount();
            UpdateSummary();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateViewModelFromUI();
            UpdateFooterTotals();
            UpdateExportLineCount();
            UpdateSummary();
        }

        private void UpdateViewModelFromUI()
        {
            switch (_currentCategory)
            {
                case "plasticblend":
                    UpdatePlasticBlendFromUI();
                    break;
                case "plasticrepair":
                    UpdatePlasticRepairFromUI();
                    break;
                case "plasticreplace":
                    UpdatePlasticReplaceFromUI();
                    break;
                case "smc":
                    UpdateSmcFromUI();
                    break;
                case "metalblend":
                    UpdateMetalBlendFromUI();
                    break;
                case "metalrepair":
                    UpdateMetalRepairFromUI();
                    break;
                case "boltedmetal":
                    UpdateBoltedMetalFromUI();
                    break;
                case "weldedmetal":
                    UpdateWeldedMetalFromUI();
                    break;
                case "innerpanel":
                    UpdateInnerPanelFromUI();
                    break;
                case "glass":
                    UpdateGlassFromUI();
                    break;
            }

            ViewModel.UpdateCurrentCategoryTotals(GetCurrentCategoryName());
        }

        private string GetCurrentCategoryName()
        {
            return _currentCategory switch
            {
                "plasticblend" => "Plastic Part Blend",
                "plasticrepair" => "Plastic Part Repair",
                "plasticreplace" => "Plastic Part Replace",
                "smc" => "Carbon Fiber / SMC / Composite",
                "metalblend" => "Metal Part Blend",
                "metalrepair" => "Metal Part Repair",
                "boltedmetal" => "Bolted on Metal Part Replace",
                "weldedmetal" => "Welded on Exterior Metal Part Replace",
                "innerpanel" => "Inner Panel",
                "glass" => "Glass",
                _ => "Unknown"
            };
        }

        private string GetComboValue(ComboBox? combo, string defaultValue)
        {
            if (combo?.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? defaultValue;
            return defaultValue;
        }

        // ==================== PLASTIC PART BLEND (Rows 33-40) ====================
        private void UpdatePlasticBlendFromUI()
        {
            ViewModel.PlasticBlendOperations.Clear();

            // Get Part Name and Refinish Units
            string partName = PB_PartName?.Text ?? "Part";
            if (string.IsNullOrWhiteSpace(partName)) partName = "Part";
            double refinishUnits = PB_RefinishUnits?.Value ?? 3;

            string panelType = GetComboValue(PB_A33, "Additional Panel");
            string partSize = GetComboValue(PB_B35, "First Large Part");

            // A33: DE-NIB
            double deNibRefinish = panelType switch
            {
                "First Panel" => refinishUnits * 0.1,
                "Additional Panel" => refinishUnits * 0.05,
                _ => refinishUnits * 0.1
            };
            if (deNibRefinish < 0.1) deNibRefinish = 0.1;
            ViewModel.PlasticBlendOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"{partName} DE-NIB",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(deNibRefinish, 2)
            });

            // B33: Adhesion Promoter
            if (GetComboValue(PB_B33, "No") == "Yes")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Adhesion Promoter",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0.3
                });
            }

            // C33: Textured Portion on Part
            if (GetComboValue(PB_C33, "No") == "Yes")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Mask Textured Portion",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // D33: Park Sensors - Punch Holes
            if (GetComboValue(PB_D33, "") == "Punch Holes")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Punch Holes for Park Sensors",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // E33: Ceramic Coat - Remove
            if (GetComboValue(PB_E33, "") == "Remove")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Ceramic Coat Removal",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // A35: Part being Removed
            if (GetComboValue(PB_A35, "No") == "Yes")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Stage and Secure for Refinish",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // B35: Flex Additive Part Size
            double flexPrice = partSize switch
            {
                "First Large Part" => 15,
                "Additional Large Part" => 10,
                "Additional Small Part" => 5,
                _ => 15
            };
            ViewModel.PlasticBlendOperations.Add(new OperationRow
            {
                OperationType = "Mat", Name = $"{partName} Flex Additive",
                Quantity = 1, Price = flexPrice, Labor = 0, Category = "M", Refinish = 0
            });

            // C35: License Plate - Equipped
            if (GetComboValue(PB_C35, "Not Equipped") == "Equipped")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} License Plate",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // D34: Number of Park Sensors (NumberBox for count)
            int numParkSensors = (int)(PB_D34?.Value ?? 0);
            if (numParkSensors > 0)
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Install Park Sensor Brackets",
                    Quantity = numParkSensors, Price = 0, Labor = 0.3 * numParkSensors, Category = "B", Refinish = 0
                });
            }

            // E34: PPF/Vinyl Wrap - Apply
            if (GetComboValue(PB_E34, "") == "Apply")
            {
                ViewModel.PlasticBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} PPF/Vinyl Wrap Apply",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Buff (Wet/Dry Sand, Rub-Out & Buff)
            double buffRefinish = refinishUnits * 0.3;
            if (buffRefinish < 0.1) buffRefinish = 0.1;
            ViewModel.PlasticBlendOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(buffRefinish, 2)
            });
        }

        // ==================== PLASTIC PART REPAIR (Rows 83-91) ====================
        private void UpdatePlasticRepairFromUI()
        {
            ViewModel.PlasticRepairOperations.Clear();

            // Get Part Name and Refinish Units
            string partName = PR_PartName?.Text ?? "Part";
            if (string.IsNullOrWhiteSpace(partName)) partName = "Part";
            double refinishUnits = PR_RefinishUnits?.Value ?? 3;

            string panelType = GetComboValue(PR_A83, "First Panel");
            string partSize = GetComboValue(PR_B85, "First Large Part");

            // A83: DE-NIB
            double deNibRefinish = panelType switch
            {
                "First Panel" => refinishUnits * 0.1,
                "Additional Panel" => refinishUnits * 0.05,
                _ => refinishUnits * 0.1
            };
            if (deNibRefinish < 0.1) deNibRefinish = 0.1;
            ViewModel.PlasticRepairOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"{partName} DE-NIB",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(deNibRefinish, 2)
            });

            // B83: Adhesion Promoter
            if (GetComboValue(PR_B83, "No") == "Yes")
            {
                ViewModel.PlasticRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Adhesion Promoter",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0.3
                });
            }

            // C83: Textured Portion on Part
            if (GetComboValue(PR_C83, "No") == "Yes")
            {
                ViewModel.PlasticRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Mask Textured Portion",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // D83: Park Sensors - Punch Holes
            if (GetComboValue(PR_D83, "") == "Punch Holes")
            {
                ViewModel.PlasticRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Punch Holes for Park Sensors",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // E83: Ceramic Coat - Remove
            if (GetComboValue(PR_E83, "") == "Remove")
            {
                ViewModel.PlasticRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Ceramic Coat Removal",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Buff (Wet/Dry Sand, Rub-Out & Buff)
            double buffRefinish = refinishUnits * 0.3;
            if (buffRefinish < 0.1) buffRefinish = 0.1;
            ViewModel.PlasticRepairOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(buffRefinish, 2)
            });
        }

        // ==================== PLASTIC PART REPLACE (Rows 133-141) ====================
        private void UpdatePlasticReplaceFromUI()
        {
            ViewModel.PlasticReplaceOperations.Clear();

            string panelType = GetComboValue(PRepl_A133, "Additional Panel");

            // A133: Panel Type
            ViewModel.PlasticReplaceOperations.Add(new OperationRow
            {
                OperationType = "Rpl", Name = $"Replace Panel: {panelType}",
                Quantity = 1, Price = 0, Labor = 0, Category = "B", Refinish = 0
            });

            // B133
            if (GetComboValue(PRepl_B133, "No") == "Yes")
            {
                ViewModel.PlasticReplaceOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "B133 Option",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // D133: Punch Holes
            if (GetComboValue(PRepl_D133, "") == "Punch Holes")
            {
                ViewModel.PlasticReplaceOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "Punch Holes",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // D134: Install Brackets
            if (GetComboValue(PRepl_D134, "") == "Install Brackets")
            {
                ViewModel.PlasticReplaceOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "Install Brackets",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Refinish
            ViewModel.PlasticReplaceOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"Refinish: {panelType}",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 3.0
            });
        }

        // ==================== SMC / CARBON FIBER (Rows 183-191) ====================
        private void UpdateSmcFromUI()
        {
            ViewModel.SmcOperations.Clear();

            string panelType = GetComboValue(SMC_A183, "First Panel");

            // A183: Panel Type
            ViewModel.SmcOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"SMC Panel: {panelType}",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 3.0
            });

            // A185
            if (GetComboValue(SMC_A185, "No") == "Yes")
            {
                ViewModel.SmcOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "A185 Option",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // A187
            if (GetComboValue(SMC_A187, "No") == "Yes")
            {
                ViewModel.SmcOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "A187 Option",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }
        }

        // ==================== METAL PART BLEND (Rows 233-248) ====================
        private void UpdateMetalBlendFromUI()
        {
            ViewModel.MetalBlendOperations.Clear();

            string partName = MB_PartName?.Text ?? "Panel";
            double refinishUnits = MB_RefinishUnits?.Value ?? 3.7;
            string deNibType = GetComboValue(MB_A233, "Additional Panel");

            // Calculate DE-NIB refinish hours based on panel type
            double deNibRefinish = deNibType switch
            {
                "First Panel" => 3.0,
                "First Panel Facing Sky" => 3.5,
                _ => 2.0
            };

            // DE-NIB operation (always added)
            ViewModel.MetalBlendOperations.Add(new OperationRow
            {
                OperationType = "Ref", Name = $"{partName} DE-NIB",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = deNibRefinish
            });

            // Add for Edging
            if (GetComboValue(MB_AddEdging, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Ref", Name = $"{partName} Add for Edging",
                    Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 0.5
                });
            }

            // Part being Removed
            if (GetComboValue(MB_A235, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Part being Removed",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Trial Fit Labor Unit
            double trialFit = MB_TrialFit?.Value ?? 0;
            if (trialFit > 0)
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Trial Fit",
                    Quantity = 1, Price = 0, Labor = trialFit, Category = "B", Refinish = 0
                });
            }

            // Material: Butyl Tape
            if (GetComboValue(MB_ButylTape, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Butyl Tape",
                    Quantity = 1, Price = 15, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Material: Foam
            if (GetComboValue(MB_Foam, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Foam",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Additional Parts: Textured Portion on Part
            if (GetComboValue(MB_C233, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Textured Portion",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: License Plate
            if (GetComboValue(MB_C235, "Not Equipped") == "Equipped")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "License Plate R&I",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: License Plate Damaged
            if (GetComboValue(MB_LicenseDamaged, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "License Plate Damaged",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: Number of Nameplates
            int nameplates = (int)(MB_Nameplates?.Value ?? 0);
            if (nameplates > 0)
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"Nameplate R&I x{nameplates}",
                    Quantity = nameplates, Price = 0, Labor = 0.2 * nameplates, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: Adhesive Cleanup
            if (GetComboValue(MB_C241, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "Adhesive Cleanup",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Add Ons: Ceramic Coat
            string ceramicCoat = GetComboValue(MB_CeramicCoat, "");
            if (ceramicCoat == "Remove")
            {
                double ceramicPrice = MB_CeramicPrice?.Value ?? 155;
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Remove Ceramic Coat",
                    Quantity = 1, Price = ceramicPrice, Labor = 0.3, Category = "M", Refinish = 0
                });
            }

            // Add Ons: PPF/Vinyl Wrap
            string ppfWrap = GetComboValue(MB_PPFWrap, "");
            if (ppfWrap == "Remove")
            {
                double ppfPrice = MB_PPFPrice?.Value ?? 255;
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Remove PPF/Vinyl Wrap",
                    Quantity = 1, Price = ppfPrice, Labor = 0.5, Category = "M", Refinish = 0
                });
            }

            // Add Ons: Pinstripes
            string pinstripes = GetComboValue(MB_Pinstripes, "");
            if (!string.IsNullOrEmpty(pinstripes) && pinstripes != "None")
            {
                double pinstripesPrice = pinstripes == "Dual Pinstripes" ? 25 : 15;
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = pinstripes,
                    Quantity = 1, Price = pinstripesPrice, Labor = 0.3, Category = "M", Refinish = 0
                });
            }

            // Add Ons: Edge Guard
            if (GetComboValue(MB_EdgeGuard, "No") == "Yes")
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Edge Guard",
                    Quantity = 1, Price = 20, Labor = 0.2, Category = "M", Refinish = 0
                });
            }

            // Base refinish operation based on Exterior Refinish Unit of Part
            if (refinishUnits > 0)
            {
                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Ref", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                    Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = refinishUnits
                });

                ViewModel.MetalBlendOperations.Add(new OperationRow
                {
                    OperationType = "Ref", Name = $"{partName} Backtape Jambs",
                    Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 0.3
                });
            }
        }

        // ==================== METAL PART REPAIR (Rows 283-298) ====================
        private void UpdateMetalRepairFromUI()
        {
            ViewModel.MetalRepairOperations.Clear();

            // Get Part Name and Units
            string partName = MR_PartName?.Text ?? "Part";
            if (string.IsNullOrWhiteSpace(partName)) partName = "Part";
            double repairUnits = MR_RepairUnits?.Value ?? 8;
            double refinishUnits = MR_RefinishUnits?.Value ?? 2.2;

            string panelType = GetComboValue(MR_A283, "Additional Panel");

            // Backtape Jambs (always added for repair)
            ViewModel.MetalRepairOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Backtape Jambs",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 0.3
            });

            // Corrosion Protection (always added for repair)
            ViewModel.MetalRepairOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Corrosion Protection",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = 0.3
            });

            // Feather Edge & Block Sand (based on repair units)
            double featherEdgeRefinish = repairUnits * 0.25;
            if (featherEdgeRefinish > 0)
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} Feather Edge & Block Sand",
                    Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(featherEdgeRefinish, 2)
                });
            }

            // DE-NIB (based on panel type and refinish units)
            double deNibRefinish = panelType switch
            {
                "First Panel" => refinishUnits * 0.2,
                "First Panel Facing Sky" => refinishUnits * 0.25,
                _ => refinishUnits * 0.2  // Additional Panel
            };
            if (deNibRefinish < 0.1) deNibRefinish = 0.1;
            ViewModel.MetalRepairOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} DE-NIB",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(deNibRefinish, 2)
            });

            // Wet/Dry Sand, Rub-Out & Buff (based on refinish units)
            double buffRefinish = refinishUnits * 0.3;
            if (buffRefinish < 0.1) buffRefinish = 0.1;
            ViewModel.MetalRepairOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                Quantity = 1, Price = 0, Labor = 0, Category = "R", Refinish = Math.Round(buffRefinish, 2)
            });

            // Material: Cavity Wax
            if (GetComboValue(MR_CavityWax, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Cavity Wax Injection",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Material: Seam Sealer
            if (GetComboValue(MR_SeamSealer, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Seam Sealer",
                    Quantity = 1, Price = 15, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Material: Chip Guard
            if (GetComboValue(MR_ChipGuard, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Chip Guard",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Material: Butyl Tape
            if (GetComboValue(MR_ButylTape, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Butyl Tape",
                    Quantity = 1, Price = 15, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Material: Foam
            if (GetComboValue(MR_Foam, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Foam",
                    Quantity = 1, Price = 10, Labor = 0, Category = "M", Refinish = 0
                });
            }

            // Additional Parts: Textured Portion on Part
            if (GetComboValue(MR_C283, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Textured Portion",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: License Plate
            if (GetComboValue(MR_C285, "Not Equipped") == "Equipped")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = "License Plate",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: License Plate Damaged
            if (GetComboValue(MR_LicenseDamaged, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = "License Plate Damaged",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: Number of Nameplates
            int nameplates = (int)(MR_Nameplates?.Value ?? 0);
            if (nameplates > 0)
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Replace", Name = $"Measure and Mark Template for {nameplates}x Nameplate Installation",
                    Quantity = 1, Price = nameplates * 2, Labor = nameplates * 0.2, Category = "B", Refinish = 0
                });
            }

            // Additional Parts: Adhesive Cleanup
            if (GetComboValue(MR_C291, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Adhesive Cleanup",
                    Quantity = 1, Price = 0, Labor = 0.6, Category = "B", Refinish = 0
                });
            }

            // Labor: Part being Removed
            if (GetComboValue(MR_A285, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Part being Removed",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0
                });
            }

            // Labor: Trial Fit
            double trialFit = MR_TrialFit?.Value ?? 0;
            if (trialFit > 0)
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Trial Fit",
                    Quantity = 1, Price = 0, Labor = trialFit, Category = "B", Refinish = 0
                });
            }

            // Add Ons: Ceramic Coat
            if (GetComboValue(MR_CeramicCoat, "No") == "Yes")
            {
                double ceramicPrice = MR_CeramicPrice?.Value ?? 155;
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Ceramic Coat",
                    Quantity = 1, Price = ceramicPrice, Labor = 0.3, Category = "M", Refinish = 0
                });
            }

            // Add Ons: PPF/Vinyl Wrap
            if (GetComboValue(MR_PPFWrap, "No") == "Yes")
            {
                double ppfPrice = MR_PPFPrice?.Value ?? 255;
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} PPF/Vinyl Wrap",
                    Quantity = 1, Price = ppfPrice, Labor = 0.5, Category = "M", Refinish = 0
                });
            }

            // Add Ons: Pinstripes
            string pinstripes = GetComboValue(MR_Pinstripes, "");
            if (!string.IsNullOrEmpty(pinstripes))
            {
                double pinstripesPrice = pinstripes == "Dual" ? 25 : 15;
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{pinstripes} Pinstripes",
                    Quantity = 1, Price = pinstripesPrice, Labor = 0.3, Category = "M", Refinish = 0
                });
            }

            // Add Ons: Edge Guard
            if (GetComboValue(MR_EdgeGuard, "No") == "Yes")
            {
                ViewModel.MetalRepairOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = "Edge Guard",
                    Quantity = 1, Price = 20, Labor = 0.2, Category = "M", Refinish = 0
                });
            }
        }

        // ==================== BOLTED METAL REPLACE (Rows 333-345) ====================
        private void UpdateBoltedMetalFromUI()
        {
            ViewModel.BoltedMetalOperations.Clear();

            string partName = BMR_PartName?.Text ?? "part";
            if (string.IsNullOrWhiteSpace(partName)) partName = "part";
            double refinishUnits = BMR_RefinishUnits?.Value ?? 2.2;

            string deNibType = GetComboValue(BMR_DeNib, "Additional Panel");

            // DE-NIB - always add if selected
            if (!string.IsNullOrEmpty(deNibType))
            {
                double deNibRefinish = deNibType switch
                {
                    "First Panel" => 0.15,
                    "Additional Panel" => 0.11,
                    "First Panel Facing Sky" => 0.2,
                    _ => 0.11
                };
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} DE-NIB",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = deNibRefinish
                });
            }

            // Wet/Dry Sand, Rub-Out & Buff (always added for replace operations)
            double wetDryRefinish = refinishUnits * 0.3; // ~0.66 for 2.2 units
            ViewModel.BoltedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = Math.Round(wetDryRefinish, 2)
            });

            // Stage and Secure for Refinish (always added)
            ViewModel.BoltedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Stage and Secure for Refinish",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.2
            });

            // Trial Fit Labor Unit
            double trialFitLabor = BMR_TrialFitLabor?.Value ?? 0;
            if (trialFitLabor > 0)
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Trial Fit",
                    Quantity = 1, Price = 0, Labor = trialFitLabor, Category = "0", Refinish = 0
                });
            }

            // Refinish Backside
            if (GetComboValue(BMR_RefinishBackside, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} Refinish Backside",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = refinishUnits
                });
            }

            // Cavity Wax
            if (GetComboValue(BMR_CavityWax, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Cavity Wax Injection",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Seam Sealer
            if (GetComboValue(BMR_SeamSealer, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Seam Sealer Cleanup, Mask, Test, Replicate",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.8
                });
            }

            // Chip Guard
            if (GetComboValue(BMR_ChipGuard, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Chip Guard Application",
                    Quantity = 1, Price = 10, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Butyl Tape
            if (GetComboValue(BMR_ButylTape, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Butyl Tape Application",
                    Quantity = 1, Price = 15, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // Foam
            if (GetComboValue(BMR_Foam, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Foam Application",
                    Quantity = 1, Price = 20, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Textured Portion on Part
            if (GetComboValue(BMR_TexturedPortion, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Textured Portion Refinish",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0.5
                });
            }

            // License Plate R&I
            if (GetComboValue(BMR_LicensePlate, "Not Equipped") == "Equipped")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} License Plate R&I",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // License Plate Damaged
            if (GetComboValue(BMR_LicensePlateDamaged, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} License Plate Damaged - Replace",
                    Quantity = 1, Price = 25, Labor = 0.1, Category = "0", Refinish = 0
                });
            }

            // Number of Nameplates
            double numNameplates = BMR_NumNameplates?.Value ?? 0;
            if (numNameplates > 0)
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Nameplate Installation",
                    Quantity = (int)numNameplates, Price = 0, Labor = 0.1 * numNameplates, Category = "0", Refinish = 0
                });
            }

            // Ceramic Coat
            if (GetComboValue(BMR_CeramicCoat, "No") == "Yes")
            {
                double ceramicPrice = BMR_CeramicCoatPrice?.Value ?? 112;
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Ceramic Coat Application",
                    Quantity = 1, Price = ceramicPrice, Labor = 0.5, Category = "0", Refinish = 0
                });
            }

            // PPF / Vinyl Wrap
            if (GetComboValue(BMR_PPFVinylWrap, "No") == "Yes")
            {
                double ppfPrice = BMR_PPFVinylWrapPrice?.Value ?? 255;
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} PPF / Vinyl Wrap Application",
                    Quantity = 1, Price = ppfPrice, Labor = 1.0, Category = "0", Refinish = 0
                });
            }

            // Pinstripes
            string pinstripes = GetComboValue(BMR_Pinstripes, "");
            if (!string.IsNullOrEmpty(pinstripes))
            {
                double pinstripeLabor = pinstripes == "Dual" ? 0.4 : 0.2;
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} {pinstripes} Pinstripes",
                    Quantity = 1, Price = pinstripes == "Dual" ? 35 : 20, Labor = pinstripeLabor, Category = "0", Refinish = 0
                });
            }

            // Edge Guard
            if (GetComboValue(BMR_EdgeGuard, "No") == "Yes")
            {
                ViewModel.BoltedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Edge Guard Installation",
                    Quantity = 1, Price = 15, Labor = 0.2, Category = "0", Refinish = 0
                });
            }
        }

        // ==================== WELDED METAL REPLACE (Rows 383-398) ====================
        private void UpdateWeldedMetalFromUI()
        {
            ViewModel.WeldedMetalOperations.Clear();

            string partName = WMR_PartName?.Text ?? "part";
            if (string.IsNullOrWhiteSpace(partName)) partName = "part";
            double refinishUnits = WMR_RefinishUnits?.Value ?? 4.8;

            string deNibType = GetComboValue(WMR_DeNib, "Additional Panel");

            // Backtape Jambs (always added for welded replace)
            ViewModel.WeldedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Backtape Jambs",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.3
            });

            // Corrosion Protection (always added for welded replace)
            ViewModel.WeldedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Corrosion Protection",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.3
            });

            // Feather Edge & Block Sand (always added for welded replace)
            ViewModel.WeldedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Feather Edge & Block Sand",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.3
            });

            // DE-NIB - always add if selected
            if (!string.IsNullOrEmpty(deNibType))
            {
                double deNibRefinish = deNibType switch
                {
                    "First Panel" => 0.3,
                    "Additional Panel" => 0.24,
                    "First Panel Facing Sky" => 0.35,
                    _ => 0.24
                };
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} DE-NIB",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = deNibRefinish
                });
            }

            // Wet/Dry Sand, Rub-Out & Buff (always added for replace operations)
            double wetDryRefinish = refinishUnits * 0.3; // ~1.44 for 4.8 units
            ViewModel.WeldedMetalOperations.Add(new OperationRow
            {
                OperationType = "Refinish", Name = $"{partName} Wet/Dry Sand, Rub-Out & Buff",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = Math.Round(wetDryRefinish, 2)
            });

            // Refinish Backside
            if (GetComboValue(WMR_RefinishBackside, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} Refinish Backside",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = refinishUnits
                });
            }

            // Trial Fit Labor Unit
            double trialFitLabor = WMR_TrialFitLabor?.Value ?? 0;
            if (trialFitLabor > 0)
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Trial Fit",
                    Quantity = 1, Price = 0, Labor = trialFitLabor, Category = "0", Refinish = 0
                });
            }

            // Number of Backing Plate
            double numBackingPlate = WMR_NumBackingPlate?.Value ?? 0;
            if (numBackingPlate > 0)
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Backing Plate Installation",
                    Quantity = (int)numBackingPlate, Price = 0, Labor = 0.2 * numBackingPlate, Category = "0", Refinish = 0
                });
            }

            // Cavity Wax
            if (GetComboValue(WMR_CavityWax, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Cavity Wax Injection",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Seam Sealer
            if (GetComboValue(WMR_SeamSealer, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Seam Sealer Cleanup, Mask, Test, Replicate",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.8
                });
            }

            // Chip Guard
            if (GetComboValue(WMR_ChipGuard, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Chip Guard Application",
                    Quantity = 1, Price = 10, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Butyl Tape
            if (GetComboValue(WMR_ButylTape, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Butyl Tape Application",
                    Quantity = 1, Price = 15, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // Foam
            if (GetComboValue(WMR_Foam, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Foam Application",
                    Quantity = 1, Price = 20, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Textured Portion on Part
            if (GetComboValue(WMR_TexturedPortion, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Textured Portion Refinish",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0.5
                });
            }

            // License Plate R&I
            if (GetComboValue(WMR_LicensePlate, "Not Equipped") == "Equipped")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} License Plate R&I",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // License Plate Damaged
            if (GetComboValue(WMR_LicensePlateDamaged, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} License Plate Damaged - Replace",
                    Quantity = 1, Price = 25, Labor = 0.1, Category = "0", Refinish = 0
                });
            }

            // Number of Nameplates
            double numNameplates = WMR_NumNameplates?.Value ?? 0;
            if (numNameplates > 0)
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Nameplate Installation",
                    Quantity = (int)numNameplates, Price = 0, Labor = 0.1 * numNameplates, Category = "0", Refinish = 0
                });
            }

            // Adhesive Cleanup
            if (GetComboValue(WMR_AdhesiveCleanup, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Adhesive Cleanup",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Ceramic Coat
            if (GetComboValue(WMR_CeramicCoat, "No") == "Yes")
            {
                double ceramicPrice = WMR_CeramicCoatPrice?.Value ?? 200;
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Ceramic Coat Application",
                    Quantity = 1, Price = ceramicPrice, Labor = 0.5, Category = "0", Refinish = 0
                });
            }

            // PPF / Vinyl Wrap
            if (GetComboValue(WMR_PPFVinylWrap, "No") == "Yes")
            {
                double ppfPrice = WMR_PPFVinylWrapPrice?.Value ?? 255;
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} PPF / Vinyl Wrap Application",
                    Quantity = 1, Price = ppfPrice, Labor = 1.0, Category = "0", Refinish = 0
                });
            }

            // Pinstripes
            string pinstripes = GetComboValue(WMR_Pinstripes, "");
            if (!string.IsNullOrEmpty(pinstripes))
            {
                double pinstripeLabor = pinstripes == "Dual" ? 0.4 : 0.2;
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} {pinstripes} Pinstripes",
                    Quantity = 1, Price = pinstripes == "Dual" ? 35 : 20, Labor = pinstripeLabor, Category = "0", Refinish = 0
                });
            }

            // Edge Guard
            if (GetComboValue(WMR_EdgeGuard, "No") == "Yes")
            {
                ViewModel.WeldedMetalOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Edge Guard Installation",
                    Quantity = 1, Price = 15, Labor = 0.2, Category = "0", Refinish = 0
                });
            }
        }

        // ==================== INNER PANEL (Rows 433-439) ====================
        private void UpdateInnerPanelFromUI()
        {
            ViewModel.InnerPanelOperations.Clear();

            string partName = IP_PartName?.Text ?? "panel";
            if (string.IsNullOrWhiteSpace(partName)) partName = "panel";
            double repairUnits = IP_RefinishUnits?.Value ?? 9;

            // Corrosion Protection (always added for inner panel)
            ViewModel.InnerPanelOperations.Add(new OperationRow
            {
                OperationType = "Rpr", Name = $"{partName} Corrosion Protection",
                Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.3
            });

            // Feather Edge & Block Sand (calculated from repair units * 0.25)
            double featherEdgeRefinish = repairUnits * 0.25;
            if (featherEdgeRefinish > 0)
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Feather Edge & Block Sand",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = Math.Round(featherEdgeRefinish, 2)
                });
            }

            // Backtape Jambs
            if (GetComboValue(IP_BacktapeJambs, "No") == "Yes")
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Refinish", Name = $"{partName} Backtape Jambs",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.3
                });
            }

            // Trial Fit Labor Unit
            double trialFitLabor = IP_TrialFitLabor?.Value ?? 0;
            if (trialFitLabor > 0)
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Trial Fit",
                    Quantity = 1, Price = 0, Labor = trialFitLabor, Category = "0", Refinish = 0
                });
            }

            // Material: Cavity Wax
            if (GetComboValue(IP_CavityWax, "No") == "Yes")
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Cavity Wax Injection",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Material: Seam Sealer
            if (GetComboValue(IP_SeamSealer, "No") == "Yes")
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Seam Sealer Cleanup, Mask, Test, Replicate",
                    Quantity = 1, Price = 0, Labor = 0, Category = "0", Refinish = 0.8
                });
            }

            // Material: Chip Guard
            if (GetComboValue(IP_ChipGuard, "No") == "Yes")
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Rpr", Name = $"{partName} Chip Guard Application",
                    Quantity = 1, Price = 10, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Material: Undercoat
            if (GetComboValue(IP_Undercoat, "No") == "Yes")
            {
                ViewModel.InnerPanelOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Undercoat Application",
                    Quantity = 1, Price = 15, Labor = 0.3, Category = "0", Refinish = 0
                });
            }
        }

        // ==================== GLASS (Rows 483-487) ====================
        private void UpdateGlassFromUI()
        {
            ViewModel.GlassOperations.Clear();

            string partName = GL_PartName?.Text ?? "glass";
            if (string.IsNullOrWhiteSpace(partName)) partName = "glass";

            string sizeOfPart = GetComboValue(GL_SizeOfPart, "Large");

            // Urethane Kit (always added for glass replacement)
            ViewModel.GlassOperations.Add(new OperationRow
            {
                OperationType = "Replace", Name = $"{partName} Urethane Kit",
                Quantity = 1, Price = 50, Labor = 0.8, Category = "0", Refinish = 0
            });

            // Glass Primer (always added for glass replacement)
            ViewModel.GlassOperations.Add(new OperationRow
            {
                OperationType = "Replace", Name = $"{partName} Glass Primer",
                Quantity = 1, Price = 25, Labor = 0.5, Category = "0", Refinish = 0
            });

            // Body Primer
            if (GetComboValue(GL_BodyPrimer, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "Replace", Name = $"{partName} Body Primer",
                    Quantity = 1, Price = 15, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Check Defrost
            if (GetComboValue(GL_CheckDefrost, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "Replace", Name = $"{partName} Check Defrost Operation",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Broken Glass Cleanup Labor Unit
            double brokenGlassCleanup = GL_BrokenGlassCleanup?.Value ?? 0;
            if (brokenGlassCleanup > 0)
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "Replace", Name = $"{partName} Broken Glass Cleanup",
                    Quantity = 1, Price = 0, Labor = brokenGlassCleanup, Category = "0", Refinish = 0
                });
            }

            // Material: Washer Fluid
            if (GetComboValue(GL_WasherFluid, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "Mat", Name = $"{partName} Washer Fluid Top-Off",
                    Quantity = 1, Price = 5, Labor = 0.1, Category = "0", Refinish = 0
                });
            }

            // Additional Parts: Inspection Sticker
            if (GetComboValue(GL_InspectionSticker, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Inspection Sticker Transfer",
                    Quantity = 1, Price = 0, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // Additional Parts: EZ Pass Velcro
            if (GetComboValue(GL_EZPassVelcro, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} EZ Pass Velcro Transfer",
                    Quantity = 1, Price = 5, Labor = 0.2, Category = "0", Refinish = 0
                });
            }

            // Equipment: Dash Cam
            if (GetComboValue(GL_DashCam, "No") == "Yes")
            {
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "R&I", Name = $"{partName} Dash Cam R&I",
                    Quantity = 1, Price = 0, Labor = 0.3, Category = "0", Refinish = 0
                });
            }

            // Add Ons: Window Tint
            if (GetComboValue(GL_WindowTint, "No") == "Yes")
            {
                double windowTintPrice = GL_WindowTintPrice?.Value ?? 0;
                ViewModel.GlassOperations.Add(new OperationRow
                {
                    OperationType = "Sub", Name = $"{partName} Window Tint Application",
                    Quantity = 1, Price = windowTintPrice, Labor = 0, Category = "0", Refinish = 0
                });
            }
        }

        private void ShowSubCategory(string category)
        {
            _currentCategory = category;

            // Reset all button styles
            ResetButtonStyles();

            // Hide all input panels
            PlasticBlendPanel.Visibility = Visibility.Collapsed;
            PlasticRepairPanel.Visibility = Visibility.Collapsed;
            PlasticReplacePanel.Visibility = Visibility.Collapsed;
            SmcPanel.Visibility = Visibility.Collapsed;
            MetalBlendPanel.Visibility = Visibility.Collapsed;
            MetalRepairPanel.Visibility = Visibility.Collapsed;
            MetalReplacePanel.Visibility = Visibility.Collapsed;
            WeldedMetalPanel.Visibility = Visibility.Collapsed;
            InnerPanelPanel.Visibility = Visibility.Collapsed;
            GlassPanel.Visibility = Visibility.Collapsed;

            // Show selected panel and highlight button
            switch (category)
            {
                case "plasticblend":
                    PlasticBlendPanel.Visibility = Visibility.Visible;
                    HighlightButton(PlasticBlendBtn);
                    UpdatePlasticBlendFromUI();
                    break;
                case "plasticrepair":
                    PlasticRepairPanel.Visibility = Visibility.Visible;
                    HighlightButton(PlasticRepairBtn);
                    UpdatePlasticRepairFromUI();
                    break;
                case "plasticreplace":
                    PlasticReplacePanel.Visibility = Visibility.Visible;
                    HighlightButton(PlasticReplaceBtn);
                    UpdatePlasticReplaceFromUI();
                    break;
                case "smc":
                    SmcPanel.Visibility = Visibility.Visible;
                    HighlightButton(SmcBtn);
                    UpdateSmcFromUI();
                    break;
                case "metalblend":
                    MetalBlendPanel.Visibility = Visibility.Visible;
                    HighlightButton(MetalBlendBtn);
                    UpdateMetalBlendFromUI();
                    break;
                case "metalrepair":
                    MetalRepairPanel.Visibility = Visibility.Visible;
                    HighlightButton(MetalRepairBtn);
                    UpdateMetalRepairFromUI();
                    break;
                case "boltedmetal":
                    MetalReplacePanel.Visibility = Visibility.Visible;
                    HighlightButton(BoltedMetalBtn);
                    UpdateBoltedMetalFromUI();
                    break;
                case "weldedmetal":
                    WeldedMetalPanel.Visibility = Visibility.Visible;
                    HighlightButton(WeldedMetalBtn);
                    UpdateWeldedMetalFromUI();
                    break;
                case "innerpanel":
                    InnerPanelPanel.Visibility = Visibility.Visible;
                    HighlightButton(InnerPanelBtn);
                    UpdateInnerPanelFromUI();
                    break;
                case "glass":
                    GlassPanel.Visibility = Visibility.Visible;
                    HighlightButton(GlassBtn);
                    UpdateGlassFromUI();
                    break;
            }

            ViewModel.UpdateCurrentCategoryTotals(GetCurrentCategoryName());
            UpdateFooterTotals();
            UpdateExportLineCount();
            UpdateSummary();
        }

        private void ResetButtonStyles()
        {
            PlasticBlendBtn.Background = null;
            PlasticBlendBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            PlasticRepairBtn.Background = null;
            PlasticRepairBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            PlasticReplaceBtn.Background = null;
            PlasticReplaceBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            SmcBtn.Background = null;
            SmcBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            MetalBlendBtn.Background = null;
            MetalBlendBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            MetalRepairBtn.Background = null;
            MetalRepairBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            BoltedMetalBtn.Background = null;
            BoltedMetalBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            WeldedMetalBtn.Background = null;
            WeldedMetalBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            InnerPanelBtn.Background = null;
            InnerPanelBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            GlassBtn.Background = null;
            GlassBtn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private void HighlightButton(Button btn)
        {
            btn.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            btn.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private void UpdateFooterTotals()
        {
            // Handled by UpdateSummary()
        }

        private void UpdateSummary()
        {
            int ops = ViewModel.CurrentCategoryOperations;
            double price = 0;
            double labor = 0;
            double refinish = 0;

            foreach (var op in ViewModel.CurrentOperations)
            {
                price += op.Price;
                labor += op.Labor;
                refinish += op.Refinish;
            }

            SummaryOps.Text = $"{ops}";
            SummaryPrice.Text = $"{price:F2}";
            SummaryLabor.Text = $"{labor:F1}";
            SummaryRefinish.Text = $"{refinish:F2}";
        }

        private void UpdateExportLineCount()
        {
            int count = GetCurrentCategoryOperationCount();
            ExportPanel.LineCount = count;
            ExportPanel.ResetStatus();
        }
    }
}
