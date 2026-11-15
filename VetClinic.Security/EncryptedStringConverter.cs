using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VetClinic.Security
{
    /// <summary>
    /// Idempotent EF ValueConverter factory за текст, криптиран с
    /// <see cref="EncryptionService"/> (AES-256-GCM, префикс "enc:v1:").
    /// Write: null → null; вече криптирано (започва с префикса) → as-is;
    /// иначе → Encrypt. Read: null → null; криптирано → Decrypt;
    /// иначе → as-is (legacy plaintext passthrough).
    /// </summary>
    public static class EncryptedStringConverter
    {
        private const string TextPrefix = "enc:v1:";

        public static ValueConverter<string?, string?> Create(
            EncryptionService encryption)
        {
            return new ValueConverter<string?, string?>(
                value => EncryptForStorage(encryption, value),
                value => DecryptFromStorage(encryption, value));
        }

        private static string? EncryptForStorage(
            EncryptionService encryption, string? value)
        {
            if (value is null)
                return null;

            if (value.StartsWith(
                    TextPrefix, StringComparison.Ordinal))
                return value;

            return encryption.Encrypt(value);
        }

        private static string? DecryptFromStorage(
            EncryptionService encryption, string? value)
        {
            if (value is null)
                return null;

            if (value.StartsWith(
                    TextPrefix, StringComparison.Ordinal))
                return encryption.Decrypt(value);

            return value;
        }
    }
}
