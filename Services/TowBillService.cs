#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for managing Tow Bills with customizable shop settings
    /// </summary>
    public class TowBillService
    {
        private static TowBillService? _instance;
        public static TowBillService Instance => _instance ??= new TowBillService();

        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private TowBillSettings _settings;

        private TowBillService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");
            _settingsFile = Path.Combine(_settingsFolder, "TowBillSettings.json");
            _settings = LoadSettings();
        }

        /// <summary>
        /// Get current tow bill settings
        /// </summary>
        public TowBillSettings GetSettings() => _settings;

        /// <summary>
        /// Save tow bill settings
        /// </summary>
        public void SaveSettings(TowBillSettings settings)
        {
            _settings = settings;
            try
            {
                if (!Directory.Exists(_settingsFolder))
                {
                    Directory.CreateDirectory(_settingsFolder);
                }

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine($"[TowBill] Settings saved to {_settingsFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TowBill] Error saving settings: {ex.Message}");
            }
        }

        private TowBillSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var settings = JsonSerializer.Deserialize<TowBillSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (settings != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[TowBill] Settings loaded from file");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TowBill] Error loading settings: {ex.Message}");
            }

            // Return default settings
            return GetDefaultSettings();
        }

        private TowBillSettings GetDefaultSettings()
        {
            return new TowBillSettings
            {
                DefaultMileageRate = 0,
                IncludeTaxLine = true,
                TaxRate = 0
            };
        }

        /// <summary>
        /// Reset settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            _settings = GetDefaultSettings();
            SaveSettings(_settings);
        }

        /// <summary>
        /// Generate PDF for a tow bill
        /// </summary>
        public string GeneratePdf(TowBillData billData)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"towbill_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0.5f, Unit.Inch);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(c => ComposeTowBillHeader(c, billData));
                    page.Content().Element(c => ComposeTowBillContent(c, billData));
                    page.Footer().Element(c => ComposeTowBillFooter(c));
                });
            }).GeneratePdf(tempPath);

            return tempPath;
        }

        private void ComposeTowBillHeader(IContainer container, TowBillData data)
        {
            container.Column(column =>
            {
                column.Item().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(shopCol =>
                    {
                        var shopName = !string.IsNullOrEmpty(data.ShopName)
                            ? data.ShopName
                            : "Tow Bill";
                        shopCol.Item().Text(shopName).FontSize(18).Bold().FontColor(Colors.White);
                    });

                    row.ConstantItem(150).AlignRight().Column(titleCol =>
                    {
                        titleCol.Item().Text("TOW BILL").FontSize(20).Bold().FontColor(Colors.White);
                        titleCol.Item().PaddingTop(4).Text($"Date: {data.TowDate:MM/dd/yyyy}")
                            .FontSize(10).FontColor(Colors.Grey.Lighten2);
                    });
                });

                column.Item().PaddingTop(10);
            });
        }

        private void ComposeTowBillContent(IContainer container, TowBillData data)
        {
            container.Column(column =>
            {
                // Info row: RO # and Vehicle
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(infoRow =>
                {
                    infoRow.RelativeItem().Column(left =>
                    {
                        AddFieldRow(left, "RO #:", data.RoNumber);
                    });
                    infoRow.ConstantItem(20);
                    infoRow.RelativeItem().Column(right =>
                    {
                        AddFieldRow(right, "Vehicle:", data.VehicleYMM);
                    });
                });

                // Notes section
                if (!string.IsNullOrWhiteSpace(data.Notes))
                {
                    column.Item().PaddingTop(10).Border(1).BorderColor(Colors.Grey.Lighten1).Column(notesCol =>
                    {
                        notesCol.Item().Background(Colors.Grey.Lighten3).Padding(6)
                            .Text("Notes").FontSize(10).Bold();
                        notesCol.Item().Padding(8).Text(data.Notes).FontSize(10);
                    });
                }

                column.Item().PaddingTop(10);

                // Equipment charges table (only checked items)
                var hasCharges = data.Charges.Any(c => c.Amount > 0);
                var hasMileage = data.Miles > 0 && data.RatePerMile > 0;

                if (hasCharges || hasMileage)
                {
                    column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Column(chargesCol =>
                    {
                        chargesCol.Item().Background(Colors.Orange.Darken2).Padding(6)
                            .Text("Charges").FontSize(11).Bold().FontColor(Colors.White);

                        chargesCol.Item().Padding(8).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Description
                                columns.ConstantColumn(100); // Amount
                            });

                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                    .Text("Description").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight()
                                    .Text("Amount").Bold();
                            });

                            decimal subtotal = 0;
                            int rowIndex = 0;

                            // Equipment charge rows
                            foreach (var charge in data.Charges)
                            {
                                if (charge.Amount > 0)
                                {
                                    var bgColor = rowIndex % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                                    table.Cell().Background(bgColor).Padding(4).Text(charge.Description);
                                    table.Cell().Background(bgColor).Padding(4).AlignRight()
                                        .Text($"${charge.Amount:N2}");

                                    subtotal += charge.Amount;
                                    rowIndex++;
                                }
                            }

                            // Mileage line
                            if (hasMileage)
                            {
                                var bgColor = rowIndex % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                                var mileageTotal = data.MileageTotal;

                                table.Cell().Background(bgColor).Padding(4)
                                    .Text($"Mileage — {data.Miles} miles @ ${data.RatePerMile:N2}/mi");
                                table.Cell().Background(bgColor).Padding(4).AlignRight()
                                    .Text($"${mileageTotal:N2}");

                                subtotal += mileageTotal;
                            }

                            // Subtotal
                            table.Cell().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                .PaddingTop(6).PaddingRight(10).AlignRight().Text("Subtotal:").Bold();
                            table.Cell().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                .PaddingTop(6).AlignRight().Text($"${subtotal:N2}").Bold();

                            // Tax
                            if (data.TaxPercent > 0)
                            {
                                var tax = subtotal * (data.TaxPercent / 100);
                                table.Cell().PaddingRight(10).AlignRight()
                                    .Text($"Tax ({data.TaxPercent:N2}%):");
                                table.Cell().AlignRight().Text($"${tax:N2}");

                                var total = subtotal + tax;
                                table.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                                    .Text("TOTAL DUE:").Bold().FontColor(Colors.White);
                                table.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                                    .Text($"${total:N2}").Bold().FontColor(Colors.White).FontSize(12);
                            }
                            else
                            {
                                // No tax — subtotal IS total
                                table.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                                    .Text("TOTAL DUE:").Bold().FontColor(Colors.White);
                                table.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                                    .Text($"${subtotal:N2}").Bold().FontColor(Colors.White).FontSize(12);
                            }
                        });
                    });
                }
            });
        }

        private void AddFieldRow(ColumnDescriptor column, string label, string? value)
        {
            column.Item().PaddingBottom(4).Row(row =>
            {
                row.ConstantItem(80).Text(label).FontSize(9).Bold();
                row.RelativeItem().Text(string.IsNullOrEmpty(value) ? "________________" : value).FontSize(9);
            });
        }

        private void ComposeTowBillFooter(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(15).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(10);

                // Signature lines
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(sig =>
                    {
                        sig.Item().Height(25).BorderBottom(1).BorderColor(Colors.Black);
                        sig.Item().PaddingTop(2).Text("Customer Signature").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(30);

                    row.ConstantItem(120).Column(sig =>
                    {
                        sig.Item().Height(25).BorderBottom(1).BorderColor(Colors.Black);
                        sig.Item().PaddingTop(2).Text("Date").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(30);

                    row.RelativeItem().Column(sig =>
                    {
                        sig.Item().Height(25).BorderBottom(1).BorderColor(Colors.Black);
                        sig.Item().PaddingTop(2).Text("Shop Representative").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }
    }

    #region Tow Bill Data Models

    public class TowBillSettings
    {
        public decimal DefaultMileageRate { get; set; }
        public bool IncludeTaxLine { get; set; }
        public decimal TaxRate { get; set; }

        // Legacy fields kept for deserialization compatibility
        public string ShopName { get; set; } = "";
        public string ShopAddress { get; set; } = "";
        public string ShopCity { get; set; } = "";
        public string ShopState { get; set; } = "";
        public string ShopZip { get; set; } = "";
        public string ShopPhone { get; set; } = "";
        public string ShopFax { get; set; } = "";
        public string ShopEmail { get; set; } = "";
        public decimal DefaultTowRate { get; set; }
        public decimal DefaultStorageRate { get; set; }
        public decimal DefaultAdminFee { get; set; }
        public string PaymentTerms { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<TowBillChargeItem> ChargeItems { get; set; } = new();
    }

    public class TowBillChargeItem
    {
        public string Name { get; set; } = "";
        public decimal DefaultAmount { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsPerMile { get; set; }
        public bool IsPerDay { get; set; }
    }

    public class TowBillData
    {
        public string ShopName { get; set; } = "";
        public string RoNumber { get; set; } = "";
        public string VehicleYMM { get; set; } = "";
        public DateTime TowDate { get; set; } = DateTime.Today;
        public string Notes { get; set; } = "";

        // Mileage
        public decimal Miles { get; set; }
        public decimal RatePerMile { get; set; }
        public decimal MileageTotal => Miles * RatePerMile;

        // Tax
        public decimal TaxPercent { get; set; }

        // Equipment charges
        public List<TowBillCharge> Charges { get; set; } = new();

        // Computed totals
        public decimal EquipmentSubtotal => Charges.Sum(c => c.Amount);
        public decimal Subtotal => EquipmentSubtotal + MileageTotal;
        public decimal TaxAmount => TaxPercent > 0 ? Subtotal * (TaxPercent / 100) : 0;
        public decimal ComputedTotal => Subtotal + TaxAmount;

        // Legacy fields kept for backward compatibility
        public string InvoiceNumber { get; set; } = "";
        public string VehicleColor { get; set; } = "";
        public string VehicleVin { get; set; } = "";
        public string LicensePlate { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CustomerCityStateZip { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string InsuranceCompany { get; set; } = "";
        public string ClaimNumber { get; set; } = "";
        public string PickupLocation { get; set; } = "";
        public string DeliveryLocation { get; set; } = "";
        public int TowMileage { get; set; }
    }

    public class TowBillCharge
    {
        public string Description { get; set; } = "";
        public string? Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    #endregion
}
