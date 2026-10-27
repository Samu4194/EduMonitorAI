import { useEffect, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import {
  Users,
  BookOpen,
  GraduationCap,
  LayoutDashboard,
  LogOut,
  ChevronRight,
  ArrowUpRight,
} from "lucide-react";
import { api, setToken, type User } from "./api";
import { useResource } from "./useResource";
import { Brand, ErrorBox } from "./components";
import { AcademicPage } from "./AcademicPages";
import "./style.css";
type Page = "resumen" | "docentes" | "materias" | "estudiantes";
const titles: Record<Page, string> = {
  resumen: "Resumen",
  docentes: "Docentes",
  materias: "Materias",
  estudiantes: "Estudiantes",
};
function App() {
  const [user, setUser] = useState<User | null>(null),
    [page, setPage] = useState<Page>("resumen"),
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
      const result = await api<{ token: string; user: User }>(
        "/auth/login",
        "POST",
        { email: form.get("email"), password: form.get("password") },
      );
      setToken(result.token);
      setUser(result.user);
      setPage("resumen");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function logout() {
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
            <span className="eyebrow">GESTIÓN ACADÉMICA · SPRINT 1</span>
            <h1>
              Más claridad.
              <br /> Más tiempo para <br />
              <em>enseñar.</em>
            </h1>
            <p>Tu comunidad educativa, organizada en un solo lugar.</p>
          </div>
          <span className="story-footer">
            Docentes · Materias · Estudiantes
          </span>
        </section>
        <section className="login-form">
          <div>
            <span className="eyebrow">BIENVENIDO A EDUMONITOR</span>
            <h2>Tu aula empieza aquí.</h2>
            <p className="muted">
              Ingresa con tu cuenta de administrador o docente.
            </p>
            <form onSubmit={login}>
              <label>
                Correo electrónico
                <input
                  type="email"
                  name="email"
                  required
                  maxLength={254}
                  autoComplete="username"
                  placeholder="nombre@institucion.edu"
                />
              </label>
              <label>
                Contraseña
                <input
                  type="password"
                  name="password"
                  required
                  maxLength={128}
                  autoComplete="current-password"
                />
              </label>
              <ErrorBox message={error} />
              <button className="primary full" disabled={busy}>
                {busy ? "Ingresando…" : "Iniciar sesión"}
                <ArrowUpRight size={18} />
              </button>
            </form>
            <p className="muted small">
              ¿Necesitas una cuenta? Solicítala al administrador de tu
              institución.
            </p>
          </div>
        </section>
      </div>
    );
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
          {(
            [
              "resumen",
              ...(admin ? ["docentes"] : []),
              "materias",
              ...(admin ? ["estudiantes"] : []),
            ] as Page[]
          ).map((p) => {
            const Icon = {
              resumen: LayoutDashboard,
              docentes: Users,
              materias: BookOpen,
              estudiantes: GraduationCap,
            }[p];
            return (
              <button
                key={p}
                className={page === p ? "active" : ""}
                onClick={() => {
                  setPage(p);
                  setError("");
                }}
              >
                <Icon size={19} />
                {titles[p]}
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
            <button
              title="Cerrar sesión"
              aria-label="Cerrar sesión"
              disabled={busy}
              onClick={() => void logout()}
            >
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
            <strong>{titles[page]}</strong>
          </div>
          <span className="role-pill">
            {admin ? "Administración" : "Portal docente"}
          </span>
        </header>
        <div className="content">
          <ErrorBox message={error} />
          {page === "resumen" ? (
            <Overview user={user} navigate={setPage} />
          ) : (
            <AcademicPage key={page} kind={page} admin={!!admin} />
          )}
          <footer>
            EduMonitor AI<span>Espacio de gestión académica · Sprint 1</span>
          </footer>
        </div>
      </main>
    </div>
  );
}
function Overview({
  user,
  navigate,
}: {
  user: User;
  navigate: (p: Page) => void;
}) {
  const admin = user.role === "Admin";
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">TU COMUNIDAD, EN PERSPECTIVA</span>
          <h1>Hola, {user.name.split(" ")[0]}.</h1>
          <p className="muted">
            Todo lo que necesitas para organizar el próximo paso.
          </p>
        </div>
      </div>
      <div className="stats">
        {admin && (
          <SummaryCard
            path="docentes"
            label="Docentes"
            click={() => navigate("docentes")}
          />
        )}
        <SummaryCard
          path="materias"
          label={admin ? "Materias" : "Mis materias"}
          click={() => navigate("materias")}
        />
        {admin && (
          <SummaryCard
            path="estudiantes"
            label="Estudiantes"
            click={() => navigate("estudiantes")}
          />
        )}
      </div>
      <section className="guide">
        <span className="eyebrow">UN BUEN COMIENZO</span>
        <h2>Un espacio para tu comunidad educativa.</h2>
        <p>
          {admin
            ? "Registra docentes, crea materias y organiza las matrículas."
            : "Consulta tus materias y los estudiantes matriculados."}
        </p>
        {(admin
          ? [
              "Registra un docente y su cuenta",
              "Crea una materia con su período y asígnala",
              "Registra estudiantes y matricúlalos desde la materia",
            ]
          : ["Abre Mis materias", "Selecciona Ver estudiantes"]
        ).map((x, i) => (
          <div key={x}>
            <span>{String(i + 1).padStart(2, "0")}</span>
            {x}
          </div>
        ))}
      </section>
    </>
  );
}
function SummaryCard({
  path,
  label,
  click,
}: {
  path: string;
  label: string;
  click: () => void;
}) {
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
