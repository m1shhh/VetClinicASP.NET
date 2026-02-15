using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VetClinicASP.NET.Models;

public partial class Veterinarian
{
    public int VetId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Specialization { get; set; }
    [Display(Name = "Работно време")]
    [StringLength(50, ErrorMessage = "Полето не може да бъде по-дълго от 50 символа.")]
    public string? WorkingHours { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual ICollection<Vaccination> Vaccinations { get; set; } = new List<Vaccination>();


}
