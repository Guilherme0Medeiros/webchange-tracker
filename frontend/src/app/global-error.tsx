"use client";

import { useEffect } from "react";

export default function GlobalError({ error, reset }: { error: Error; reset: () => void }) {
  useEffect(() => { console.error("Application rendering failed", error); }, [error]);
  return <html lang="pt-BR"><body>
    <main style={{ maxWidth: 480, margin: "20vh auto", padding: 24, textAlign: "center", fontFamily: "sans-serif" }}>
      <h1>Não foi possível abrir o WebChange Radar</h1>
      <p>Tente novamente. Se o problema continuar, volte mais tarde.</p>
      <button onClick={reset}>Tentar novamente</button>
    </main>
  </body></html>;
}
