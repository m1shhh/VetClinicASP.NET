using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public interface IVeterinarianService
    {
        // 1. Списък с всички лекари
        List<Veterinarian> GetAllVeterinarians();

        // 2. Взимане на един лекар по ID (за редакция)
        Veterinarian GetVeterinarianById(int id);

        // 3. Добавяне на нов лекар
        void AddVeterinarian(Veterinarian vet);

        // 4. Редакция
        void UpdateVeterinarian(Veterinarian vet);

        // 5. Изтриване (по желание, но нека го имаме)
        void DeleteVeterinarian(int id);
    }
}