using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using VetClinic.Controllers;
using VetClinic.Models.ViewModels;
using VetClinic.Security;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using Xunit;
using PetCare.Tests.Infrastructure;

namespace PetCare.Tests
{
    /// <summary>
    /// Изтриване/редакция guard-ове (Task 11 — деферъли от Task 8 ревю):
    /// - DeleteOwner е БЛОКИРАН, ако стопанинът има пациенти (точното BG съобщение), данните
    ///   остават непокътнати; контролерът surface-ва грешката БЕЗ 500;
    /// - Update пътищата валидират FK през ФИЛТРИРАНИЯ контекст (същият модел като create);
    /// - MedicalRecordsController.Edit форсира СОБСТВЕНИЯ профил за Vet потребител;
    /// - изтриване на Veterinarian, свързан с ApplicationUser, е блокирано (няма DB FK).
    /// </summary>
    [Collection("SqlTestDatabase")]
    public class DeletionGuardsTests
    {
        private const string OwnerHasPatientsMessage =
            "Стопанинът има регистрирани пациенти. Първо изтрийте пациентите му.";

        private const string VetLinkedMessage =
            "Ветеринарният профил е свързан със служебен акаунт и не може да бъде изтрит.";

        private readonly SqlTestDatabaseFixture _fixture;

        public DeletionGuardsTests(SqlTestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        // =========================================================
        // Owner deletion guard
        // =========================================================

        [Fact]
        public void DeleteOwner_WithPatients_ThrowsExactMessageAndKeepsData()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new OwnerService(context, currentClinic);

            var ex = Assert.Throws<InvalidOperationException>(() => service.DeleteOwner(seed.OwnerA1Id));

            Assert.Equal(OwnerHasPatientsMessage, ex.Message);

            using var raw = CreateRawContext();
            Assert.True(raw.Owners.IgnoreQueryFilters().Any(o => o.OwnerId == seed.OwnerA1Id));
            Assert.True(raw.Patients.IgnoreQueryFilters().Any(p => p.PatientId == seed.PatientA1Id));
        }

        [Fact]
        public void DeleteOwner_WithoutPatients_DeletesSuccessfully()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new OwnerService(context, currentClinic);

            service.DeleteOwner(seed.OwnerA2Id);

            using var raw = CreateRawContext();
            Assert.False(raw.Owners.IgnoreQueryFilters().Any(o => o.OwnerId == seed.OwnerA2Id));
        }

        [Fact]
        public void OwnersController_DeleteWithPatients_ReturnsUserVisibleErrorNot500()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new OwnersController(new OwnerService(context, currentClinic))
            {
                TempData = CreateTempData()
            };

            var result = controller.DeleteConfirmed(seed.OwnerA1Id);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(OwnerHasPatientsMessage, controller.TempData["ErrorMessage"]);

