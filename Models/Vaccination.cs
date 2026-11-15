using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Vaccination
{
    public int VaccinationId { get; set; }

    public string VaccineName { get; set; } = null!;

    public DateOnly DateAdministered { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public int PatientId { get; set; }

    public int? VetId { get; set; }

    public int ClinicId { get; set; }

    public virtual Clinic Clinic { get; set; } = null!;

    public virtual Patient Patient { get; set; } = null!;

    public virtual Veterinarian? Vet { get; set; }
}
