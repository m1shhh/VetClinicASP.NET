using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

        public SqlTestDatabaseFixture()
        {
            _masterConnectionString = ResolveMasterConnectionString();
            TestDatabaseName = TestDatabasePrefix + Guid.NewGuid().ToString("N");
            ConnectionString = WithCatalog(_masterConnectionString, TestDatabaseName);
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
            var options = new DbContextOptionsBuilder<VetClinicContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            await using var context = new VetClinicContext(options, new TestClinicContext(null));
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
