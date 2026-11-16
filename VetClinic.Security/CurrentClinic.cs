using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace VetClinic.Security
{
    /// <summary>ICurrentClinic от claims на текущия HTTP заявка (scoped).</summary>
    public class CurrentClinic : ICurrentClinic
    {
        private readonly IHttpContextAccessor _accessor;

        public CurrentClinic(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private ClaimsPrincipal? User => _accessor.HttpContext?.User;

        public int? ClinicId
        {
            get
            {
                var raw = User?.FindFirstValue("clinic_id");
                return int.TryParse(raw, out var clinicId) ? clinicId : null;
            }
        }

        public int? VetProfileId
        {
            get
            {
                var raw = User?.FindFirstValue("vet_profile_id");
                return int.TryParse(raw, out var vetProfileId) ? vetProfileId : null;
            }
        }

        public bool IsOperator => User?.IsInRole(AppRoles.Operator) ?? false;

        public int GetRequiredClinicId() =>
            ClinicId ?? throw new InvalidOperationException("Няма клиничен контекст.");
    }
}
