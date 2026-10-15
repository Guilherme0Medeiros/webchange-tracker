"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { useSWRConfig } from "swr";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { mutate, type Monitor, type MonitorInput } from "@/lib/api";
import { invalidatePaths } from "@/lib/radar-query";

export function MonitorForm({ monitor }: { monitor?: Monitor }) {
  const router = useRouter();
  const { mutate: revalidate } = useSWRConfig();
  const [name, setName] = useState(monitor?.name ?? "");
  const [url, setUrl] = useState(monitor?.url ?? "");
  const [intervalMinutes, setIntervalMinutes] = useState(monitor?.intervalMinutes ?? 360);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    const input: MonitorInput = { name, url, intervalMinutes };
    try {
      const saved = await mutate<Monitor>(monitor ? `/monitors/${monitor.id}` : "/monitors", monitor ? "PUT" : "POST", input);
      await invalidatePaths(revalidate, ["/dashboard", "/monitors", ...(monitor ? [`/monitors/${monitor.id}`] : [])]);
      router.push(`/monitors/${saved.id}`);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Não foi possível salvar o monitoramento."); }
    finally { setBusy(false); }
  }
  return <form className="max-w-xl space-y-5" onSubmit={submit}><label className="block text-sm font-medium">Nome<Input className="mt-2" maxLength={120} required value={name} onChange={event => setName(event.target.value)} placeholder="Ex.: Novidades do produto" /></label><label className="block text-sm font-medium">URL pública<Input className="mt-2" type="url" required value={url} onChange={event => setUrl(event.target.value)} placeholder="https://exemplo.com/pagina" /></label><label className="block text-sm font-medium">Frequência<select className="mt-2 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm" value={intervalMinutes} onChange={event => setIntervalMinutes(Number(event.target.value))}><option value={60}>A cada 1 hora</option><option value={360}>A cada 6 horas</option><option value={1440}>A cada 24 horas</option></select></label>{monitor && <p className="text-sm text-slate-500">Alterar a URL inicia uma nova linha de base. O histórico anterior permanece disponível.</p>}{error && <p className="text-sm text-red-700" role="alert">{error}</p>}<div className="flex gap-3"><Button disabled={busy}>{busy ? "Salvando..." : monitor ? "Salvar alterações" : "Criar monitoramento"}</Button><Button type="button" variant="outline" onClick={() => router.back()}>Cancelar</Button></div></form>;
}
