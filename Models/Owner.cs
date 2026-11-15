using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Owner
{
    public int OwnerId { get; set; }

    public string FullName { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public int ClinicId { get; set; }

    public virtual Clinic Clinic { get; set; } = null!;

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();
}
