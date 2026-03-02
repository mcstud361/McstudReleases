#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Comprehensive tracking service for document creation, feature usage, and analytics.
    /// Tracks invoices, shop docs, PDF exports, feature interactions, and usage patterns.
    /// </summary>
    public class DocumentUsageTrackingService
    {
        private readonly string _dataFilePath;
        private DocumentUsageData _data;
        private readonly object _lock = new();

        private static DocumentUsageTrackingService? _instance;
        public static DocumentUsageTrackingService Instance => _instance ??= new DocumentUsageTrackingService();

        public DocumentUsageTrackingService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _dataFilePath = Path.Combine(appDataPath, "document_usage.json");
            _data = LoadData();
        }

        #region Invoice Tracking

        /// <summary>
        /// Record a Color Tint invoice creation
        /// </summary>
        public void RecordColorTintInvoice(string invoiceNumber, int itemCount, decimal subtotal, decimal total, string? customerName = null)
        {
            lock (_lock)
            {
                var record = new InvoiceRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    InvoiceType = InvoiceType.ColorTint,
                    InvoiceNumber = invoiceNumber,
                    ItemCount = itemCount,
                    Subtotal = subtotal,
                    Total = total,
                    CustomerName = customerName ?? ""
                };

                _data.Invoices.Add(record);
                UpdateInvoiceStats(record);
                SaveData();
            }
        }

        /// <summary>
        /// Record a Shop Stock invoice creation
        /// </summary>
        public void RecordShopStockInvoice(string invoiceNumber, int itemCount, decimal subtotal, decimal total, string? customerName = null, string? vehicle = null)
        {
            lock (_lock)
            {
                var record = new InvoiceRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    InvoiceType = InvoiceType.ShopStock,
                    InvoiceNumber = invoiceNumber,
                    ItemCount = itemCount,
                    Subtotal = subtotal,
                    Total = total,
                    CustomerName = customerName ?? "",
                    Vehicle = vehicle ?? ""
                };

                _data.Invoices.Add(record);
                UpdateInvoiceStats(record);
                SaveData();
            }
        }

        /// <summary>
        /// Record a Tow Bill creation
        /// </summary>
        public void RecordTowBill(string billNumber, decimal amount, string? customerName = null)
        {
            lock (_lock)
            {
                var record = new InvoiceRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    InvoiceType = InvoiceType.TowBill,
                    InvoiceNumber = billNumber,
                    ItemCount = 1,
                    Subtotal = amount,
                    Total = amount,
                    CustomerName = customerName ?? ""
                };

                _data.Invoices.Add(record);
                UpdateInvoiceStats(record);
                SaveData();
            }
        }

        private void UpdateInvoiceStats(InvoiceRecord record)
        {
            var key = record.InvoiceType.ToString();
            if (!_data.InvoiceTypeStats.ContainsKey(key))
            {
                _data.InvoiceTypeStats[key] = new InvoiceTypeAggregate();
            }

            var stats = _data.InvoiceTypeStats[key];
            stats.TotalCount++;
            stats.TotalValue += record.Total;
            stats.TotalItems += record.ItemCount;
            stats.LastUsed = record.Timestamp;
        }

        #endregion

        #region Document/PDF Tracking

        /// <summary>
        /// Record a PDF document export
        /// </summary>
        public void RecordPdfExport(string documentType, string fileName, int pageCount = 1, long fileSizeBytes = 0)
        {
            lock (_lock)
            {
                var record = new DocumentRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    DocumentType = documentType,
                    FileName = fileName,
                    PageCount = pageCount,
                    FileSizeBytes = fileSizeBytes,
                    Action = DocumentAction.Export
                };

                _data.Documents.Add(record);
                UpdateDocumentStats(documentType, DocumentAction.Export);
                SaveData();
            }
        }

        /// <summary>
        /// Record a document view/access
        /// </summary>
        public void RecordDocumentView(string documentType, string? documentName = null)
        {
            lock (_lock)
            {
                var record = new DocumentRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    DocumentType = documentType,
                    FileName = documentName ?? "",
                    Action = DocumentAction.View
                };

                _data.Documents.Add(record);
                UpdateDocumentStats(documentType, DocumentAction.View);
                SaveData();
            }
        }

        /// <summary>
        /// Record a document print
        /// </summary>
        public void RecordDocumentPrint(string documentType, string? documentName = null, int copies = 1)
        {
            lock (_lock)
            {
                var record = new DocumentRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    DocumentType = documentType,
                    FileName = documentName ?? "",
                    PageCount = copies,
                    Action = DocumentAction.Print
                };

                _data.Documents.Add(record);
                UpdateDocumentStats(documentType, DocumentAction.Print);
                SaveData();
            }
        }

        private void UpdateDocumentStats(string docType, DocumentAction action)
        {
            if (!_data.DocumentTypeStats.ContainsKey(docType))
            {
                _data.DocumentTypeStats[docType] = new DocumentTypeAggregate();
            }

            var stats = _data.DocumentTypeStats[docType];
            stats.TotalAccesses++;
            stats.LastAccessed = DateTime.Now;

            switch (action)
            {
                case DocumentAction.Export: stats.ExportCount++; break;
                case DocumentAction.View: stats.ViewCount++; break;
                case DocumentAction.Print: stats.PrintCount++; break;
            }
        }

        #endregion

        #region Feature Usage Tracking

        /// <summary>
        /// Record a feature/tool usage
        /// </summary>
        public void RecordFeatureUsage(string featureName, string? context = null, int? itemCount = null)
        {
            lock (_lock)
            {
                var record = new FeatureUsageRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    FeatureName = featureName,
                    Context = context ?? "",
                    ItemCount = itemCount ?? 0
                };

                _data.FeatureUsage.Add(record);
                UpdateFeatureStats(featureName);
                SaveData();
            }
        }

        /// <summary>
        /// Record tab navigation
        /// </summary>
        public void RecordTabVisit(string tabName)
        {
            lock (_lock)
            {
                if (!_data.TabVisits.ContainsKey(tabName))
                {
                    _data.TabVisits[tabName] = 0;
                }
                _data.TabVisits[tabName]++;

                // Also record as feature usage for detailed tracking
                var record = new FeatureUsageRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    FeatureName = $"Tab:{tabName}",
                    Context = "Navigation"
                };
                _data.FeatureUsage.Add(record);

                SaveData();
            }
        }

        /// <summary>
        /// Record a search performed
        /// </summary>
        public void RecordSearch(string searchContext, string searchTerm, int resultCount)
        {
            lock (_lock)
            {
                var record = new SearchRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    Context = searchContext,
                    SearchTerm = searchTerm,
                    ResultCount = resultCount
                };

                _data.Searches.Add(record);
                SaveData();
            }
        }

        private void UpdateFeatureStats(string featureName)
        {
            if (!_data.FeatureStats.ContainsKey(featureName))
            {
                _data.FeatureStats[featureName] = new FeatureAggregate();
            }

            var stats = _data.FeatureStats[featureName];
            stats.UsageCount++;
            stats.LastUsed = DateTime.Now;
        }

        #endregion

        #region Clipboard Operations Tracking

        /// <summary>
        /// Record a clipboard copy operation
        /// </summary>
        public void RecordClipboardCopy(string sourceFeature, int lineCount, string? contentType = null)
        {
            lock (_lock)
            {
                var record = new ClipboardRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    Operation = ClipboardOperation.Copy,
                    SourceFeature = sourceFeature,
                    LineCount = lineCount,
                    ContentType = contentType ?? "text"
                };

                _data.ClipboardOperations.Add(record);
                SaveData();
            }
        }

        /// <summary>
        /// Record a clipboard paste operation
        /// </summary>
        public void RecordClipboardPaste(string targetFeature, int lineCount, string? contentType = null)
        {
            lock (_lock)
            {
                var record = new ClipboardRecord
                {
                    Timestamp = DateTime.Now,
                    UserId = GetCurrentUserId(),
                    Operation = ClipboardOperation.Paste,
                    SourceFeature = targetFeature,
                    LineCount = lineCount,
                    ContentType = contentType ?? "text"
                };

                _data.ClipboardOperations.Add(record);
                SaveData();
            }
        }

        #endregion

        #region Analytics Queries

        /// <summary>
        /// Get comprehensive usage summary
        /// </summary>
        public UsageSummary GetUsageSummary(StatsPeriod period, string? userId = null)
        {
            var invoices = FilterByPeriod(_data.Invoices, period);
            var documents = FilterByPeriod(_data.Documents, period);
            var features = FilterByPeriod(_data.FeatureUsage, period);

            if (!string.IsNullOrEmpty(userId))
            {
                invoices = invoices.Where(i => i.UserId == userId);
                documents = documents.Where(d => d.UserId == userId);
                features = features.Where(f => f.UserId == userId);
            }

            var invoiceList = invoices.ToList();
            var documentList = documents.ToList();
            var featureList = features.ToList();

            return new UsageSummary
            {
                Period = period.ToString(),

                // Invoice stats
                TotalInvoices = invoiceList.Count,
                TotalInvoiceValue = invoiceList.Sum(i => i.Total),
                ColorTintInvoices = invoiceList.Count(i => i.InvoiceType == InvoiceType.ColorTint),
                ShopStockInvoices = invoiceList.Count(i => i.InvoiceType == InvoiceType.ShopStock),
                TowBills = invoiceList.Count(i => i.InvoiceType == InvoiceType.TowBill),
                AvgInvoiceValue = invoiceList.Any() ? invoiceList.Average(i => i.Total) : 0,
                AvgItemsPerInvoice = invoiceList.Any() ? invoiceList.Average(i => i.ItemCount) : 0,

                // Document stats
                TotalDocuments = documentList.Count,
                TotalPdfExports = documentList.Count(d => d.Action == DocumentAction.Export),
                TotalDocumentViews = documentList.Count(d => d.Action == DocumentAction.View),
                TotalPrints = documentList.Count(d => d.Action == DocumentAction.Print),

                // Feature stats
                TotalFeatureUses = featureList.Count,
                UniqueFeatures = featureList.Select(f => f.FeatureName).Distinct().Count()
            };
        }

        /// <summary>
        /// Get invoice statistics breakdown
        /// </summary>
        public InvoiceStats GetInvoiceStats(StatsPeriod period, string? userId = null)
        {
            var invoices = FilterByPeriod(_data.Invoices, period);
            if (!string.IsNullOrEmpty(userId))
            {
                invoices = invoices.Where(i => i.UserId == userId);
            }

            var list = invoices.ToList();

            var stats = new InvoiceStats
            {
                TotalInvoices = list.Count,
                TotalValue = list.Sum(i => i.Total),
                TotalItems = list.Sum(i => i.ItemCount),
                AvgValue = list.Any() ? list.Average(i => i.Total) : 0,
                AvgItems = list.Any() ? list.Average(i => i.ItemCount) : 0,
                MaxValue = list.Any() ? list.Max(i => i.Total) : 0,
                MinValue = list.Any() ? list.Min(i => i.Total) : 0
            };

            // Breakdown by type
            stats.ByType = list
                .GroupBy(i => i.InvoiceType)
                .ToDictionary(
                    g => g.Key.ToString(),
                    g => new InvoiceTypeBreakdown
                    {
                        Count = g.Count(),
                        TotalValue = g.Sum(i => i.Total),
                        AvgValue = g.Average(i => i.Total),
                        TotalItems = g.Sum(i => i.ItemCount)
                    }
                );

            // Daily breakdown
            stats.DailyBreakdown = list
                .GroupBy(i => i.Timestamp.Date)
                .OrderByDescending(g => g.Key)
                .Take(30)
                .Select(g => new DailyInvoiceStats
                {
                    Date = g.Key,
                    Count = g.Count(),
                    TotalValue = g.Sum(i => i.Total),
                    AvgValue = g.Average(i => i.Total)
                })
                .ToList();

            // Hourly distribution
            stats.HourlyDistribution = Enumerable.Range(0, 24)
                .Select(h => new HourlyInvoiceStats
                {
                    Hour = h,
                    Count = list.Count(i => i.Timestamp.Hour == h),
                    TotalValue = list.Where(i => i.Timestamp.Hour == h).Sum(i => i.Total)
                })
                .ToList();

            return stats;
        }

        /// <summary>
        /// Get most used documents/features
        /// </summary>
        public List<PopularItemStats> GetMostUsedDocuments(StatsPeriod period, int topN = 10, string? userId = null)
        {
            var documents = FilterByPeriod(_data.Documents, period);
            if (!string.IsNullOrEmpty(userId))
            {
                documents = documents.Where(d => d.UserId == userId);
            }

            return documents
                .GroupBy(d => d.DocumentType)
                .Select(g => new PopularItemStats
                {
                    Name = g.Key,
                    UsageCount = g.Count(),
                    LastUsed = g.Max(d => d.Timestamp),
                    ExportCount = g.Count(d => d.Action == DocumentAction.Export),
                    ViewCount = g.Count(d => d.Action == DocumentAction.View)
                })
                .OrderByDescending(p => p.UsageCount)
                .Take(topN)
                .ToList();
        }

        /// <summary>
        /// Get most used features
        /// </summary>
        public List<PopularItemStats> GetMostUsedFeatures(StatsPeriod period, int topN = 15, string? userId = null)
        {
            var features = FilterByPeriod(_data.FeatureUsage, period);
            if (!string.IsNullOrEmpty(userId))
            {
                features = features.Where(f => f.UserId == userId);
            }

            return features
                .GroupBy(f => f.FeatureName)
                .Select(g => new PopularItemStats
                {
                    Name = g.Key,
                    UsageCount = g.Count(),
                    LastUsed = g.Max(f => f.Timestamp)
                })
                .OrderByDescending(p => p.UsageCount)
                .Take(topN)
                .ToList();
        }

        /// <summary>
        /// Get tab visit statistics
        /// </summary>
        public List<TabVisitStats> GetTabVisitStats()
        {
            var total = _data.TabVisits.Values.Sum();
            return _data.TabVisits
                .Select(kvp => new TabVisitStats
                {
                    TabName = kvp.Key,
                    VisitCount = kvp.Value,
                    Percentage = total > 0 ? (double)kvp.Value / total * 100 : 0
                })
                .OrderByDescending(t => t.VisitCount)
                .ToList();
        }

        /// <summary>
        /// Get daily activity timeline
        /// </summary>
        public List<DailyActivityStats> GetDailyActivity(StatsPeriod period, string? userId = null)
        {
            var today = DateTime.Today;
            var days = period switch
            {
                StatsPeriod.Today => 1,
                StatsPeriod.ThisWeek => 7,
                StatsPeriod.ThisMonth => 30,
                StatsPeriod.ThisYear => 365,
                _ => 30
            };

            var results = new List<DailyActivityStats>();
            for (int i = 0; i < days; i++)
            {
                var date = today.AddDays(-i);
                var dayInvoices = _data.Invoices.Where(inv => inv.Timestamp.Date == date);
                var dayDocs = _data.Documents.Where(d => d.Timestamp.Date == date);
                var dayFeatures = _data.FeatureUsage.Where(f => f.Timestamp.Date == date);

                if (!string.IsNullOrEmpty(userId))
                {
                    dayInvoices = dayInvoices.Where(i => i.UserId == userId);
                    dayDocs = dayDocs.Where(d => d.UserId == userId);
                    dayFeatures = dayFeatures.Where(f => f.UserId == userId);
                }

                results.Add(new DailyActivityStats
                {
                    Date = date,
                    InvoiceCount = dayInvoices.Count(),
                    InvoiceValue = dayInvoices.Sum(i => i.Total),
                    DocumentCount = dayDocs.Count(),
                    FeatureUseCount = dayFeatures.Count(),
                    TotalActions = dayInvoices.Count() + dayDocs.Count() + dayFeatures.Count()
                });
            }

            return results.OrderBy(d => d.Date).ToList();
        }

        /// <summary>
        /// Get user activity ranking
        /// </summary>
        public List<UserActivityRanking> GetUserActivityRanking(StatsPeriod period)
        {
            var invoices = FilterByPeriod(_data.Invoices, period).ToList();
            var documents = FilterByPeriod(_data.Documents, period).ToList();
            var features = FilterByPeriod(_data.FeatureUsage, period).ToList();

            var allUsers = invoices.Select(i => i.UserId)
                .Concat(documents.Select(d => d.UserId))
                .Concat(features.Select(f => f.UserId))
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct();

            return allUsers.Select(userId => new UserActivityRanking
            {
                UserId = userId,
                InvoicesCreated = invoices.Count(i => i.UserId == userId),
                InvoiceValue = invoices.Where(i => i.UserId == userId).Sum(i => i.Total),
                DocumentsCreated = documents.Count(d => d.UserId == userId),
                FeaturesUsed = features.Count(f => f.UserId == userId),
                TotalScore = invoices.Count(i => i.UserId == userId) * 10 +
                             documents.Count(d => d.UserId == userId) * 5 +
                             features.Count(f => f.UserId == userId)
            })
            .OrderByDescending(u => u.TotalScore)
            .ToList();
        }

        /// <summary>
        /// Get recent activity feed
        /// </summary>
        public List<ActivityFeedItem> GetRecentActivity(int count = 50, string? userId = null)
        {
            var items = new List<ActivityFeedItem>();

            // Add invoices
            var invoices = _data.Invoices.AsEnumerable();
            if (!string.IsNullOrEmpty(userId)) invoices = invoices.Where(i => i.UserId == userId);
            items.AddRange(invoices.Select(i => new ActivityFeedItem
            {
                Timestamp = i.Timestamp,
                UserId = i.UserId,
                ActivityType = "Invoice",
                Description = $"{i.InvoiceType} Invoice #{i.InvoiceNumber}",
                Value = i.Total,
                Icon = "\uE8C7"
            }));

            // Add documents
            var docs = _data.Documents.AsEnumerable();
            if (!string.IsNullOrEmpty(userId)) docs = docs.Where(d => d.UserId == userId);
            items.AddRange(docs.Select(d => new ActivityFeedItem
            {
                Timestamp = d.Timestamp,
                UserId = d.UserId,
                ActivityType = d.Action.ToString(),
                Description = $"{d.Action} {d.DocumentType}",
                Icon = d.Action == DocumentAction.Export ? "\uE8A5" : "\uE8F6"
            }));

            return items
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get productivity metrics
        /// </summary>
        public ProductivityMetrics GetProductivityMetrics(StatsPeriod period, string? userId = null)
        {
            var invoices = FilterByPeriod(_data.Invoices, period);
            var documents = FilterByPeriod(_data.Documents, period);

            if (!string.IsNullOrEmpty(userId))
            {
                invoices = invoices.Where(i => i.UserId == userId);
                documents = documents.Where(d => d.UserId == userId);
            }

            var invoiceList = invoices.ToList();
            var documentList = documents.ToList();

            // Calculate working days
            var dates = invoiceList.Select(i => i.Timestamp.Date)
                .Concat(documentList.Select(d => d.Timestamp.Date))
                .Distinct()
                .ToList();
            var workingDays = dates.Count;

            var metrics = new ProductivityMetrics
            {
                WorkingDays = workingDays,
                TotalInvoices = invoiceList.Count,
                TotalDocuments = documentList.Count,
                TotalValue = invoiceList.Sum(i => i.Total),

                AvgInvoicesPerDay = workingDays > 0 ? (double)invoiceList.Count / workingDays : 0,
                AvgDocumentsPerDay = workingDays > 0 ? (double)documentList.Count / workingDays : 0,
                AvgValuePerDay = workingDays > 0 ? invoiceList.Sum(i => i.Total) / workingDays : 0,

                MostProductiveDay = dates.Any()
                    ? dates.OrderByDescending(d =>
                        invoiceList.Count(i => i.Timestamp.Date == d) +
                        documentList.Count(doc => doc.Timestamp.Date == d))
                      .First()
                    : DateTime.Today,

                MostProductiveHour = GetMostProductiveHour(invoiceList, documentList)
            };

            // Peak productivity
            if (invoiceList.Any())
            {
                var peakDay = dates.OrderByDescending(d => invoiceList.Where(i => i.Timestamp.Date == d).Sum(i => i.Total)).First();
                metrics.PeakDayValue = invoiceList.Where(i => i.Timestamp.Date == peakDay).Sum(i => i.Total);
            }

            return metrics;
        }

        private int GetMostProductiveHour(List<InvoiceRecord> invoices, List<DocumentRecord> documents)
        {
            var hourlyCounts = Enumerable.Range(0, 24)
                .Select(h => new
                {
                    Hour = h,
                    Count = invoices.Count(i => i.Timestamp.Hour == h) + documents.Count(d => d.Timestamp.Hour == h)
                })
                .OrderByDescending(h => h.Count)
                .First();
            return hourlyCounts.Hour;
        }

        #endregion

        #region Data Management

        private IEnumerable<T> FilterByPeriod<T>(IEnumerable<T> items, StatsPeriod period) where T : IHasTimestamp
        {
            var today = DateTime.Today;
            return period switch
            {
                StatsPeriod.Today => items.Where(i => i.Timestamp.Date == today),
                StatsPeriod.ThisWeek => items.Where(i => i.Timestamp.Date >= today.AddDays(-(int)today.DayOfWeek)),
                StatsPeriod.ThisMonth => items.Where(i => i.Timestamp.Date >= new DateTime(today.Year, today.Month, 1)),
                StatsPeriod.ThisYear => items.Where(i => i.Timestamp.Date >= new DateTime(today.Year, 1, 1)),
                StatsPeriod.AllTime => items,
                _ => items
            };
        }

        private string GetCurrentUserId()
        {
            try { return Environment.UserName; }
            catch { return "unknown"; }
        }

        private DocumentUsageData LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<DocumentUsageData>(json, options) ?? new DocumentUsageData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocUsage] Load error: {ex.Message}");
            }
            return new DocumentUsageData();
        }

        private void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_data, options);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocUsage] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all usage data
        /// </summary>
        public void ClearAllData()
        {
            lock (_lock)
            {
                _data = new DocumentUsageData();
                SaveData();
            }
        }

        #endregion
    }

    #region Data Models

    public interface IHasTimestamp
    {
        DateTime Timestamp { get; }
    }

    public class DocumentUsageData
    {
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public List<InvoiceRecord> Invoices { get; set; } = new();
        public List<DocumentRecord> Documents { get; set; } = new();
        public List<FeatureUsageRecord> FeatureUsage { get; set; } = new();
        public List<ClipboardRecord> ClipboardOperations { get; set; } = new();
        public List<SearchRecord> Searches { get; set; } = new();

        // Aggregated stats
        public Dictionary<string, InvoiceTypeAggregate> InvoiceTypeStats { get; set; } = new();
        public Dictionary<string, DocumentTypeAggregate> DocumentTypeStats { get; set; } = new();
        public Dictionary<string, FeatureAggregate> FeatureStats { get; set; } = new();
        public Dictionary<string, int> TabVisits { get; set; } = new();
    }

    public enum InvoiceType
    {
        ColorTint,
        ShopStock,
        TowBill,
        PPF,
        Custom
    }

    public enum DocumentAction
    {
        View,
        Export,
        Print,
        Create
    }

    public enum ClipboardOperation
    {
        Copy,
        Paste
    }

    public class InvoiceRecord : IHasTimestamp
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public InvoiceType InvoiceType { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public int ItemCount { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Total { get; set; }
        public string CustomerName { get; set; } = "";
        public string Vehicle { get; set; } = "";
    }

    public class DocumentRecord : IHasTimestamp
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string FileName { get; set; } = "";
        public int PageCount { get; set; }
        public long FileSizeBytes { get; set; }
        public DocumentAction Action { get; set; }
    }

    public class FeatureUsageRecord : IHasTimestamp
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string FeatureName { get; set; } = "";
        public string Context { get; set; } = "";
        public int ItemCount { get; set; }
    }

    public class ClipboardRecord : IHasTimestamp
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public ClipboardOperation Operation { get; set; }
        public string SourceFeature { get; set; } = "";
        public int LineCount { get; set; }
        public string ContentType { get; set; } = "";
    }

    public class SearchRecord : IHasTimestamp
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string Context { get; set; } = "";
        public string SearchTerm { get; set; } = "";
        public int ResultCount { get; set; }
    }

    public class InvoiceTypeAggregate
    {
        public int TotalCount { get; set; }
        public decimal TotalValue { get; set; }
        public int TotalItems { get; set; }
        public DateTime LastUsed { get; set; }
    }

    public class DocumentTypeAggregate
    {
        public int TotalAccesses { get; set; }
        public int ViewCount { get; set; }
        public int ExportCount { get; set; }
        public int PrintCount { get; set; }
        public DateTime LastAccessed { get; set; }
    }

    public class FeatureAggregate
    {
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; }
    }

    #endregion

    #region Output Models

    public class UsageSummary
    {
        public string Period { get; set; } = "";

        // Invoice
        public int TotalInvoices { get; set; }
        public decimal TotalInvoiceValue { get; set; }
        public int ColorTintInvoices { get; set; }
        public int ShopStockInvoices { get; set; }
        public int TowBills { get; set; }
        public decimal AvgInvoiceValue { get; set; }
        public double AvgItemsPerInvoice { get; set; }

        // Documents
        public int TotalDocuments { get; set; }
        public int TotalPdfExports { get; set; }
        public int TotalDocumentViews { get; set; }
        public int TotalPrints { get; set; }

        // Features
        public int TotalFeatureUses { get; set; }
        public int UniqueFeatures { get; set; }

        public string FormattedInvoiceValue => TotalInvoiceValue.ToString("C0");
        public string FormattedAvgValue => AvgInvoiceValue.ToString("C0");
    }

    public class InvoiceStats
    {
        public int TotalInvoices { get; set; }
        public decimal TotalValue { get; set; }
        public int TotalItems { get; set; }
        public decimal AvgValue { get; set; }
        public double AvgItems { get; set; }
        public decimal MaxValue { get; set; }
        public decimal MinValue { get; set; }

        public Dictionary<string, InvoiceTypeBreakdown> ByType { get; set; } = new();
        public List<DailyInvoiceStats> DailyBreakdown { get; set; } = new();
        public List<HourlyInvoiceStats> HourlyDistribution { get; set; } = new();

        public string FormattedTotal => TotalValue.ToString("C0");
        public string FormattedAvg => AvgValue.ToString("C0");
    }

    public class InvoiceTypeBreakdown
    {
        public int Count { get; set; }
        public decimal TotalValue { get; set; }
        public decimal AvgValue { get; set; }
        public int TotalItems { get; set; }
    }

    public class DailyInvoiceStats
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public decimal TotalValue { get; set; }
        public decimal AvgValue { get; set; }
        public string FormattedDate => Date.ToString("MMM dd");
    }

    public class HourlyInvoiceStats
    {
        public int Hour { get; set; }
        public int Count { get; set; }
        public decimal TotalValue { get; set; }
        public string FormattedHour => Hour switch
        {
            0 => "12 AM", 12 => "12 PM",
            < 12 => $"{Hour} AM", _ => $"{Hour - 12} PM"
        };
    }

    public class PopularItemStats
    {
        public string Name { get; set; } = "";
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; }
        public int ExportCount { get; set; }
        public int ViewCount { get; set; }
    }

    public class TabVisitStats
    {
        public string TabName { get; set; } = "";
        public int VisitCount { get; set; }
        public double Percentage { get; set; }
    }

    public class DailyActivityStats
    {
        public DateTime Date { get; set; }
        public int InvoiceCount { get; set; }
        public decimal InvoiceValue { get; set; }
        public int DocumentCount { get; set; }
        public int FeatureUseCount { get; set; }
        public int TotalActions { get; set; }
        public string FormattedDate => Date.ToString("MMM dd");
    }

    public class UserActivityRanking
    {
        public string UserId { get; set; } = "";
        public int InvoicesCreated { get; set; }
        public decimal InvoiceValue { get; set; }
        public int DocumentsCreated { get; set; }
        public int FeaturesUsed { get; set; }
        public int TotalScore { get; set; }
        public string FormattedValue => InvoiceValue.ToString("C0");
    }

    public class ActivityFeedItem
    {
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = "";
        public string ActivityType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Value { get; set; }
        public string Icon { get; set; } = "";
        public string FormattedTime => Timestamp.ToString("h:mm tt");
        public string FormattedDate => Timestamp.ToString("MMM d");
    }

    public class ProductivityMetrics
    {
        public int WorkingDays { get; set; }
        public int TotalInvoices { get; set; }
        public int TotalDocuments { get; set; }
        public decimal TotalValue { get; set; }
        public double AvgInvoicesPerDay { get; set; }
        public double AvgDocumentsPerDay { get; set; }
        public decimal AvgValuePerDay { get; set; }
        public DateTime MostProductiveDay { get; set; }
        public int MostProductiveHour { get; set; }
        public decimal PeakDayValue { get; set; }

        public string FormattedProductiveHour => MostProductiveHour switch
        {
            0 => "12 AM", 12 => "12 PM",
            < 12 => $"{MostProductiveHour} AM", _ => $"{MostProductiveHour - 12} PM"
        };
    }

    #endregion
}
