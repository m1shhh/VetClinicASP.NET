# Фаза 1 (Основи) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Логини с роли, изолация по клиники на ниво заявка, криптиране при запис и GDPR основи — основата, върху която висят Фазите 2–6.

**Architecture:** Multi-tenant по ClinicId: глобален EF query filter + `ICurrentClinic` от claims (deny-by-default); ASP.NET Identity с 4 роли; AES-256-GCM през ValueConverter (порт от AzITi); операторски панел само с метаданни.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10.0.2 + SQL Server, ASP.NET Identity (Identity.EntityFrameworkCore 10.0.2), xUnit + WebApplicationFactory върху реален SQL Server.

**Spec:** `/Users/mishi/Documents/PetCare/docs/2026-11-15-phase1-foundations-design.md` (всички решения дотам — Approaches, права, стойности).

## Global Constraints

- .NET 10; EF Core пакети 10.0.2; всичко в един проект + нов тестов проект.
- Криптирани полета: Owner.FullName/PhoneNumber/Email/Address, Appointment.Reason, MedicalRecord.Diagnosis/Treatment/Notes, Veterinarian.PhoneNumber/Email. Криптирани колони стават `nvarchar(max)`.
- НЕ криптираме: Patient.Name, Vaccination.VaccineName, Species/Breed текстове.
- Формат на криптиран запис: `enc:v1:` + Base64(nonce12+tag16+cipher); AES-256-GCM; ключ точно 32 байта Base64 на `Encryption:MasterKey`; fail-fast при липсващ/невалиден ключ.
- ValueConverter-ът е idempotent: write — ако стойността вече започва с `enc:v1:` остава as-is; read — ако няма префикс връща се as-is (legacy plaintext passthrough).
- Deny-by-default: без ClinicId контекст всяка tenant заявка връща нула реда; service-ите НИКАК не приемат ClinicId от контролери; `ICurrentClinic` е единственият източник.
- Роли: `Operator`, `ClinicAdmin`, `Vet`, `Receptionist` (константи в `AppRoles`).
- Бисквитка: `PetCare.Auth`, HttpOnly, Secure=Always, SameSite=Lax, 8 ч sliding.
- Lockout: 5 грешни опита → 15 мин; пароли: мин. 8, с главна + малка + цифра (без изискване за спец. символ).
- Login rate limit: 10/min на IP → HTTP 429.
- Headers (само production): nosniff, Referrer-Policy strict-origin-when-cross-origin, X-Frame-Options DENY, CSP точно като AzITi: `default-src 'self'; img-src 'self' https:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'`, HSTS, HTTPS redirect.
- `UseForwardedHeaders` само когато `ReverseProxy:Enabled=true`.
- AuditLog: само кодове/Id-та; забранено PII и IP.
- GDPR: ConsentGivenAtUtc + ConsentTextVersion ("v1", файл `docs/consent-text-v1.md`); отметката валидирана на сървъра.
- `Features:PublicRegistration` = **false** по подразбиране.
- Erasure: hard delete fan-out в транзакция; стопанин с пациенти НЕ се изтрива (блокиран с обяснение).
- UI текстове на български; никакви реални connection strings в git (dev → user-secrets).
- Clinic.Billing полетата съществуват в схемата, но НЕ се пипат от код във Фаза 1 (само дефолт по регистрация).

## Review Focus

1. **Изтичане между клиники** (navigation/IgnoreQueryFilters злоупотреба) — очаквано: никога ред от клиника Б при заявка от клиника А. Тест: Task 5 `TenancyIsolationTests`.
2. **Писане без конвертора** (raw SQL/нов път, изминал ValueConverter) — очаквано: в DB стойността винаги започва с `enc:v1:`. Тест: Task 6 `EncryptionAtRestTests` (чете суровата колона чрез отделна връзка).
3. **Оператор достига tenant данни** — очаквано: `InvalidOperationException`, нула реда. Тест: Task 5/9 `TenancyIsolationTests`.
4. **Register без отметка за съгласие / toggle затворен** — очаквано: server-side reject. Тест: Task 7 `ConsentAndRegistrationTests`.
5. **Proxy + rate limit**: зад proxy всички IP са proxy-то → 429 за всички. Очаквано: с `ReverseProxy:Enabled=true` X-Forwarded-For разделя bucket-ите. Тест: Task 12 `RateLimitForwardedHeadersTests`.

---

## File Structure

