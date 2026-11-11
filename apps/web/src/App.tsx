import { useEffect, useState } from "react";

type Health = { status: string; database?: string };

export default function App() {
  const [health, setHealth] = useState<Health | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/health/ready")
      .then((r) => r.json())
      .then(setHealth)
      .catch((e) => setError(String(e)));
  }, []);

  return (
    <main style={{ fontFamily: "system-ui", padding: 24 }}>
      <h1>Job Search &amp; Application Tracker</h1>
      <p>API: {error ? `unreachable (${error})` : health ? `${health.status}, database ${health.database}` : "checking…"}</p>
    </main>
  );
}
