#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace McStudDesktop.Services;

/// <summary>
/// Service for generating PDF documents for shop invoices and quotes.
/// Handles Color Tint Invoice, Shop Stock Invoice, and Vehicle Protection Pricing.
/// </summary>
public class ShopDocsPdfService
{
    private static ShopDocsPdfService? _instance;
    public static ShopDocsPdfService Instance => _instance ??= new ShopDocsPdfService();

    private ShopDocsPdfService()
    {
        // Ensure QuestPDF license is set
        QuestPDF.Settings.License = LicenseType.Community;
    }

    #region Color Tint Invoice PDF

    public string GenerateColorTintInvoicePdf(ColorTintInvoicePdfData data, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("ColorTint_Invoice");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeInvoiceHeader(c, "COLOR TINT INVOICE", data.InvoiceNumber, data.Date));

                page.Content().Element(content =>
                {
                    content.PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        // Customer/Job Info
                        column.Item().Element(c => ComposeCustomerInfo(c, data.BillTo, data.JobNumber, data.Vehicle));

                        // Items table
                        column.Item().Element(c => ComposeColorTintItemsTable(c, data.Items, data.ShowCostColumn, data.ShowListPriceColumn));

                        // Totals
                        column.Item().Element(c => ComposeInvoiceTotalsWithPricing(c, data.Subtotal, data.TaxRate, data.Tax, data.Total,
                            data.ShowCostColumn, data.CostSubtotal, data.ShowListPriceColumn, data.ListSubtotal));
                    });
                });

                page.Footer().Element(c => ComposeFooter(c, "Color Tint Invoice"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    private void ComposeColorTintItemsTable(IContainer container, List<ColorTintInvoicePdfItem> items,
        bool showCost = false, bool showList = false)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(40);  // Qty
                columns.ConstantColumn(100); // Part #
                columns.RelativeColumn(2);   // Description
                if (showCost) columns.ConstantColumn(70); // Cost
                columns.ConstantColumn(70);  // Unit Price
                if (showList) columns.ConstantColumn(70); // List
                columns.ConstantColumn(70);  // Total
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Background(Colors.Grey.Darken3).Padding(6).Text("Qty").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Grey.Darken3).Padding(6).Text("Part #").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Grey.Darken3).Padding(6).Text("Description").FontColor(Colors.White).Bold();
                if (showCost)
                    header.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight().Text("Cost").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight().Text("Bill Price").FontColor(Colors.White).Bold();
                if (showList)
                    header.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight().Text("List Price").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Grey.Darken3).Padding(6).AlignRight().Text("Total").FontColor(Colors.White).Bold();
            });

            // Items
            foreach (var item in items)
            {
                var bgColor = items.IndexOf(item) % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                table.Cell().Background(bgColor).Padding(5).Text(item.Quantity.ToString());
                table.Cell().Background(bgColor).Padding(5).Text(item.PartNumber ?? "");
                table.Cell().Background(bgColor).Padding(5).Text(item.Description ?? "");
                if (showCost)
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.CostPrice:F2}");
                table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.UnitPrice:F2}");
                if (showList)
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.ListPrice:F2}");
                table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.LineTotal:F2}");
            }
        });
    }

    #endregion

    #region Shop Stock Invoice PDF

    public string GenerateShopStockInvoicePdf(ShopStockInvoicePdfData data, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("ShopStock_Invoice");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeShopStockHeader(c, data));

                page.Content().Element(content =>
                {
                    content.PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        // Customer/Job Info
                        column.Item().Element(c => ComposeCustomerInfo(c, data.CustomerName, data.JobNumber, data.Vehicle));

                        // Items table
                        column.Item().Element(c => ComposeShopStockItemsTable(c, data.Items, data.ShowCostColumn, data.ShowListPriceColumn));

                        // Totals
                        column.Item().Element(c => ComposeInvoiceTotalsWithPricing(c, data.Subtotal, data.TaxRate, data.Tax, data.Total,
                            data.ShowCostColumn, data.CostSubtotal, data.ShowListPriceColumn, data.ListSubtotal));
                    });
                });

                page.Footer().Element(c => ComposeFooter(c, "Shop Stock Parts Invoice"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    private void ComposeShopStockHeader(IContainer container, ShopStockInvoicePdfData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(data.ShopName ?? "Shop Stock Parts")
                        .FontSize(18)
                        .Bold()
                        .FontColor(Colors.Blue.Darken2);

                    if (!string.IsNullOrEmpty(data.ShopAddress))
                        col.Item().Text(data.ShopAddress).FontSize(9);
                    if (!string.IsNullOrEmpty(data.ShopCity))
                        col.Item().Text(data.ShopCity).FontSize(9);
                    if (!string.IsNullOrEmpty(data.ShopPhone))
                        col.Item().Text(data.ShopPhone).FontSize(9);
                });

                row.ConstantItem(180).AlignRight().Column(col =>
                {
                    col.Item().Text("PARTS INVOICE")
                        .FontSize(14)
                        .Bold()
                        .FontColor(Colors.Grey.Darken2);

                    col.Item().PaddingTop(5).Row(r =>
                    {
                        r.AutoItem().Text("Invoice #: ").Bold();
                        r.AutoItem().Text(data.InvoiceNumber ?? "");
                    });

                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Date: ").Bold();
                        r.AutoItem().Text(data.Date.ToString("MM/dd/yyyy"));
                    });
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeShopStockItemsTable(IContainer container, List<ShopStockInvoicePdfItem> items,
        bool showCost = false, bool showList = false)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(40);  // Qty
                columns.ConstantColumn(90);  // Part #
                columns.RelativeColumn(2);   // Description
                if (showCost) columns.ConstantColumn(70); // Cost
                columns.ConstantColumn(70);  // Unit Price
                if (showList) columns.ConstantColumn(70); // List
                columns.ConstantColumn(70);  // Total
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Qty").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Part #").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Description").FontColor(Colors.White).Bold();
                if (showCost)
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Cost").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Bill Price").FontColor(Colors.White).Bold();
                if (showList)
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("List Price").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Total").FontColor(Colors.White).Bold();
            });

            // Items
            foreach (var item in items)
            {
                var bgColor = items.IndexOf(item) % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                table.Cell().Background(bgColor).Padding(5).Text(item.Quantity.ToString());
                table.Cell().Background(bgColor).Padding(5).Text(item.PartNumber ?? "");
                table.Cell().Background(bgColor).Padding(5).Text(item.Description ?? "");
                if (showCost)
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.CostPrice:F2}");
                table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.UnitPrice:F2}");
                if (showList)
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.ListPrice:F2}");
                table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${item.LineTotal:F2}");
            }
        });
    }

    #endregion

    #region Vehicle Protection Pricing PDF

    public string GenerateVehicleProtectionPdf(VehicleProtectionPdfData data, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("VehicleProtection_Quote");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeVehicleProtectionHeader(c, data));

                page.Content().Element(content =>
                {
                    content.PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        // Service and Vehicle Info
                        column.Item().Element(c => ComposeVehicleProtectionInfo(c, data));

                        // Selected Panels
                        column.Item().Element(c => ComposeVehicleProtectionPanels(c, data.Panels));

                        // Product Info
                        if (!string.IsNullOrEmpty(data.ProductName))
                        {
                            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                            {
                                row.AutoItem().Text("Product: ").Bold();
                                row.AutoItem().Text(data.ProductName);
                            });
                        }

                        // Totals
                        column.Item().Element(c => ComposeVehicleProtectionTotals(c, data));
                    });
                });

                page.Footer().Element(c => ComposeFooter(c, "Vehicle Protection Quote"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    private void ComposeVehicleProtectionHeader(IContainer container, VehicleProtectionPdfData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("VEHICLE PROTECTION QUOTE")
                        .FontSize(18)
                        .Bold()
                        .FontColor(Colors.Blue.Darken2);

                    col.Item().PaddingTop(5).Text($"Generated: {data.Date:MMMM dd, yyyy}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    var serviceColor = data.ServiceType switch
                    {
                        "ceramic" => Colors.Orange.Darken2,
                        "vinyl" => Colors.Purple.Darken2,
                        _ => Colors.Blue.Darken2
                    };

                    col.Item().Text(data.ServiceTypeName ?? "PPF")
                        .FontSize(14)
                        .Bold()
                        .FontColor(serviceColor);
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeVehicleProtectionInfo(IContainer container, VehicleProtectionPdfData data)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (!string.IsNullOrEmpty(data.CustomerName))
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Customer: ").Bold();
                        r.AutoItem().Text(data.CustomerName);
                    });
                }

                if (!string.IsNullOrEmpty(data.VehicleStyle))
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Vehicle Type: ").Bold();
                        r.AutoItem().Text(data.VehicleStyle);
                    });
                }
            });
        });
    }

    private void ComposeVehicleProtectionPanels(IContainer container, List<VehicleProtectionPdfPanel> panels)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);   // Panel Name
                columns.ConstantColumn(80);  // Category
                columns.ConstantColumn(80);  // Price
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Panel").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Category").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Price").FontColor(Colors.White).Bold();
            });

            // Panels
            foreach (var panel in panels)
            {
                var bgColor = panels.IndexOf(panel) % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                table.Cell().Background(bgColor).Padding(5).Text(panel.Name ?? "");
                table.Cell().Background(bgColor).Padding(5).Text(panel.Category ?? "");
                table.Cell().Background(bgColor).Padding(5).AlignRight().Text($"${panel.Price:F2}");
            }
        });
    }

    private void ComposeVehicleProtectionTotals(IContainer container, VehicleProtectionPdfData data)
    {
        container.AlignRight().Width(250).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:");
                    row.AutoItem().Text($"${data.Subtotal:F2}");
                });

                if (data.DiscountPercent > 0)
                {
                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Text($"Volume Discount ({data.DiscountPercent}%):")
                            .FontColor(Colors.Green.Darken2);
                        row.AutoItem().Text($"-${data.DiscountAmount:F2}")
                            .FontColor(Colors.Green.Darken2);
                    });
                }

                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL:").Bold().FontSize(14);
                    row.AutoItem().Text($"${data.Total:F2}").Bold().FontSize(14);
                });
            });

            // Discount notice
            if (data.DiscountPercent > 0)
            {
                column.Item().PaddingTop(8).Text($"Multi-panel discount applied: {data.PanelCount} panels selected")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken1)
                    .AlignCenter();
            }
        });
    }

    #endregion

    #region Shared Components

    private void ComposeInvoiceHeader(IContainer container, string title, string? invoiceNumber, DateTime date)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(title)
                    .FontSize(18)
                    .Bold()
                    .FontColor(Colors.Blue.Darken2);

                row.ConstantItem(180).AlignRight().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Invoice #: ").Bold();
                        r.AutoItem().Text(invoiceNumber ?? "");
                    });

                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Date: ").Bold();
                        r.AutoItem().Text(date.ToString("MM/dd/yyyy"));
                    });
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeCustomerInfo(IContainer container, string? billTo, string? jobNumber, string? vehicle)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (!string.IsNullOrEmpty(billTo))
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Bill To: ").Bold();
                        r.AutoItem().Text(billTo);
                    });
                }

                if (!string.IsNullOrEmpty(jobNumber))
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Job #: ").Bold();
                        r.AutoItem().Text(jobNumber);
                    });
                }

                if (!string.IsNullOrEmpty(vehicle))
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().Text("Vehicle: ").Bold();
                        r.AutoItem().Text(vehicle);
                    });
                }
            });
        });
    }

    private void ComposeInvoiceTotals(IContainer container, decimal subtotal, decimal taxRate, decimal tax, decimal total)
    {
        container.AlignRight().Width(200).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3).Padding(10).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:");
                    row.AutoItem().Text($"${subtotal:F2}");
                });

                col.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text($"Tax ({taxRate}%):");
                    row.AutoItem().Text($"${tax:F2}");
                });

                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL:").Bold().FontSize(14);
                    row.AutoItem().Text($"${total:F2}").Bold().FontSize(14);
                });
            });
        });
    }

    private void ComposeInvoiceTotalsWithPricing(IContainer container, decimal subtotal, decimal taxRate, decimal tax, decimal total,
        bool showCost, decimal costSubtotal, bool showList, decimal listSubtotal)
    {
        container.AlignRight().Width(showCost || showList ? 300 : 200).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3).Padding(10).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:");
                    row.AutoItem().Text($"${subtotal:F2}");
                });

                if (showCost)
                {
                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("Cost Total:").FontSize(9).FontColor(Colors.Grey.Darken1);
                        row.AutoItem().Text($"${costSubtotal:F2}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    // Profit margin when cost is visible
                    var margin = subtotal > 0 && costSubtotal > 0 ? ((subtotal - costSubtotal) / subtotal * 100) : 0;
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Margin:").FontSize(9).FontColor(Colors.Green.Darken2);
                        row.AutoItem().Text($"{margin:F1}%").FontSize(9).FontColor(Colors.Green.Darken2);
                    });
                }

                if (showList)
                {
                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("List Total:").FontSize(9).FontColor(Colors.Grey.Darken1);
                        row.AutoItem().Text($"${listSubtotal:F2}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                }

                if (taxRate > 0)
                {
                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Text($"Tax ({taxRate}%):");
                        row.AutoItem().Text($"${tax:F2}");
                    });
                }

                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL:").Bold().FontSize(14);
                    row.AutoItem().Text($"${total:F2}").Bold().FontSize(14);
                });
            });
        });
    }

    private void ComposeFooter(IContainer container, string docType)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
            text.Span($" | McStud Tool - {docType}")
                .FontSize(8)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    #endregion

    #region Price Catalog PDF

    public string GeneratePriceCatalogPdf(PriceCatalog catalog, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("PriceCatalog");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                    {
                        row.RelativeItem().Text(catalog.Name.ToUpperInvariant())
                            .FontSize(16).Bold().FontColor(Colors.White);
                        row.ConstantItem(200).AlignRight().Column(right =>
                        {
                            if (!string.IsNullOrEmpty(catalog.Supplier))
                                right.Item().Text($"Supplier: {catalog.Supplier}")
                                    .FontSize(9).FontColor(Colors.Grey.Lighten2);
                            right.Item().Text($"Items: {catalog.Items.Count}")
                                .FontSize(9).FontColor(Colors.Grey.Lighten2);
                            right.Item().Text($"Date: {DateTime.Now:MM/dd/yyyy}")
                                .FontSize(9).FontColor(Colors.Grey.Lighten2);
                        });
                    });
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);  // Description
                        cols.RelativeColumn(1.5f); // Part #
                        cols.RelativeColumn(1);  // Category
                        cols.ConstantColumn(70);  // Cost
                        cols.ConstantColumn(70);  // List
                    });

                    // Header row
                    table.Header(header =>
                    {
                        foreach (var label in new[] { "Description", "Part #", "Category", "Cost", "List" })
                        {
                            header.Cell().Background(Colors.Grey.Darken2).Padding(4)
                                .Text(label).FontSize(9).Bold().FontColor(Colors.White);
                        }
                    });

                    // Data rows
                    var alt = false;
                    foreach (var item in catalog.Items)
                    {
                        var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(item.Description ?? "").FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(item.PartNumber ?? "").FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(item.Category ?? "").FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(item.CostPrice > 0 ? item.CostPrice.ToString("C2") : "").FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .AlignRight().Text(item.ListPrice > 0 ? item.ListPrice.ToString("C2") : "").FontSize(8);
                        alt = !alt;
                    }
                });

                page.Footer().Element(c => ComposeFooter(c, "Price Catalog"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    #endregion

    #region Dealer List PDF

    public string GenerateDealerListPdf(List<(string Name, string Phone, string Address, string Manufacturer)> dealers, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("DealerList");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                {
                    row.RelativeItem().Text("DEALER / LABOR RATES")
                        .FontSize(16).Bold().FontColor(Colors.White);
                    row.ConstantItem(150).AlignRight().Text($"Date: {DateTime.Now:MM/dd/yyyy}")
                        .FontSize(9).FontColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);  // Name
                        cols.RelativeColumn(1.5f); // Phone
                        cols.RelativeColumn(2);  // Address
                        cols.RelativeColumn(1);  // Manufacturer
                    });

                    table.Header(header =>
                    {
                        foreach (var label in new[] { "Dealer Name", "Phone", "Address", "Manufacturer" })
                        {
                            header.Cell().Background(Colors.Grey.Darken2).Padding(4)
                                .Text(label).FontSize(9).Bold().FontColor(Colors.White);
                        }
                    });

                    var alt = false;
                    foreach (var d in dealers)
                    {
                        var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(d.Name).FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(d.Phone).FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(d.Address).FontSize(8);
                        table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(d.Manufacturer).FontSize(8);
                        alt = !alt;
                    }
                });

                page.Footer().Element(c => ComposeFooter(c, "Dealer List"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    #endregion

    #region Vendor List PDF

    public string GenerateVendorListPdf(List<DealerLaborRate> vendors, string? outputPath = null)
    {
        outputPath ??= GetDefaultOutputPath("VendorList");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Background(Colors.Grey.Darken3).Padding(12).Row(row =>
                {
                    row.RelativeItem().Text("VENDOR INFORMATION")
                        .FontSize(16).Bold().FontColor(Colors.White);
                    row.ConstantItem(200).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Vendors: {vendors.Count}")
                            .FontSize(9).FontColor(Colors.Grey.Lighten2);
                        col.Item().Text($"Date: {DateTime.Now:MM/dd/yyyy}")
                            .FontSize(9).FontColor(Colors.Grey.Lighten2);
                    });
                });

                page.Content().PaddingTop(8).Column(column =>
                {
                    column.Spacing(12);

                    foreach (var vendor in vendors)
                    {
                        column.Item().Element(c => ComposeVendorBlock(c, vendor));
                    }
                });

                page.Footer().Element(c => ComposeFooter(c, "Vendor List"));
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }

    private static string VendorTypeLabel(VendorType vt) => vt switch
    {
        VendorType.Dealer => "Dealer",
        VendorType.TowCompany => "Tow Company",
        VendorType.GlassShop => "Glass Shop",
        VendorType.AlignmentShop => "Alignment Shop",
        VendorType.MechanicalShop => "Mechanical Shop",
        VendorType.SubletOther => "Sublet / Other",
        _ => vt.ToString()
    };

    private void ComposeVendorBlock(IContainer container, DealerLaborRate vendor)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(column =>
        {
            column.Spacing(4);

            // Name + type + manufacturer
            column.Item().Row(row =>
            {
                row.AutoItem().Text(vendor.DealerName ?? "Unknown").Bold().FontSize(12);
                row.AutoItem().PaddingLeft(8).Text($"[{VendorTypeLabel(vendor.VendorType)}]")
                    .FontSize(9).FontColor(Colors.Blue.Darken2);
                if (!string.IsNullOrEmpty(vendor.Manufacturer))
                    row.AutoItem().PaddingLeft(8).Text(vendor.Manufacturer)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            // Address
            var addrParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(vendor.Address)) addrParts.Add(vendor.Address);
            var csz = new List<string>();
            if (!string.IsNullOrWhiteSpace(vendor.City)) csz.Add(vendor.City);
            if (!string.IsNullOrWhiteSpace(vendor.State)) csz.Add(vendor.State);
            if (csz.Count > 0) addrParts.Add(string.Join(", ", csz));
            if (!string.IsNullOrWhiteSpace(vendor.Zip)) addrParts.Add(vendor.Zip);
            if (addrParts.Count > 0)
                column.Item().Text(string.Join(", ", addrParts)).FontSize(9).FontColor(Colors.Grey.Darken1);

            // Phones + Emails
            if (vendor.PhoneNumbers.Count > 0)
                column.Item().Text($"Phone: {string.Join(", ", vendor.PhoneNumbers)}").FontSize(9);
            if (vendor.Emails.Count > 0)
                column.Item().Text($"Email: {string.Join(", ", vendor.Emails)}").FontSize(9);

            // Contacts
            foreach (var c in vendor.Contacts)
            {
                var contactLine = c.Name ?? "";
                if (!string.IsNullOrEmpty(c.Role)) contactLine += $" ({c.Role})";
                if (c.PhoneNumbers.Count > 0) contactLine += $" - {c.PhoneNumbers[0]}";
                if (c.Emails.Count > 0) contactLine += $" - {c.Emails[0]}";
                column.Item().Text(contactLine).FontSize(8).FontColor(Colors.Grey.Darken2);
            }

            // Rate templates table
            if (vendor.RateTemplates.Count > 0)
            {
                column.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);   // Template name
                        cols.ConstantColumn(55);   // Body
                        cols.ConstantColumn(55);   // Mech
                        cols.ConstantColumn(55);   // Paint
                        cols.ConstantColumn(55);   // Frame
                        cols.ConstantColumn(55);   // Glass
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Rate Template").FontSize(8).Bold().FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).AlignRight().Text("Body").FontSize(8).Bold().FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).AlignRight().Text("Mech").FontSize(8).Bold().FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).AlignRight().Text("Paint").FontSize(8).Bold().FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).AlignRight().Text("Frame").FontSize(8).Bold().FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).AlignRight().Text("Glass").FontSize(8).Bold().FontColor(Colors.White);
                    });

                    var alt = false;
                    foreach (var tmpl in vendor.RateTemplates)
                    {
                        var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                        table.Cell().Background(bg).Padding(2).Text(tmpl.Name).FontSize(8);
                        table.Cell().Background(bg).Padding(2).AlignRight().Text(tmpl.BodyLaborRate > 0 ? $"${tmpl.BodyLaborRate:N2}" : "-").FontSize(8);
                        table.Cell().Background(bg).Padding(2).AlignRight().Text(tmpl.MechLaborRate > 0 ? $"${tmpl.MechLaborRate:N2}" : "-").FontSize(8);
                        table.Cell().Background(bg).Padding(2).AlignRight().Text(tmpl.PaintLaborRate > 0 ? $"${tmpl.PaintLaborRate:N2}" : "-").FontSize(8);
                        table.Cell().Background(bg).Padding(2).AlignRight().Text(tmpl.FrameLaborRate > 0 ? $"${tmpl.FrameLaborRate:N2}" : "-").FontSize(8);
                        table.Cell().Background(bg).Padding(2).AlignRight().Text(tmpl.GlassLaborRate > 0 ? $"${tmpl.GlassLaborRate:N2}" : "-").FontSize(8);
                        alt = !alt;
                    }
                });
            }

            // Parts / Delivery / Returns
            var infoItems = new List<string>();
            if (vendor.PartsDiscountPercent > 0) infoItems.Add($"Parts Discount: {vendor.PartsDiscountPercent}%");
            if (vendor.Delivery.DeliversParts)
            {
                var delParts = new List<string> { "Delivers" };
                if (!string.IsNullOrEmpty(vendor.Delivery.DeliveryTime)) delParts.Add(vendor.Delivery.DeliveryTime);
                if (vendor.Delivery.RunsPerDay > 0) delParts.Add($"{vendor.Delivery.RunsPerDay}x/day");
                infoItems.Add(string.Join(", ", delParts));
            }
            if (vendor.Returns.AcceptsReturns)
            {
                var retNote = "Accepts Returns";
                if (!string.IsNullOrEmpty(vendor.Returns.ReturnsNote)) retNote += $" ({vendor.Returns.ReturnsNote})";
                infoItems.Add(retNote);
            }
            if (infoItems.Count > 0)
                column.Item().PaddingTop(2).Text(string.Join("  |  ", infoItems)).FontSize(8).FontColor(Colors.Grey.Darken2);

            // Notes
            if (!string.IsNullOrEmpty(vendor.Notes))
                column.Item().Text($"Notes: {vendor.Notes}").FontSize(8).FontColor(Colors.Grey.Darken1).Italic();
        });
    }

    #endregion

    private string GetDefaultOutputPath(string prefix)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var fileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return Path.Combine(documentsPath, fileName);
    }
}