```
PetCare/
├── Program.cs                          (modify: Identity, Encryption, rate limiter, headers, policies, seed)
├── appsettings.json                    (modify: Features, Operator, ReverseProxy секции; ключът НИКОГА тук)
├── Models/
│   ├── Clinic.cs                       (new)
│   ├── ClinicBillingStatus.cs          (new enum)
│   ├── AuditLog.cs                     (new)
│   ├── ApplicationUser.cs              (new)
│   ├── Owner/Patient/Appointment/MedicalRecord/Vaccination/Veterinarian.cs (modify: ClinicId)
│   ├── VetClinicContext.cs             (modify: махне OnConfiguring/#warning, +DbSets, +ctor(ICurrentClinic))
│   └── VetClinicContext.Tenancy.cs     (new partial: ClinicId колонки, query filters, конвертори, Clinic config)
├── VetClinic.Security/                 (new folder, namespace VetClinic.Security)
│   ├── EncryptionService.cs            (port от AzITi + TextPrefix)
│   ├── EncryptedStringConverter.cs     (idempotent ValueConverter factory)
│   ├── ICurrentClinic.cs / CurrentClinic.cs
│   ├── AppRoles.cs
│   └── ClinicActiveFilter.cs           (IAsyncAuthorizationFilter, cache 60 s)
├── VetClinic.Services/                 (namespace VetClinic.Services)
│   ├── IAuditService.cs / AuditService.cs                      (new)
│   ├── IStaffService.cs / StaffService.cs                      (new)
│   ├── IErasureService.cs / ErasureService.cs                  (new)
│   ├── IOperatorMetadataService.cs / OperatorMetadataService.cs (new)
│   └── PatientService.cs, OwnerService.cs, AppointmentService.cs, VaccinationService.cs (modify: ClinicId на Create; OwnerService delete block)
├── Controllers/
│   ├── AccountController.cs            (new: Register/Login/Logout/Locked)
│   ├── ClinicAdminController.cs        (new: Users, CreateStaff, SetStaffActive, DeleteClinic)
│   └── OperatorController.cs           (new: Index, SetActive, NewClinic)
├── Views/Account/{Login,Register,Locked}.cshtml, Views/ClinicAdmin/Users.cshtml, Views/Operator/{Index,NewClinic}.cshtml (new, BG)
├── Views/Home/Privacy.cshtml           (modify: реален текст)
├── docs/consent-text-v1.md             (new)
├── Infrastructure/EncryptExistingDataService.cs (new: `--encrypt-existing` one-off)
├── Migrations/                         (generated)
├── PetCare.Tests/                      (new xUnit project)
│   ├── Infrastructure/SqlTestDatabaseFixture.cs
│   ├── EncryptionServiceTests.cs
│   ├── TenancyIsolationTests.cs
│   ├── EncryptionAtRestTests.cs
│   ├── ConsentAndRegistrationTests.cs
│   ├── LockoutTests.cs
│   ├── ErasureServiceTests.cs
│   ├── AuditServiceTests.cs
│   ├── ClinicActiveEnforcementTests.cs
│   └── RateLimitForwardedHeadersTests.cs
└── .github/workflows/ci.yml            (new)
```

---

### Task 1: EncryptionService (порт от AzITi) + тестове

**Files:** Create `VetClinic.Security/EncryptionService.cs`, `VetClinic.Security/EncryptedStringConverter.cs`; Create test project `PetCare.Tests/` (`PetCare.Tests.csproj`, xUnit, ref към root csproj); Test `PetCare.Tests/EncryptionServiceTests.cs`.

**Interfaces (Produces):** `VetClinic.Security.EncryptionService` — `string Encrypt(string)`, `string Decrypt(string)`, `byte[] EncryptBytes(byte[])`, `byte[] DecryptBytes(byte[])`, ctor `EncryptionService(IConfiguration)`; constants `NonceSize=12`, `TagSize=16`, `TextPrefix="enc:v1:"`. `EncryptedStringConverter.Create(EncryptionService) → ValueConverter<string?, string?>` (idempotent: write skip-а вече `enc:v1:`; read passthrough-ва без-префикс).

- [ ] **Step 1:** Създай тестовия проект: `dotnet new xunit -o PetCare.Tests` + `dotnet add PetCare.Tests reference VetClinicASP.NET.csproj`. Пренеси `EncryptionService` 1:1 от `/Users/mishi/Documents/AzITi-master/Services/EncryptionService.cs` (само namespace → `VetClinic.Security`).
- [ ] **Step 2 (failing tests):** `EncryptionServiceTests`: `Roundtrip_PreservesText` (BG текст с диакритика), `Decrypt_TamperedData_ThrowsCryptographicException` (сменя последния байт), `Decrypt_WithoutPrefix_Throws`, `Converters_AreIdempotent` (`converter.ConvertToProvider` върху вече-криптирано не двойно-криптира: декриптиран резултат == оригинал), `Converter_Read_LegacyPlaintext_Passthrough`.
- [ ] **Step 3 (verify fail):** `dotnet test PetCare.Tests` → FAIL (липсва EncryptedStringConverter).
- [ ] **Step 4:** Имплементирай `EncryptedStringConverter.Create` (write: `null→null; StartsWith(TextPrefix)→as-is; else Encrypt`; read: `null→null; StartsWith(TextPrefix)→Decrypt; else as-is`).
- [ ] **Step 5:** `dotnet test PetCare.Tests` → PASS. Ключ за тестовете: тестовете конструират EncryptionService с in-memory `ConfigurationBuilder` (`Encryption:MasterKey` = нов случайн 32-байтов Base64 на всяка проба).
- [ ] **Step 6:** Commit: `feat(security): port AES-256-GCM EncryptionService with idempotent converter`.