            using var raw = CreateRawContext();
            Assert.True(raw.Owners.IgnoreQueryFilters().Any(o => o.OwnerId == seed.OwnerA1Id));
        }

        // =========================================================
        // Update-FK: PatientService.UpdatePatient
        // =========================================================

        [Fact]
        public void UpdatePatient_CrossTenantOwner_ThrowsAndLeavesRowUnchanged()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new PatientService(context, currentClinic);

            var ex = Assert.Throws<InvalidOperationException>(() => service.UpdatePatient(new PatientInputModel
            {
                Id = seed.PatientA1Id,
                Name = "Опит за смяна",
                Species = "Куче",
                OwnerId = seed.OwnerBId
            }));

            Assert.Equal("Стопанинът не е намерен.", ex.Message);

            using var raw = CreateRawContext();
            var patient = raw.Patients.IgnoreQueryFilters().Single(p => p.PatientId == seed.PatientA1Id);
            Assert.Equal(seed.OwnerA1Id, patient.OwnerId);
            Assert.NotEqual("Опит за смяна", patient.Name);
        }

        [Fact]
        public void UpdatePatient_SameClinicOwner_Succeeds()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new PatientService(context, currentClinic);

            service.UpdatePatient(new PatientInputModel
            {
                Id = seed.PatientA1Id,
                Name = "Ново име",
                Species = "Куче",
                OwnerId = seed.OwnerA2Id
            });

            using var raw = CreateRawContext();
            var patient = raw.Patients.IgnoreQueryFilters().Single(p => p.PatientId == seed.PatientA1Id);
            Assert.Equal(seed.OwnerA2Id, patient.OwnerId);
            Assert.Equal("Ново име", patient.Name);
        }

        // =========================================================
        // Update-FK: AppointmentService.UpdateAppointment
        // =========================================================

        [Fact]
        public void UpdateAppointment_CrossTenantPatient_ThrowsAndLeavesRowUnchanged()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new AppointmentService(context, currentClinic);

            var ex = Assert.Throws<InvalidOperationException>(() => service.UpdateAppointment(new AppointmentInputModel
            {
                AppointmentId = seed.AppointmentAId,
                PatientId = seed.PatientBId,
                VeterinarianId = seed.VetA1Id,
                Reason = "Чужд пациент",
                DateTime = DateTime.Now
            }));

            Assert.Equal("Пациентът не е намерен.", ex.Message);

            using var raw = CreateRawContext();
            var appointment = raw.Appointments.IgnoreQueryFilters().Single(a => a.AppointmentId == seed.AppointmentAId);
            Assert.Equal(seed.PatientA1Id, appointment.PatientId);
            Assert.Equal(seed.VetA1Id, appointment.VetId);
        }

        [Fact]
        public void UpdateAppointment_CrossTenantVet_ThrowsAndLeavesRowUnchanged()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new AppointmentService(context, currentClinic);

            var ex = Assert.Throws<InvalidOperationException>(() => service.UpdateAppointment(new AppointmentInputModel
            {
                AppointmentId = seed.AppointmentAId,
                PatientId = seed.PatientA1Id,
                VeterinarianId = seed.VetBId,
                Reason = "Чужд лекар",
                DateTime = DateTime.Now
            }));

            Assert.Equal("Ветеринарният лекар не е намерен.", ex.Message);

            using var raw = CreateRawContext();
            var appointment = raw.Appointments.IgnoreQueryFilters().Single(a => a.AppointmentId == seed.AppointmentAId);
            Assert.Equal(seed.VetA1Id, appointment.VetId);
        }

        [Fact]
        public void UpdateAppointment_SameClinic_Succeeds()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new AppointmentService(context, currentClinic);

            service.UpdateAppointment(new AppointmentInputModel
            {
                AppointmentId = seed.AppointmentAId,
                PatientId = seed.PatientA1Id,
                VeterinarianId = seed.VetA2Id,
                Reason = "Местна смяна",
                DateTime = DateTime.Now
            });

            using var raw = CreateRawContext();
            var appointment = raw.Appointments.IgnoreQueryFilters().Single(a => a.AppointmentId == seed.AppointmentAId);
            Assert.Equal(seed.VetA2Id, appointment.VetId);
        }

        [Fact]
        public void UpdateAppointment_VetUser_ForcesOwnProfileIgnoringFormVetId()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId, seed.VetA1Id);
            using var context = _fixture.CreateContext(currentClinic);
            var service = new AppointmentService(context, currentClinic);

            service.UpdateAppointment(new AppointmentInputModel
            {
                AppointmentId = seed.AppointmentAId,
                PatientId = seed.PatientA1Id,
                VeterinarianId = seed.VetBId, // чужд/друг лекар от формата — игнорира се
                Reason = "Самоприписване",
                DateTime = DateTime.Now
            });

            using var raw = CreateRawContext();
            var appointment = raw.Appointments.IgnoreQueryFilters().Single(a => a.AppointmentId == seed.AppointmentAId);
            Assert.Equal(seed.VetA1Id, appointment.VetId);
        }

        // =========================================================
        // Update-FK: MedicalRecordsController.Edit (VetId + self-assignment)
        // =========================================================

        [Fact]
        public void MedicalRecordEdit_VetUser_ForcesOwnProfileIgnoringFormVetId()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId, seed.VetA1Id);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new MedicalRecordsController(context, currentClinic);

            var result = controller.Edit(new MedicalRecord
            {
                RecordId = seed.RecordAId,
                PatientId = seed.PatientA1Id,
                VetId = seed.VetBId, // опит за приписване на чужд лекар
                Diagnosis = "Нова диагноза",
                Treatment = "Ново лечение",
                Notes = "Бележка",
                RecordDate = DateTime.UtcNow
            });

            Assert.IsType<RedirectToActionResult>(result);

            using var raw = CreateRawContext();
            var record = raw.MedicalRecords.IgnoreQueryFilters().Single(r => r.RecordId == seed.RecordAId);
            Assert.Equal(seed.VetA1Id, record.VetId);
            Assert.Equal("Нова диагноза", record.Diagnosis);
        }

        [Fact]
        public void MedicalRecordEdit_NonVetStaff_ForeignVetId_ReturnsNotFoundAndKeepsRow()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new MedicalRecordsController(context, currentClinic);

            var result = controller.Edit(new MedicalRecord
            {
                RecordId = seed.RecordAId,
                PatientId = seed.PatientA1Id,
                VetId = seed.VetBId,
                Diagnosis = "Чужда диагноза",
                RecordDate = DateTime.UtcNow
            });

            Assert.IsType<NotFoundObjectResult>(result);

            using var raw = CreateRawContext();
            var record = raw.MedicalRecords.IgnoreQueryFilters().Single(r => r.RecordId == seed.RecordAId);
            Assert.Equal(seed.VetA1Id, record.VetId);
            Assert.NotEqual("Чужда диагноза", record.Diagnosis);
        }

        [Fact]
        public void MedicalRecordEdit_NonVetStaff_LocalVetId_Succeeds()
        {
            var seed = SeedUpdateScenario();
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new MedicalRecordsController(context, currentClinic);

            var result = controller.Edit(new MedicalRecord
            {
                RecordId = seed.RecordAId,
                PatientId = seed.PatientA1Id,
                VetId = seed.VetA2Id,
                Diagnosis = "Местна диагноза",
                RecordDate = DateTime.UtcNow
            });

            Assert.IsType<RedirectToActionResult>(result);

            using var raw = CreateRawContext();
            var record = raw.MedicalRecords.IgnoreQueryFilters().Single(r => r.RecordId == seed.RecordAId);
            Assert.Equal(seed.VetA2Id, record.VetId);
        }

        // =========================================================
        // Veterinarian delete guard (AspNetUsers.VeterinarianId няма DB FK)
        // =========================================================

        [Fact]
        public void DeleteVeterinarian_LinkedUser_IsBlockedAndVetRemains()
        {
            var seed = SeedVetScenario(linkedUser: true);
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new VeterinariansController(
                new VeterinarianService(context, currentClinic),
                context)
            {
                TempData = CreateTempData()
            };

            var result = controller.DeleteConfirmed(seed.LinkedVetId);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(VetLinkedMessage, controller.TempData["ErrorMessage"]);

            using var raw = CreateRawContext();
            Assert.True(raw.Veterinarians.IgnoreQueryFilters().Any(v => v.VetId == seed.LinkedVetId));
        }

        [Fact]
        public void DeleteVeterinarian_Unlinked_IsDeleted()
        {
            var seed = SeedVetScenario(linkedUser: false);
            var currentClinic = new TestClinicContext(seed.ClinicId);
            using var context = _fixture.CreateContext(currentClinic);
            var controller = new VeterinariansController(
                new VeterinarianService(context, currentClinic),
                context)
            {
                TempData = CreateTempData()
            };

            var result = controller.DeleteConfirmed(seed.LinkedVetId);

            Assert.IsType<RedirectToActionResult>(result);

            using var raw = CreateRawContext();
            Assert.False(raw.Veterinarians.IgnoreQueryFilters().Any(v => v.VetId == seed.LinkedVetId));
        }

        // =========================================================
        // Помощни методи
        // =========================================================

        private VetClinicContext CreateRawContext() =>
            _fixture.CreateContext(new TestClinicContext(null));

        private static TempDataDictionary CreateTempData() =>
            new(new DefaultHttpContext(), new NullTempDataProvider());

        private UpdateScenarioSeed SeedUpdateScenario()
        {
            var suffix = UniqueSuffix();

            using var raw = CreateRawContext();

            var clinicA = new Clinic { Name = $"Guard А {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            var clinicB = new Clinic { Name = $"Guard Б {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            raw.Clinics.AddRange(clinicA, clinicB);
            raw.SaveChanges();

            var ownerA1 = new Owner { FullName = $"Стопанин А1 {suffix}", ClinicId = clinicA.ClinicId };
            var ownerA2 = new Owner { FullName = $"Стопанин А2 {suffix}", ClinicId = clinicA.ClinicId };
            var ownerB = new Owner { FullName = $"Стопанин Б {suffix}", ClinicId = clinicB.ClinicId };
            raw.Owners.AddRange(ownerA1, ownerA2, ownerB);

            var vetA1 = new Veterinarian { FullName = $"Д-р А1 {suffix}", ClinicId = clinicA.ClinicId };
            var vetA2 = new Veterinarian { FullName = $"Д-р А2 {suffix}", ClinicId = clinicA.ClinicId };
            var vetB = new Veterinarian { FullName = $"Д-р Б {suffix}", ClinicId = clinicB.ClinicId };
            raw.Veterinarians.AddRange(vetA1, vetA2, vetB);
            raw.SaveChanges();

            var patientA1 = new Patient { Name = $"Пациент А1 {suffix}", Species = "Куче", OwnerId = ownerA1.OwnerId, ClinicId = clinicA.ClinicId };
            var patientB = new Patient { Name = $"Пациент Б {suffix}", Species = "Котка", OwnerId = ownerB.OwnerId, ClinicId = clinicB.ClinicId };
            raw.Patients.AddRange(patientA1, patientB);
            raw.SaveChanges();

            var appointmentA = new Appointment
            {
                PatientId = patientA1.PatientId,
                VetId = vetA1.VetId,
                ClinicId = clinicA.ClinicId,
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                AppointmentTime = new TimeOnly(9, 0),
                DurationMinutes = 30,
                Reason = "Първоначален преглед"
            };
            var recordA = new MedicalRecord
            {
                PatientId = patientA1.PatientId,
                VetId = vetA1.VetId,
                ClinicId = clinicA.ClinicId,
                Diagnosis = "Първоначална диагноза",
                RecordDate = DateTime.UtcNow
            };
            raw.Appointments.Add(appointmentA);
            raw.MedicalRecords.Add(recordA);
            raw.SaveChanges();

            return new UpdateScenarioSeed(
                clinicA.ClinicId,
                ownerA1.OwnerId,
                ownerA2.OwnerId,
                ownerB.OwnerId,
                patientA1.PatientId,
                patientB.PatientId,
                vetA1.VetId,
                vetA2.VetId,
                vetB.VetId,
                appointmentA.AppointmentId,
                recordA.RecordId);
        }

        private VetScenarioSeed SeedVetScenario(bool linkedUser)
        {
            var suffix = UniqueSuffix();

            using var raw = CreateRawContext();

            var clinic = new Clinic { Name = $"Vet guard {suffix}", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            raw.Clinics.Add(clinic);
            raw.SaveChanges();

            var vet = new Veterinarian { FullName = $"Д-р Guard {suffix}", ClinicId = clinic.ClinicId };
            raw.Veterinarians.Add(vet);
            raw.SaveChanges();

            if (linkedUser)
            {
                var email = $"vet-guard-{suffix}@test.bg";
                raw.Users.Add(new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(),
                    Email = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    DisplayName = $"Служебен {suffix}",
                    ClinicId = clinic.ClinicId,
                    VeterinarianId = vet.VetId,
                    IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString("N")
                });
                raw.SaveChanges();
            }

            return new VetScenarioSeed(clinic.ClinicId, vet.VetId);
        }

        private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

        private sealed record UpdateScenarioSeed(
            int ClinicId,
            int OwnerA1Id,
            int OwnerA2Id,
            int OwnerBId,
            int PatientA1Id,
            int PatientBId,
            int VetA1Id,
            int VetA2Id,
            int VetBId,
            int AppointmentAId,
            int RecordAId);

        private sealed record VetScenarioSeed(int ClinicId, int LinkedVetId);

        private sealed class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object?> LoadTempData(HttpContext context) =>
                new Dictionary<string, object?>();

            public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
            {
            }
        }
    }
}
