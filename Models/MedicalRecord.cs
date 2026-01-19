using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class MedicalRecord
{
    public int RecordId { get; set; }

    public string? Diagnosis { get; set; }

    public string? Treatment { get; set; }

    public string? Notes { get; set; }

    public DateTime? RecordDate { get; set; }

    public int PatientId { get; set; }

    public int? VetId { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual Veterinarian? Vet { get; set; }
}