### Task 2: Connection string от конфигурация

**Files:** Modify `Models/VetClinicContext.cs`, `appsettings.json`.

- [ ] **Step 1:** Махни `OnConfiguring` + `#warning`-a от `VetClinicContext`; `appsettings.json` добавя `"ConnectionStrings": { "DefaultConnection": "" }`.
- [ ] **Step 2:** Dev стойност в user-secrets: `dotnet user-secrets init` + `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Data Source=<dev-sql-instance>;Initial Catalog=VetClinic;Integrated Security=True;Trust Server Certificate=True"` (същата като днешната).
- [ ] **Step 3 (verify):** `dotnet build` OK; `dotnet ef dbcontext list` показва context (EF tools четат user-secrets). Commit: `chore(db): connection string from configuration only`.

### Task 3: EF Migrations baseline върху съществуващата БД

> **Адаптация (15.11, одобрена от собственика):** старата dev БД (`mishi\SQLEXpress`, Windows) е недостъпна от Mac и е излязла от употреба. Dev база = Docker SQL Server 2025 на localhost:1433 (SQL auth, паролата само в user-secrets, въведена от собственика). Базата ще се създаде празна от самите миграции → рецептата "mark applied" отпада; fresh-DB пътят е единственият. Таблица от пре-fлай проверката за `DurationMinutes` отпада също — fresh базата получава схемата от модела (включително DurationMinutes). Поправеният connection string е потвърден като работещ — собственикът повторно записа user-secrets на 15.11.

**Files:** Create `Migrations/<ts>_InitialBaseline.cs` (generated); modify root csproj (пакет `Microsoft.EntityFrameworkCore.Design` 10.0.2).

- [ ] **Step 1 (preflight):** SQL проверка, че колоната `Appointments.DurationMinutes` СЪЩЕСТВУВА в dev БД (комит „added a calendar and appointment duration“ го подсказва). Ако липсва — ръчно `ALTER TABLE Appointments ADD DurationMinutes int NOT NULL DEFAULT 30;` ПИНВА се: без това baseline-ът ще create-не таблица без нея и следващите UPDATE-и на dev БД ще гърмят.
- [ ] **Step 2:** `dotnet add package Microsoft.EntityFrameworkCore.Design` + `dotnet tool install -g dotnet-ef` (или update). `dotnet ef migrations add InitialBaseline` — Up() с ПЪЛНАТА схема (шест таблици).
- [ ] **Step 3 (dev БД: маркирай като приложена, БЕЗ изпълнение):** върху съществуващата `VetClinic` DB изпълни T-SQL (тук и само тук, еднократно):
  ```sql
  IF OBJECT_ID(N'__EFMigrationsHistory') IS NULL CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL, [ProductVersion] nvarchar(32) NOT NULL, CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId]));
  INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'<ts>_InitialBaseline', N'10.0.2');  -- <ts> = реалният префикс на генерирания файл
  ```
- [ ] **Step 4 (verify):** `dotnet ef migrations list` → InitialBaseline е applied; `dotnet ef database update` не изпълнява нищо; commit схема-файловете. Commit: `chore(db): migrations baseline over existing DB`.

### Task 4: Clinic + ClinicId на шестте таблици (миграция + backfill)

**Files:** Create `Models/Clinic.cs`, `Models/ClinicBillingStatus.cs`; Modify `Models/Owner.cs`, `Patient.cs`, `Appointment.cs`, `MedicalRecord.cs`, `Vaccination.cs`, `Veterinarian.cs` (+`public int ClinicId { get; set; }`); Create `Models/VetClinicContext.Tenancy.cs` (partial: Clinic конфиг, ClinicId колони `HasColumnName("ClinicID")`, FK→Clinic, index по ClinicId на всяка таблица); Create миграция `AddClinicAndClinicId`.

**Interfaces (Produces):** `Clinic`: `int ClinicId` (PK), `string Name` (≤100), `string? Address`, `string? Phone`, `string? LogoPath`, `DateTime CreatedAtUtc`, `bool IsActive`, `ClinicBillingStatus BillingStatus`, `DateTime? BillingPaidUntilUtc`, `string? BillingProblem`, `DateTime? TrialEndsAtUtc`, `DateTime? ConsentGivenAtUtc`, `string? ConsentTextVersion`. Enum: `Trial=0, Active=1, PastDue=2, Suspended=3, Cancelled=4`. Шестте entity-та имат `int ClinicId` (след миграцията non-nullable).

