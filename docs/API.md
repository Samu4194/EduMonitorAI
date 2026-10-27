# Contratos REST del Sprint 1

El navegador utiliza el origen `http://127.0.0.1:5080`. Nginx envía `/api` a YARP. El Gateway solo enruta; los tres servicios validan JWT y permisos.

En rutas protegidas: `Authorization: Bearer <token>`. Ningún endpoint de listado o detalle devuelve hashes de contraseñas.

| Método | Ruta | Permiso | Servicio |
|---|---|---|---|
| POST | `/api/auth/login` | Público, limitado por intentos | Usuarios |
| GET | `/api/auth/me` | Autenticado | Usuarios |
| GET | `/api/auth/session` | Autenticado; verifica vigencia | Usuarios |
| POST | `/api/auth/logout` | Autenticado | Usuarios |
| GET / POST | `/api/docentes` | Administrador | Usuarios |
| GET / PUT | `/api/docentes/{id}` | Administrador | Usuarios |
| GET | `/api/materias` | Administrador: todas; docente: asignadas | Materias |
| GET | `/api/materias/{id}` | Administrador o docente asignado | Materias |
| POST | `/api/materias` | Administrador | Materias |
| PUT | `/api/materias/{id}` | Administrador | Materias |
| PUT | `/api/materias/{id}/docente` | Administrador | Materias |
| GET / POST | `/api/estudiantes` | Administrador | Estudiantes |
| GET / PUT | `/api/estudiantes/{id}` | Administrador | Estudiantes |
| POST | `/api/matriculas` | Administrador | Estudiantes |
| GET | `/api/matriculas/materia/{id}` | Administrador o docente asignado | Estudiantes |

No hay rutas DELETE. Los estudiantes no tienen cuentas de acceso.

## Cuerpos de solicitud

Login:

```json
{"email":"correo de la cuenta","password":"contraseña de la cuenta"}
```

Devuelve `token`, `expiresAt` y `user: {id,name,email,role}`.

Docente:

```json
{"name":"Nombre completo","email":"docente@example.test","password":"una contraseña de al menos 10 caracteres"}
```

En una edición, `password:null` conserva la contraseña. Las contraseñas tienen un máximo de 128 caracteres. El correo es único entre todos los usuarios y se normaliza a minúsculas.

Materia:

```json
{"code":"IS-01","name":"Ingeniería de Software","academicPeriod":"Octubre 2026 – Febrero 2027","teacherId":null}
```

Asignación posterior: `{"teacherId":7}`. El identificador debe corresponder a un docente real de Usuarios. Un administrador no puede asignarse como docente. En PUT de la materia se envía el registro completo, incluido el docente o `null`.

Estudiante:

```json
{"studentCode":"EST-01","firstNames":"Nombres","lastNames":"Apellidos"}
```

Matrícula: `{"materiaId":1,"studentId":1}`. Ambos identificadores se validan antes de guardar. La clave compuesta `(MateriaId,StudentId)` impide duplicados incluso ante solicitudes concurrentes.

## Errores

400: campos inválidos o incompletos. 401: credenciales o token no válidos. 403: operación administrativa prohibida. 404: entidad inexistente o materia no autorizada. 409: código, correo o matrícula duplicados. 429: límite de login. 503: dependencia o servicio no disponible. 500: fallo inesperado, con identificador de registro técnico.

Las validaciones de ASP.NET Core devuelven `errors`; los errores controlados devuelven `message` y, cuando corresponde, `traceId`. React soporta ambos formatos.

## Health checks internos

Cada API publica `/health/live` (proceso vivo) y `/health/ready` (conexión a su base). El Gateway tiene ambas rutas para su propio proceso; no falla su health check porque una API esté detenida. Nginx publica `/health` para la interfaz.

Ejemplo sin publicar puertos adicionales:

```powershell
docker compose exec -T materias curl -fsS http://localhost:8080/health/ready
```

Las APIs se comunican por HTTP autenticado dentro de la red de Docker. Este despliegue es local; un despliegue fuera del equipo necesita HTTPS y gestión de secretos adecuada.
