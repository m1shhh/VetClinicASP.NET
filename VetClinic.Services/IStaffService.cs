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

        void SetStaffActive(string userId, bool active);

        List<ApplicationUser> ListStaff();
    }
}