- [ ] **Step 1:** Модели + partial конфиг. **Step 2:** `dotnet ef migrations add AddClinicAndClinicId`.
- [ ] **Step 3:** В миграцията, СЛЕД AddColumn на nullable `ClinicId`: `migrationBuilder.Sql("INSERT INTO Clinics (ClinicID, Name, CreatedAtUtc, IsActive, BillingStatus) VALUES (1, N'Пилотна клиника', SYSUTCDATETIME(), 1, 1); UPDATE Owners SET ClinicID = 1; UPDATE Patients SET ClinicID = 1; UPDATE Appointments SET ClinicID = 1; UPDATE MedicalRecords SET ClinicID = 1; UPDATE Vaccinations SET ClinicID = 1; UPDATE Veterinarians SET ClinicID = 1;")` — и чак тогава `AlterColumn` към NOT NULL + FK + index.
- [ ] **Step 4 (verify):** `dotnet ef database update` върху dev БД → всеки ред има ClinicID=1; fresh проверка: следващите тестове доказват свежи DB. Commit: `feat(tenancy): Clinic table + ClinicId everywhere, backfill to pilot clinic`.

### Task 5: ICurrentClinic + глобални query filters + Create-пътища + изолационни тестове

**Files:** Create `VetClinic.Security/ICurrentClinic.cs`, `CurrentClinic.cs`, `AppRoles.cs`; Modify `Models/VetClinicContext.cs` (втори ctor: `(DbContextOptions, ICurrentClinic)`; `using VetClinic.Security`), `VetClinicContext.Tenancy.cs` (query filter за 6-те entity-та + Clinic); Modify `VetClinic.Services/PatientService.cs`, `OwnerService.cs`, `AppointmentService.cs`, `VaccinationService.cs` (Create-пътищата пишат `ClinicId = _currentClinic.GetRequiredClinicId()`); Create `PetCare.Tests/Infrastructure/SqlTestDatabaseFixture.cs`, `TenancyIsolationTests.cs`.

**Interfaces:** `ICurrentClinic { int? ClinicId { get; } int GetRequiredClinicId(); int? VetProfileId { get; } bool IsOperator { get; } }` — реализация от `IHttpContextAccessor`: claim `"clinic_id"` (int), `"vet_profile_id"`, роля Operator → IsOperator. `GetRequiredClinicId()` → `ClinicId ?? throw new InvalidOperationException("Няма клиничен контекст.")`. Query filter: `e.ClinicId == _currentClinicId` (`_currentClinicId` е int? поле на context, захванато в ctor-а). **Филтрите важат за Clinic НЕ** (tenant корен). Staff/Identity заявки НЕ са филтрирани от EF — service-ите за персонал филтрират изрично по `user.ClinicId == current`.

**SqlTestDatabaseFixture:** env `PETCARE_TEST_MASTER` (default `Data Source=<dev-sql-instance>;Initial Catalog=master;Integrated Security=True;Trust Server Certificate=True`) → създава `PetCareTests_{Guid:N}` → `Database.Migrate()` → tearDown DROP. `[assembly: CollectionBehavior(DisableTestParallelization = true)]` в AssemblyInfo. DI за context в тестове: `AddDbContext<VetClinicContext>((sp, o) => o.UseSqlServer(fixture.ConnectionString))` + scoped `CurrentClinic` с ръчно набрани claims (тестов `TestClinicContext : ICurrentClinic` с конструкторски ClinicId — НЕ IHttpContextAccessor за service-тестовете).

- [ ] **Step 1 (failing tests):** `TenancyIsolationTests` — seed две клиники (Id 10 „А“, 11 „Б“) + по 1 пациент всяка: `PatientService.GetAllPatients(...)` с контекст А НЕ връща пациента на Б; `GetPatientDetails(базов_ид_на_пациент_на_Б)` → null; `AddPatient` с контекст А записва с ClinicId=10; контекст без ClinicId (`TestClinicContext(null)`) → `GetRequiredClinicId()` хвърля `InvalidOperationException`, `GetAllPatients` връща празно.
- [ ] **Step 2 (verify fail).** **Step 3:** Имплементирай Security класовете + филтрите + Create-пътищата. **Step 4 (verify pass).** **Step 5:** Commit: `feat(tenancy): deny-by-default clinic isolation via global query filters`.

### Task 6: Криптиране на полетата + `--encrypt-existing`

