using VetClinicASP.NET.Models;
using VetClinic.Security;

namespace VetClinic.Services
{
    public class VeterinarianService : IVeterinarianService
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public VeterinarianService(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        public List<Veterinarian> GetAllVeterinarians()
        {
            return _context.Veterinarians.ToList();
        }

        public Veterinarian GetVeterinarianById(int id)
        {
            return _context.Veterinarians.Find(id);
        }

        public void AddVeterinarian(Veterinarian vet)
        {
            vet.ClinicId = _currentClinic.GetRequiredClinicId();
            _context.Veterinarians.Add(vet);
            _context.SaveChanges();
        }

        public void UpdateVeterinarian(Veterinarian vet)
        {
            var existingVet = _context.Veterinarians.Find(vet.VetId);
            if (existingVet != null)
            {
                existingVet.FullName = vet.FullName;
                existingVet.Specialization = vet.Specialization;
                existingVet.PhoneNumber = vet.PhoneNumber;
                existingVet.WorkingHours = vet.WorkingHours; 
                existingVet.Email = vet.Email;               

                _context.SaveChanges();
            }
        }

        public void DeleteVeterinarian(int id)
        {
            var vet = _context.Veterinarians.Find(id);
            if (vet != null)
            {
                _context.Veterinarians.Remove(vet);
                _context.SaveChanges();
            }
        }
    }
}