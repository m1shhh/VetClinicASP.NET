using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Veterinarian
{
    public int VetId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Specialization { get; set; }

    public string? WorkingHours { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual ICollection<Vaccination> Vaccinations { get; set; } = new List<Vaccination>();
}