> **Адаптация (16.11, от имплементацията, потвърдена от ревю):** runner-ът НЕ е наивното "зареди, презапиши, SaveChanges" — EF change tracker пази *декриптирания* plaintext като оригинал, затова презаписване със същата стойност не отключва промяна, а форсиран запис на всеки ред би прекриптирал вече криптирани (чупи byte-idempotence). Реализиран механизъм: суров ADO pre-scan за клетки без `enc:v1:` → презапис само на тях през `EncryptionService.Encrypt` → SaveChanges. Миграцията е с 8 AlterColumn (Treatment/Notes вече бяха nvarchar(max) в baseline). Търсенето по стопанин е преместено в паметта (SQL LIKE върху шифрован текст хвърля SqlException).

**Files:** Modify `VetClinicContext.Tenancy.cs` (конвертори върху посочените полета; колоните → `nvarchar(max)`); Create `Infrastructure/EncryptExistingDataService.cs`; Create миграция `EncryptColumnsToNvarcharMax`; Test `EncryptionAtRestTests.cs`.

**Interfaces (Produces):** `EncryptExistingDataService` — стартира с `dotnet run -- --encrypt-existing`: за шестте таблици зарежда редовете (read passthrough на legacy plaintext), докосва криптираните свойства и прави `SaveChanges()` (write конверторът криптира); idempotent (второ пускане = no-op).

- [ ] **Step 1:** Конвертори + миграция (тип на колоните). **Step 2:** Runner + регистрация в `Program.cs`: `if (args.Contains("--encrypt-existing")) { scope.RunEncryptExisting(); return; }`.
- [ ] **Step 3 (failing test):** `EncryptionAtRestTests` — seed пациент сplaintext FullName; пусни `--encrypt-existing` логиката като метод върху fixture context; отвори СУРОВА връзка (`fixture.MasterConnection`, `USE PetCareTests_...`) и провери: `FullName LIKE 'enc:v1:%'`, а `Patient.Name` НЕ е криптиран; повторно пускане не променя дължината на стойността (idempotence).
- [ ] **Step 4 (verify pass)** + пускане върху dev БД (`dotnet run -- --encrypt-existing`). **Step 5:** Commit: `feat(security): encrypt owner PII, appointment reason, medical records at rest`.

### Task 7: Identity — Register / Login / Logout + съгласие + toggle

**Files:** Create `Models/ApplicationUser.cs` (`IdentityUser` + `int? ClinicId`, `string DisplayName`, `bool IsActive = true`, `int? VeterinarianId`); Modify `Program.cs` (`AddIdentityCore<ApplicationUser>` + `AddRoles<IdentityRole>` + `AddEntityFrameworkStores<VetClinicContext>` + cookie по Global Constraints; `AddAuthentication/AddAuthorization`); Create `Controllers/AccountController.cs`, `Views/Account/{Login,Register,Locked}.cshtml`, `docs/consent-text-v1.md`; Create миграция `AddIdentity`; Modify `Program.cs` (регистрация на `CurrentClinic` + `IHttpContextAccessor`); Test `ConsentAndRegistrationTests.cs`, `LockoutTests.cs`.

**Interfaces:** `AccountController`: `GET/POST Register`, `GET/POST Login`, `POST Logout`, `GET Locked`. POST Register: `{ ClinicName, DisplayName, Email, Password, ConfirmPassword, ConsentAccepted }` → създава Clinic (`BillingStatus=Trial`, `IsActive=true`, `ConsentGivenAtUtc=UtcNow`, `ConsentTextVersion="v1"`) + ClinicAdmin user, Sign-in, redirect `Patients/Index`. Ако `Features:PublicRegistration != true` → `NotFound()`. POST Login: валидира `user.IsActive` (деактивиран → общ „невалидни данни“), `_signInManager.PasswordSignInAsync(user, password, isPersistent:false, lockoutOnFailure:true)`.

- [ ] **Step 1 (failing tests):** `ConsentAndRegistrationTests`: POST Register без `ConsentAccepted` → ModelState грешка, НИКАКЪВ Clinic ред (consent е сървърен, не required-атрибут); с отметка → Clinic със ConsentGivenAtUtc≠null и ConsentTextVersion="v1"; register при toggle=false → 404. `LockoutTests`: 5 грешни POST Login → `UserManager.IsLockedOutAsync` true; 6-ти опит с ВЯРНА парола → отказан.
- [ ] **Step 2 (verify fail).** **Step 3:** Имплементация (Identity options точно по Global Constraints; antiforgery по подразбиране). **Step 4 (verify pass).** **Step 5:** Commit: `feat(auth): identity with roles, consent capture, public registration behind toggle`.

### Task 8: Персонал + линк Veterinarian

**Files:** Create `VetClinic.Services/IStaffService.cs`/`StaffService.cs`, `Controllers/ClinicAdminController.cs`, `Views/ClinicAdmin/Users.cshtml`; Modify `Controllers/AppointmentsController.cs` + `VetClinic.Services/AppointmentService.cs` (+ MedicalRecords/Vaccinations Create: `VetId` по подразбиране); Test `StaffServiceTests.cs` (fixture).

