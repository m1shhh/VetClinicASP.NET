using System.Collections.Generic;
using System.Linq;
using VetClinicASP.NET.Models;
using Microsoft.EntityFrameworkCore;

namespace VetClinic.Services
{
    public class OwnerService : IOwnerService
    {
        private readonly VetClinicContext _context;

        public OwnerService(VetClinicContext context)
        {
            _context = context;
        }

        public IEnumerable<Owner> GetAllOwners()
        {
            return _context.Owners
                .Include(o => o.Patients)
                .ToList();
        }

        public void AddOwner(Owner owner)
        {
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
            var owner = _context.Owners
                .Include(o => o.Patients)
                    .ThenInclude(p => p.Appointments)   // Зареждане на часовете
                .Include(o => o.Patients)
                    .ThenInclude(p => p.Vaccinations)   // Зареждане на ваксините
                .Include(o => o.Patients)
                    .ThenInclude(p => p.MedicalRecords) // Зареждане на мед. картони
                .FirstOrDefault(o => o.OwnerId == id);

            if (owner != null)
            {
                if (owner.Patients != null && owner.Patients.Any())
                {
                    // Обхождаме всяко животно на този собственик
                    foreach (var patient in owner.Patients)
                    {
                        // Изтриване на  часовете
                        if (patient.Appointments != null && patient.Appointments.Any())
                        {
                            _context.Appointments.RemoveRange(patient.Appointments);
                        }

                        // Изтриване на ваксините
                        if (patient.Vaccinations != null && patient.Vaccinations.Any())
                        {
                            _context.Vaccinations.RemoveRange(patient.Vaccinations);
                        }

                        // Изтриване на  медицинските записи
                        if (patient.MedicalRecords != null && patient.MedicalRecords.Any())
                        {
                            _context.MedicalRecords.RemoveRange(patient.MedicalRecords);
                        }
                    }

                    // След като всички "деца" на животното са изтрити, изтриваме самите животни
                    _context.Patients.RemoveRange(owner.Patients);
                }

                // изтриване на собственика
                _context.Owners.Remove(owner);

                // Запазване на всички промени 
                _context.SaveChanges();
            }
        }
    }
}