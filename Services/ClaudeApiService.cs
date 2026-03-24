#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Service for making Claude API calls with gating, sanitization, and usage tracking.
    /// All outbound text is sanitized to remove sensitive data before sending.
    /// Returns null on any failure — callers must have fallback logic.
    /// </summary>
    public class ClaudeApiService
    {
        private static ClaudeApiService? _instance;
        public static ClaudeApiService Instance => _instance ??= new ClaudeApiService();

        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string API_ENDPOINT = "https://api.anthropic.com/v1/messages";
        private const string API_VERSION = "2023-06-01";
        private const int MAX_TOKENS_DEFAULT = 1024;

        // Sensitive data patterns to strip before sending
        private static readonly Regex _vinPattern = new(@"\b[A-HJ-NPR-Z0-9]{17}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _claimNumberPattern = new(@"\b\d{2}-\d{5,7}\b|\bCLM[-#]?\d{5,}\b|\bclaim\s*#?\s*\d{5,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _roNumberPattern = new(@"\bRO\s*#?\s*\d{4,}\b|\b\d{6,8}(?=\s*RO)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _phonePattern = new(@"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled);
        private static readonly Regex _emailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _insurancePattern = new(@"\b(?:State Farm|Allstate|GEICO|Progressive|USAA|Liberty Mutual|Nationwide|Farmers|Travelers|American Family|Erie|Hartford|Safeco|MetLife|Kemper|Bristol West|Elephant|Root|Lemonade|AAA|Amica|Auto[- ]?Owners|Chubb|Cincinnati Financial|Hanover|Mercury|Plymouth Rock|Wawanesa|Country Financial|Shelter Insurance|CSAA|NJM|Donegal|Westfield|Pekin Insurance|Grange Insurance|Central Insurance|Motorists Insurance|Indiana Farm Bureau|Farm Bureau|Kentucky Farm Bureau|Grinnell Mutual|Mutual of Enumclaw|Secura|West Bend|Auto Club|Interinsurance Exchange|PEMCO|Aetna|Encompass|Foremost|Good2Go|National General|Infinity Insurance|Dairyland|21st Century|Esurance|Sentry|EMC Insurance|Frankenmuth|Acuity|Society Insurance|IMT Insurance|PURE Insurance)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Send a prompt to Claude and get a response. Returns null on any failure.
        /// All three gates (enabled, budget, feature toggle) are checked before calling.
        /// </summary>
        public async Task<AiResponse?> SendAsync(string systemPrompt, string userMessage, AiFeature feature, int maxTokens = MAX_TOKENS_DEFAULT, CancellationToken cancellationToken = default)
        {
            try
            {
                // Gate 1: Is AI enabled?
                var config = AiConfigService.Instance;
                if (!config.IsEnabled)
                {
                    Debug.WriteLine("[ClaudeAPI] AI is disabled");
                    return null;
                }

                // Gate 2: Is this specific feature enabled?
                if (!config.IsFeatureEnabled(feature))
                {
                    Debug.WriteLine($"[ClaudeAPI] Feature {feature} is disabled");
                    return null;
                }

                // Gate 3: Are we within budget/call limits?
                if (!AiUsageTracker.Instance.CanMakeCall())
                {
                    Debug.WriteLine("[ClaudeAPI] Budget or call limit reached");
                    return null;
                }

                // Sanitize all outbound text
                var sanitizedSystem = SanitizeSensitiveData(systemPrompt);
                var sanitizedUser = SanitizeSensitiveData(userMessage);

                var apiKey = config.GetApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Debug.WriteLine("[ClaudeAPI] No API key configured");
                    return null;
                }

                // Build request
                var requestBody = new ClaudeApiRequest
                {
                    Model = config.GetModelName(),
                    MaxTokens = maxTokens,
                    System = sanitizedSystem,
                    Messages = new List<ClaudeMessage>
                    {
                        new ClaudeMessage { Role = "user", Content = sanitizedUser }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var request = new HttpRequestMessage(HttpMethod.Post, API_ENDPOINT)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", API_VERSION);

                // Send request and measure latency
                var sw = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request, cancellationToken);
                sw.Stop();

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ClaudeAPI] HTTP {(int)response.StatusCode}: {responseBody}");
                    AiUsageTracker.Instance.RecordCall(
                        feature.ToString(), config.GetModelName(),
                        0, 0, sw.ElapsedMilliseconds, false,
                        $"HTTP {(int)response.StatusCode}");
                    return null;
                }

                // Parse response
                var apiResponse = JsonSerializer.Deserialize<ClaudeApiResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                if (apiResponse == null)
                {
                    AiUsageTracker.Instance.RecordCall(
                        feature.ToString(), config.GetModelName(),
                        0, 0, sw.ElapsedMilliseconds, false, "Null response");
                    return null;
                }

                // Extract text content
                var textContent = "";
                if (apiResponse.Content != null)
                {
                    foreach (var block in apiResponse.Content)
                    {
                        if (block.Type == "text")
                            textContent += block.Text;
                    }
                }

                var inputTokens = apiResponse.Usage?.InputTokens ?? 0;
                var outputTokens = apiResponse.Usage?.OutputTokens ?? 0;

                // Record successful call
                AiUsageTracker.Instance.RecordCall(
                    feature.ToString(), config.GetModelName(),
                    inputTokens, outputTokens, sw.ElapsedMilliseconds, true);

                Debug.WriteLine($"[ClaudeAPI] {feature}: {inputTokens}in/{outputTokens}out, {sw.ElapsedMilliseconds}ms, ${AiUsageTracker.CalculateCost(inputTokens, outputTokens, config.GetModelName()):F6}");

                return new AiResponse
                {
                    Text = textContent,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Model = apiResponse.Model ?? config.GetModelName()
                };
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[ClaudeAPI] Request cancelled");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[ClaudeAPI] Network error: {ex.Message}");
                AiUsageTracker.Instance.RecordCall(
                    feature.ToString(), AiConfigService.Instance.GetModelName(),
                    0, 0, 0, false, $"Network: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeAPI] Unexpected error: {ex.Message}");
                AiUsageTracker.Instance.RecordCall(
                    feature.ToString(), AiConfigService.Instance.GetModelName(),
                    0, 0, 0, false, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Send a multi-turn conversation to Claude. Returns null on any failure.
        /// Messages list should alternate user/assistant roles.
        /// </summary>
        public async Task<AiResponse?> SendWithHistoryAsync(string systemPrompt, List<ClaudeMessage> messages, AiFeature feature, int maxTokens = MAX_TOKENS_DEFAULT, CancellationToken cancellationToken = default)
        {
            try
            {
                var config = AiConfigService.Instance;
                if (!config.IsEnabled) return null;
                if (!config.IsFeatureEnabled(feature)) return null;
                if (!AiUsageTracker.Instance.CanMakeCall()) return null;

                var sanitizedSystem = SanitizeSensitiveData(systemPrompt);
                var sanitizedMessages = messages.Select(m => new ClaudeMessage
                {
                    Role = m.Role,
                    Content = SanitizeSensitiveData(m.Content)
                }).ToList();

                var apiKey = config.GetApiKey();
                if (string.IsNullOrWhiteSpace(apiKey)) return null;

                var requestBody = new ClaudeApiRequest
                {
                    Model = config.GetModelName(),
                    MaxTokens = maxTokens,
                    System = sanitizedSystem,
                    Messages = sanitizedMessages
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var request = new HttpRequestMessage(HttpMethod.Post, API_ENDPOINT)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", API_VERSION);

                var sw = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request, cancellationToken);
                sw.Stop();

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ClaudeAPI] HTTP {(int)response.StatusCode}: {responseBody}");
                    AiUsageTracker.Instance.RecordCall(feature.ToString(), config.GetModelName(), 0, 0, sw.ElapsedMilliseconds, false, $"HTTP {(int)response.StatusCode}");
                    return null;
                }

                var apiResponse = JsonSerializer.Deserialize<ClaudeApiResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                if (apiResponse == null)
                {
                    AiUsageTracker.Instance.RecordCall(feature.ToString(), config.GetModelName(), 0, 0, sw.ElapsedMilliseconds, false, "Null response");
                    return null;
                }

                var textContent = "";
                if (apiResponse.Content != null)
                    foreach (var block in apiResponse.Content)
                        if (block.Type == "text")
                            textContent += block.Text;

                var inputTokens = apiResponse.Usage?.InputTokens ?? 0;
                var outputTokens = apiResponse.Usage?.OutputTokens ?? 0;

                AiUsageTracker.Instance.RecordCall(feature.ToString(), config.GetModelName(), inputTokens, outputTokens, sw.ElapsedMilliseconds, true);
                Debug.WriteLine($"[ClaudeAPI] {feature}: {inputTokens}in/{outputTokens}out, {sw.ElapsedMilliseconds}ms");

                return new AiResponse
                {
                    Text = textContent,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Model = apiResponse.Model ?? config.GetModelName()
                };
            }
            catch (TaskCanceledException) { return null; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeAPI] SendWithHistory error: {ex.Message}");
                AiUsageTracker.Instance.RecordCall(feature.ToString(), AiConfigService.Instance.GetModelName(), 0, 0, 0, false, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Test the API connection. Returns (success, message).
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = AiConfigService.Instance.GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                return (false, "No API key configured");

            try
            {
                var requestBody = new ClaudeApiRequest
                {
                    Model = AiConfigService.Instance.GetModelName(),
                    MaxTokens = 10,
                    Messages = new List<ClaudeMessage>
                    {
                        new ClaudeMessage { Role = "user", Content = "Reply with just the word: connected" }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var request = new HttpRequestMessage(HttpMethod.Post, API_ENDPOINT)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", API_VERSION);

                var model = AiConfigService.Instance.GetModelName();
                var sw = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request, cancellationToken);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    // Parse response to get token counts for tracking
                    var respBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    try
                    {
                        var apiResp = JsonSerializer.Deserialize<ClaudeApiResponse>(respBody, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        });
                        AiUsageTracker.Instance.RecordCall("TestConnection", model,
                            apiResp?.Usage?.InputTokens ?? 0, apiResp?.Usage?.OutputTokens ?? 0,
                            sw.ElapsedMilliseconds, true);
                    }
                    catch { /* don't fail the test over tracking */ }
                    return (true, $"Connected ({sw.ElapsedMilliseconds}ms)");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                AiUsageTracker.Instance.RecordCall("TestConnection", model, 0, 0, sw.ElapsedMilliseconds, false, $"HTTP {(int)response.StatusCode}");
                return (false, $"HTTP {(int)response.StatusCode}: {body}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Request timed out");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Strip sensitive data from text before sending to AI.
        /// Removes VINs, claim numbers, RO numbers, phone numbers, emails, insurance company names.
        /// </summary>
        public static string SanitizeSensitiveData(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = _vinPattern.Replace(text, "[VIN]");
            text = _claimNumberPattern.Replace(text, "[CLAIM#]");
            text = _roNumberPattern.Replace(text, "[RO#]");
            text = _phonePattern.Replace(text, "[PHONE]");
            text = _emailPattern.Replace(text, "[EMAIL]");
            text = _insurancePattern.Replace(text, "[INSURANCE]");

            return text;
        }

        /// <summary>
        /// Strip markdown code fences from AI response text to get raw JSON/content.
        /// Handles ```json, ```, trailing whitespace, etc.
        /// </summary>
        public static string StripCodeFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            text = text.Trim();
            if (!text.StartsWith("```")) return text;

            // Remove opening fence (```json, ```text, etc.)
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text.Substring(firstNewline + 1);

            // Remove closing fence
            var lastFence = text.LastIndexOf("```");
            if (lastFence >= 0)
                text = text.Substring(0, lastFence);

            return text.Trim();
        }
    }

    #region API Models

    public class AiResponse
    {
        public string Text { get; set; } = "";
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public long LatencyMs { get; set; }
        public string Model { get; set; } = "";
    }

    // Request DTOs
    public class ClaudeApiRequest
    {
        public string Model { get; set; } = "";
        public int MaxTokens { get; set; }
        public string? System { get; set; }
        public List<ClaudeMessage> Messages { get; set; } = new();
    }

    public class ClaudeMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    // Response DTOs
    public class ClaudeApiResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Role { get; set; }
        public string? Model { get; set; }
        public List<ClaudeContent>? Content { get; set; }
        public string? StopReason { get; set; }
        public ClaudeUsage? Usage { get; set; }
    }

    public class ClaudeContent
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
    }

    public class ClaudeUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    #endregion
}
