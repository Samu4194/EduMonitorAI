using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class MallaDeLaInscripcion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MallaVersion",
                table: "Inscripciones",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MallaVersion",
                table: "Inscripciones");
        }
    }
}
