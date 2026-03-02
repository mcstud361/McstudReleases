#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service to read operations from Excel and prepare for export
    /// </summary>
    public class ExcelExportService
    {
        /// <summary>
        /// Get list of sheet names from Excel file
        /// </summary>
        public List<string> GetSheetNames(string excelFilePath)
        {
            var sheets = new List<string>();

            try
            {
                using var workbook = new XLWorkbook(excelFilePath);

                foreach (var sheet in workbook.Worksheets)
                {
                    // Skip hidden sheets (like License sheet)
                    if (sheet.Visibility == XLWorksheetVisibility.Visible)
                    {
                        sheets.Add(sheet.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading sheets: {ex.Message}");
            }

            return sheets;
        }

        /// <summary>
        /// Read operations from a specific sheet
        /// </summary>
        public async Task<List<ExportOperation>> GetOperationsAsync(string excelFilePath, string sheetName)
        {
            return await Task.Run(() => GetOperations(excelFilePath, sheetName));
        }

        /// <summary>
        /// Read operations from a specific sheet (sync version)
        /// </summary>
        public List<ExportOperation> GetOperations(string excelFilePath, string sheetName)
        {
            var operations = new List<ExportOperation>();

            try
            {
                using var workbook = new XLWorkbook(excelFilePath);

                if (!workbook.TryGetWorksheet(sheetName, out var sheet))
                {
                    return operations;
                }

                // Find the data range
                // Assuming standard layout:
                // Column M or N: Operation code (Rpr, R&I, etc.)
                // Column O: Description
                // Column Q: Quantity
                // Column R: Price
                // Column V: Labor hours
                // Column X: Refinish hours

                var usedRange = sheet.RangeUsed();
                if (usedRange == null) return operations;

                int lastRow = usedRange.LastRow().RowNumber();

                // Start from row 29 (typical start of operations)
                // Adjust this based on your actual Excel structure
                int startRow = 29;

                for (int row = startRow; row <= lastRow; row++)
                {
                    try
                    {
                        // Read operation data
                        var opCode = sheet.Cell(row, 13).GetString().Trim();     // Column M
                        var description = sheet.Cell(row, 15).GetString().Trim(); // Column O
                        var quantity = GetCellAsDecimal(sheet.Cell(row, 17));     // Column Q
                        var price = GetCellAsDecimal(sheet.Cell(row, 18));        // Column R
                        var laborHours = GetCellAsDecimal(sheet.Cell(row, 22));   // Column V
                        var refinishHours = GetCellAsDecimal(sheet.Cell(row, 24)); // Column X

                        // Skip empty rows or header rows
                        if (string.IsNullOrWhiteSpace(description))
                            continue;

                        // Skip if no labor, refinish, or price (likely a header or empty operation)
                        if (laborHours == 0 && refinishHours == 0 && price == 0)
                            continue;

                        operations.Add(new ExportOperation
                        {
                            RowNumber = row,
                            OperationCode = opCode,
                            Description = description,
                            Quantity = (int)quantity,
                            Price = price,
                            LaborHours = laborHours,
                            RefinishHours = refinishHours,
                            OperationType = ParseOperationType(opCode)
                        });
                    }
                    catch
                    {
                        // Skip problematic rows
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading operations: {ex.Message}");
            }

            return operations;
        }

        private decimal GetCellAsDecimal(IXLCell cell)
        {
            if (cell.IsEmpty()) return 0;

            try
            {
                if (cell.DataType == XLDataType.Number)
                {
                    return (decimal)cell.GetDouble();
                }

                var str = cell.GetString().Trim();
                if (decimal.TryParse(str, out var result))
                {
                    return result;
                }
            }
            catch { }

            return 0;
        }

        private OperationType ParseOperationType(string code)
        {
            return code?.ToUpperInvariant() switch
            {
                "RPR" => OperationType.Repair,
                "R&I" => OperationType.RemoveAndInstall,
                "REPL" => OperationType.Replace,
                "REF" => OperationType.Refinish,
                "BLD" => OperationType.Blend,
                "SUBL" => OperationType.Sublet,
                _ => OperationType.Other
            };
        }

        /// <summary>
        /// Get summary of operations for a sheet
        /// </summary>
        public ExportSummary GetSummary(List<ExportOperation> operations)
        {
            var summary = new ExportSummary();

            foreach (var op in operations)
            {
                summary.TotalOperations++;
                summary.TotalPrice += op.Price * op.Quantity;
                summary.TotalLaborHours += op.LaborHours * op.Quantity;
                summary.TotalRefinishHours += op.RefinishHours * op.Quantity;
            }

            return summary;
        }
    }

    /// <summary>
    /// Represents an operation to export
    /// </summary>
    public class ExportOperation
    {
        public int RowNumber { get; set; }
        public string OperationCode { get; set; } = "";
        public string Description { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public decimal Price { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public OperationType OperationType { get; set; }

        public decimal TotalPrice => Price * Quantity;
        public decimal TotalLaborHours => LaborHours * Quantity;
        public decimal TotalRefinishHours => RefinishHours * Quantity;
    }

    /// <summary>
    /// Operation type codes
    /// </summary>
    public enum OperationType
    {
        Repair,
        RemoveAndInstall,
        Replace,
        Refinish,
        Blend,
        Sublet,
        Other
    }

    /// <summary>
    /// Summary of operations
    /// </summary>
    public class ExportSummary
    {
        public int TotalOperations { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalLaborHours { get; set; }
        public decimal TotalRefinishHours { get; set; }
    }
}
