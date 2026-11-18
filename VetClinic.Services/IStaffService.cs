using Microsoft.AspNetCore.Identity;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    /// <summary>
    /// Управление на персонала (Task 8) — ролевият guard е на контролера ([Authorize(Roles =
    /// ClinicAdmin)]). ВАЖНО: Identity потребителите НЯМАТ query filter → изолацията по
    /// клиника тук е ИЗРИЧНА (user.ClinicId == текуща), не deny-by-default.
    /// </summary>
    public interface IStaffService
    {
        IdentityResult CreateStaff(string email, string displayName, string password, string role);

        /// <summary>
        /// Активира/деактивира акаунт САМО в текущата клиника.
        /// </summary>
        /// <returns>true само когато промяната е реално записана; false при липсващ
        /// клиничен контекст, липсващ/чужд user или провален UpdateAsync (одитът в
        /// контролера зависи от този резултат — не бива фалшив ред).</returns>
        bool SetStaffActive(string userId, bool active);

        List<ApplicationUser> ListStaff();
    }
}
