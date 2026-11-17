using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VetClinicASP.NET.Models;

namespace VetClinic.Security
{
    /// <summary>
    /// Claims factory (Task 7/8): след базовите Identity claims добавя "clinic_id" за клинични
    /// потребители — claim-ът е единственият източник на ICurrentClinic (Task 5, CurrentClinic.cs).
    /// За ветеринарен лекар (VeterinarianId) добавя и "vet_profile_id" (Task 8) — източникът на
    /// ICurrentClinic.VetProfileId за self-приписването на Create-пътищата. Операторът
    /// (ClinicId = null) НЕ получава clinic_id → deny-by-default остава в сила.
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

            // Task 8: линкът към Veterinarian профила става claim — CurrentClinic.VetProfileId
            // го чете и Create-пътищата self-приписват записите на собствения профил на лекаря.
            if (user.VeterinarianId.HasValue)
            {
                identity.AddClaim(new Claim("vet_profile_id", user.VeterinarianId.Value.ToString()));
            }

            return identity;
        }
    }
}
