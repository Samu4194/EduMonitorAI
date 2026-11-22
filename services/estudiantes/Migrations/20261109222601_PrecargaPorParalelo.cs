using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class PrecargaPorParalelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Se elimina la matrícula: el estudiante pertenece a un paralelo y llega por precarga.
            // La tabla Students cambia por completo (cédula, correo, carrera, nivel y paralelo), así que se reemplaza.
            migrationBuilder.DropTable(
                name: "Enrollments");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Cedula = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Apellidos = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nombres = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CarreraCodigo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    Paralelo = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Students_CarreraCodigo_Nivel_Paralelo",
                table: "Students",
                columns: new[] { "CarreraCodigo", "Nivel", "Paralelo" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_Cedula",
                table: "Students",
                column: "Cedula",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Students");

            // Estructura anterior (los datos no se pueden recuperar).
            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FirstNames = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastNames = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Enrollments",
                columns: table => new
                {
                    MateriaId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => new { x.MateriaId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_Enrollments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_StudentId",
                table: "Enrollments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_StudentCode",
                table: "Students",
                column: "StudentCode",
                unique: true);
        }
    }
}
