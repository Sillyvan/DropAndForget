using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropAndForget.Serialization;
using NSec.Cryptography;

namespace DropAndForget.Services.Encryption;

internal static class EncryptedBucketCrypto
{
    internal static byte[] DeriveKeyEncryptionKey(string passphrase, byte[] salt, Argon2Parameters parameters, AeadAlgorithm encryptionAlgorithm)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormKC));
        try
        {
            var algorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters);
            return algorithm.DeriveBytes(passwordBytes, salt, encryptionAlgorithm.KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    internal static byte[] WrapKey(byte[] wrappingKey, byte[] plaintextKey, string purpose, AeadAlgorithm encryptionAlgorithm, JsonSerializerOptions jsonOptions, int formatVersion)
    {
        return EncryptBytes(wrappingKey, plaintextKey, purpose, encryptionAlgorithm, jsonOptions, formatVersion);
    }

    internal static byte[] UnwrapKey(byte[] wrappingKey, byte[] wrappedKey, string purpose, AeadAlgorithm encryptionAlgorithm, JsonSerializerOptions jsonOptions, int formatVersion)
    {
        return DecryptBytes(wrappingKey, wrappedKey, purpose, encryptionAlgorithm, jsonOptions, formatVersion);
    }

    internal static byte[] EncryptBytes(byte[] rawKey, byte[] plaintext, string purpose, AeadAlgorithm encryptionAlgorithm, JsonSerializerOptions jsonOptions, int formatVersion)
    {
        using var key = ImportKey(rawKey, encryptionAlgorithm);
        var nonce = RandomNumberGenerator.GetBytes(encryptionAlgorithm.NonceSize);
        var envelope = new EncryptedPayloadEnvelope
        {
            Version = formatVersion,
            Purpose = purpose,
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(encryptionAlgorithm.Encrypt(key, nonce, Encoding.UTF8.GetBytes($"daf:{purpose}:v{formatVersion}"), plaintext))
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, AppJsonSerializerContext.Default.EncryptedPayloadEnvelope));
    }

    internal static byte[] DecryptBytes(byte[] rawKey, byte[] payload, string purpose, AeadAlgorithm encryptionAlgorithm, JsonSerializerOptions jsonOptions, int formatVersion)
    {
        var envelope = JsonSerializer.Deserialize(payload, AppJsonSerializerContext.Default.EncryptedPayloadEnvelope)
            ?? throw new InvalidOperationException("Encrypted payload unreadable.");
        if (!string.Equals(envelope.Purpose, purpose, StringComparison.Ordinal) || envelope.Version != formatVersion)
        {
            throw new InvalidOperationException("Encrypted payload format mismatch.");
        }

        using var key = ImportKey(rawKey, encryptionAlgorithm);
        var plaintext = encryptionAlgorithm.Decrypt(
            key,
            Convert.FromBase64String(envelope.Nonce),
            Encoding.UTF8.GetBytes($"daf:{purpose}:v{envelope.Version}"),
            Convert.FromBase64String(envelope.Ciphertext));

        return plaintext ?? throw new InvalidOperationException("Encrypted payload authentication failed.");
    }

    private static Key ImportKey(byte[] rawKey, AeadAlgorithm encryptionAlgorithm)
    {
        var parameters = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        return Key.Import(encryptionAlgorithm, rawKey, KeyBlobFormat.RawSymmetricKey, in parameters);
    }
}

internal sealed class EncryptedPayloadEnvelope
{
    public int Version { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Ciphertext { get; set; } = string.Empty;
}
