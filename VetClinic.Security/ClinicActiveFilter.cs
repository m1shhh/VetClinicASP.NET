using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VetClinicASP.NET.Models;

namespace VetClinic.Security
{
    /// <summary>
    /// ClinicActiveFilter (Task 9, design doc §8): деактивирана клиника = НЕЗАБАВЕН ефект —
    /// жива сесия се прекъсва при ПЪРВАТА заявка след деактивацията (кеш ≤ 60 s), не при
    /// следващия logout. Глобален IAsyncAuthorizationFilter.
    ///
    /// Skip правила (заявката преминава):
    /// (а) не-аутентикиран потребител — login страницата трябва да рендери;
    /// (б) роля Operator — операторът няма клиничен контекст изобщо;
    /// (в) пътят започва с /Account — login/register/locked работят винаги (блокираният
    ///     потребител трябва да стигне до екрана "клиниката е деактивирана");
    /// (г) без clinic_id claim — deny-by-default остава на query filter-ите (нуля реда);
    ///     redirect-ът тук би бил misleading, затова пропускаме без действие.
    ///
    /// Иначе: Clinic.IsActive от IMemoryCache (60 s TTL, ключ по clinic id; на miss →
    /// свеж scope → DB) → неактивна: SignOutAsync + redirect /Account/Locked (short-circuit
    /// чрез context.Result).
    ///
    /// DI бележка: филтърът е регистриран глобально и се инстанцира от MVC за заявка —
    /// затова НЕ държи scoped DbContext, а IServiceScopeFactory (singleton) и прави
    /// свеж scope за всеки DB read. IMemoryCache е singleton — споделен между заявките.
    /// </summary>
    public class ClinicActiveFilter : IAsyncAuthorizationFilter
    {
        private const string CacheKeyPrefix = "clinic-active:";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopeFactory;

        public ClinicActiveFilter(IMemoryCache cache, IServiceScopeFactory scopeFactory)
        {
            _cache = cache;
            _scopeFactory = scopeFactory;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                return; // (а)
            }

            if (user.IsInRole(AppRoles.Operator))
            {
                return; // (б)
            }

            if (context.HttpContext.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
            {
                return; // (в)
            }

            if (!int.TryParse(user.FindFirstValue("clinic_id"), out var clinicId))
            {
                return; // (г) — deny-by-default остава в сила чрез query filter-ите
            }

            if (await IsClinicActiveAsync(clinicId, context.HttpContext.RequestAborted))
            {
                return;
            }

            // Прекъсване на живата сесия + short-circuit на pipeline-а.
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Result = new RedirectResult("/Account/Locked");
        }

        /// <summary>Clinic.IsActive с 60 s кеш; на miss — свеж scope/context (филтърът не държи DbContext).</summary>
        private async Task<bool> IsClinicActiveAsync(int clinicId, CancellationToken cancellationToken)
        {
            var key = CacheKeyPrefix + clinicId;

            if (_cache.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VetClinicContext>();

            var clinic = await db.Clinics
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClinicId == clinicId, cancellationToken);

            // Clinic НЕ е query-филтриран; липсващ ред (несъществуващ clinic_id) → deny (false).
            var active = clinic?.IsActive ?? false;

            _cache.Set(key, active, CacheTtl);
            return active;
        }
    }
}
