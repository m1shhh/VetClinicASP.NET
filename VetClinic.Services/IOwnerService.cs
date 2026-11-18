using VetClinicASP.NET.Models;

public interface IOwnerService
{
    IEnumerable<Owner> GetAllOwners();
    void AddOwner(Owner owner);

    /// <summary>
    /// Изтрива стопанин БЕЗ пациенти. Ако има пациенти, хвърля
    /// <see cref="InvalidOperationException"/> (GDPR guard, Task 11) — не се губи
    /// медицинска история чрез каскадно изтриване.
    /// </summary>
    /// <exception cref="InvalidOperationException">Стопанинът има регистрирани пациенти.</exception>
    void DeleteOwner(int id);

    // меотди за редакцията на собствениците
    Owner GetOwnerById(int id);
    void UpdateOwner(Owner owner);
}