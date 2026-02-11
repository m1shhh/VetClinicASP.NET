using Microsoft.EntityFrameworkCore;
using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public class OwnerService : IOwnerService
    {
        private readonly VetClinicContext _context;

        public OwnerService(VetClinicContext context)
        {
            _context = context;
        }

        public List<Owner> GetAllOwners()
        {
            // Включваме и Patients, за да покажем колко животни има всеки
            return _context.Owners
                .Include(o => o.Patients)
                .OrderBy(o => o.FullName) // Подреждаме по азбучен ред
                .ToList();
        }

        public Owner GetOwnerById(int id)
        {
            return _context.Owners.Find(id);
        }

        public void AddOwner(Owner owner)
        {
            _context.Owners.Add(owner);
            _context.SaveChanges();
        }

        public void DeleteOwner(int id)
        {
            // 1. Намираме собственика + неговите животни
            var owner = _context.Owners
                .Include(o => o.Patients)
                .FirstOrDefault(o => o.OwnerId == id);

            if (owner != null)
            {
                // 2. Ако има животни, първо трябва да изчистим техните данни
                // (Тук е малко опасно, но за учебен проект е най-лесно: 
                // трием животните, за да можем да изтрием човека)
                if (owner.Patients.Any())
                {
                    _context.Patients.RemoveRange(owner.Patients);
                }

                // 3. Трием самия собственик
                _context.Owners.Remove(owner);
                _context.SaveChanges();
            }
        }
    }
}