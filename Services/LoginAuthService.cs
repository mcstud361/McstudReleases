#nullable enable
using System.Net.Http;
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
    /// Validate an email against Google Sheets via Apps Script GET request.
    /// Returns VALID, INVALID, or NO_EMAIL as plain text.
    /// </summary>
    public static async Task<LoginResult> LoginAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(AppsScriptUrl))
        {
            return new LoginResult
            {
                Success = false,
                Message = "License service not configured (no Apps Script URL)"
            };
        }

        try
        {
            var url = $"{AppsScriptUrl}?email={Uri.EscapeDataString(email)}";
            var response = await _httpClient.GetAsync(url);
            var responseBody = (await response.Content.ReadAsStringAsync()).Trim();

            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Response: {responseBody}");

            return responseBody.Equals("VALID", StringComparison.OrdinalIgnoreCase)
                ? new LoginResult { Success = true, Message = "License activated" }
                : new LoginResult { Success = false, Message = "Invalid license email. Contact support if you believe this is an error." };
        }
        catch (TaskCanceledException)
        {
            return new LoginResult { Success = false, Message = "License server timeout — check your internet connection" };
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult { Success = false, Message = $"Cannot reach license server: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"License check error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Re-validate the saved session email on startup.
    /// Returns a LoginResult indicating whether the session is still valid.
    /// </summary>
    public static async Task<LoginResult> ValidateSessionAsync()
    {
        var email = LoadSession();
        if (string.IsNullOrWhiteSpace(email))
        {
            return new LoginResult { Success = false, Message = "No saved session" };
        }

        var result = await LoginAsync(email);
        if (!result.Success)
        {
            ClearSession();
        }
        return result;
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
}
