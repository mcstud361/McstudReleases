#nullable enable
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace McstudDesktop.Services.Legacy;

public class EmailSettings
{
    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string AdminEmail { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderPassword { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
}

public class EmailNotificationService
{
    private const string EmailSettingsPath = "email_settings.json";
    private EmailSettings _settings;
    private static EmailNotificationService? _instance;
    private static readonly object _lock = new();

    public static EmailNotificationService Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new EmailNotificationService();
                return _instance;
            }
        }
    }

    private EmailNotificationService()
    {
        _settings = LoadSettings();
    }

    public EmailSettings GetSettings()
    {
        return _settings;
    }

    public void SaveSettings(EmailSettings settings)
    {
        _settings = settings;
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(EmailSettingsPath, json);
            System.Diagnostics.Debug.WriteLine("[Email] Settings saved successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Email] Error saving settings: {ex.Message}");
        }
    }

    private EmailSettings LoadSettings()
    {
        try
        {
            if (File.Exists(EmailSettingsPath))
            {
                var json = File.ReadAllText(EmailSettingsPath);
                var settings = JsonSerializer.Deserialize<EmailSettings>(json);
                if (settings != null)
                {
                    System.Diagnostics.Debug.WriteLine("[Email] Settings loaded successfully");
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Email] Error loading settings: {ex.Message}");
        }

        return new EmailSettings();
    }

    public async Task<bool> SendPasswordResetNotificationAsync(string username, string userEmail = "")
    {
        if (string.IsNullOrWhiteSpace(_settings.AdminEmail) ||
            string.IsNullOrWhiteSpace(_settings.SenderEmail) ||
            string.IsNullOrWhiteSpace(_settings.SenderPassword))
        {
            System.Diagnostics.Debug.WriteLine("[Email] Email settings not configured");
            return false;
        }

        try
        {
            var subject = $"Password Reset Request - {username}";
            var body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>Password Reset Request</h2>
    <p>A password reset has been requested for the following account:</p>
    <ul>
        <li><strong>Username:</strong> {username}</li>
        <li><strong>Email:</strong> {userEmail}</li>
        <li><strong>Time:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</li>
    </ul>
    <p>To reset the password, please use the admin tools in the Mcstud Desktop application.</p>
    <hr>
    <p style='color: gray; font-size: 12px;'>This is an automated notification from Mcstud Desktop.</p>
</body>
</html>";

            await SendEmailAsync(_settings.AdminEmail, subject, body);
            System.Diagnostics.Debug.WriteLine($"[Email] Password reset notification sent for user: {username}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Email] Error sending password reset notification: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendRegistrationNotificationAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(_settings.AdminEmail) ||
            string.IsNullOrWhiteSpace(_settings.SenderEmail) ||
            string.IsNullOrWhiteSpace(_settings.SenderPassword))
        {
            System.Diagnostics.Debug.WriteLine("[Email] Email settings not configured");
            return false;
        }

        try
        {
            var subject = $"New User Registration - {username}";
            var body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>New User Registration</h2>
    <p>A new user has registered and is awaiting approval:</p>
    <ul>
        <li><strong>Username:</strong> {username}</li>
        <li><strong>Registration Time:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</li>
    </ul>
    <p>Please review and approve this registration in the admin panel of the Mcstud Desktop application.</p>
    <hr>
    <p style='color: gray; font-size: 12px;'>This is an automated notification from Mcstud Desktop.</p>
</body>
</html>";

            await SendEmailAsync(_settings.AdminEmail, subject, body);
            System.Diagnostics.Debug.WriteLine($"[Email] Registration notification sent for user: {username}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Email] Error sending registration notification: {ex.Message}");
            return false;
        }
    }

    private async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        using var client = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort)
        {
            Credentials = new NetworkCredential(_settings.SenderEmail, _settings.SenderPassword),
            EnableSsl = _settings.EnableSsl
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(_settings.SenderEmail, "Mcstud Desktop"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        mailMessage.To.Add(toEmail);

        await client.SendMailAsync(mailMessage);
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_settings.AdminEmail) &&
               !string.IsNullOrWhiteSpace(_settings.SenderEmail) &&
               !string.IsNullOrWhiteSpace(_settings.SenderPassword);
    }
}
