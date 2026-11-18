using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    ///
    /// РЕШЕНИЕ за дължина на Entity (Task 11, разширение от Task 10 ревю): Entity също е
    /// nvarchar(50) и се ТРУНКВА до 50 — същата причина (одитът не смутява бизнес операцията).
    ///
    /// РЕШЕНИЕ за DB грешка (Task 11, разширение от Task 10 ревю): FAIL-OPEN за бизнес
    /// операциите. Ако одитният ред не може да се запази (DB недостъпна, constraint и т.н.),
    /// грешката се логва (БЕЗ PII — само одитния код) и бизнес операцията продължава.
    /// Destructive операциите са изключението: ErasureService пише `clinic.deleted` В
    /// ТРАНЗАКЦИЯТА на изтриването и е FAIL-CLOSED — одитът там е част от атомарността.
    /// </summary>
    public class AuditService : IAuditService
    {
        private const int MaxActionLength = 50;
        private const int MaxEntityLength = 50;

        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            VetClinicContext context,
            ICurrentClinic currentClinic,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditService>? logger = null)
        {
            _context = context;
            _currentClinic = currentClinic;
            _httpContextAccessor = httpContextAccessor;
            // Optional: директните тестови конструкции не са длъжни да подават logger;
            // DI-контейнерът винаги доставя ILogger<AuditService>.
            _logger = logger ?? NullLogger<AuditService>.Instance;
        }

        public void Log(string action, string? entity = null, int? entityId = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Одитният код не може да бъде празен.", nameof(action));
            }

            var storedAction = Truncate(action, MaxActionLength);
            var storedEntity = entity == null ? null : Truncate(entity, MaxEntityLength);

            var record = new AuditLog
            {
                // null за операторски действия (операторът няма clinic_id claim).
                ClinicId = _currentClinic.ClinicId,
                UserId = _httpContextAccessor.HttpContext?.User
                    ?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Action = storedAction,
                Entity = storedEntity,
                EntityId = entityId,
                AtUtc = DateTime.UtcNow
            };

            try
            {
                _context.AuditLogs.Add(record);

                // Синхронно с действието — без отлагане към края на заявката.
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                // FAIL-OPEN: не оставяме неуспешния ред в change tracker-а — иначе следващият
                // бизнес SaveChanges би се опитал да го запише отново и би провалил операцията.
                try
                {
                    _context.Entry(record).State = EntityState.Detached;
                }
                catch
                {
                    // Контекстът може да е в неизползваемо състояние (напр. disposed) —
                    // detach-ът е best-effort и не бива да отменя fail-open политиката.
                }

                _logger.LogError(
                    ex,
                    "Одитният запис не беше запазен (action: {Action}); бизнес операцията продължава.",
                    storedAction);
            }
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length > maxLength ? value[..maxLength] : value;
    }
}
