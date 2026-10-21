"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { useSWRConfig } from "swr";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { userError, mutate, type EmailStatus, type Monitor, type MonitorInput } from "@/lib/api";
import { invalidatePaths, useRadarQuery } from "@/lib/radar-query";

export function MonitorForm({ monitor }: { monitor?: Monitor }) {
  const router = useRouter();
  const { mutate: revalidate } = useSWRConfig();
  const [name, setName] = useState(monitor?.name ?? "");
  const [url, setUrl] = useState(monitor?.url ?? "");
  const [intervalMinutes, setIntervalMinutes] = useState(monitor?.intervalMinutes ?? 360);
  const [contentSelector, setContentSelector] = useState(monitor?.contentSelector ?? "");
  const [keywords, setKeywords] = useState(monitor?.keywords?.join("\n") ?? "");
  const [emailAlertsEnabled, setEmailAlertsEnabled] = useState(monitor?.emailAlertsEnabled ?? false);
  const emailStatus = useRadarQuery<EmailStatus>("/notifications/email-status");
  const [confirmationSent, setConfirmationSent] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    const input: MonitorInput = { name, url, intervalMinutes,
      contentSelector: contentSelector.trim() || null,
      keywords: keywords.split(/[,\n]/).map(value => value.trim()).filter(Boolean),
      emailAlertsEnabled };
    try {
      const saved = await mutate<Monitor>(monitor ? `/monitors/${monitor.id}` : "/monitors", monitor ? "PUT" : "POST", input);
      await invalidatePaths(revalidate, ["/dashboard", "/monitors", ...(monitor ? [`/monitors/${monitor.id}`] : [])]);
      router.push(`/monitors/${saved.id}`);
    } catch (cause) { setError(userError(cause, "Não foi possível salvar o monitoramento.")); }
    finally { setBusy(false); }
  }
  return <form className="max-w-xl space-y-5" onSubmit={submit}>
    <label className="block text-sm font-medium">Nome<Input className="mt-2" maxLength={120} required value={name} onChange={event => setName(event.target.value)} placeholder="Ex.: Novidades do produto" /></label>
    <label className="block text-sm font-medium">URL pública<Input className="mt-2" type="url" required value={url} onChange={event => setUrl(event.target.value)} placeholder="https://exemplo.com/pagina" /></label>
    <label className="block text-sm font-medium">Frequência<select className="mt-2 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm" value={intervalMinutes} onChange={event => setIntervalMinutes(Number(event.target.value))}><option value={60}>A cada 1 hora</option><option value={360}>A cada 6 horas</option><option value={1440}>A cada 24 horas</option></select></label>
    <label className="block text-sm font-medium">Seletor CSS (opcional)<Input className="mt-2" maxLength={160} value={contentSelector} onChange={event => setContentSelector(event.target.value)} placeholder="main, .noticias ou #avisos" /></label>
    <label className="block text-sm font-medium">Palavras-chave para alertas (opcional)<textarea className="mt-2 min-h-24 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm" value={keywords} onChange={event => setKeywords(event.target.value)} placeholder="Uma expressão por linha, até 10" /><span className="mt-1 block text-xs text-slate-500">A mudança fica no histórico mesmo quando nenhuma palavra corresponde.</span></label>
    <div className="space-y-2 text-sm"><label className="flex items-center gap-2"><input type="checkbox" checked={emailAlertsEnabled} onChange={event => setEmailAlertsEnabled(event.target.checked)} /> Alertas por e-mail</label>
      {emailStatus.data && !emailStatus.data.available && <p className="text-slate-500">Canal de e-mail indisponível: configure o SMTP para enviar alertas.</p>}
      {emailStatus.data?.available && !emailStatus.data.confirmed && <div><p className="text-slate-500">Confirme o e-mail da conta antes de receber alertas.</p><Button type="button" variant="outline" onClick={async () => { try { await mutate<void>("/notifications/request-email-confirmation", "POST"); setConfirmationSent(true); } catch (cause) { setError(userError(cause, "Falha ao solicitar confirmação.")); } }}>{confirmationSent ? "Mensagem solicitada" : "Enviar confirmação"}</Button></div>}
    </div>
    {monitor && <p className="text-sm text-slate-500">Alterar a URL ou o seletor inicia uma nova linha de base. O histórico anterior permanece disponível.</p>}
    {error && <p className="text-sm text-red-700" role="alert">{error}</p>}
    <div className="flex gap-3"><Button disabled={busy}>{busy ? "Salvando..." : monitor ? "Salvar alterações" : "Criar monitoramento"}</Button><Button type="button" variant="outline" onClick={() => router.back()}>Cancelar</Button></div>
  </form>;
}
