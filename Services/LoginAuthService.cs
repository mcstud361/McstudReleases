#nullable enable
using System.Net.Http;
using System.Text.Json;

namespace McStudDesktop.Services;

public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Tier { get; set; } = "standard";
}

public class LoginAuthService
{
    // Set this to your deployed Google Apps Script Web App URL
    // Example: https://script.google.com/macros/s/AKfycby.../exec
    private const string AppsScriptUrl = "https://script.google.com/macros/s/AKfycbyxkSNj7nmM2J2pIA7Bew9CAVCtFeeuoFKOegCgKVMi43l638G-Cec9tYZQGsEG8lUD_Q/exec";

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
    /// Sends app version, machine name, OS version, and public IP for tracking.
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
            var appVersion = GetAppVersion();
            var machineName = Environment.MachineName;
            var osVersion = Environment.OSVersion.ToString();
            var ip = await GetPublicIpAsync();

            var url = $"{AppsScriptUrl}" +
                $"?email={Uri.EscapeDataString(email)}" +
                $"&appVersion={Uri.EscapeDataString(appVersion)}" +
                $"&machineName={Uri.EscapeDataString(machineName)}" +
                $"&osVersion={Uri.EscapeDataString(osVersion)}" +
                $"&ip={Uri.EscapeDataString(ip)}";

            var response = await _httpClient.GetAsync(url);
            var responseBody = (await response.Content.ReadAsStringAsync()).Trim();

            System.Diagnostics.Debug.WriteLine($"[LoginAuth] Response: {responseBody}");

            // Parse JSON response from Apps Script
            var doc = JsonDocument.Parse(responseBody);
            var status = doc.RootElement.GetProperty("status").GetString() ?? "";
            var message = doc.RootElement.GetProperty("message").GetString() ?? "";
            var tier = doc.RootElement.TryGetProperty("tier", out var tierProp)
                ? tierProp.GetString() ?? "standard"
                : "standard";

            return new LoginResult
            {
                Success = status.Equals("VALID", StringComparison.OrdinalIgnoreCase),
                Message = message,
                Tier = tier
            };
        }
        catch (TaskCanceledException)
        {
            return new LoginResult { Success = false, Message = "License server timeout — check your internet connection" };
        }
        catch (HttpRequestException)
        {
            return new LoginResult { Success = false, Message = "Cannot reach license server. An internet connection is required." };
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"License check error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Re-validate the saved session email on startup.
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

    /// <summary>
    /// Get the app version from the assembly or Velopack.
    /// </summary>
    private static string GetAppVersion()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver != null ? ver.ToString(3) : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Try to get the public IP via a free API. Returns "unknown" on failure.
    /// </summary>
    private static async Task<string> GetPublicIpAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var ip = (await _httpClient.GetStringAsync("https://api.ipify.org", cts.Token)).Trim();
            return ip;
        }
        catch
        {
            return "unknown";
        }
    }
}
