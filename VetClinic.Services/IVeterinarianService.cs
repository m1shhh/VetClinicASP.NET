using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public interface IVeterinarianService
    {
        List<Veterinarian> GetAllVeterinarians();

        Veterinarian GetVeterinarianById(int id);

        void AddVeterinarian(Veterinarian vet);

        void UpdateVeterinarian(Veterinarian vet);

        void DeleteVeterinarian(int id);
    }
}