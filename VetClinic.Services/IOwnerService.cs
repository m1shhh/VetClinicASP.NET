using VetClinicASP.NET.Models;

public interface IOwnerService
{
    IEnumerable<Owner> GetAllOwners();
    void AddOwner(Owner owner);
    void DeleteOwner(int id);

    // меотди за редакцията на собствениците
    Owner GetOwnerById(int id);
    void UpdateOwner(Owner owner);
}