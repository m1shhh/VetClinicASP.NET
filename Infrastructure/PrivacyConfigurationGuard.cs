using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VetClinicASP.NET.Infrastructure
{
    /// <summary>
    /// Task 13 (privacy TODO-guard): публичната регистрация е зад toggle
    /// (<c>Features:PublicRegistration</c>), но privacy страницата няма публичен контакт,
    /// докато <c>Contact:Email</c> не бъде конфигуриран (решение на собственика — по-късно).
    /// При включена регистрация без имейл стартът НЕ се блокира: логва се WARNING, за да не
    /// остане изискването незабелязано. Изнесено в статичен helper, за да е тестваемо без host.
    /// </summary>
    public static class PrivacyConfigurationGuard
    {
        /// <summary>
        /// Проверява конфигурацията и логва WARNING при невалидна комбинация.
        /// Връща <c>true</c>, ако предупреждение е било записано (иначе <c>false</c>).
        /// </summary>
        public static bool Check(IConfiguration configuration, ILogger logger)
        {
            var publicRegistrationEnabled = string.Equals(
                configuration["Features:PublicRegistration"],
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (!publicRegistrationEnabled)
            {
                return false;
            }

            var contactEmail = configuration["Contact:Email"];
            if (!string.IsNullOrWhiteSpace(contactEmail))
            {
                return false;
            }

            logger.LogWarning(
                "Features:PublicRegistration е включена, но Contact:Email липсва — " +
                "privacy страницата не може да посочи контакт.");

            return true;
        }
    }
}
