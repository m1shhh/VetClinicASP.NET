using System;
using System.Security.Cryptography;
using VetClinic.Security;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace PetCare.Tests
{
    public class EncryptionServiceTests
    {
        // Свеж случайн 32-байтов ключ (Base64) на всяка проба.
        private static EncryptionService CreateService()
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Encryption:MasterKey"] = Convert.ToBase64String(key)
                })
                .Build();

            return new EncryptionService(configuration);
        }

        private static string FlipLastBase64Char(string base64)
        {
            var last = base64[^1];
            var replacement = last == 'A' ? 'B' : 'A';
            return base64[..^1] + replacement;
        }

        [Fact]
        public void Roundtrip_PreservesText()
        {
            var service = CreateService();
            var plaintext = "Стопанин Иван Петров — Преглед, диагностика: βулгарски текст с диакритика estival №5!";

            var encrypted = service.Encrypt(plaintext);
            var decrypted = service.Decrypt(encrypted);

            Assert.StartsWith("enc:v1:", encrypted);
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void Decrypt_TamperedData_ThrowsCryptographicException()
        {
            var service = CreateService();
            var encrypted = service.Encrypt("Тайна информация");

            var prefix = "enc:v1:";
            Assert.StartsWith(prefix, encrypted);
            var payload = encrypted[prefix.Length..];
            var tampered = prefix + FlipLastBase64Char(payload);

            Assert.ThrowsAny<CryptographicException>(
                () => service.Decrypt(tampered));
        }

        [Fact]
        public void Decrypt_WithoutPrefix_Throws()
        {
            var service = CreateService();

            Assert.Throws<CryptographicException>(
                () => service.Decrypt("обикновен текст без префикс"));
        }

        [Fact]
        public void Converters_AreIdempotent()
        {
            var service = CreateService();
            var converter = EncryptedStringConverter.Create(service);

            var plaintext = "Мария Илиева, тел. 0888 123 456";
            var encrypted = service.Encrypt(plaintext);

            var written = (string)converter.ConvertToProvider(encrypted)!;

            Assert.Equal(encrypted, written);
            Assert.Equal(plaintext, service.Decrypt(written));
        }

        [Fact]
        public void Converter_Read_LegacyPlaintext_Passthrough()
        {
            var service = CreateService();
            var converter = EncryptedStringConverter.Create(service);

            var legacy = "Стара PlaintText стойност";
            var read = (string)converter.ConvertFromProvider(legacy)!;

            Assert.Equal(legacy, read);
        }
    }
}
