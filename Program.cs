using Microsoft.EntityFrameworkCore;
using VetClinic.Services;
using VetClinicASP.NET.Models;
using VetClinic.Models;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// 1. ЗОНА ЗА РЕГИСТРАЦИЯ НА УСЛУГИ (SERVICES)
// Всички AddScoped и AddDbContext трябва да са ТУК (преди Build)
// =========================================================

// 1.1. Добавяне на MVC (Контролери и Views)
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IVeterinarianService, VeterinarianService>();
// 1.2. Връзка с SQL Server База данни
builder.Services.AddDbContext<VetClinicContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 1.3. Регистриране на твоите Сървиси (Services)
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IVaccinationService, VaccinationService>(); // <-- Това добавихме последно
builder.Services.AddScoped<IOwnerService, OwnerService>();

// =========================================================
// 2. СГЛОБЯВАНЕ НА ПРИЛОЖЕНИЕТО (BUILD)
// След този ред НЕ можеш да ползваш builder.Services!
// =========================================================
var app = builder.Build();

// =========================================================
// 3. КОНФИГУРАЦИЯ НА HTTP REQUEST PIPELINE
// =========================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Важно за зареждане на CSS, JS и картинки от wwwroot
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Ако ползваш .NET 9, тези редове са стандартни, но UseStaticFiles по-горе е по-важният
// за стандартните папки.
// app.MapStaticAssets(); 

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
// .WithStaticAssets(); // Можеш да го махнеш, ако дава грешка, но в .NET 9 е ОК.

app.Run();