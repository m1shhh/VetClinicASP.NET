using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace VetClinicASP.NET.Models;

public partial class VetClinicContext
{
    public virtual DbSet<Clinic> Clinics { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
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
    }
}