#region PDF Data Models

public class ColorTintInvoicePdfData
{
    public string? InvoiceNumber { get; set; }
    public DateTime Date { get; set; }
    public string? BillTo { get; set; }
    public string? JobNumber { get; set; }
    public string? Vehicle { get; set; }
    public List<ColorTintInvoicePdfItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public bool ShowCostColumn { get; set; }
    public bool ShowListPriceColumn { get; set; }
    public decimal CostSubtotal { get; set; }
    public decimal ListSubtotal { get; set; }
}

public class ColorTintInvoicePdfItem
{
    public int Quantity { get; set; }
    public string? PartNumber { get; set; }
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostPrice { get; set; }
    public decimal ListPrice { get; set; }
}

public class ShopStockInvoicePdfData
{
    public string? InvoiceNumber { get; set; }
    public DateTime Date { get; set; }
    public string? ShopName { get; set; }
    public string? ShopAddress { get; set; }
    public string? ShopCity { get; set; }
    public string? ShopPhone { get; set; }
    public string? CustomerName { get; set; }
    public string? JobNumber { get; set; }
    public string? Vehicle { get; set; }
    public List<ShopStockInvoicePdfItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public bool ShowCostColumn { get; set; }
    public bool ShowListPriceColumn { get; set; }
    public decimal CostSubtotal { get; set; }
    public decimal ListSubtotal { get; set; }
}

public class ShopStockInvoicePdfItem
{
    public int Quantity { get; set; }
    public string? PartNumber { get; set; }
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostPrice { get; set; }
    public decimal ListPrice { get; set; }
}

public class VehicleProtectionPdfData
{
    public DateTime Date { get; set; }
    public string? ServiceType { get; set; }
    public string? ServiceTypeName { get; set; }
    public string? CustomerName { get; set; }
    public string? VehicleStyle { get; set; }
    public string? ProductName { get; set; }
    public List<VehicleProtectionPdfPanel> Panels { get; set; } = new();
    public int PanelCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
}

public class VehicleProtectionPdfPanel
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public decimal Price { get; set; }
}

#endregion
