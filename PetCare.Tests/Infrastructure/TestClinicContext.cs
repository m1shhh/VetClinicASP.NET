using VetClinic.Security;

namespace PetCare.Tests.Infrastructure
{
    /// <summary>
    /// Тестова реализация на ICurrentClinic с конструкторски ClinicId —
    /// НЕ IHttpContextAccessor (за service-тестове без HTTP pipeline).
    /// </summary>
    public sealed record TestClinicContext(int? ClinicId, int? VetProfileId = null) : ICurrentClinic
    {
        public int GetRequiredClinicId() =>
            ClinicId ?? throw new InvalidOperationException("Няма клиничен контекст.");

        public bool IsOperator => false;
    }
}
