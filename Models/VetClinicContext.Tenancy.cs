using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using VetClinic.Security;

namespace VetClinicASP.NET.Models;

public partial class VetClinicContext
{
    public virtual DbSet<Clinic> Clinics { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // ЗАДЪЛЖИТЕЛЕН EncryptionService (fail-fast). Причина: EF кешира ЕДИН модел за тип
        // context (по подразбиране, без IModelCacheKeyFactory) — ако context-и от същия тип
        // биват строени СЪС и БЕЗ конвертори, първо кешираният модел "отровно" важи за всички
        // (конверторите идват от конкретната инстанция в ctor-а). Униформени конвертори + този
        // throw избягват сложността на IModelCacheKeyFactory. Параметричният ctor
        // (options, ICurrentClinic, EncryptionService) е единственият runtime път.
        if (_encryptionService is null)
            throw new InvalidOperationException(
                "EncryptionService е задължителен за VetClinicContext.");

        // ЕДИН конвертор-инстанция за всичките 10 криптирани колони (design doc §3.2/§3.3):
        // write — plaintext → "enc:v1:..." (idempotent: вече-криптирано остава as-is);
        // read — "enc:v1:..." → plaintext (legacy plaintext passthrough).
        var encConverter = EncryptedStringConverter.Create(_encryptionService);

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.Property(e => e.FullName).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.PhoneNumber).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Email).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Address).HasConversion(encConverter).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.Property(e => e.Reason).HasConversion(encConverter).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<MedicalRecord>(entity =>
        {
            entity.Property(e => e.Diagnosis).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Treatment).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Notes).HasConversion(encConverter).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<Veterinarian>(entity =>
        {
            entity.Property(e => e.PhoneNumber).HasConversion(encConverter).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Email).HasConversion(encConverter).HasColumnType("nvarchar(max)");
        });

        // НЕ криптираме (search/sort/display се нуждаят от cleartext в SQL — design doc §4):
        // Patient.Name, Patient.Breed, Vaccination.VaccineName, Veterinarian.FullName.

        // Deny-by-default изолация по клиники (design doc §3.1):
        // заявка без клиничен контекст → ClinicID = NULL → нула реда (SQL UNKNOWN), не изключение.
        // Clinic НЕ е филтриран — той е tenant коренът, видими метаданни за всички сесии.
        modelBuilder.Entity<Owner>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<Patient>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<Appointment>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<MedicalRecord>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<Vaccination>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<Veterinarian>().HasQueryFilter(e => e.ClinicId == _currentClinicId);

        modelBuilder.Entity<Clinic>(entity =>
        {
            entity.HasKey(e => e.ClinicId).HasName("PK_Clinics");

            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.LogoPath).HasMaxLength(500);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.BillingStatus).HasColumnName("BillingStatus");
            entity.Property(e => e.BillingPaidUntilUtc).HasColumnType("datetime2");
            entity.Property(e => e.BillingProblem).HasMaxLength(200);
            entity.Property(e => e.TrialEndsAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.ConsentGivenAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.ConsentTextVersion).HasMaxLength(20);
        });

        // ApplicationUser (Task 7) НЕ е tenant entity — БЕЗ query filter (users не са клинични данни;
        // Operator има ClinicId=null). FK към Clinic с Restrict: изтриване на клиниката минава през
        // ErasureService (Task 11), който изтрива потребителите изрично преди Clinic реда.
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicId");
            entity.Property(e => e.DisplayName).HasMaxLength(100);

            entity.HasOne(d => d.Clinic)
                .WithMany()
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_AspNetUsers_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_AspNetUsers_ClinicId");
        });

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.Owners)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Owners_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_Owners_ClinicID");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.Patients)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Patients_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_Patients_ClinicID");
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Appointments_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_Appointments_ClinicID");
        });

        modelBuilder.Entity<MedicalRecord>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.MedicalRecords)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MedicalRecords_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_MedicalRecords_ClinicID");
        });

        modelBuilder.Entity<Vaccination>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.Vaccinations)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Vaccinations_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_Vaccinations_ClinicID");
        });

        modelBuilder.Entity<Veterinarian>(entity =>
        {
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");

            entity.HasOne(d => d.Clinic).WithMany(p => p.Veterinarians)
                .HasForeignKey(d => d.ClinicId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Veterinarians_Clinic");

            entity.HasIndex(e => e.ClinicId, "IX_Veterinarians_ClinicID");
        });

        // AuditLog (Task 10): НЕ е tenant entity — БЕЗ query filter (ClinicId може да е null
        // за операторски действия; IAuditService е единственият записвач). Без FK-та по
        // замисъл: UserId НЯМА връзка към AspNetUsers.Id, ClinicId — към Clinics.ClinicID —
        // одитът оцелява при изтриване на потребител/клиника (GDPR erasure, Task 11).
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PK_AuditLogs");

            // bigint IDENTITY по конвенция (long PK); колоната пази името от заданието.
            entity.Property(e => e.AuditId).HasColumnName("AuditID");
            entity.Property(e => e.ClinicId).HasColumnName("ClinicID");
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Entity).HasMaxLength(50);
            // Същият тип като AspNetUsers.Id (nvarchar(450)) — само съвместимост, без FK.
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(450);
            entity.Property(e => e.AtUtc).HasColumnType("datetime2");

            // Заявките са по време — глобално и по клиника (Phase 2 операторски изглед).
            entity.HasIndex(e => e.AtUtc, "IX_AuditLogs_AtUtc");
            entity.HasIndex(e => new { e.ClinicId, e.AtUtc }, "IX_AuditLogs_ClinicID_AtUtc");
        });
    }
}
