using Microsoft.EntityFrameworkCore;

namespace VetClinicASP.NET.Models;

public partial class VetClinicContext : DbContext
{
    public VetClinicContext()
    {
    }

    public VetClinicContext(DbContextOptions<VetClinicContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Appointment> Appointments { get; set; }

    public virtual DbSet<MedicalRecord> MedicalRecords { get; set; }

    public virtual DbSet<Owner> Owners { get; set; }

    public virtual DbSet<Patient> Patients { get; set; }

    public virtual DbSet<Vaccination> Vaccinations { get; set; }

    public virtual DbSet<Veterinarian> Veterinarians { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.AppointmentId).HasName("PK__Appointm__8ECDFCA2BEAFEBE4");

            entity.Property(e => e.AppointmentId).HasColumnName("AppointmentID");
            entity.Property(e => e.PatientId).HasColumnName("PatientID");
            entity.Property(e => e.Reason).HasMaxLength(255);
            entity.Property(e => e.VetId).HasColumnName("VetID");

            entity.HasOne(d => d.Patient).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Appointme__Patie__5165187F");

            entity.HasOne(d => d.Vet).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.VetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Appointme__VetID__52593CB8");
        });

        modelBuilder.Entity<MedicalRecord>(entity =>
        {
            entity.HasKey(e => e.RecordId).HasName("PK__MedicalR__FBDF78C98993C7E5");

            entity.Property(e => e.RecordId).HasColumnName("RecordID");
            entity.Property(e => e.Diagnosis).HasMaxLength(255);
            entity.Property(e => e.PatientId).HasColumnName("PatientID");
            entity.Property(e => e.RecordDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.VetId).HasColumnName("VetID");

            entity.HasOne(d => d.Patient).WithMany(p => p.MedicalRecords)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__MedicalRe__Patie__5629CD9C");

            entity.HasOne(d => d.Vet).WithMany(p => p.MedicalRecords)
                .HasForeignKey(d => d.VetId)
                .HasConstraintName("FK__MedicalRe__VetID__571DF1D5");
        });

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.HasKey(e => e.OwnerId).HasName("PK__Owners__819385988929FEA8");

            entity.Property(e => e.OwnerId).HasColumnName("OwnerID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.PatientId).HasName("PK__Patients__970EC34605B5F257");

            entity.Property(e => e.PatientId).HasColumnName("PatientID");
            entity.Property(e => e.Breed).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.OwnerId).HasColumnName("OwnerID");
            entity.Property(e => e.Species).HasMaxLength(50);

            entity.HasOne(d => d.Owner).WithMany(p => p.Patients)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Patients__OwnerI__4E88ABD4");
        });

        modelBuilder.Entity<Vaccination>(entity =>
        {
            entity.HasKey(e => e.VaccinationId).HasName("PK__Vaccinat__466BCFA7FAF29C06");

            entity.Property(e => e.VaccinationId).HasColumnName("VaccinationID");
            entity.Property(e => e.PatientId).HasColumnName("PatientID");
            entity.Property(e => e.VaccineName).HasMaxLength(100);
            entity.Property(e => e.VetId).HasColumnName("VetID");

            entity.HasOne(d => d.Patient).WithMany(p => p.Vaccinations)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Vaccinati__Patie__59FA5E80");

            entity.HasOne(d => d.Vet).WithMany(p => p.Vaccinations)
                .HasForeignKey(d => d.VetId)
                .HasConstraintName("FK__Vaccinati__VetID__5AEE82B9");
        });

        modelBuilder.Entity<Veterinarian>(entity =>
        {
            entity.HasKey(e => e.VetId).HasName("PK__Veterina__2556B80EC135EAEB");

            entity.Property(e => e.VetId).HasColumnName("VetID");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.Specialization).HasMaxLength(100);
            entity.Property(e => e.WorkingHours).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
