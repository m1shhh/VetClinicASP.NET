using VetClinicASP.NET.Models;

public interface IOwnerService
{
    IEnumerable<Owner> GetAllOwners();
    void AddOwner(Owner owner);
    void DeleteOwner(int id); // Увери се, че това го има за Delete бутона!

    // НОВИ МЕТОДИ ЗА РЕДАКЦИЯ:
    Owner GetOwnerById(int id);
    void UpdateOwner(Owner owner);
}