**Interfaces:** `IStaffService { IdentityResult CreateStaff(string email, string displayName, string password, string role); void SetStaffActive(string userId, bool active); List<ApplicationUser> ListStaff(); }` — CreateStaff: validates роля ∈ {ClinicAdmin, Vet, Receptionist}; за `Vet` създава и `Veterinarian` ред (FullName=DisplayName, ClinicId=current) и го линква (`user.VeterinarianId`); ListStaff → само `user.ClinicId == current` (explicit, без филтър); SetStaffActive(false) НЕ пипа Veterinarian реда. Create-пътищата на прегледи/записи: ако current е Vet → `VetId = current.VetProfileId.Value` (self-приписване); Receptionist подава VetId изрично (валиден само сред клиниката — query filter го гарантира).

- [ ] **Step 1 (failing tests):** CreateStaff(Vet) → user с role Vet + linked Veterinarian ред със същия ClinicId; SetStaffActive(false) → user.IsActive=false, Veterinarian редът ОСТАВА, истор. записите непокътнати; CreateStaff като Receptionist в чужда клиника → не вижда чужд персонал (ListStaff празен за друг контекст).
- [ ] **Step 2–4:** verify fail → имплементация → verify pass. **Step 5:** Commit: `feat(staff): staff management with linked veterinarian profiles`.

> **Разширение (16.11, от Task 5 ревю, отложено тук):** Create-пътищата на Patient/Appointment/Vaccination валидират FK референциите (OwnerId / PatientId / VeterinarianId) през ФИЛТРИРАНИЯ контекст — cross-tenant безопасно (чужд/несъществуващ FK id → NotFound), по модела на MedicalRecordsController.Create/Edit от Task 5.

### Task 9: Операторски панел + bootstrap seed + IsActive enforcement

> **Разширение (17.11, от Task 7 ревю, отложено тук):** (а) при seed-ването на ролите — idempotent guard при стартиране + AccountController.Register проверява `AddToRoleAsync` резултата (rollback + model error при провал); (б) Logout → само POST с antiforgery (GET/logout линковете идват от _Layout в тази задача); (в) тест, assert-ващ самия `clinic_id` claim в principal-а след sign-in.

**Files:** Create `VetClinic.Services/IOperatorMetadataService.cs`/`OperatorMetadataService.cs`, `Controllers/OperatorController.cs`, `Views/Operator/{Index,NewClinic}.cshtml`, `VetClinic.Security/ClinicActiveFilter.cs`, `Infrastructure/OperatorSeedService.cs` (IHostedService); Modify `Program.cs` (filter + seed + metadata DI); Modify `Views/Shared/_Layout.cshtml` (условни линкове: Operator → панел; ClinicAdmin → Служители); Test `OperatorPanelTests.cs`, `ClinicActiveEnforcementTests.cs`.

**Interfaces:** `OperatorMetadataService` (всички заявки `IgnoreQueryFilters()`, агрегати САМО): `List<OperatorClinicRow> GetClinics()` — `record OperatorClinicRow(int ClinicId, string Name, string BillingStatus, DateTime CreatedAtUtc, bool IsActive, int Patients, int Appointments)`; `void SetActive(int clinicId, bool active)`; `int CreatePilotClinic(string name, string adminEmail, string adminDisplayName, string adminPassword)` (Clinic BillingStatus=Active + ClinicAdmin user). `OperatorSeedService`: на старт, ако няма Operator user и `Operator:Email`+`Operator:InitialPassword` са конфигурирани → създава; липсват → warning в лог, НЕ crash. `ClinicActiveFilter` (IAsyncAuthorizationFilter, global): skip ако не-аутентикиран, роля Operator, или пътят е `/Account/*`; иначе — Clinic.IsActive от `IMemoryCache` (60 s); false → `SignOutAsync` + redirect `/Account/Locked`.

- [ ] **Step 1 (failing tests):** OperatorPanelTests: GetClinics връща агрегати за ДВЕ клиники (без PII — няма поле за пациенти/имена извън Name/BillingStatus); опит на metadata service да върне съдържание (пациенти) — няма такъв API (compile-level); SetActive(false) → Clinic.IsActive=false. ClinicActiveEnforcementTests: деактивирана клиника → authenticated GET `/Patients` → 302 `/Account/Locked` (≤ кеш 60 s), свеж login → отказан; активиране обратно → достъпът се връща.
- [ ] **Step 2–4:** verify fail → имплементация → verify pass. **Step 5:** Commit: `feat(operator): metadata-only operator panel, operator seed, immediate clinic deactivation`.

### Task 10: Audit log

