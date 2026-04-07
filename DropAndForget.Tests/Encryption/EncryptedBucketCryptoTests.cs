using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using DropAndForget.Services.Encryption;
using FluentAssertions;
using NSec.Cryptography;

namespace DropAndForget.Tests.Encryption;

public sealed class EncryptedBucketCryptoTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly AeadAlgorithm EncryptionAlgorithm = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly Argon2Parameters Argon2Parameters = new()
    {
        MemorySize = 32768,
        NumberOfPasses = 2,
        DegreeOfParallelism = 1
    };

    [Fact]
    public void EncryptAndDecryptBytes_ShouldRoundTripPayload()
    {
        var key = RandomNumberGenerator.GetBytes(EncryptionAlgorithm.KeySize);
        var plaintext = Encoding.UTF8.GetBytes("secret payload");

        var payload = EncryptedBucketCrypto.EncryptBytes(key, plaintext, "file-payload", EncryptionAlgorithm, JsonOptions, 1);
        var decrypted = EncryptedBucketCrypto.DecryptBytes(key, payload, "file-payload", EncryptionAlgorithm, JsonOptions, 1);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void DecryptBytes_ShouldRejectPayloadWithWrongPurpose()
    {
        var key = RandomNumberGenerator.GetBytes(EncryptionAlgorithm.KeySize);
        var payload = EncryptedBucketCrypto.EncryptBytes(key, Encoding.UTF8.GetBytes("secret payload"), "file-payload", EncryptionAlgorithm, JsonOptions, 1);

        var act = () => EncryptedBucketCrypto.DecryptBytes(key, payload, "index", EncryptionAlgorithm, JsonOptions, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Encrypted payload format mismatch.");
    }

    [Fact]
    public void DecryptBytes_ShouldRejectTamperedPayload()
    {
        var key = EncryptedBucketCrypto.DeriveKeyEncryptionKey("correct horse battery staple", RandomNumberGenerator.GetBytes(16), Argon2Parameters, EncryptionAlgorithm);
        var payload = EncryptedBucketCrypto.EncryptBytes(key, Encoding.UTF8.GetBytes("secret payload"), "file-payload", EncryptionAlgorithm, JsonOptions, 1);
        payload[^8] ^= 0x5A;

        var act = () => EncryptedBucketCrypto.DecryptBytes(key, payload, "file-payload", EncryptionAlgorithm, JsonOptions, 1);

        act.Should().Throw<Exception>()
            .Where(ex => ex.GetType() == typeof(InvalidOperationException)
                || ex.GetType() == typeof(FormatException)
                || ex.GetType() == typeof(JsonException));
    }
}
