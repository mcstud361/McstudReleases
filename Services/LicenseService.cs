#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Handles license validation via Google Sheets App Script
    /// Works exactly like your AutoHotkey - pings the script and gets true/false
    /// The tool doesn't need to stay open - just checks when user launches or exports
    /// </summary>
    public class LicenseService
    {
        // Your Google Apps Script Web App URL
        // This is the URL you get when you deploy your Apps Script as a web app
        // Example: https://script.google.com/macros/s/AKfycby.../exec
        private string _appScriptUrl = "";

        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(10) // Don't hang forever if network issues
        };

        /// <summary>
        /// Set your Google Apps Script URL
        /// </summary>
        public void SetAppScriptUrl(string url)
        {
            _appScriptUrl = url;
        }

        /// <summary>
        /// Check if the user is licensed via Google Sheets App Script
        /// Same approach as your AutoHotkey - ping the script, get true/false
        /// </summary>
        /// <param name="userId">User identifier (email, username, or machine ID)</param>
        /// <returns>License result with valid status</returns>
        public async Task<LicenseResult> CheckLicenseAsync(string? userId = null)
        {
            // If no App Script URL configured, assume valid Shop license (for testing)
            if (string.IsNullOrEmpty(_appScriptUrl))
            {
                return new LicenseResult
                {
                    IsValid = true,
                    Message = "License check not configured (dev mode)",
                    Source = LicenseSource.None,
                    Tier = LicenseTier.Shop // Full access in dev mode
                };
            }

            try
            {
                // Build the request URL with user ID parameter
                // Your Apps Script should handle: ?action=verify&user=USERNAME
                var requestUrl = _appScriptUrl;
                if (!string.IsNullOrEmpty(userId))
                {
                    var separator = _appScriptUrl.Contains("?") ? "&" : "?";
                    requestUrl = $"{_appScriptUrl}{separator}action=verify&user={Uri.EscapeDataString(userId)}";
                }

                System.Diagnostics.Debug.WriteLine($"[License] Checking: {requestUrl}");

                var response = await _httpClient.GetStringAsync(requestUrl);

                System.Diagnostics.Debug.WriteLine($"[License] Response: {response}");

                // Handle different response formats from your Apps Script
                return ParseLicenseResponse(response);
            }
            catch (TaskCanceledException)
            {
                // Timeout - network slow or offline
                return new LicenseResult
                {
                    IsValid = false,
                    Message = "License server timeout - check internet connection",
                    Source = LicenseSource.Online
                };
            }
            catch (HttpRequestException ex)
            {
                // Network error
                return new LicenseResult
                {
                    IsValid = false,
                    Message = $"Cannot reach license server: {ex.Message}",
                    Source = LicenseSource.Online
                };
            }
            catch (Exception ex)
            {
                return new LicenseResult
                {
                    IsValid = false,
                    Message = $"License check error: {ex.Message}",
                    Source = LicenseSource.None
                };
            }
        }

        /// <summary>
        /// Parse the response from your Google Apps Script
        /// Supports multiple formats: plain text "true"/"false", JSON, etc.
        /// </summary>
        private LicenseResult ParseLicenseResponse(string response)
        {
            var trimmed = response.Trim().ToLowerInvariant();

            // Simple true/false response (like AutoHotkey expects)
            // Default to Shop tier for simple "true" responses (backwards compatible)
            if (trimmed == "true" || trimmed == "1" || trimmed == "valid" || trimmed == "active")
            {
                return new LicenseResult
                {
                    IsValid = true,
                    Message = "License active",
                    Source = LicenseSource.Online,
                    Tier = LicenseTier.Shop // Default to shop for simple responses
                };
            }

            // Check for tier-specific responses: "client", "shop", "admin"
            if (trimmed == "client")
            {
                return new LicenseResult
                {
                    IsValid = true,
                    Message = "Client license active",
                    Source = LicenseSource.Online,
                    Tier = LicenseTier.Client
                };
            }

            if (trimmed == "shop")
            {
                return new LicenseResult
                {
                    IsValid = true,
                    Message = "Shop license active",
                    Source = LicenseSource.Online,
                    Tier = LicenseTier.Shop
                };
            }

            if (trimmed == "admin")
            {
                return new LicenseResult
                {
                    IsValid = true,
                    Message = "Admin license active",
                    Source = LicenseSource.Online,
                    Tier = LicenseTier.Admin
                };
            }

            if (trimmed == "false" || trimmed == "0" || trimmed == "invalid" || trimmed == "expired")
            {
                return new LicenseResult
                {
                    IsValid = false,
                    Message = "License expired or invalid",
                    Source = LicenseSource.Online,
                    Tier = LicenseTier.None
                };
            }

            // Try to parse as JSON (if your script returns JSON)
            try
            {
                var json = JsonSerializer.Deserialize<LicenseResponse>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (json != null)
                {
                    var tier = json.GetTier();
                    // If valid but no tier specified, default to Shop for backwards compatibility
                    if ((json.Valid || json.Active || json.Licensed) && tier == LicenseTier.None)
                    {
                        tier = LicenseTier.Shop;
                    }

                    return new LicenseResult
                    {
                        IsValid = json.Valid || json.Active || json.Licensed,
                        Message = json.Message ?? (json.Valid ? "License active" : "License invalid"),
                        ExpirationDate = json.Expiration,
                        Source = LicenseSource.Online,
                        Tier = tier
                    };
                }
            }
            catch
            {
                // Not JSON, continue
            }

            // Unknown response format
            return new LicenseResult
            {
                IsValid = false,
                Message = $"Unknown license response: {response.Substring(0, Math.Min(50, response.Length))}",
                Source = LicenseSource.Online
            };
        }

        /// <summary>
        /// Quick synchronous check - for when you need immediate result
        /// Blocks the thread briefly - use async version when possible
        /// </summary>
        public LicenseResult CheckLicenseSync(string? userId = null)
        {
            return CheckLicenseAsync(userId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get the machine identifier (can be used as userId)
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                return Environment.MachineName;
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Get Windows username (can be used as userId)
        /// </summary>
        public static string GetUserName()
        {
            try
            {
                return Environment.UserName;
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// Result of license check
    /// </summary>
    public class LicenseResult
    {
        public bool IsValid { get; set; }
        public bool IsExplicitlyDisabled { get; set; }
        public string Message { get; set; } = "";
        public DateTime? ExpirationDate { get; set; }
        public LicenseSource Source { get; set; }

        /// <summary>
        /// License tier determines feature access
        /// </summary>
        public LicenseTier Tier { get; set; } = LicenseTier.None;

        /// <summary>
        /// Can this license train/learn from estimates?
        /// Only Shop and Admin tiers can train.
        /// </summary>
        public bool CanTrain => Tier == LicenseTier.Shop || Tier == LicenseTier.Admin;

        /// <summary>
        /// Can this license use learned suggestions? (All valid licenses can)
        /// </summary>
        public bool CanUseSuggestions => IsValid;
    }

    /// <summary>
    /// Where the license was validated from
    /// </summary>
    public enum LicenseSource
    {
        None,
        Online,
        Excel
    }

    /// <summary>
    /// License tier determines feature access
    /// </summary>
    public enum LicenseTier
    {
        /// <summary>No license - limited/trial features</summary>
        None,

        /// <summary>Client license - read-only access to learning data, cannot train</summary>
        Client,

        /// <summary>Shop license - full access including training/learning</summary>
        Shop,

        /// <summary>Admin license - full access plus admin features</summary>
        Admin
    }

    /// <summary>
    /// JSON response structure (if your Apps Script returns JSON)
    /// </summary>
    public class LicenseResponse
    {
        public bool Valid { get; set; }
        public bool Active { get; set; }
        public bool Licensed { get; set; }
        public string? Message { get; set; }
        public DateTime? Expiration { get; set; }

        /// <summary>
        /// License tier: "client", "shop", "admin"
        /// </summary>
        public string? Tier { get; set; }

        /// <summary>
        /// Alternative: license type
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Parse tier from string
        /// </summary>
        public LicenseTier GetTier()
        {
            var tierStr = (Tier ?? Type ?? "").ToLowerInvariant();
            return tierStr switch
            {
                "admin" => LicenseTier.Admin,
                "shop" => LicenseTier.Shop,
                "client" => LicenseTier.Client,
                "user" => LicenseTier.Client, // "user" = client
                _ => LicenseTier.None
            };
        }
    }
}
