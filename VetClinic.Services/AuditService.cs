using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VetClinic.Security;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    /// <summary>
    /// Одитен запис (Task 10, design doc §4/§7): САМО кодове и Id-та — НИКАКВО PII
    /// (имена/имейли/пароли) и НИКАКВИ IP адреси.
    ///
    /// - ClinicId идва единствено от ICurrentClinic (null за операторски/без-контекст
    ///   действия; контролерите НИКОГА не подават ClinicId);
    /// - UserId е NameIdentifier claim-ът на HttpContext.User (null при анонимни действия —
    ///   напр. по време на login опит principal-ът още не е аутентикиран; имейлът на
    ///   опитващия се НИКОГА не се записва);
    /// - SaveChanges веднага: одитът е синхронен с бизнес действието. Извикващите го правят
    ///   СЛЕД успешния запис на операцията (и след commit на транзакцията, ако има такава).
    ///
    /// РЕШЕНИЕ за валидация на action (по задание):
    /// - null/празен/whitespace → ArgumentException (fail-fast срещу небрежност — кодът е
    ///   програмистки инвариант, не потребителски вход);
    /// - > 50 знака → ТРУНКВАНЕ до 50 (колоната е nvarchar(50)). Одитът НИКОГА не бива да
    ///   проваля успешна бизнес операция заради собствените си данни — затова тук няма
    ///   изключение за дължина.
    /// </summary>
    public class AuditService : IAuditService
    {
        private const int MaxActionLength = 50;

        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(
            VetClinicContext context,
            ICurrentClinic currentClinic,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _currentClinic = currentClinic;
            _httpContextAccessor = httpContextAccessor;
        }

        public void Log(string action, string? entity = null, int? entityId = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Одитният код не може да бъде празен.", nameof(action));
            }

            var storedAction = action.Length > MaxActionLength
                ? action[..MaxActionLength]
                : action;

            _context.AuditLogs.Add(new AuditLog
            {
                // null за операторски действия (операторът няма clinic_id claim).
                ClinicId = _currentClinic.ClinicId,
                UserId = _httpContextAccessor.HttpContext?.User
                    ?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Action = storedAction,
                Entity = entity,
                EntityId = entityId,
                AtUtc = DateTime.UtcNow
            });

            // Синхронно с действието — без отлагане към края на заявката.
            _context.SaveChanges();
        }
    }
}
