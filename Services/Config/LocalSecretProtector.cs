using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DropAndForget.Services.Diagnostics;
using NSec.Cryptography;

namespace DropAndForget.Services.Config;

public sealed class LocalSecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int KeySize = 32;
    private static readonly AeadAlgorithm EncryptionAlgorithm = AeadAlgorithm.XChaCha20Poly1305;
    private readonly string _keyPath;

    public LocalSecretProtector(string? keyPath = null)
    {
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            _keyPath = keyPath;
            return;
        }

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "DropAndForget");
        _keyPath = Path.Combine(appDirectory, "config.key");
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(EncryptionAlgorithm.NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        using var importedKey = ImportKey(key);
        var ciphertext = EncryptionAlgorithm.Encrypt(importedKey, nonce, Encoding.UTF8.GetBytes("daf:config:v1"), plaintextBytes);
        var payload = new byte[EncryptionAlgorithm.NonceSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, EncryptionAlgorithm.NonceSize);
        Buffer.BlockCopy(ciphertext, 0, payload, EncryptionAlgorithm.NonceSize, ciphertext.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return string.Empty;
        }

        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        try
        {
            var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
            if (payload.Length < EncryptionAlgorithm.NonceSize + EncryptionAlgorithm.TagSize)
            {
                return string.Empty;
            }

            var key = LoadOrCreateKey();
            var nonce = payload.AsSpan(0, EncryptionAlgorithm.NonceSize).ToArray();
            var encrypted = payload.AsSpan(EncryptionAlgorithm.NonceSize).ToArray();

            using var importedKey = ImportKey(key);
            var plaintext = EncryptionAlgorithm.Decrypt(importedKey, nonce, Encoding.UTF8.GetBytes("daf:config:v1"), encrypted);
            if (plaintext is null)
            {
                DebugLog.Write("Secret decrypt failed: authentication failed.");
                throw new InvalidOperationException("Saved credentials couldn't be read.");
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"Secret decrypt failed: {ex.Message}");
            throw new InvalidOperationException("Saved credentials couldn't be read.", ex);
        }
    }

    private byte[] LoadOrCreateKey()
    {
        var directory = Path.GetDirectoryName(_keyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Secret key directory missing.");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(_keyPath))
        {
            var existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == KeySize)
            {
                return existing;
            }

            DebugLog.Write($"Secret key had invalid size {existing.Length}. Regenerating.");
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(_keyPath, key);
        return key;
    }

    private static Key ImportKey(byte[] rawKey)
    {
        var parameters = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        return Key.Import(EncryptionAlgorithm, rawKey, KeyBlobFormat.RawSymmetricKey, in parameters);
    }
}
