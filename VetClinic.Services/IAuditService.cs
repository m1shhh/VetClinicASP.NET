namespace VetClinic.Services
{
    /// <summary>
    /// Одитен запис (Task 10, design doc §4/§7). Пише САМО кодове и Id-та — интерфейсът
    /// умишлено НЕ приема свободен текст/име/имейл: PII и IP адреси са забранени.
    /// </summary>
    public interface IAuditService
    {
        /// <param name="action">Кратък код — напр. "login.failed", "patient.created" (≤ 50 знака).</param>
        /// <param name="entity">Тип на засегнатия обект — напр. "Patient", "Clinic".</param>
        /// <param name="entityId">Id на засегнатия обект (когато е приложимо).</param>
        void Log(string action, string? entity = null, int? entityId = null);
    }
}
