using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Patient
{
    public int PatientId { get; set; }

    public string Name { get; set; } = null!;

    public string Species { get; set; } = null!;

    public string? Breed { get; set; }

    public int? Age { get; set; }

    public int OwnerId { get; set; }

    public int ClinicId { get; set; }

    public virtual Clinic Clinic { get; set; } = null!;

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual Owner Owner { get; set; } = null!;

    public virtual ICollection<Vaccination> Vaccinations { get; set; } = new List<Vaccination>();
}
