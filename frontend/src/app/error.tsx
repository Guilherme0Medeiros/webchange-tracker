"use client";

import { useEffect } from "react";
import { Button } from "@/components/ui/button";

export default function ErrorPage({ error, reset }: { error: Error; reset: () => void }) {
  useEffect(() => { console.error("Page rendering failed", error); }, [error]);
  return <main className="mx-auto flex min-h-screen max-w-md flex-col items-center justify-center px-6 text-center">
    <h1 className="text-2xl font-semibold">Não foi possível carregar a página</h1>
    <p className="mt-3 text-slate-600">Tente novamente. Se o problema continuar, volte mais tarde.</p>
    <Button className="mt-6" onClick={reset}>Tentar novamente</Button>
  </main>;
}
