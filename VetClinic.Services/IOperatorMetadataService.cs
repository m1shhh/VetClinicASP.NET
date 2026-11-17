namespace VetClinic.Services
{
    /// <summary>
    /// Един ред от операторския списък (Task 9). Формата на записа Е PII контрактът:
    /// име на клиниката + билингов статус + агрегатни брояци — НИКАКВИ имена на
    /// пациенти/стопани/лекари, причини, диагнози или друго tenant съдържание
    /// (design doc §3.3: операторът = МЕТАДАННИ САМО).
    /// </summary>
    public record OperatorClinicRow(int ClinicId, string Name, string BillingStatus, DateTime CreatedAtUtc, bool IsActive, int Patients, int Appointments);

    /// <summary>
    /// Метаданен сервис за ролята Operator (Task 9, design doc §8). КОМПИЛНО-НИВОВА гаранция:
    /// интерфейсът НЕ предлага никакъв API за tenant съдържание (пациенти/стопани/записи) —
    /// всичко, което операторът може да получи, е List&lt;OperatorClinicRow&gt;, void или int.
    /// </summary>
    public interface IOperatorMetadataService
    {
        /// <summary>Списък на ВСИЧКИ клиники (IgnoreQueryFilters) с агрегатни брояци — без съдържание.</summary>
        List<OperatorClinicRow> GetClinics();

        /// <summary>Незабавно превключване на Clinic.IsActive (деактивираната клиника губи достъп ≤ 60 s).</summary>
        void SetActive(int clinicId, bool active);

        /// <summary>Provisioning без преглед на данни: Clinic (BillingStatus=Active) + ClinicAdmin акаунт, атомарно.</summary>
        int CreatePilotClinic(string name, string adminEmail, string adminDisplayName, string adminPassword);
    }
}
