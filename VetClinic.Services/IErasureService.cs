namespace VetClinic.Services
{
    /// <summary>
    /// GDPR erasure (Task 11, design doc §7): пълно (hard) изтриване на клиника и на ВСИЧКИ
    /// нейни tenant данни. Публичният договор е синхронен по плана; вътрешните операции
    /// (EF ExecuteDelete/SaveChanges) са синхронни, така че няма sync-over-async.
    /// </summary>
    public interface IErasureService
    {
        /// <summary>
        /// Изтрива клиниката и всички нейни данни в ЕДНА транзакция: ваксинации →
        /// медицински записи → записвания → пациенти → стопани → ветеринарни профили →
        /// потребители + Identity join редове → Clinic ред. Записва одит `clinic.deleted`
        /// В ТРАНЗАКЦИЯТА (fail-closed). Одитните редове НЕ се трият (няма FK).
        /// Idempotent: липсваща/вече изтрита клиника → no-op без изключение и без нов одит.
        /// </summary>
        void EraseClinic(int clinicId);
    }
}
