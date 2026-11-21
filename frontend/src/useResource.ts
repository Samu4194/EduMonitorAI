import { useEffect, useState } from "react";
import { api } from "./api";
// Cada módulo carga su API por separado. Un fallo no bloquea el resto del panel.
export function useResource<T>(path: string | null) {
  const [data, setData] = useState<T[]>([]),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [version, setVersion] = useState(0);
  useEffect(() => {
    let active = true;
    if (!path) {
      setLoading(false);
      setData([]);
      return;
    }
    setLoading(true);
    setError("");
    api<T[]>(path)
      .then((value) => {
        if (active) setData(value);
      })
      .catch((e) => {
        if (active) {
          setData([]);
          setError(e.message);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [path, version]);
  return { data, error, loading, reload: () => setVersion((v) => v + 1) };
}

// Igual que useResource, pero para respuestas que son un solo objeto.
export function useObject<T>(path: string | null) {
  const [data, setData] = useState<T | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(false),
    [version, setVersion] = useState(0);
  useEffect(() => {
    let active = true;
    if (!path) {
      setData(null);
      setError("");
      setLoading(false);
      return;
    }
    setLoading(true);
    setError("");
    api<T>(path)
      .then((value) => {
        if (active) setData(value);
      })
      .catch((e) => {
        if (active) {
          setData(null);
          setError(e.message);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [path, version]);
  return { data, error, loading, reload: () => setVersion((v) => v + 1) };
}
