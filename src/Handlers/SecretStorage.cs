using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace Thio_Universal_Agent;

/// <summary>Represents the raw AES-encrypted payload for a single vault entry (IV + ciphertext, both Base64-encoded).</summary>
public sealed record VaultEntryData(string IV, string Ciphertext);

public interface ISecretProvider
{
    void SaveSecret(string keyName, string plainTextSecret, string passwordHash);
    string? LoadSecret(string keyName, string passwordHash);
    bool SecretExists(string keyName);
    void DeleteSecret(string keyName);

    /// <summary>Returns the raw encrypted entries for every key present in the vault, without decrypting them.</summary>
    IReadOnlyDictionary<string, VaultEntryData> ExportAllEncryptedSecrets();

    /// <summary>Writes raw encrypted entries directly into the vault, overwriting any existing entry for the same key. No decryption is performed.</summary>
    void ImportEncryptedSecrets(IReadOnlyDictionary<string, VaultEntryData> entries);
}

/// <summary>
/// Provides cross-platform encrypted storage for secrets (such as API keys) using AES encryption and PBKDF2-SHA256 key derivation.
/// The user provides a password that is hashed client-side and sent to the backend to derive an AES key.
/// All secrets are stored together in a single vault file (<c>secrets_vault.json</c>) under the OS LocalApplicationData folder.
/// Each entry in the vault is independently AES-encrypted (with its own random IV) so that key names and existence
/// can be queried without a password, while values remain protected. Because all entries share one vault file,
/// a single vault password governs access to every secret — there is no way to end up with different passwords per entry.
/// </summary>
/// <remarks>
/// The vault file resides at <c>%LocalAppData%/ThioUniversalAgent/secrets_vault.json</c> (or the platform equivalent).
/// Key names are stored in plaintext as JSON properties; values are stored as Base64-encoded AES ciphertext alongside
/// their IV. The AES key is derived from the provided password hash via PBKDF2 with 100,000 iterations of SHA-256.
/// </remarks>
public class SecretsHandler : ISecretProvider
{
    private static readonly byte[] AppSalt = Encoding.UTF8.GetBytes("ThioAgent_CrossPlatform_Salt_V1!");
    private const string VaultFileName = "secrets_vault.json";

    private readonly string _vaultFilePath;
    private readonly Lock _fileLock = new();

    public SecretsHandler()
    {
        // Automatically resolves to the correct AppData folder on Windows, Mac, and Linux
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDataFolder = Path.Combine(localAppData, "ThioUniversalAgent");
        Directory.CreateDirectory(appDataFolder);
        _vaultFilePath = Path.Combine(appDataFolder, VaultFileName);
    }

    public void SaveSecret(string keyName, string plainTextSecret, string passwordHash)
    {
        byte[] key = DeriveKey(Encoding.UTF8.GetBytes(passwordHash));

        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        byte[] ciphertext;
        using (MemoryStream ms = new())
        {
            using CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
            byte[] secretBytes = Encoding.UTF8.GetBytes(plainTextSecret);
            cs.Write(secretBytes);
            cs.FlushFinalBlock();
            ciphertext = ms.ToArray();
        }

        lock (_fileLock)
        {
            Dictionary<string, VaultEntry> vault = ReadVaultFile();
            vault[keyName] = new VaultEntry(Convert.ToBase64String(aes.IV), Convert.ToBase64String(ciphertext));
            WriteVaultFile(vault);
        }
    }

    public string? LoadSecret(string keyName, string passwordHash)
    {
        Dictionary<string, VaultEntry> vault;
        lock (_fileLock)
            vault = ReadVaultFile();

        if (!vault.TryGetValue(keyName, out VaultEntry? entry))
            return null;

        byte[] key = DeriveKey(Encoding.UTF8.GetBytes(passwordHash));
        byte[] iv = Convert.FromBase64String(entry.IV);
        byte[] ciphertext = Convert.FromBase64String(entry.Ciphertext);

        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        try
        {
            using MemoryStream ms = new(ciphertext);
            using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using StreamReader reader = new(cs, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool SecretExists(string keyName)
    {
        lock (_fileLock)
            return ReadVaultFile().ContainsKey(keyName);
    }

    public void DeleteSecret(string keyName)
    {
        lock (_fileLock)
        {
            Dictionary<string, VaultEntry> vault = ReadVaultFile();
            if (vault.Remove(keyName))
                WriteVaultFile(vault);
        }
    }

    public IReadOnlyDictionary<string, VaultEntryData> ExportAllEncryptedSecrets()
    {
        lock (_fileLock)
        {
            Dictionary<string, VaultEntry> vault = ReadVaultFile();
            return vault.ToDictionary(
                kvp => kvp.Key,
                kvp => new VaultEntryData(kvp.Value.IV, kvp.Value.Ciphertext));
        }
    }

    public void ImportEncryptedSecrets(IReadOnlyDictionary<string, VaultEntryData> entries)
    {
        lock (_fileLock)
        {
            Dictionary<string, VaultEntry> vault = ReadVaultFile();
            foreach (KeyValuePair<string, VaultEntryData> kvp in entries)
                vault[kvp.Key] = new VaultEntry(kvp.Value.IV, kvp.Value.Ciphertext);
            WriteVaultFile(vault);
        }
    }

    private Dictionary<string, VaultEntry> ReadVaultFile()
    {
        if (!File.Exists(_vaultFilePath))
            return [];

        string json = File.ReadAllText(_vaultFilePath);
        return JsonSerializer.Deserialize<Dictionary<string, VaultEntry>>(json) ?? [];
    }

    private void WriteVaultFile(Dictionary<string, VaultEntry> vault)
    {
        string json = JsonSerializer.Serialize(vault, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_vaultFilePath, json);
    }

    private static byte[] DeriveKey(byte[] passwordHashEntropy)
    {
        return Rfc2898DeriveBytes.Pbkdf2(passwordHashEntropy, AppSalt, 100_000, HashAlgorithmName.SHA256, 32);
    }

    private sealed record VaultEntry(string IV, string Ciphertext);  // internal file format — not exposed via API
}