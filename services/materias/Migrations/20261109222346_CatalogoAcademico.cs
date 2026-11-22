using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Materias.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoAcademico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La tabla Materias anterior (código, nombre, período y docente por materia) cambia por completo de forma:
            // ahora es el catálogo de la malla curricular. Se reemplaza en lugar de renombrar columnas.
            migrationBuilder.DropTable(
                name: "Materias");

            migrationBuilder.CreateTable(
                name: "Carreras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PrefijoUta = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Modalidad = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Niveles = table.Column<int>(type: "int", nullable: false),
                    HorasTotales = table.Column<int>(type: "int", nullable: false),
                    CreditosTotales = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carreras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CarreraId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodigoUta = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    OrdenNivel = table.Column<int>(type: "int", nullable: false),
                    Unidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HorasContactoDocente = table.Column<int>(type: "int", nullable: true),
                    HorasPracticoExperimental = table.Column<int>(type: "int", nullable: true),
                    HorasAutonomas = table.Column<int>(type: "int", nullable: true),
                    HorasTotales = table.Column<int>(type: "int", nullable: false),
                    Creditos = table.Column<int>(type: "int", nullable: false),
                    CreditosDerivado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Materias_Carreras_CarreraId",
                        column: x => x.CarreraId,
                        principalTable: "Carreras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Periodos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Periodos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Paralelos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodoId = table.Column<int>(type: "int", nullable: false),
                    CarreraId = table.Column<int>(type: "int", nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: false),
                    Letra = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paralelos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Paralelos_Carreras_CarreraId",
                        column: x => x.CarreraId,
                        principalTable: "Carreras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Paralelos_Periodos_PeriodoId",
                        column: x => x.PeriodoId,
                        principalTable: "Periodos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cursos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MateriaId = table.Column<int>(type: "int", nullable: false),
                    ParaleloId = table.Column<int>(type: "int", nullable: false),
                    TeacherId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cursos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cursos_Materias_MateriaId",
                        column: x => x.MateriaId,
                        principalTable: "Materias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cursos_Paralelos_ParaleloId",
                        column: x => x.ParaleloId,
                        principalTable: "Paralelos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carreras_Codigo",
                table: "Carreras",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materias_CarreraId",
                table: "Materias",
                column: "CarreraId");

            migrationBuilder.CreateIndex(
                name: "IX_Materias_Codigo",
                table: "Materias",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materias_CodigoUta",
                table: "Materias",
                column: "CodigoUta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cursos_MateriaId_ParaleloId",
                table: "Cursos",
                columns: new[] { "MateriaId", "ParaleloId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cursos_ParaleloId",
                table: "Cursos",
                column: "ParaleloId");

            migrationBuilder.CreateIndex(
                name: "IX_Paralelos_CarreraId",
                table: "Paralelos",
                column: "CarreraId");

            migrationBuilder.CreateIndex(
                name: "IX_Paralelos_PeriodoId_CarreraId_Nivel_Letra",
                table: "Paralelos",
                columns: new[] { "PeriodoId", "CarreraId", "Nivel", "Letra" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Periodos_Nombre",
                table: "Periodos",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cursos");

            migrationBuilder.DropTable(
                name: "Paralelos");

            migrationBuilder.DropTable(
                name: "Materias");

            migrationBuilder.DropTable(
                name: "Periodos");

            migrationBuilder.DropTable(
                name: "Carreras");

            // Estructura anterior de Materias (los datos no se pueden recuperar).
            migrationBuilder.CreateTable(
                name: "Materias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AcademicPeriod = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TeacherId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materias", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Materias_Code",
                table: "Materias",
                column: "Code",
                unique: true);
        }
    }
}
