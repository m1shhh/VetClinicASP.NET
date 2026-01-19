using System;
using System.Collections.Generic;

namespace VetClinicASP.NET.Models;

public partial class Appointment
{
    public int AppointmentId { get; set; }

    public DateOnly AppointmentDate { get; set; }

    public TimeOnly AppointmentTime { get; set; }

    public string? Reason { get; set; }

    public int PatientId { get; set; }

    public int VetId { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual Veterinarian Vet { get; set; } = null!;
}
