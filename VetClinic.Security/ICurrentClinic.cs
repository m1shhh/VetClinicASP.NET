namespace VetClinic.Security
{
    /// <summary>
    /// Единственият източник на клиничен контекст по време на заявка (от claims).
    /// Service-ите НИКАК не приемат ClinicId като параметър — винаги през този интерфейс.
    /// </summary>
    public interface ICurrentClinic
    {
        /// <summary>ClinicId от claim "clinic_id", или null (оператор / не-логнат / тест без контекст).</summary>
        int? ClinicId { get; }

        /// <summary>ClinicId или InvalidOperationException — за пътища, които задължително пишат tenant данни.</summary>
        int GetRequiredClinicId();

        /// <summary>Veterinarian profile-ът на текущия потребител (claim "vet_profile_id"), или null.</summary>
        int? VetProfileId { get; }

        /// <summary>true само за роля Operator — операторът няма tenant контекст.</summary>
        bool IsOperator { get; }
    }
}
