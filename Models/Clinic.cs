using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Clinic
{
    public int ClinicId { get; set; }

    public string Name { get; set; } = null!;

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? LogoPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsActive { get; set; }

    public ClinicBillingStatus BillingStatus { get; set; }

    public DateTime? BillingPaidUntilUtc { get; set; }

    public string? BillingProblem { get; set; }

    public DateTime? TrialEndsAtUtc { get; set; }

    public DateTime? ConsentGivenAtUtc { get; set; }

    public string? ConsentTextVersion { get; set; }

    public virtual ICollection<Owner> Owners { get; set; } = new List<Owner>();

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual ICollection<Vaccination> Vaccinations { get; set; } = new List<Vaccination>();

    public virtual ICollection<Veterinarian> Veterinarians { get; set; } = new List<Veterinarian>();
}
