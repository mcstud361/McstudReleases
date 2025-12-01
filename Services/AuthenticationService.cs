#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McStudDesktop.Services;

public enum UserStatus
{
    Active,
    PendingApproval,
    Disabled
}

public class UserAccount
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserStatus Status { get; set; } = UserStatus.PendingApproval;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginDate { get; set; }
    public bool IsAdmin { get; set; } = false;
}

public class AuthenticationService
{
    private const string UsersFilePath = "users.dat";
    private const string SettingsFilePath = "auth_settings.json";
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("McStud2025EncryptK"); // 16 bytes for AES-128

    private List<UserAccount> _users = new();
    private static AuthenticationService? _instance;
    private static readonly object _lock = new();

    public static AuthenticationService Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new AuthenticationService();
                return _instance;
            }
        }
    }

    private AuthenticationService()
    {
        LoadUsers();
        EnsureDefaultAdminExists();
    }

    private void EnsureDefaultAdminExists()
    {
        // Create default admin account if no admin exists
        if (!_users.Any(u => u.IsAdmin))
        {
            var adminAccount = new UserAccount
            {
                Username = "admin",
                PasswordHash = HashPassword("admin123"),
                Status = UserStatus.Active,
                IsAdmin = true,
                CreatedDate = DateTime.UtcNow
            };
            _users.Add(adminAccount);
            SaveUsers();
            System.Diagnostics.Debug.WriteLine("[Auth] Default admin account created (username: admin, password: admin123)");
        }
    }

    public bool ValidateCredentials(string username, string password)
    {
        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null || user.Status != UserStatus.Active)
            return false;

        var passwordHash = HashPassword(password);
        if (user.PasswordHash == passwordHash)
        {
            user.LastLoginDate = DateTime.UtcNow;
            SaveUsers();
            return true;
        }

        return false;
    }

    public bool RegisterUser(string username, string password, out string errorMessage)
    {
        errorMessage = string.Empty;

        // Validate username
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
        {
            errorMessage = "Username must be at least 3 characters long.";
            return false;
        }

        // Check if username already exists
        if (_users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "Username already exists.";
            return false;
        }

        // Validate password
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            errorMessage = "Password must be at least 6 characters long.";
            return false;
        }

        // Create new user account (pending approval)
        var newUser = new UserAccount
        {
            Username = username,
            PasswordHash = HashPassword(password),
            Status = UserStatus.PendingApproval,
            CreatedDate = DateTime.UtcNow,
            IsAdmin = false
        };

        _users.Add(newUser);
        SaveUsers();

        System.Diagnostics.Debug.WriteLine($"[Auth] New user registered: {username} (pending approval)");
        return true;
    }

    public List<UserAccount> GetPendingUsers()
    {
        return _users.Where(u => u.Status == UserStatus.PendingApproval).ToList();
    }

    public void ApproveUser(string username)
    {
        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user != null)
        {
            user.Status = UserStatus.Active;
            SaveUsers();
            System.Diagnostics.Debug.WriteLine($"[Auth] User approved: {username}");
        }
    }

    public void RejectUser(string username)
    {
        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user != null)
        {
            _users.Remove(user);
            SaveUsers();
            System.Diagnostics.Debug.WriteLine($"[Auth] User rejected and removed: {username}");
        }
    }

    public bool ResetPassword(string username, string newPassword)
    {
        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user != null)
        {
            user.PasswordHash = HashPassword(newPassword);
            SaveUsers();
            System.Diagnostics.Debug.WriteLine($"[Auth] Password reset for user: {username}");
            return true;
        }

        return false;
    }

    public bool IsUserActive(string username)
    {
        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        return user?.Status == UserStatus.Active;
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private void LoadUsers()
    {
        try
        {
            if (File.Exists(UsersFilePath))
            {
                var encryptedData = File.ReadAllBytes(UsersFilePath);
                var decryptedData = DecryptData(encryptedData);
                var json = Encoding.UTF8.GetString(decryptedData);
                _users = JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
                System.Diagnostics.Debug.WriteLine($"[Auth] Loaded {_users.Count} users from encrypted file");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Error loading users: {ex.Message}");
            _users = new List<UserAccount>();
        }
    }

    private void SaveUsers()
    {
        try
        {
            var json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
            var dataBytes = Encoding.UTF8.GetBytes(json);
            var encryptedData = EncryptData(dataBytes);
            File.WriteAllBytes(UsersFilePath, encryptedData);
            System.Diagnostics.Debug.WriteLine($"[Auth] Saved {_users.Count} users to encrypted file");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Auth] Error saving users: {ex.Message}");
        }
    }

    private byte[] EncryptData(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    private byte[] DecryptData(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = EncryptionKey;

        // Extract IV from the beginning of the data
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var encryptedData = new byte[data.Length - iv.Length];
        Buffer.BlockCopy(data, iv.Length, encryptedData, 0, encryptedData.Length);

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
    }
}
