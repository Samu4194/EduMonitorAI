# Instalación completa en otra computadora

Guía para que una persona del equipo tenga **todo** corriendo en su laptop: EduMonitor, Moodle, los datos de demostración y, si quiere, la IA.
Cada computadora tiene sus propias bases de datos: nada de lo que hagas aquí se comparte con las demás.

## Qué necesitas instalar
| Programa | Para qué | Obligatorio |
|---|---|---|
| Docker Desktop (contenedores Linux) | Corre EduMonitor, su base de datos y Moodle | Sí |
| Git | Descargar el proyecto | Sí |
| `MoodleLocal-Portable.zip` (te lo pasa quien lo tenga; no está en Git) | El Moodle con el aula de Física | Sí, si usarás Moodle |
| .NET SDK 10 | Solo para correr las pruebas (`scripts/Test.ps1`) | No |
| Ollama (`winget install Ollama.Ollama`) | IA local | No |

## 1. Descargar y arrancar EduMonitor
```powershell
git clone https://github.com/Samu4194/EduMonitorAI.git
cd EduMonitorAI
git checkout feature/moodle-fuente
```
Abre Docker Desktop, espera a que esté listo y ejecuta **Iniciar EduMonitor.cmd** (doble clic en el Explorador). La primera vez tarda varios minutos.
Cuando diga «EduMonitor está listo», abre http://127.0.0.1:5080. El correo y la contraseña del administrador están en `.local/ACCESO.txt` (o ejecuta **Ver acceso.cmd**).

## 2. Instalar Moodle
Extrae el ZIP en una carpeta llamada `MoodleLocal` **junto a la carpeta del proyecto** (misma carpeta padre), abre PowerShell dentro de ella y ejecuta una sola vez:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Restaurar.ps1
```
Moodle queda en http://localhost:8085. Las claves de `admin.demo`, del docente de Moodle y de los estudiantes están en `MoodleLocal\.secrets\accesos-moodle.json` (guárdalo en privado).

## 3. Conectar EduMonitor con Moodle
Desde la carpeta del proyecto:
```powershell
.\scripts\moodle\Preparar-Lectura.ps1
docker compose up -d --force-recreate --no-deps evaluaciones
```
Crea la cuenta de **solo lectura** que usa EduMonitor y guarda su token en `.local/moodle.env`. Desde esta versión del script esa cuenta puede leer **todos** los cursos
(no solo Física): no hay que tocar permisos a mano. Si Moodle quedó en otra carpeta, usa `-MoodleLocal "C:\ruta\MoodleLocal"`.

## 4. Datos de demostración
```powershell
.\scripts\Datos-Demo.ps1
```
Crea un docente de prueba asignado a Software, nivel I, paralelo A. Después sigue [datos-demo/LEEME.md](../datos-demo/LEEME.md) para cargar estudiantes y notas en Moodle.

Flujo normal con un curso, por ejemplo Física A (`SOF-101-2026AD-A`, que ya viene en el ZIP):
1. **Administrador:** Cursos → Software → Nivel I → Física A → **Vincular Moodle** → **Vincular curso**; luego **Revisar matrícula de Moodle** → **Confirmar matrícula**.
2. **Docente:** Mis cursos → Física → **Sincronizar con Moodle**.
3. Revisa Contenido, Actividades, Calificaciones, Rendimiento y el Asistente.

### Otro curso (por ejemplo Álgebra Lineal)
1. En Moodle crea el curso con el **Número de ID** que muestra EduMonitor en «Vincular Moodle» (por ejemplo `SOF-102-2026AD-A`). Se puede crear a mano o con un CSV
   (Administración del sitio → Cursos → Subir cursos; columnas `shortname,fullname,idnumber`).
2. Copia la estructura de otro curso: Más → Reutilización de curso → Importar (marca «Incluir actividades y recursos»). Comprueba en
   Calificaciones → Configuración Calificaciones que aparezcan **todas** las actividades.
3. Matricula a los estudiantes y al docente (Participantes → Matricular usuarios) e importa las notas (Calificaciones → Importar → CSV).
4. Vuelve a EduMonitor: Vincular, Revisar matrícula, Confirmar y Sincronizar.

## 5. IA (opcional)
Sin IA, el asistente redacta con plantillas. Para activarla:
```powershell
.\scripts\Preparar-IA.ps1 -Modo Ollama   # IA local, sin internet
.\scripts\Preparar-IA.ps1 -Modo Nube     # Gemini: pide TU propia clave gratuita de https://aistudio.google.com/apikey
```
Cada persona usa su propia clave de Gemini (no se comparte ni se sube a Git). El orden por defecto es Gemini primero y la IA local de respaldo (ver [AGENTE.md](AGENTE.md)).

## 6. Pruebas (opcional)
```powershell
.\scripts\Test.ps1
```
Necesita el SDK de .NET. Las pruebas usan el sistema levantado y crean y borran sus propios datos: no las ejecutes mientras alguien usa la demostración.

## Problemas frecuentes
- **«El Gateway no está disponible» o un servicio no responde:** espera unos segundos tras arrancar; si sigue, `docker compose ps`.
- **«En Moodle no existe ningún curso con el Número de ID…»:** el curso no existe en Moodle o su Número de ID no es exactamente el que muestra EduMonitor.
- **«Moodle no dejó leer este curso»:** vuelve a ejecutar `.\scripts\moodle\Preparar-Lectura.ps1` (da a la cuenta de lectura permiso en todo el sitio).
- **«Matrícula ausente o ambigua»:** el curso no tiene estudiantes matriculados todavía.
- **«La matrícula Moodle y las correspondencias locales difieren»:** hay alguien nuevo en Moodle: Vincular Moodle → Revisar matrícula → Confirmar antes de sincronizar.
- **La IA local no responde:** reinicia Ollama. El asistente sigue funcionando con Gemini o con plantillas.
- **No ejecutes `docker compose down -v`:** borra el volumen con la base de datos.
