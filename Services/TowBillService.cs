#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
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
                ShopName = "Your Shop Name",
                ShopAddress = "123 Main Street",
                ShopCity = "City",
                ShopState = "ST",
                ShopZip = "12345",
                ShopPhone = "(555) 555-5555",
                ShopFax = "",
                ShopEmail = "",
                DefaultTowRate = 0,
                DefaultStorageRate = 0,
                DefaultAdminFee = 0,
                DefaultMileageRate = 0,
                IncludeTaxLine = true,
                TaxRate = 0,
                PaymentTerms = "Payment due upon release of vehicle",
                Notes = "",
                ChargeItems = new List<TowBillChargeItem>
                {
                    new() { Name = "Basic Tow", DefaultAmount = 0, IsEnabled = true },
                    new() { Name = "Mileage", DefaultAmount = 0, IsEnabled = true, IsPerMile = true },
                    new() { Name = "Storage (per day)", DefaultAmount = 0, IsEnabled = true, IsPerDay = true },
                    new() { Name = "Admin Fee", DefaultAmount = 0, IsEnabled = true },
                    new() { Name = "Winch Service", DefaultAmount = 0, IsEnabled = false },
                    new() { Name = "After Hours", DefaultAmount = 0, IsEnabled = false },
                    new() { Name = "Fuel Surcharge", DefaultAmount = 0, IsEnabled = false },
                    new() { Name = "Dolly Service", DefaultAmount = 0, IsEnabled = false }
                }
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
                    page.Footer().Element(c => ComposeTowBillFooter(c, billData));
                });
            }).GeneratePdf(tempPath);

            return tempPath;
        }

        private void ComposeTowBillHeader(IContainer container, TowBillData data)
        {
            container.Column(column =>
            {
                // Shop header
                column.Item().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(shopCol =>
                    {
                        shopCol.Item().Text(_settings.ShopName).FontSize(18).Bold().FontColor(Colors.White);
                        shopCol.Item().Text($"{_settings.ShopAddress}").FontSize(9).FontColor(Colors.Grey.Lighten2);
                        shopCol.Item().Text($"{_settings.ShopCity}, {_settings.ShopState} {_settings.ShopZip}")
                            .FontSize(9).FontColor(Colors.Grey.Lighten2);
                        if (!string.IsNullOrEmpty(_settings.ShopPhone))
                            shopCol.Item().Text($"Phone: {_settings.ShopPhone}").FontSize(9).FontColor(Colors.Grey.Lighten2);
                    });

                    row.ConstantItem(150).AlignRight().Column(titleCol =>
                    {
                        titleCol.Item().Text("TOW BILL").FontSize(20).Bold().FontColor(Colors.White);
                        titleCol.Item().PaddingTop(4).Text($"Date: {data.TowDate:MM/dd/yyyy}")
                            .FontSize(10).FontColor(Colors.Grey.Lighten2);
                        if (!string.IsNullOrEmpty(data.InvoiceNumber))
                            titleCol.Item().Text($"Invoice #: {data.InvoiceNumber}")
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
                // Vehicle & Customer Info side by side
                column.Item().Row(infoRow =>
                {
                    // Vehicle Information
                    infoRow.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Column(vehCol =>
                    {
                        vehCol.Item().Background(Colors.Blue.Darken2).Padding(6)
                            .Text("Vehicle Information").FontSize(11).Bold().FontColor(Colors.White);

                        vehCol.Item().Padding(8).Column(fields =>
                        {
                            AddFieldRow(fields, "Year/Make/Model:", data.VehicleYMM);
                            AddFieldRow(fields, "Color:", data.VehicleColor);
                            AddFieldRow(fields, "VIN:", data.VehicleVin);
                            AddFieldRow(fields, "License Plate:", data.LicensePlate);
                            AddFieldRow(fields, "RO Number:", data.RoNumber);
                        });
                    });

                    infoRow.ConstantItem(10);

                    // Customer Information
                    infoRow.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Column(custCol =>
                    {
                        custCol.Item().Background(Colors.Blue.Darken2).Padding(6)
                            .Text("Customer Information").FontSize(11).Bold().FontColor(Colors.White);

                        custCol.Item().Padding(8).Column(fields =>
                        {
                            AddFieldRow(fields, "Name:", data.CustomerName);
                            AddFieldRow(fields, "Address:", data.CustomerAddress);
                            AddFieldRow(fields, "City/State/Zip:", data.CustomerCityStateZip);
                            AddFieldRow(fields, "Phone:", data.CustomerPhone);
                            AddFieldRow(fields, "Insurance:", data.InsuranceCompany);
                            AddFieldRow(fields, "Claim #:", data.ClaimNumber);
                        });
                    });
                });

                column.Item().PaddingTop(10);

                // Tow Information
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Column(towCol =>
                {
                    towCol.Item().Background(Colors.Green.Darken2).Padding(6)
                        .Text("Tow Information").FontSize(11).Bold().FontColor(Colors.White);

                    towCol.Item().Padding(8).Row(towRow =>
                    {
                        towRow.RelativeItem().Column(left =>
                        {
                            AddFieldRow(left, "Pickup Location:", data.PickupLocation);
                            AddFieldRow(left, "Delivery Location:", data.DeliveryLocation);
                        });
                        towRow.ConstantItem(20);
                        towRow.ConstantItem(200).Column(right =>
                        {
                            AddFieldRow(right, "Tow Date:", data.TowDate.ToString("MM/dd/yyyy"));
                            AddFieldRow(right, "Mileage:", $"{data.TowMileage} miles");
                        });
                    });
                });

                column.Item().PaddingTop(10);

                // Charges Table
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Column(chargesCol =>
                {
                    chargesCol.Item().Background(Colors.Orange.Darken2).Padding(6)
                        .Text("Charges").FontSize(11).Bold().FontColor(Colors.White);

                    chargesCol.Item().Padding(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Description
                            columns.ConstantColumn(80); // Qty/Rate
                            columns.ConstantColumn(100); // Amount
                        });

                        // Header
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .Text("Description").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter()
                                .Text("Qty/Rate").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight()
                                .Text("Amount").Bold();
                        });

                        decimal subtotal = 0;
                        int rowIndex = 0;

                        // Charge rows
                        foreach (var charge in data.Charges)
                        {
                            if (charge.Amount > 0)
                            {
                                var bgColor = rowIndex % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                                table.Cell().Background(bgColor).Padding(4).Text(charge.Description);
                                table.Cell().Background(bgColor).Padding(4).AlignCenter()
                                    .Text(charge.Quantity ?? "");
                                table.Cell().Background(bgColor).Padding(4).AlignRight()
                                    .Text($"${charge.Amount:N2}");

                                subtotal += charge.Amount;
                                rowIndex++;
                            }
                        }

                        // Subtotal
                        table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Colors.Grey.Medium)
                            .PaddingTop(6).PaddingRight(10).AlignRight().Text("Subtotal:").Bold();
                        table.Cell().BorderTop(1).BorderColor(Colors.Grey.Medium)
                            .PaddingTop(6).AlignRight().Text($"${subtotal:N2}").Bold();

                        // Tax if applicable
                        if (_settings.IncludeTaxLine && data.TaxAmount > 0)
                        {
                            table.Cell().ColumnSpan(2).PaddingRight(10).AlignRight()
                                .Text($"Tax ({_settings.TaxRate}%):");
                            table.Cell().AlignRight().Text($"${data.TaxAmount:N2}");
                        }

                        // Total
                        var total = subtotal + data.TaxAmount;
                        table.Cell().ColumnSpan(2).Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                            .Text("TOTAL DUE:").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight()
                            .Text($"${total:N2}").Bold().FontColor(Colors.White).FontSize(12);
                    });
                });

                // Notes section if any
                if (!string.IsNullOrWhiteSpace(data.Notes))
                {
                    column.Item().PaddingTop(10).Border(1).BorderColor(Colors.Grey.Lighten1).Column(notesCol =>
                    {
                        notesCol.Item().Background(Colors.Grey.Lighten3).Padding(4)
                            .Text("Notes").FontSize(9).Bold();
                        notesCol.Item().Padding(6).Text(data.Notes).FontSize(9);
                    });
                }
            });
        }

        private void AddFieldRow(ColumnDescriptor column, string label, string? value)
        {
            column.Item().PaddingBottom(4).Row(row =>
            {
                row.ConstantItem(100).Text(label).FontSize(9).Bold();
                row.RelativeItem().Text(value ?? "________________").FontSize(9);
            });
        }

        private void ComposeTowBillFooter(IContainer container, TowBillData data)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(15).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(10);

                // Payment terms
                if (!string.IsNullOrEmpty(_settings.PaymentTerms))
                {
                    column.Item().Text(_settings.PaymentTerms).FontSize(9).Italic();
                }

                column.Item().PaddingTop(15);

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

                // Shop contact
                column.Item().PaddingTop(15).AlignCenter().Text(text =>
                {
                    if (!string.IsNullOrEmpty(_settings.ShopEmail))
                        text.Span($"Email: {_settings.ShopEmail}  |  ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrEmpty(_settings.ShopFax))
                        text.Span($"Fax: {_settings.ShopFax}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }
    }

    #region Tow Bill Data Models

    public class TowBillSettings
    {
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
        public decimal DefaultMileageRate { get; set; }

        public bool IncludeTaxLine { get; set; }
        public decimal TaxRate { get; set; }

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
        public string InvoiceNumber { get; set; } = "";
        public DateTime TowDate { get; set; } = DateTime.Today;

        // Vehicle Info
        public string VehicleYMM { get; set; } = "";
        public string VehicleColor { get; set; } = "";
        public string VehicleVin { get; set; } = "";
        public string LicensePlate { get; set; } = "";
        public string RoNumber { get; set; } = "";

        // Customer Info
        public string CustomerName { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CustomerCityStateZip { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string InsuranceCompany { get; set; } = "";
        public string ClaimNumber { get; set; } = "";

        // Tow Info
        public string PickupLocation { get; set; } = "";
        public string DeliveryLocation { get; set; } = "";
        public int TowMileage { get; set; }

        // Charges
        public List<TowBillCharge> Charges { get; set; } = new();
        public decimal TaxAmount { get; set; }

        public string Notes { get; set; } = "";
    }

    public class TowBillCharge
    {
        public string Description { get; set; } = "";
        public string? Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    #endregion
}
