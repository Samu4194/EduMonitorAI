using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class InscripcionesPorMateria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_CarreraCodigo_Nivel_Paralelo",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Nivel",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Paralelo",
                table: "Students");

            migrationBuilder.CreateTable(
                name: "Inscripciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    Paralelo = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    MateriaCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MateriaNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    BajaEn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inscripciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inscripciones_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Students_CarreraCodigo",
                table: "Students",
                column: "CarreraCodigo");

            migrationBuilder.CreateIndex(
                name: "IX_Inscripciones_CursoId",
                table: "Inscripciones",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_Inscripciones_Nivel_Paralelo_MateriaCodigo",
                table: "Inscripciones",
                columns: new[] { "Nivel", "Paralelo", "MateriaCodigo" });

            migrationBuilder.CreateIndex(
                name: "IX_Inscripciones_StudentId_CursoId",
                table: "Inscripciones",
                columns: new[] { "StudentId", "CursoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inscripciones");

            migrationBuilder.DropIndex(
                name: "IX_Students_CarreraCodigo",
                table: "Students");

            migrationBuilder.AddColumn<int>(
                name: "Nivel",
                table: "Students",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Paralelo",
                table: "Students",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Students_CarreraCodigo_Nivel_Paralelo",
                table: "Students",
                columns: new[] { "CarreraCodigo", "Nivel", "Paralelo" });
        }
    }
}
