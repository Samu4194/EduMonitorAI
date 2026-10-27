import { useEffect, useRef, type ReactNode } from "react";
import { BookOpen, GraduationCap, X, RefreshCw } from "lucide-react";
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
  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal();
    return () => dialog?.close();
  }, []);
  return (
    <dialog
      ref={ref}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) close();
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
