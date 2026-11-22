using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class BajaLogicaEstudiantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Students",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BajaEn",
                table: "Students",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "BajaEn",
                table: "Students");
        }
    }
}
