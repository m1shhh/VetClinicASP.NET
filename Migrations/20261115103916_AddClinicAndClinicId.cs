using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetClinicASP.NET.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicAndClinicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (a) Clinics first — principal table of the six FKs below.
            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    ClinicID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BillingStatus = table.Column<int>(type: "int", nullable: false),
                    BillingPaidUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BillingProblem = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentGivenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentTextVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.ClinicID);
                });

            // (b) ClinicID as nullable first — so the backfill below can run before NOT NULL.
            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "Appointments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "MedicalRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "Owners",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "Patients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "Vaccinations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicID",
                table: "Veterinarians",
                type: "int",
                nullable: true);

            // (c) Backfill: pilot clinic (explicit Id=1) + bind every existing row to it.
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT Clinics ON;
INSERT INTO Clinics (ClinicID, Name, CreatedAtUtc, IsActive, BillingStatus) VALUES (1, N'Пилотна клиника', SYSUTCDATETIME(), 1, 1);
SET IDENTITY_INSERT Clinics OFF;
UPDATE Owners SET ClinicID = 1;
UPDATE Patients SET ClinicID = 1;
UPDATE Appointments SET ClinicID = 1;
UPDATE MedicalRecords SET ClinicID = 1;
UPDATE Vaccinations SET ClinicID = 1;
UPDATE Veterinarians SET ClinicID = 1;
");

            // (d) Only after the backfill: ClinicID becomes non-nullable.
            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "Appointments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "MedicalRecords",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "Owners",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "Patients",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "Vaccinations",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicID",
                table: "Veterinarians",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // (e) Index + FK (Restrict — deletion goes through ErasureService, not DB cascade).
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicID",
                table: "Appointments",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_ClinicID",
                table: "MedicalRecords",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_ClinicID",
                table: "Owners",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicID",
                table: "Patients",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_ClinicID",
                table: "Vaccinations",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Veterinarians_ClinicID",
                table: "Veterinarians",
                column: "ClinicID");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Clinic",
                table: "Appointments",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalRecords_Clinic",
                table: "MedicalRecords",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Owners_Clinic",
                table: "Owners",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinic",
                table: "Patients",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vaccinations_Clinic",
                table: "Vaccinations",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Veterinarians_Clinic",
                table: "Veterinarians",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ClinicID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Clinic",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_MedicalRecords_Clinic",
                table: "MedicalRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Owners_Clinic",
                table: "Owners");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinic",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Vaccinations_Clinic",
                table: "Vaccinations");

            migrationBuilder.DropForeignKey(
                name: "FK_Veterinarians_Clinic",
                table: "Veterinarians");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicID",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_MedicalRecords_ClinicID",
                table: "MedicalRecords");

            migrationBuilder.DropIndex(
                name: "IX_Owners_ClinicID",
                table: "Owners");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicID",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Vaccinations_ClinicID",
                table: "Vaccinations");

            migrationBuilder.DropIndex(
                name: "IX_Veterinarians_ClinicID",
                table: "Veterinarians");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "ClinicID",
                table: "Veterinarians");

            migrationBuilder.DropTable(
                name: "Clinics");
        }
    }
}
