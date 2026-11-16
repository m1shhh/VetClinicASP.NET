namespace VetClinic.Security
{
    /// <summary>Ролите в системата (ASP.NET Identity). Операторът е единствената роля без ClinicId.</summary>
    public static class AppRoles
    {
        public const string Operator = "Operator";

        public const string ClinicAdmin = "ClinicAdmin";

        public const string Vet = "Vet";

        public const string Receptionist = "Receptionist";
    }
}
