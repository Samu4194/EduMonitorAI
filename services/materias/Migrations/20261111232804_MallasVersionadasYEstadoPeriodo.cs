using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Materias.Migrations
{
    /// <inheritdoc />
    public partial class MallasVersionadasYEstadoPeriodo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Materias_CodigoUta",
                table: "Materias");

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Periodos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Preparación");

            migrationBuilder.AddColumn<int>(
                name: "MallaId",
                table: "Paralelos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MallaId",
                table: "Materias",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Mallas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CarreraId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mallas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mallas_Carreras_CarreraId",
                        column: x => x.CarreraId,
                        principalTable: "Carreras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Los datos existentes pasan a la versión 1 de la malla de su carrera: materias y paralelos quedan con ella.
            migrationBuilder.Sql("INSERT INTO Mallas (CarreraId, Version, CreadaEn) SELECT Id, 1, SYSUTCDATETIME() FROM Carreras;");
            migrationBuilder.Sql("UPDATE m SET m.MallaId = ma.Id FROM Materias m JOIN Mallas ma ON ma.CarreraId = m.CarreraId AND ma.Version = 1;");
            migrationBuilder.Sql("UPDATE p SET p.MallaId = ma.Id FROM Paralelos p JOIN Mallas ma ON ma.CarreraId = p.CarreraId AND ma.Version = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Paralelos_MallaId",
                table: "Paralelos",
                column: "MallaId");

            migrationBuilder.CreateIndex(
                name: "IX_Materias_MallaId_CodigoUta",
                table: "Materias",
                columns: new[] { "MallaId", "CodigoUta" },
                unique: true,
                filter: "[CodigoUta] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Mallas_CarreraId_Version",
                table: "Mallas",
                columns: new[] { "CarreraId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Materias_Mallas_MallaId",
                table: "Materias",
                column: "MallaId",
                principalTable: "Mallas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Paralelos_Mallas_MallaId",
                table: "Paralelos",
                column: "MallaId",
                principalTable: "Mallas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materias_Mallas_MallaId",
                table: "Materias");

            migrationBuilder.DropForeignKey(
                name: "FK_Paralelos_Mallas_MallaId",
                table: "Paralelos");

            migrationBuilder.DropTable(
                name: "Mallas");

            migrationBuilder.DropIndex(
                name: "IX_Paralelos_MallaId",
                table: "Paralelos");

            migrationBuilder.DropIndex(
                name: "IX_Materias_MallaId_CodigoUta",
                table: "Materias");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Periodos");

            migrationBuilder.DropColumn(
                name: "MallaId",
                table: "Paralelos");

            migrationBuilder.DropColumn(
                name: "MallaId",
                table: "Materias");

            migrationBuilder.CreateIndex(
                name: "IX_Materias_CodigoUta",
                table: "Materias",
                column: "CodigoUta",
                unique: true);
        }
    }
}
