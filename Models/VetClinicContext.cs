using Microsoft.EntityFrameworkCore;
using VetClinic.Security;

namespace VetClinicASP.NET.Models;

public partial class VetClinicContext : DbContext
{
    private readonly int? _currentClinicId;

    // Задължителен по време на работа (fail-fast в OnModelCreatingPartial — VetClinicContext.Tenancy.cs):
    // без конверторите моделът на този тип context би бил кеширан БЕЗ тях и би се отровил за всички.
    // Nullable е само за да е валиден параметричният design-time ctor — той НЕ се ползва по време на работа.
    private readonly EncryptionService? _encryptionService;

    public VetClinicContext()
    {
    }

    public VetClinicContext(DbContextOptions<VetClinicContext> options)
        : base(options)
    {
    }

    // Контекст с клиничен контекст: query filter-ите (VetClinicContext.Tenancy.cs) заключват
    // всички tenant заявки към тази клиника. null (design-time / оператор) → deny-by-default.
    // ВАЖНО: никога НЕ ползвай този контекст с DbContext pooling (AddDbContextPool) —
    // прихванатият в ctor-а _currentClinicId би замръзнал между заявките.
    public VetClinicContext(
        DbContextOptions<VetClinicContext> options,
        ICurrentClinic currentClinic,
        EncryptionService encryption)
        : base(options)
    {
        _currentClinicId = currentClinic.ClinicId;
        _encryptionService = encryption;
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
            // Reason е криптирана колона (nvarchar(max) + конвертор — VetClinicContext.Tenancy.cs);
            // дължината се валидира на ниво приложение, не в схемата.
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
            // Diagnosis/Treatment/Notes са криптирани колони (nvarchar(max) + конвертор —
            // VetClinicContext.Tenancy.cs); дължината се валидира на ниво приложение.
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
            // FullName/PhoneNumber/Email/Address са криптирани колони (nvarchar(max) +
            // конвертор — VetClinicContext.Tenancy.cs); дължините се валидират на ниво приложение.
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
            // PhoneNumber/Email са криптирани колони (nvarchar(max) + конвертор —
            // VetClinicContext.Tenancy.cs); FullName ОСТАВА cleartext (търсене/показване в SQL).
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Specialization).HasMaxLength(100);
            entity.Property(e => e.WorkingHours).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
