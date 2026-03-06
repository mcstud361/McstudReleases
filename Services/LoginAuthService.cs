#nullable enable
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace McStudDesktop.Services;

public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class LoginAuthService
{
    // Set this to your deployed Google Apps Script Web App URL
    // Example: https://script.google.com/macros/s/AKfycby.../exec
    private const string AppsScriptUrl = "";

    private static readonly HttpClient _httpClient = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McStudDesktop");

    private static readonly string SessionFile = Path.Combine(SessionDir, "session.json");

    /// <summary>
    /// Authenticate against Google Sheets via Apps Script.
    /// If no URL configured, returns failure with dev-mode message.
    /// </summary>
    public static async Task<LoginResult> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(AppsScriptUrl))
        {
            return new LoginResult
            {
                Success = false,
                Message = "Login service not configured (no Apps Script URL)"
            };
        }

        try
        {
            var payload = new { email, password };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(AppsScriptUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Response: {responseBody}");

            var result = JsonSerializer.Deserialize<LoginResult>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new LoginResult { Success = false, Message = "Invalid server response" };
        }
        catch (TaskCanceledException)
        {
            return new LoginResult { Success = false, Message = "Login server timeout - check internet connection" };
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult { Success = false, Message = $"Cannot reach login server: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"Login error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Save a session so the user doesn't re-login every launch.
    /// </summary>
    public static void SaveSession(string email)
    {
        try
        {
            Directory.CreateDirectory(SessionDir);
            var session = new { Email = email, SavedAt = DateTime.UtcNow.ToString("o") };
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(session));
            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Session saved for {email}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Error saving session: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the saved session email, or null if none exists.
    /// </summary>
    public static string? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return null;

            var json = File.ReadAllText(SessionFile);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Email", out var emailProp))
                return emailProp.GetString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Error loading session: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Clear the saved session (logout).
    /// </summary>
    public static void ClearSession()
    {
        try
        {
            if (File.Exists(SessionFile))
                File.Delete(SessionFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Error clearing session: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if a saved session exists (for auto-login on startup).
    /// </summary>
    public static bool IsSessionValid()
    {
        return LoadSession() != null;
    }
}
