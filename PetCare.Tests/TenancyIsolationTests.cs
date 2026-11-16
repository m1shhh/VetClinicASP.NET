using Microsoft.EntityFrameworkCore;
using VetClinic.Models.ViewModels;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Изолация по клиники (Task 5): global query filter + ICurrentClinic, deny-by-default.
    /// Клиника А НЕ вижда нищо от клиника Б — нито при списък, нито по id,
    /// нито при Create (записът винаги получава ClinicId от ICurrentClinic).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class TenancyIsolationTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public TenancyIsolationTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void GetAllPatients_WithClinicAContext_ReturnsOnlyClinicAPatient()
        {
            using var seed = SeedTwoClinics();

            using var serviceContext = CreateContext(seed.ClinicA.ClinicId);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicA.ClinicId));

            var patients = service.GetAllPatients(null!, null!, null!).ToList();

            var patient = Assert.Single(patients);
            Assert.Equal(seed.PatientA.PatientId, patient.PatientId);
            Assert.NotEqual(seed.PatientB.PatientId, patient.PatientId);
        }

        [Fact]
        public void GetPatientDetails_ForOtherClinicPatient_ReturnsNull()
        {
            using var seed = SeedTwoClinics();

            using var serviceContext = CreateContext(seed.ClinicA.ClinicId);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicA.ClinicId));

            var details = service.GetPatientDetails(seed.PatientB.PatientId);

            Assert.Null(details);
        }

        [Fact]
        public void GetPatientDetails_ForSameClinicPatient_ReturnsDetails()
        {
            using var seed = SeedTwoClinics();

            using var serviceContext = CreateContext(seed.ClinicA.ClinicId);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicA.ClinicId));

            var details = service.GetPatientDetails(seed.PatientA.PatientId);

            Assert.NotNull(details);
            Assert.Equal(seed.PatientA.PatientId, details!.Id);
            Assert.Equal(seed.PatientA.Name, details.Name);
        }

        [Fact]
        public void AddPatient_WithClinicAContext_PersistsRowWithClinicAId()
        {
            using var seed = SeedTwoClinics();

            using var serviceContext = CreateContext(seed.ClinicA.ClinicId);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicA.ClinicId));

            service.AddPatient(new PatientInputModel
            {
                Name = "Рекс",
                Species = "Куче",
                Breed = "Лабрадор",
                Age = 3,
                OwnerId = seed.OwnerA.OwnerId
            });

            // Сурова проверка (IgnoreQueryFilters от null-контекст) — редът физически
            // е в базата със ClinicId на клиника А, а не на някой друг.
            using var rawContext = CreateContext(null);
            var stored = rawContext.Patients.IgnoreQueryFilters().Single(p => p.Name == "Рекс");

            Assert.Equal(seed.ClinicA.ClinicId, stored.ClinicId);
            Assert.Equal(seed.OwnerA.OwnerId, stored.OwnerId);
        }

        [Fact]
        public void AddPatient_WithoutClinicContext_ThrowsInvalidOperationException()
        {
            using var seed = SeedTwoClinics();

            using var serviceContext = CreateContext(null);
            var service = new PatientService(serviceContext, new TestClinicContext(null));

            Assert.Throws<InvalidOperationException>(() =>
                service.AddPatient(new PatientInputModel
                {
                    Name = "Без клиника",
                    Species = "Куче",
                    OwnerId = seed.OwnerA.OwnerId
                }));
        }

        [Fact]
        public void GetRequiredClinicId_WithoutClinicContext_ThrowsInvalidOperationException()
        {
            var currentClinic = new TestClinicContext(null);

            Assert.Throws<InvalidOperationException>(() => currentClinic.GetRequiredClinicId());
        }

        [Fact]
        public void GetAllPatients_WithoutClinicContext_ReturnsEmptyWithoutThrowing()
        {
            using var seed = SeedTwoClinics(); // в базата ИМА редове и на двете клиники

            using var serviceContext = CreateContext(null);
            var service = new PatientService(serviceContext, new TestClinicContext(null));

            // Deny-by-default: заявката се ИЗПЪЛНЯВА (не хвърля) и връща нула реда —
            // SQL сравнението ClinicID = NULL е UNKNOWN за всеки ред.
            var patients = service.GetAllPatients(null!, null!, null!).ToList();

            Assert.Empty(patients);
        }

        private VetClinicContext CreateContext(int? clinicId)
        {
            // През fixture-а: контекстът получава тестовия EncryptionService (конвертори)
            // и tenant филтрите на TestClinicContext(clinicId).
            return _fixture.CreateContext(new TestClinicContext(clinicId));
        }

        private SeedResult SeedTwoClinics()
        {
            // Seed чрез суров контекст с null-clinic: INSERT-ите не минават през query
            // filter-ите (те важат само за SELECT), така че двата клона се записват директно.
            var context = CreateContext(null);

            var clinicA = new Clinic { Name = "Ветклиника А", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            var clinicB = new Clinic { Name = "Ветклиника Б", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            context.Clinics.AddRange(clinicA, clinicB);
            context.SaveChanges();

            var ownerA = new Owner { FullName = "Иван Петров", ClinicId = clinicA.ClinicId };
            var patientA = new Patient
            {
                Name = "Барон",
                Species = "Куче",
                Breed = "Дворянин",
                Age = 4,
                ClinicId = clinicA.ClinicId
            };
            ownerA.Patients.Add(patientA);
            context.Owners.Add(ownerA);

            var ownerB = new Owner { FullName = "Мария Илиева", ClinicId = clinicB.ClinicId };
            var patientB = new Patient
            {
                Name = "Мяу",
                Species = "Котка",
                Breed = "Улична",
                Age = 2,
                ClinicId = clinicB.ClinicId
            };
            ownerB.Patients.Add(patientB);
            context.Owners.Add(ownerB);

            context.SaveChanges();

            return new SeedResult(context, clinicA, clinicB, ownerA, patientA, patientB);
        }

        private sealed class SeedResult : IDisposable
        {
            public VetClinicContext Context { get; }
            public Clinic ClinicA { get; }
            public Clinic ClinicB { get; }
            public Owner OwnerA { get; }
            public Patient PatientA { get; }
            public Patient PatientB { get; }

            public SeedResult(
                VetClinicContext context,
                Clinic clinicA,
                Clinic clinicB,
                Owner ownerA,
                Patient patientA,
                Patient patientB)
            {
                Context = context;
                ClinicA = clinicA;
                ClinicB = clinicB;
                OwnerA = ownerA;
                PatientA = patientA;
                PatientB = patientB;
            }

            public void Dispose() => Context.Dispose();
        }
    }
}
