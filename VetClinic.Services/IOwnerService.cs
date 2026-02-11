using VetClinicASP.NET.Models;

namespace VetClinic.Services
{
    public interface IOwnerService
    {
        // 1. Взима всички собственици
        List<Owner> GetAllOwners();

        // 2. Взима конкретен собственик (за редакция или детайли)
        Owner GetOwnerById(int id);

        // 3. Добавяне
        void AddOwner(Owner owner);

        // 4. Изтриване
        void DeleteOwner(int id);
    }
}