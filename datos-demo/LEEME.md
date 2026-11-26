# Datos de demostración

Todos los datos son inventados (correos `@example.test`, cédulas generadas con el algoritmo oficial). La contraseña inicial de
los estudiantes de Moodle es `Estudiante#2026`: es solo para pruebas locales.

## 1. En EduMonitor (docente y paralelo)

```powershell
.\scripts\Datos-Demo.ps1
```

Crea el paralelo A de Software, nivel I, y un docente de demostración asignado a sus cursos. Se puede repetir sin duplicar nada.

## 2. En Moodle (estudiantes)

Entra a http://localhost:8085 como administrador (las claves están en `.secrets/accesos-moodle.json` de la carpeta de Moodle).

1. **Administración del sitio → Usuarios → Cuentas → Subir usuarios** y sube `estudiantes-moodle.csv` con separador **coma** y UTF-8.
   Crea 20 estudiantes y los matricula como *estudiante* en el curso `SOF-101-2026AD-A` (si el nombre corto de tu curso es otro,
   cámbialo en la columna `course1`).
2. Matricula al docente de demostración como **Profesor** en ese curso (Participantes → Matricular usuarios). En Moodle crea antes su
   usuario con el mismo correo que usó en EduMonitor.

## 3. En Moodle (notas)

Como docente del curso: **Calificaciones → Importar → Archivo CSV**, sube `notas-moodle.csv` (separador coma) y marca «Forzar importar».
En el mapeo elige **Número de ID** en «Mapa desde» y en «Mapa a», y asigna cada columna de actividad a su elemento de calificación
(las dos primeras columnas se dejan en «Ignorar»). Si tu aula tiene otras actividades, ajusta los encabezados.

## 4. En EduMonitor (traer todo)

1. Administrador: **Cursos → Vincular Moodle** en la materia (el identificador viene generado) y **Revisar matrícula de Moodle →
   Confirmar matrícula**: las cédulas se toman del Número de ID.
2. Docente: abre el curso y pulsa **Sincronizar con Moodle**.
