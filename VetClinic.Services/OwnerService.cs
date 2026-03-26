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
            // 1. Зареждаме собственика И абсолютно всички свързани данни към неговите животни
            var owner = _context.Owners
                .Include(o => o.Patients)
                    .ThenInclude(p => p.Appointments)   // Зареждаме часовете
                .Include(o => o.Patients)
                    .ThenInclude(p => p.Vaccinations)   // <-- НОВО: Зареждаме ваксините
                .Include(o => o.Patients)
                    .ThenInclude(p => p.MedicalRecords) // <-- НОВО: Зареждаме мед. картони
                .FirstOrDefault(o => o.OwnerId == id);

            if (owner != null)
            {
                if (owner.Patients != null && owner.Patients.Any())
                {
                    // 2. Обхождаме всяко животно на този собственик
                    foreach (var patient in owner.Patients)
                    {
                        // Изтриваме часовете
                        if (patient.Appointments != null && patient.Appointments.Any())
                        {
                            _context.Appointments.RemoveRange(patient.Appointments);
                        }

                        // <-- НОВО: Изтриваме ваксините
                        if (patient.Vaccinations != null && patient.Vaccinations.Any())
                        {
                            _context.Vaccinations.RemoveRange(patient.Vaccinations);
                        }

                        // <-- НОВО: Изтриваме медицинските записи
                        if (patient.MedicalRecords != null && patient.MedicalRecords.Any())
                        {
                            _context.MedicalRecords.RemoveRange(patient.MedicalRecords);
                        }
                    }

                    // 3. След като всички "деца" на животното са изтрити, изтриваме самите животни
                    _context.Patients.RemoveRange(owner.Patients);
                }

                // 4. Накрая изтриваме собственика
                _context.Owners.Remove(owner);

                // 5. Запазваме всички промени анблок (SQL Server ще ги изпълни в правилния ред)
                _context.SaveChanges();
            }
        }
    }
}