**Files:** Create `Models/AuditLog.cs` (`long AuditId`, `int? ClinicId`, `string? UserId`, `string Action(50)`, `string? Entity`, `int? EntityId`, `DateTime AtUtc`); Create миграция; Create `VetClinic.Services/IAuditService.cs`/`AuditService.cs`; Modify `AccountController` (login.success/login.failed), `PatientsController` (patient.created/patient.deleted), `ClinicAdminController` (staff.created/staff.deactivated), `OperatorController` (clinic.activated/clinic.deactivated) — action кодове по списъка; Test `AuditServiceTests.cs`.

**Interfaces:** `IAuditService { void Log(string action, string? entity = null, int? entityId = null); }` — взима UserId/ClinicId от ICurrentClinic/HttpContext.User; ClinicId = null за операторски действия. Направи AuditLog filtered? НЕ — audit пише отвън; query filter не се прилага (ClinicId може null) → **AuditLog не е tenant entity** (без filter, service-ът е единственият записвач).

- [ ] **Step 1 (failing tests):** Log записва ред с правилните ClinicId/UserId/Action/AtUtc (UtcNow, толеранс 2 s); login.failed при 5 грешни опита → 5 реда login.failed + 1 lockout ред; нито едно поле не съдържа PII (assert-и върху Action/Entity само).
- [ ] **Step 2–4:** verify fail → имплементация → verify pass. **Step 5:** Commit: `feat(audit): action audit log without PII`.

### Task 11: ErasureService + блокирани изтривания на стопанин

> **Разширение (17.11, от Task 8 ревю, отложено тук):** Update пътищата с променени FK валидират през ФИЛТРИРАНИЯ контекст (същият модел като create): `PatientService.UpdatePatient` (OwnerId), `AppointmentService.UpdateAppointment` (PatientId/VetId), `MedicalRecordsController.Edit` (VetId + self-assignment за Vet потребител). Също: блокирай/разлинквай изтриване на Veterinarian, свързан с user (AspNetUsers.VeterinarianId няма DB FK — stale линк до re-login).
>
> **Разширение (18.11, от Task 10 ревю):** ErasureService пише `clinic.deleted` ВЪТРЕ в транзакцията — уточни политиката за audit провал: (а) `Entity` също се truncate-ва до 50 (днес само `Action`), (б) решение fail-open (audit грешка се логва, бизнес операцията продължава) vs fail-closed (цялата транзакция се връща) — за erasure fail-closed е приемливо, за create-пътища не; документирай в `AuditService`.

**Files:** Create `VetClinic.Services/IErasureService.cs`/`ErasureService.cs`; Modify `ClinicAdminController` (DeleteClinic GET/POST — двойно потвърждение: въвежда се име на клиниката; след успех → SignOut), `VetClinic.Services/OwnerService.cs` (DeleteOwner → block, ако има пациенти); Test `ErasureServiceTests.cs`.

**Interfaces:** `IErasureService { void EraseClinic(int clinicId); }` — `IgnoreQueryFilters()` + изрично `Where(ClinicId == clinicId)`, в транзакция, ред: Vaccinations → MedicalRecords → Appointments → Patients → Owners → Veterinarians → ApplicationUser(s)+Identity joins → Clinic; записва audit `clinic.deleted` (ClinicId остава, без съдържание); idempotent (повторно = no-op). OwnerService.DeleteOwner: `_context.Patients.Any(p => p.OwnerId == id)` → `throw new InvalidOperationException("Стопанинът има регистрирани пациенти. Първо изтрийте пациентите му.");`

- [ ] **Step 1 (failing tests):** seed клиника с 2 стопани/3 пациенти/записи/записвания/ваксинации/Vet профил/2 акаунта → EraseClinic → нула реда навсякъде (всички таблици), Clinic редът изчезнал, audit `clinic.deleted` остава; повторно EraseClinic → no-op. DeleteOwner с пациенти → throw, данните непокътнати.
- [ ] **Step 2–4:** verify fail → имплементация → verify pass. **Step 5:** Commit: `feat(gdpr): clinic erasure fan-out; block owner deletion with patients`.

### Task 12: Headers/CSP/HSTS + rate limiting + ForwardedHeaders

> **Разширение (18.11, от Task 10 ревю):** `AccountController.Register` не забранява на вече аутентикиран user да POST-не регистрация — такъв POST би взел стария `clinic_id` claim и би записал `clinic.registered` с грешна ClinicId. Добави guard (анонимен само) или explicit null при логването.

**Files:** Modify `Program.cs`; Test `SecurityHeadersTests.cs`, `RateLimitForwardedHeadersTests.cs`.

