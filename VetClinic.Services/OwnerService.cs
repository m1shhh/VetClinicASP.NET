using System.Collections.Generic;
using System.Linq;
using VetClinicASP.NET.Models;
using VetClinic.Security;
using Microsoft.EntityFrameworkCore;

namespace VetClinic.Services
{
    public class OwnerService : IOwnerService
    {
        private readonly VetClinicContext _context;
        private readonly ICurrentClinic _currentClinic;

        public OwnerService(VetClinicContext context, ICurrentClinic currentClinic)
        {
            _context = context;
            _currentClinic = currentClinic;
        }

        public IEnumerable<Owner> GetAllOwners()
        {
            return _context.Owners
                .Include(o => o.Patients)
                .ToList();
        }

        public void AddOwner(Owner owner)
        {
            owner.ClinicId = _currentClinic.GetRequiredClinicId();
            _context.Owners.Add(owner);
            _context.SaveChanges();
        }

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

        public void DeleteOwner(int id)
        {
            var owner = _context.Owners.FirstOrDefault(o => o.OwnerId == id);

            // Чужд/несъществуващ id → no-op (филтрираният context не вижда чужди стопани).
            if (owner == null)
            {
                return;
            }

            // GDPR guard (Task 11, design doc §7): стопанин с пациенти НЕ се трие. Старото
            // поведение каскадно триеше животните с цялата им медицинска история — мълчалива
            // загуба на данни. Сега потребителят получава обяснение; първо се трият/преместват
            // пациентите. Проверката е през ФИЛТРИРАНИЯ контекст (текущата клиника).
            if (_context.Patients.Any(p => p.OwnerId == id))
            {
                throw new InvalidOperationException(
                    "Стопанинът има регистрирани пациенти. Първо изтрийте пациентите му.");
            }

            _context.Owners.Remove(owner);
            _context.SaveChanges();
        }
    }
}