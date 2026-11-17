using Microsoft.EntityFrameworkCore;
using VetClinic.Models.ViewModels;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// FK валидация на Create-пътищата (Task 8 — деферълът от Task 5 ревю): всяко
    /// създаване валидира FK референциите (OwnerId / PatientId / VetId) през
    /// ФИЛТРИРАНИЯ контекст — чужд/несъществуващ id хвърля InvalidOperationException
    /// (същият стил като "Няма клиничен контекст.") и НИЩО не се записва.
    /// Self-приписване (Task 8): когато текущият потребител е ветеринарен лекар
    /// (vet_profile_id), VetId се форсира към СОБСТВЕНИЯ профил — подаденият от
    /// формата VetId се игнорира, дори да е чужд.
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class CreatePathValidationTests
    {
        private readonly SqlTestDatabaseFixture _fixture;

        public CreatePathValidationTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void AddAppointment_CrossTenantPatient_ThrowsAndWritesNothing()
        {
            var seed = SeedTwoClinics();
            var serviceContext = CreateContext(seed.ClinicAId, null);
            var service = new AppointmentService(serviceContext, new TestClinicContext(seed.ClinicAId));

            var reason = $"Чужд пациент {UniqueSuffix()}";

            var ex = Assert.Throws<InvalidOperationException>(() => service.AddAppointment(new AppointmentInputModel
            {
                PatientId = seed.PatientBId,      // пациент на клиника Б
                VeterinarianId = seed.VetAId,     // местен лекар (валиден)
                Reason = reason,
                DateTime = DateTime.Now
            }));

            Assert.Contains("Пациент", ex.Message);

            // НИКАКЪВ запис (фильтри по некриптирани колони — свеж seed = нула appointments).
            using var raw = CreateRawContext();
            Assert.Equal(0, raw.Appointments.IgnoreQueryFilters().Count(a => a.PatientId == seed.PatientBId));
        }

        [Fact]
        public void AddAppointment_InClinicPatient_ButForeignVetId_ThrowsAndWritesNothing()
        {
            var seed = SeedTwoClinics();
            var serviceContext = CreateContext(seed.ClinicAId, null);
            var service = new AppointmentService(serviceContext, new TestClinicContext(seed.ClinicAId));

            var reason = $"Чужд лекар {UniqueSuffix()}";

            var ex = Assert.Throws<InvalidOperationException>(() => service.AddAppointment(new AppointmentInputModel
            {
                PatientId = seed.PatientAId,      // местен пациент (валиден)
                VeterinarianId = seed.VetBId,     // лекар на клиника Б
                Reason = reason,
                DateTime = DateTime.Now
            }));

            Assert.Contains("лекар", ex.Message);

            using var raw = CreateRawContext();
            Assert.Equal(0, raw.Appointments.IgnoreQueryFilters().Count(a => a.PatientId == seed.PatientAId));
        }

        [Fact]
        public void AddAppointment_VetSelfAssignment_IgnoresFormSuppliedVetId()
        {
            var seed = SeedTwoClinics();

            // Контекст на клиника А с vet_profile_id = vetA: текущият потребител Е лекар.
            var serviceContext = CreateContext(seed.ClinicAId, seed.VetAId);
            var service = new AppointmentService(serviceContext, new TestClinicContext(seed.ClinicAId, seed.VetAId));

            var reason = $"Самоприписване {UniqueSuffix()}";

            // Подаденият от формата VetId (чужд VetB) ТРЯБВА да се игнорира — не и да се
            // валидира/приеме: записът е на собствения профил.
            service.AddAppointment(new AppointmentInputModel
            {
                PatientId = seed.PatientAId,
                VeterinarianId = seed.VetBId,
                Reason = reason,
                DateTime = DateTime.Now
            });

            using var raw = CreateRawContext();
            // Reason е КРИПТИРАНА колона — сравнение в SQL е сравнение на шифротекст.
            // Материализираме (по некриптирания PatientId) и сравняваме в паметта.
            var stored = raw.Appointments.IgnoreQueryFilters()
                .Where(a => a.PatientId == seed.PatientAId)
                .ToList()
                .Single(a => a.Reason == reason);
            Assert.Equal(seed.VetAId, stored.VetId);
            Assert.Equal(seed.ClinicAId, stored.ClinicId);
        }

        [Fact]
        public void AddVaccination_CrossTenantPatient_ThrowsAndWritesNothing()
        {
            var seed = SeedTwoClinics();
            var serviceContext = CreateContext(seed.ClinicAId, null);
            var service = new VaccinationService(serviceContext, new TestClinicContext(seed.ClinicAId));

            var vaccineName = $"Контролна ваксина {UniqueSuffix()}";

            var ex = Assert.Throws<InvalidOperationException>(() => service.AddVaccination(new VaccinationInputModel
            {
                PatientId = seed.PatientBId,      // пациент на клиника Б
                Name = vaccineName,
                DateAdministered = DateTime.Now
            }));

            Assert.Contains("Пациент", ex.Message);

            using var raw = CreateRawContext();
            Assert.False(raw.Vaccinations.IgnoreQueryFilters().Any(v => v.VaccineName == vaccineName));
        }

        [Fact]
        public void AddVaccination_VetSelfAssignment_StampVetProfileId()
        {
            var seed = SeedTwoClinics();

            var serviceContext = CreateContext(seed.ClinicAId, seed.VetAId);
            var service = new VaccinationService(serviceContext, new TestClinicContext(seed.ClinicAId, seed.VetAId));

            var vaccineName = $"Ваксина {UniqueSuffix()}";

            service.AddVaccination(new VaccinationInputModel
            {
                PatientId = seed.PatientAId,
                Name = vaccineName,
                DateAdministered = DateTime.Now
            });

            // Редът получава VetId = собствен профил на текущия лекар.
            using var raw = CreateRawContext();
            var stored = raw.Vaccinations.IgnoreQueryFilters().Single(v => v.VaccineName == vaccineName);
            Assert.Equal(seed.VetAId, stored.VetId);
        }

        [Fact]
        public void AddPatient_CrossTenantOwner_ThrowsAndWritesNothing()
        {
            var seed = SeedTwoClinics();
            var serviceContext = CreateContext(seed.ClinicAId, null);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicAId));

            var patientName = $"Чужд стопанин {UniqueSuffix()}";

            var ex = Assert.Throws<InvalidOperationException>(() => service.AddPatient(new PatientInputModel
            {
                Name = patientName,
                Species = "Куче",
                OwnerId = seed.OwnerBId,          // стопанин на клиника Б
            }));

            Assert.Contains("Стопанин", ex.Message);

            using var raw = CreateRawContext();
            Assert.False(raw.Patients.IgnoreQueryFilters().Any(p => p.Name == patientName));
        }

        [Fact]
        public void AddPatient_InClinicOwner_StillPersistsWithCurrentClinic()
        {
            // Позитивен контрол: валиден местен стопанин минава и записът е с ClinicId = текуща.
            var seed = SeedTwoClinics();
            var serviceContext = CreateContext(seed.ClinicAId, null);
            var service = new PatientService(serviceContext, new TestClinicContext(seed.ClinicAId));

            var patientName = $"Местен пациент {UniqueSuffix()}";

            service.AddPatient(new PatientInputModel
            {
                Name = patientName,
                Species = "Куче",
                OwnerId = seed.OwnerAId
            });

            using var raw = CreateRawContext();
            var stored = raw.Patients.IgnoreQueryFilters().Single(p => p.Name == patientName);
            Assert.Equal(seed.ClinicAId, stored.ClinicId);
            Assert.Equal(seed.OwnerAId, stored.OwnerId);
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        private VetClinicContext CreateContext(int? clinicId, int? vetProfileId) =>
            _fixture.CreateContext(new TestClinicContext(clinicId, vetProfileId));

        private VetClinicContext CreateRawContext() =>
            _fixture.CreateContext(new TestClinicContext(null));

        private FkSeed SeedTwoClinics()
        {
            var suffix = UniqueSuffix();

            using var raw = CreateRawContext();

            var clinicA = new Clinic { Name = $"FK клиника А {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            var clinicB = new Clinic { Name = $"FK клиника Б {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            raw.Clinics.AddRange(clinicA, clinicB);
            raw.SaveChanges();

            var ownerA = new Owner { FullName = $"Стопанин А {suffix}", ClinicId = clinicA.ClinicId };
            var ownerB = new Owner { FullName = $"Стопанин Б {suffix}", ClinicId = clinicB.ClinicId };
            raw.Owners.AddRange(ownerA, ownerB);
            raw.SaveChanges();

            var patientA = new Patient
            {
                Name = $"Пациент А {suffix}",
                Species = "Куче",
                OwnerId = ownerA.OwnerId,
                ClinicId = clinicA.ClinicId
            };
            var patientB = new Patient
            {
                Name = $"Пациент Б {suffix}",
                Species = "Котка",
                OwnerId = ownerB.OwnerId,
                ClinicId = clinicB.ClinicId
            };
            raw.Patients.AddRange(patientA, patientB);

            var vetA = new Veterinarian { FullName = $"Д-р А {suffix}", ClinicId = clinicA.ClinicId };
            var vetB = new Veterinarian { FullName = $"Д-р Б {suffix}", ClinicId = clinicB.ClinicId };
            raw.Veterinarians.AddRange(vetA, vetB);
            raw.SaveChanges();

            return new FkSeed(
                clinicA.ClinicId, clinicB.ClinicId,
                ownerA.OwnerId, ownerB.OwnerId,
                patientA.PatientId, patientB.PatientId,
                vetA.VetId, vetB.VetId);
        }

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

        private sealed record FkSeed(
            int ClinicAId, int ClinicBId,
            int OwnerAId, int OwnerBId,
            int PatientAId, int PatientBId,
            int VetAId, int VetBId);
    }
}