- [ ] **Step 1 (failing tests):** SecurityHeaders (production env WAF): 4-те headers + CSP със ГОРНИЯ точен низ, HSTS present; development env → без тях. RateLimit: 11 POST Login за 1 мин от едно IP → последният 429; `ReverseProxy:Enabled=true` + `X-Forwarded-For: 1.2.3.4` (10 пъти) → 429 само за 1.2.3.4, различен XFF минава.
- [ ] **Step 2–4:** verify fail → имплементация (копия на AzITi middleware/limiter; ForwardedHeaders зад `ReverseProxy:Enabled`) → verify pass. **Step 5:** Commit: `feat(security): security headers, CSP, login rate limiting, forwarded headers`.

### Task 13: Privacy страница + consent текст (реални данни)

**Files:** Modify `Views/Home/Privacy.cshtml`; Create `docs/consent-text-v1.md`.

Съдържание (реално, без placeholder): оператор = **Мишо — физическо лице**; хостинг = **Microsoft Azure, регион в ЕС (West Europe)**; какви данни (имена/телефон/имейл/адрес на стопанин, медицински записи на животните), къде (Azure SQL, криптирани AES-256-GCM при запис), retention (докато клиниката е активна), права (достъп/корекция/изтриване — чрез клиниката; клиниката може да изиска пълно изтриване), контакт: секцията за имейл е написана като „Контакт: предстои — до тогава връзката е директна с оператора“ **и в кода има TODO-guard**: при включване на `Features:PublicRegistration` без конфигуриран `Contact:Email` → startup warning (не блокиращ).

- [ ] **Step 1:** Напиши двата файла. **Step 2 (verify):** страницата рендерира; TODO-guard unit test: toggle=true без `Contact:Email` → warning лог (assert чрез captured logger). **Step 3:** Commit: `docs(gdpr): real privacy notice and consent text v1`.

### Task 14: CI + пълен проход по критериите

**Files:** Create `.github/workflows/ci.yml`.

```yaml
on: [push, pull_request]
jobs:
  build-test:
    runs-on: ubuntu-latest
    services:
      mssql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env: { ACCEPT_EULA: "Y", MSSQL_SA_PASSWORD: "PetCare_T3st!", MSSQL_PID: "Developer" }
        ports: ["1433:1433"]
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'PetCare_T3st!' -Q 'SELECT 1'" 
          --health-interval 10s --health-timeout 5s --health-retries 10
    env:
      PETCARE_TEST_MASTER: "Server=localhost,1433;Database=master;User Id=sa;Password=PetCare_T3st!;TrustServerCertificate=True"
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet build
      - run: dotnet test PetCare.Tests --no-build -v n
```

- [ ] **Step 1:** workflow файл. **Step 2 (verify локално):** `dotnet test PetCare.Tests -v n` → всичко зелено. **Step 3:** Провери списъка на критериите от design doc §10 (изолация, оператор-отказ, сурови криптирани колони, lockout, headers, erasure-нула реда) — всеки е покрит от тест. **Step 4:** Commit: `ci: build + SQL Server-backed test run`.

> **Разширение (18.11, от Task 10 ревю — whole-branch notes):**
> - Липсващи audit тестове: OperatorController (`clinic.activated/deactivated/created`), `clinic.registered`, login на деактивиран акаунт, negative `patient.deleted` (несъществуващ/чужд id).
> - PII тестът да сканира ВСИЧКИ нови редове (`Assert.All`), не само един login.failed ред; включи seeded DisplayName и реалната тестова парола.
> - `patient.deleted` race: existence check и `DeletePatient` са отделни операции — `bool DeletePatient` би бил точен; логвай само при реален delete.
> - Бъдещ tenant-facing audit read ЗАДЪЛЖИТЕЛНО филтрира по ClinicId (днес няма read API — AuditLogs не е tenant entity); иначе метаданни на други клиники изтичат.

---

## Самопроверка (извършена)

- **Spec coverage:** §4 модел (T4/7/9/10), §5 права (T7/8/9 филтри по роли в контролерите — policy `RequireRole` на ClinicAdmin/Operator), §6 сигурност (T1/2/6/12), §7 GDPR (T7/11/13), §8 оператор (T9), §9 стъпки 1–11 (T2–T14), §10 критерии (T14 Step 3), §11 (не се докосва тук). Пропуснато съзнателно: пилотните данни на съществуващата БД остават в пилотната клиника (T4 backfill) — критерият „две клиники“ се тества в тестовете, не в dev БД.
- **Step scan:** всеки стъпка = един резултат; телата на сервиси са оставени на имплементатора с точни сигнатури; само конверторът и fixture-ът са дадени като алгоритъм.
- **Type consistency:** `ICurrentClinic.ClinicId/GetRequiredClinicId/VetProfileId/IsOperator` използвани еднакво в T5/8/9/10; `OperatorClinicRow` в T9; action кодовете в T10.
- **Review Focus:** 5/5 имат притежател-тестове (T5, T6, T5+9, T7, T12).
