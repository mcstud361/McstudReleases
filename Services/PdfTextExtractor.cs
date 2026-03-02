#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Extracts text from PDF files using iText7
    /// </summary>
    public static class PdfTextExtractorService
    {
        /// <summary>
        /// Extract all text from a PDF file
        /// </summary>
        public static async Task<string> ExtractTextAsync(string filePath)
        {
            return await Task.Run(() => ExtractText(filePath));
        }

        /// <summary>
        /// Extract all text from a PDF file (synchronous)
        /// </summary>
        public static string ExtractText(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF file not found", filePath);
            }

            var sb = new StringBuilder();

            try
            {
                using (var reader = new PdfReader(filePath))
                using (var pdfDoc = new PdfDocument(reader))
                {
                    int numberOfPages = pdfDoc.GetNumberOfPages();
                    System.Diagnostics.Debug.WriteLine($"[PDF] Document has {numberOfPages} pages");

                    for (int i = 1; i <= numberOfPages; i++)
                    {
                        var page = pdfDoc.GetPage(i);
                        var strategy = new LocationTextExtractionStrategy();
                        var pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

                        System.Diagnostics.Debug.WriteLine($"[PDF] Page {i}: extracted {pageText?.Length ?? 0} chars");

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            sb.AppendLine(pageText);
                            sb.AppendLine(); // Add separator between pages
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDF] Error extracting text: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[PDF] Stack: {ex.StackTrace}");
                throw;
            }

            System.Diagnostics.Debug.WriteLine($"[PDF] Total extracted: {sb.Length} chars");
            return sb.ToString();
        }

        /// <summary>
        /// Extract text from a PDF file given its bytes
        /// </summary>
        public static string ExtractTextFromBytes(byte[] pdfBytes)
        {
            var sb = new StringBuilder();

            try
            {
                System.Diagnostics.Debug.WriteLine($"[PDF] Processing {pdfBytes.Length} bytes");

                using (var ms = new MemoryStream(pdfBytes))
                using (var reader = new PdfReader(ms))
                using (var pdfDoc = new PdfDocument(reader))
                {
                    int numberOfPages = pdfDoc.GetNumberOfPages();
                    System.Diagnostics.Debug.WriteLine($"[PDF] Document has {numberOfPages} pages");

                    for (int i = 1; i <= numberOfPages; i++)
                    {
                        var page = pdfDoc.GetPage(i);
                        var strategy = new LocationTextExtractionStrategy();
                        var pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);

                        System.Diagnostics.Debug.WriteLine($"[PDF] Page {i}: extracted {pageText?.Length ?? 0} chars");

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            sb.AppendLine(pageText);
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDF] Error extracting text from bytes: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[PDF] Stack: {ex.StackTrace}");
                throw;
            }

            System.Diagnostics.Debug.WriteLine($"[PDF] Total extracted: {sb.Length} chars");
            return sb.ToString();
        }
    }
}
