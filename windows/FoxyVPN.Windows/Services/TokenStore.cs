using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoxyVPN.Windows.Models;

namespace FoxyVPN.Windows.Services;

internal sealed class TokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FoxyVPN.Windows.AuthStore.v1");
    private readonly string _path;
    private readonly object _sync = new();

    public TokenStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoxyVPN");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "session.bin");
    }

    public RuntimeAuth? Load()
    {
        lock (_sync)
        {
            byte[]? cipher = null;
            byte[]? plain = null;
            try
            {
                if (!File.Exists(_path)) return null;
                cipher = File.ReadAllBytes(_path);
                plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<RuntimeAuth>(plain);
            }
            catch (CryptographicException) { return null; }
            catch (IOException) { return null; }
            catch (JsonException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            finally
            {
                if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    public void Save(RuntimeAuth auth)
    {
        lock (_sync)
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(auth);
            byte[]? cipher = null;
            var temp = _path + ".tmp";
            try
            {
                cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(temp, cipher);
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
        }
    }
}
