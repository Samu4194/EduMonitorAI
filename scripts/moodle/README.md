# Aula de Física

En la instalación local, Moodle publica su servicio REST en el puerto 8085 y EduMonitor en el 5080. Evaluaciones llega a Moodle mediante `host.docker.internal` y envía la cabecera `Host: localhost:8085` que espera esta instalación.

## Configurar la lectura

Desde la raíz de EduMonitor, ejecuta lo siguiente si necesitas preparar de nuevo el servicio de lectura para el aula `SOF-101-2026AD-A`.

```powershell
.\scripts\moodle\Preparar-Lectura.ps1
docker compose -f compose.yaml up -d --build --no-deps estudiantes evaluaciones gateway frontend
```

El script reutiliza la cuenta técnica y su servicio de cuatro funciones REST. El token se guarda en `.local/moodle.env`, excluido de Git. El contenedor Evaluaciones es el único que recibe este archivo. La instalación que usa SQL Server Express emplea `compose.express.yaml` en lugar de `compose.yaml`.

## Vincular y sincronizar

1. Como administrador, abre **Cursos**, selecciona **Software → Nivel I → Física, paralelo A** y pulsa **Vincular Moodle**. Introduce `SOF-101-2026AD-A`. Revisa la cédula que corresponde a cada identificador de Moodle y pulsa **Confirmar matrícula**. La opción **Consultar matrícula vinculada** permite revisar el vínculo posteriormente.
2. Como docente asignado, abre **Mis cursos → Física A → Sincronizar con Moodle**. Consulta las notas en **Calificaciones** y los promedios en **Rendimiento académico**. Una nota vacía conserva el valor local y aparece en el resumen de conflictos. Un cero explícito se registra como cero.
3. Los identificadores de sección, usuario y actividad proceden de Moodle. Las temáticas del aula se reconocen por sus encabezados numerados. La configuración de Evaluaciones relaciona la APE de gráficos con movimiento rectilíneo y la APE de balance energético con trabajo y energía mediante sus códigos estables.

Las pruebas de contrato se ejecutan con `dotnet test tests/EduMonitor.IntegrationTests --filter FullyQualifiedName~MoodleContratoTests`.
