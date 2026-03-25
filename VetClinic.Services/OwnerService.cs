using System.Collections.Generic;
using System.Linq;
using VetClinicASP.NET.Models;
using Microsoft.EntityFrameworkCore;
// Make sure your DbContext using statement is here if needed (e.g., using VetClinic.Data;)

namespace VetClinic.Services
{
    public class OwnerService : IOwnerService
    {
        private readonly VetClinicContext _context;

        public OwnerService(VetClinicContext context)
        {
            _context = context;
        }

        // ПОПРАВКА 1: Точно съвпадение с интерфейса (IEnumerable<Owner>)
        public IEnumerable<Owner> GetAllOwners()
        {
            // Добавяме .Include(o => o.Patients), за да заредим животните на всеки собственик
            return _context.Owners
                .Include(o => o.Patients)
                .ToList();
        }

        public void AddOwner(Owner owner)
        {
            _context.Owners.Add(owner);
            _context.SaveChanges();
        }

        // ПОПРАВКА 2: Оставяме само ЕДНО копие на GetOwnerById
        public Owner GetOwnerById(int id)
        {
            return _context.Owners.FirstOrDefault(o => o.OwnerId == id);
        }

        public void UpdateOwner(Owner owner)
        {
            var existingOwner = _context.Owners.FirstOrDefault(o => o.OwnerId == owner.OwnerId);
            if (existingOwner != null)
            {
                existingOwner.FullName = owner.FullName;
                existingOwner.PhoneNumber = owner.PhoneNumber;
                existingOwner.Address = owner.Address;

                _context.SaveChanges();
            }
        }

        // ПОПРАВКА 3: Оставяме само ЕДНО копие на DeleteOwner
        public void DeleteOwner(int id)
        {
            // 1. Намираме собственика И зареждаме неговите животни (Patients)
            // Важно: Трябва да имате using Microsoft.EntityFrameworkCore; най-горе във файла!
            var owner = _context.Owners
                .Include(o => o.Patients)
                .FirstOrDefault(o => o.OwnerId == id);

            if (owner != null)
            {
                // 2. Първо изтриваме всички животни на този собственик
                if (owner.Patients != null && owner.Patients.Any())
                {
                    _context.Patients.RemoveRange(owner.Patients);
                }

                // 3. След това изтриваме самия собственик
                _context.Owners.Remove(owner);

                // 4. Запазваме промените анблок
                _context.SaveChanges();
            }
        }
    }
}