using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VetClinicASP.NET.Models;

namespace VetClinic.Security
{
    /// <summary>
    /// Claims factory (Task 7): след базовите Identity claims добавя "clinic_id" за клинични
    /// потребители — claim-ът е единственият източник на ICurrentClinic (Task 5, CurrentClinic.cs).
    /// Операторът (ClinicId = null) НЕ получава claim → deny-by-default остава в сила.
    /// (Task 9 ще добави vet_profile_id / Operator ролеви wiring върху същата схема.)
    /// </summary>
    public class ClinicUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
    {
        public ClinicUserClaimsPrincipalFactory(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, roleManager, optionsAccessor)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);

            if (user.ClinicId.HasValue)
            {
                identity.AddClaim(new Claim("clinic_id", user.ClinicId.Value.ToString()));
            }

            return identity;
        }
    }
}
