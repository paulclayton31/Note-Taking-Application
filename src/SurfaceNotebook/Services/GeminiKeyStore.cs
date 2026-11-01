using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SurfaceNotebook.Services;

// Personal API credentials stay on this Windows account, outside notebook data.
public sealed class GeminiKeyStore(string directory)
{
    private string KeyPath => Path.Combine(directory, "gemini-key.bin");
    public string Load()
    {
        if (!File.Exists(KeyPath)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException) { return ""; }
    }
    public void Save(string key)
    {
        Directory.CreateDirectory(directory);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        var temporary = KeyPath + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, KeyPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Delete() { if (File.Exists(KeyPath)) File.Delete(KeyPath); }
}
