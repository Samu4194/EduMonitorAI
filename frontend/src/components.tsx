import { useEffect, useRef, type ReactNode } from "react";
import { BookOpen, GraduationCap, X, RefreshCw, Check } from "lucide-react";
export function Brand() {
  return (
    <div className="brand">
      <span>
        <GraduationCap size={23} />
      </span>
      EduMonitor <sup>AI</sup>
    </div>
  );
}
export function ErrorBox({
  message,
  retry,
}: {
  message: string;
  retry?: () => void;
}) {
  return message ? (
    <div className="error" role="alert">
      <span>{message}</span>
      {retry && (
        <button onClick={retry}>
          <RefreshCw size={16} />
          Reintentar
        </button>
      )}
    </div>
  ) : null;
}
export function Empty({ text }: { text: string }) {
  return (
    <div className="empty">
      <BookOpen size={28} />
      <h3>{text}</h3>
    </div>
  );
}
export function Modal({
  title,
  close,
  children,
  busy = false,
}: {
  title: string;
  close: () => void;
  children: ReactNode;
  busy?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  // Si ya se escribió algo, la tecla Esc no cierra el formulario: algunos navegadores (p. ej. Brave) la envían también al cerrar la lista
  // de autocompletado del correo, y se perdería lo que el usuario llevaba escrito. Se cierra con «Cancelar» o con la X.
  const editado = useRef(false);
  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal();
    return () => dialog?.close();
  }, []);
  return (
    <dialog
      ref={ref}
      onInput={() => (editado.current = true)}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy && !editado.current) close();
      }}
      aria-label={title}
    >
      <div className="modal-heading">
        <h2>{title}</h2>
        <button aria-label="Cerrar" disabled={busy} onClick={close}>
          <X size={20} />
        </button>
      </div>
      {children}
    </dialog>
  );
}

// Aviso breve que confirma una acción y se cierra solo.
export function Toast({ message, close, duration = 6000 }: { message: string; close: () => void; duration?: number }) {
  useEffect(() => {
    if (!message) return;
    const espera = setTimeout(close, duration);
    return () => clearTimeout(espera);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [message, duration]);
  return message ? (
    <div className="toast" role="status" aria-live="polite">
      <Check size={18} />
      <span>{message}</span>
      <button aria-label="Cerrar el aviso" onClick={close}>
        <X size={16} />
      </button>
    </div>
  ) : null;
}
