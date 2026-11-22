import { useEffect, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import { Users, BookOpen, GraduationCap, Library, LayoutDashboard, LogOut, ChevronRight, ArrowUpRight } from "lucide-react";
import { api, setToken, type User } from "./api";
import { useResource } from "./useResource";
import { Brand, ErrorBox } from "./components";
import { DocentesPage } from "./DocentesPage";
import { CatalogoPage } from "./CatalogoPage";
import { CursosPage } from "./CursosPage";
import { EstudiantesPage } from "./EstudiantesPage";
import { MisCursos } from "./CursoAula";
import { confirmarSalida } from "./AulaComun";
import "./style.css";

// El administrador gestiona la estructura académica. El docente trabaja siempre dentro de sus cursos:
// el contenido, las actividades, las calificaciones y el rendimiento están dentro de cada curso, no como módulos sueltos.
type Page = "resumen" | "docentes" | "carreras" | "cursos" | "estudiantes";
const titles: Record<Page, string> = {
  resumen: "Resumen",
  docentes: "Docentes",
  carreras: "Carreras y mallas",
  cursos: "Cursos",
  estudiantes: "Estudiantes",
};
const iconos = { resumen: LayoutDashboard, docentes: Users, carreras: Library, cursos: BookOpen, estudiantes: GraduationCap };
const titleOf = (page: Page, admin: boolean) => (page === "cursos" && !admin ? "Mis cursos" : page === "carreras" && !admin ? "Mallas curriculares" : titles[page]);

function App() {
  const [user, setUser] = useState<User | null>(null),
    [page, setPage] = useState<Page>("resumen"),
    // Cambia cada vez que se pulsa el menú: así «Mis cursos» vuelve a la lista aunque ya se estuviera dentro de un curso.
    [visita, setVisita] = useState(0),
    // Dónde está el docente dentro de la página (el curso abierto), para la ruta de la cabecera.
    [detalle, setDetalle] = useState<string | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const admin = user?.role === "Admin";
  useEffect(() => {
    const expired = () => {
      setToken("");
      setUser(null);
      setError("Tu sesión terminó. Vuelve a iniciar sesión.");
    };
    window.addEventListener("session-expired", expired);
    return () => window.removeEventListener("session-expired", expired);
  }, []);
  async function login(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const form = new FormData(e.currentTarget);
    setBusy(true);
    setError("");
    try {
      const result = await api<{ token: string; user: User }>("/auth/login", "POST", { email: form.get("email"), password: form.get("password") });
      setToken(result.token);
      setUser(result.user);
      setDetalle(null);
      // El docente entra directo a sus cursos; el administrador, al resumen.
      setPage(result.user.role === "Admin" ? "resumen" : "cursos");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function logout() {
    if (!confirmarSalida()) return;
    setBusy(true);
    try {
      await api("/auth/logout", "POST");
      setToken("");
      setUser(null);
      setError("");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (!user)
    return (
      <div className="login">
        <section className="login-story">
          <Brand />
          <div>
            <span className="eyebrow">GESTIÓN ACADÉMICA</span>
            <h1>
              Más claridad.
              <br /> Más tiempo para <br />
              <em>enseñar.</em>
            </h1>
            <p>Tu comunidad educativa, organizada en un solo lugar.</p>
          </div>
          <span className="story-footer">Carreras · Docentes · Estudiantes</span>
        </section>
        <section className="login-form">
          <div>
            <span className="eyebrow">BIENVENIDO A EDUMONITOR</span>
            <h2>Tu aula empieza aquí.</h2>
            <p className="muted">Ingresa con tu cuenta de administrador o docente.</p>
            <form onSubmit={login}>
              <label>
                Correo electrónico
                <input type="email" name="email" required maxLength={254} autoComplete="username" placeholder="nombre@institucion.edu" />
              </label>
              <label>
                Contraseña
                <input type="password" name="password" required maxLength={128} autoComplete="current-password" />
              </label>
              <ErrorBox message={error} />
              <button className="primary full" disabled={busy}>
                {busy ? "Ingresando…" : "Iniciar sesión"}
                <ArrowUpRight size={18} />
              </button>
            </form>
            <p className="muted small">¿Necesitas una cuenta? Solicítala al administrador de tu institución.</p>
          </div>
        </section>
      </div>
    );
  const menu: Page[] = admin ? ["resumen", "docentes", "carreras", "cursos", "estudiantes"] : ["cursos", "carreras"];
  return (
    <div className="app">
      <aside>
        <Brand />
        <div className="workspace">
          <span className="workspace-icon">E</span>
          <div>
            Espacio académico<small>EduMonitor AI</small>
          </div>
          <ChevronRight size={16} />
        </div>
        <span className="nav-caption">PRINCIPAL</span>
        <nav>
          {menu.map((p) => {
            const Icon = iconos[p];
            return (
              <button
                key={p}
                className={page === p ? "active" : ""}
                onClick={() => {
                  if (!confirmarSalida()) return;
                  setPage(p);
                  setVisita((v) => v + 1);
                  setDetalle(null);
                  setError("");
                }}
              >
                <Icon size={19} />
                {titleOf(p, !!admin)}
              </button>
            );
          })}
        </nav>
        <div className="sidebar-bottom">
          <div className="phase">
            <span className="dot" />
            <small>Gestión académica</small>
          </div>
          <div className="profile">
            <span className="avatar">{user.name[0]}</span>
            <div>
              {user.name}
              <small>{admin ? "Administrador" : "Docente"}</small>
            </div>
            <button title="Cerrar sesión" aria-label="Cerrar sesión" disabled={busy} onClick={() => void logout()}>
              <LogOut size={17} />
            </button>
          </div>
        </div>
      </aside>
      <main>
        <header>
          <div>
            Espacio académico
            <ChevronRight size={14} />
            {detalle ? titleOf(page, !!admin) : <strong>{titleOf(page, !!admin)}</strong>}
            {detalle && (
              <>
                <ChevronRight size={14} />
                <strong>{detalle}</strong>
              </>
            )}
          </div>
          <span className="role-pill">{admin ? "Administración" : "Portal docente"}</span>
        </header>
        <div className="content">
          <ErrorBox message={error} />
          {page === "resumen" ? (
            <Overview user={user} navigate={setPage} />
          ) : page === "docentes" ? (
            <DocentesPage key={visita} />
          ) : page === "carreras" ? (
            <CatalogoPage key={visita} admin={!!admin} />
          ) : page === "cursos" ? (
            admin ? (
              <CursosPage key={visita} />
            ) : (
              <MisCursos key={visita} detalle={setDetalle} />
            )
          ) : (
            <EstudiantesPage key={visita} />
          )}
          <footer>
            EduMonitor AI<span>Espacio de gestión académica</span>
          </footer>
        </div>
      </main>
    </div>
  );
}

// Resumen del administrador. El docente no lo tiene: entra directo a sus cursos.
function Overview({ user, navigate }: { user: User; navigate: (p: Page) => void }) {
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">TU COMUNIDAD, EN PERSPECTIVA</span>
          <h1>Hola, {user.name.split(" ")[0]}.</h1>
          <p className="muted">Todo lo que necesitas para organizar el próximo paso.</p>
        </div>
      </div>
      <div className="stats">
        <SummaryCard path="docentes" label="Docentes" click={() => navigate("docentes")} />
        <SummaryCard path="carreras" label="Carreras" click={() => navigate("carreras")} />
        <SummaryCard path="estudiantes" label="Estudiantes" click={() => navigate("estudiantes")} />
      </div>
      <section className="guide">
        <span className="eyebrow">UN BUEN COMIENZO</span>
        <h2>Un espacio para tu comunidad educativa.</h2>
        <p>Consulta las mallas, registra docentes, asigna cursos y carga a los estudiantes por materia y paralelo.</p>
        {[
          "Revisa las carreras y sus mallas curriculares",
          "Registra a tus docentes y las carreras a las que pertenecen",
          "Asigna un docente a cada curso de un paralelo",
          "Carga la lista de estudiantes de cada materia desde un CSV",
        ].map((x, i) => (
          <div key={x}>
            <span>{String(i + 1).padStart(2, "0")}</span>
            {x}
          </div>
        ))}
      </section>
    </>
  );
}
function SummaryCard({ path, label, click }: { path: string; label: string; click: () => void }) {
  const r = useResource<{ id: number }>(`/${path}`);
  return (
    <section className="stat">
      <span>
        {label}
        <BookOpen size={20} />
      </span>
      <strong>{r.loading ? "…" : r.error ? "—" : r.data.length}</strong>
      {r.error ? (
        <ErrorBox message={r.error} retry={r.reload} />
      ) : (
        <button className="text-button" onClick={click}>
          Ver detalle
          <ArrowUpRight size={15} />
        </button>
      )}
    </section>
  );
}
createRoot(document.getElementById("root")!).render(<App />);
