using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    /// <summary>
    /// Персонал (Task 8). UserManager-ът и VetClinicContext-ът са СЪЩИЯТ scoped instance
    /// (DI) — затова EF транзакцията тук покрива потребителя, Veterinarian реда и ролята
    /// ЕДНОВРЕМЕННО (същият механизъм като AccountController.Register, където UserManager
    /// дели контролеровия scoped context).
    /// Identity таблиците НЯМАТ query filter → всеки метод тук филтрира ИЗРИЧНО по
    /// user.ClinicId == текуща клиника (deny-by-default не е автоматичен за тях).
    /// </summary>
    public class StaffService : IStaffService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public StaffService(
            UserManager<ApplicationUser> userManager,
            VetClinicContext context,
            ICurrentClinic currentClinic)
        {
            _userManager = userManager;
            _context = context;
            _currentClinic = currentClinic;
        }

        public IdentityResult CreateStaff(string email, string displayName, string password, string role)
        {
            // Само трите клинични роли — Operator се създава САМО от bootstrap seed (Task 9).
            if (role != AppRoles.ClinicAdmin && role != AppRoles.Vet && role != AppRoles.Receptionist)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidRole",
                    Description = "Невалидна роля."
                });
            }

            var clinicId = _currentClinic.GetRequiredClinicId();

            // Атомарност (план, Task 8): user + (Veterinarian) + роля — ЕДНА транзакция върху
            // споделения context. UserManager операциите минават през същия context (EF store),
            // затова rollback-ът им е действителен.
            using var transaction = _context.Database.BeginTransaction();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                ClinicId = clinicId,
                IsActive = true
            };

            var createResult = _userManager.CreateAsync(user, password).GetAwaiter().GetResult();
            if (!createResult.Succeeded)
            {
                transaction.Rollback();
                _context.ChangeTracker.Clear();
                return createResult; // без частично състояние
            }

            if (role == AppRoles.Vet)
            {
                // Линкът Veterinarian (1:1): профилът е tenant данна с ClinicId = текущата клиника;
                // останалите колони остават default/nullable.
                var vet = new Veterinarian
                {
                    FullName = displayName,
                    ClinicId = clinicId
                };
                _context.Veterinarians.Add(vet);
                _context.SaveChanges(); // flush → identity VetId

                user.VeterinarianId = vet.VetId;
                var updateResult = _userManager.UpdateAsync(user).GetAwaiter().GetResult();
                if (!updateResult.Succeeded)
                {
                    transaction.Rollback();
                    _context.ChangeTracker.Clear();
                    return updateResult;
                }
            }

            // Резултатът от AddToRoleAsync НЕ се пренебрегва (урок от Task 7 ревю):
            // провал → rollback + Failed, без user само с частични роли.
            var roleResult = _userManager.AddToRoleAsync(user, role).GetAwaiter().GetResult();
            if (!roleResult.Succeeded)
            {
                transaction.Rollback();
                _context.ChangeTracker.Clear();
                return roleResult;
            }

            transaction.Commit();
            return IdentityResult.Success;
        }

        public void SetStaffActive(string userId, bool active)
        {
            var clinicId = _currentClinic.GetRequiredClinicId();

            // ИЗРИЧЕН филтър по клиника: userId от ЧУЖДА клиника → "не е намерен" → тихо
            // не-действие (без throw, без промяна; не разкрива съществуването на чужд акаунт).
            // Деактивирането пипа САМО акаунта — Veterinarian профилът и историческите
            // редове (appointments/records) остават непокътнати.
            var user = _context.Users
                .FirstOrDefault(u => u.Id == userId && u.ClinicId == clinicId);

            if (user == null)
            {
                return;
            }

            user.IsActive = active;
            _userManager.UpdateAsync(user).GetAwaiter().GetResult();
        }

        public List<ApplicationUser> ListStaff()
        {
            // Deny-by-default: без клиничен контекст → празен списък (както четящите
            // tenant заявки връщат нула реда, а не изключение).
            if (_currentClinic.ClinicId is not int clinicId)
            {
                return new List<ApplicationUser>();
            }

            // ApplicationUser НЯМА query filter → ИЗРИЧНО Where по ClinicId е ЗАДЪЛЖИТЕЛНО.
            return _context.Users
                .Where(u => u.ClinicId == clinicId)
                .OrderBy(u => u.DisplayName)
                .ToList();
        }
    }
}
