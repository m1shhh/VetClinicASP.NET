using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VetClinic.Security;
using VetClinicASP.NET.Models;
using Xunit;

// Тестовете срещу реален SQL Server тичат последователно — една ephemeral база за цялото изпълнение.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PetCare.Tests.Infrastructure
{
    /// <summary>
    /// Създава ephemeral база PetCareTests_{guid} върху dev SQL Server-а, прилага миграциите
    /// и я пуска при DisposeAsync. Master връзката идва от env PETCARE_TEST_MASTER (CI),
    /// иначе от user-secrets на root проекта (паролата НИКОГА не се отпечатва).
    ///
    /// Притежава ЕДИН тестов EncryptionService (случайн 32-байтов ключ, in-memory конфиг —
    /// същият модел като EncryptionServiceTests): всички context-и през
    /// <see cref="CreateContext"/> получават конверторите с ТОЗИ ключ. Еднакъв ключ е задължителен —
    /// EF кешира един модел за тип context, така че смесени ключове/липсващи конвертори
    /// биха се отровили взаимно.
    /// </summary>
    [CollectionDefinition("SqlTestDatabase")]
    public sealed class SqlTestDatabaseCollection : ICollectionFixture<SqlTestDatabaseFixture>
    {
    }

    public sealed class SqlTestDatabaseFixture : IAsyncLifetime
    {
        private const string MasterEnvVar = "PETCARE_TEST_MASTER";
        private const string TestDatabasePrefix = "PetCareTests_";

        private readonly string _masterConnectionString;

        public string TestDatabaseName { get; }

        public string ConnectionString { get; }

        /// <summary>Единственият EncryptionService за цялото тестово изпълнение (случайн ключ).</summary>
        public EncryptionService Encryption { get; }

        public SqlTestDatabaseFixture()
        {
            _masterConnectionString = ResolveMasterConnectionString();
            TestDatabaseName = TestDatabasePrefix + Guid.NewGuid().ToString("N");
            ConnectionString = WithCatalog(_masterConnectionString, TestDatabaseName);
            Encryption = CreateEncryptionService();
        }

        /// <summary>Контекст с конверторите (fixture-ов ключ) + tenant филтрите на подадения ICurrentClinic.</summary>
        public VetClinicContext CreateContext(ICurrentClinic currentClinic)
        {
            var options = new DbContextOptionsBuilder<VetClinicContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            return new VetClinicContext(options, currentClinic, Encryption);
        }

        public async Task InitializeAsync()
        {
            await using (var connection = new SqlConnection(_masterConnectionString))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"CREATE DATABASE [{TestDatabaseName}]";
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }

            // Migrate върху контекст с null-clinic: миграциите са DDL и не пипат query filter-ите.
            await using var context = CreateContext(new TestClinicContext(null));
            await context.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            try
            {
                // Pooled връзки към тестовата база държат DROP-а заключен — изчистваме ги първо.
                SqlConnection.ClearAllPools();

                await using var connection = new SqlConnection(_masterConnectionString);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"IF DB_ID(N'{TestDatabaseName}') IS NOT NULL " +
                    $"BEGIN " +
                    $"ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{TestDatabaseName}]; " +
                    $"END";

                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                // Почистването е best-effort — не маскира резултата от самите тестове.
            }
        }

        /// <summary>Заменя catalog частта с master (и Initial Catalog=, и Database= форма).</summary>
        private static string WithCatalog(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName
            };

            return builder.ConnectionString;
        }

        // Свеж случайн 32-байтов ключ (Base64) на всяко тестово изпълнение —
        // същият модел като EncryptionServiceTests (in-memory ConfigurationBuilder).
        private static EncryptionService CreateEncryptionService()
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

        private static string ResolveMasterConnectionString()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(MasterEnvVar);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return WithCatalog(fromEnvironment, "master");

            var secretsPath = GetUserSecretsPath();
            if (secretsPath == null || !File.Exists(secretsPath))
                throw new InvalidOperationException(
                    $"Нито {MasterEnvVar}, нито user-secrets файл са налични за тестовата master връзка." +
                    (secretsPath == null
                        ? " Root csproj с UserSecretsId не е намерен."
                        : $" Липсва файл: {secretsPath}"));

            using var document = JsonDocument.Parse(File.ReadAllText(secretsPath));
            if (!document.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var value))
                throw new InvalidOperationException(
                    "user-secrets не съдържа ConnectionStrings:DefaultConnection.");

            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection в user-secrets е празна.");

            return WithCatalog(raw, "master");
        }

        private static string? GetUserSecretsPath()
        {
            var rootProject = FindRootProjectPath();
            if (rootProject == null) return null;

            var document = XDocument.Load(rootProject);
            var userSecretsId = document
                .Descendants("UserSecretsId")
                .FirstOrDefault()
                ?.Value
                .Trim();

            if (string.IsNullOrEmpty(userSecretsId)) return null;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets",
                userSecretsId,
                "secrets.json");
        }

        private static string? FindRootProjectPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "VetClinicASP.NET.csproj");
                if (File.Exists(candidate)) return candidate;

                directory = directory.Parent;
            }

            return null;
        }
    }
}
