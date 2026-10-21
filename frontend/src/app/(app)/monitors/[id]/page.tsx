"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useSWRConfig } from "swr";
import Link from "next/link";
import { ArrowLeft, ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState, ErrorState, Loading, Pagination } from "@/components/feedback";
import { RefreshButton } from "@/components/refresh-button";
import { MonitorSchedule } from "@/components/monitor-schedule";
import { userError, mutate, query, type Change, type Check, type Monitor, type Page } from "@/lib/api";
import { invalidatePaths, useRadarQuery } from "@/lib/radar-query";
import { dateTime } from "@/lib/utils";

const PAGE_SIZE = 10;
const errorLabels: Record<string, string> = {
  selector_no_match: "O seletor CSS não encontrou elementos na página.",
  unsafe_destination: "A URL não aponta para um endereço público permitido.",
  dns_error: "Não foi possível localizar o endereço da página.",
  connection_error: "Não foi possível conectar à página.",
  timeout: "A página demorou demais para responder.",
  http_error: "A página retornou uma resposta de erro.",
  fetch_error: "Não foi possível verificar a página agora.",
  invalid_url: "A URL do monitoramento é inválida.",
  too_many_redirects: "A página redirecionou muitas vezes.",
  invalid_redirect: "A página retornou um redirecionamento inválido.",
  unsafe_redirect: "A página redirecionou para um endereço não permitido.",
  invalid_content_type: "A página não retornou conteúdo HTML.",
  response_too_large: "A página excede o tamanho permitido para verificação.",
  invalid_charset: "Não foi possível ler a codificação da página.",
};
const errorLabel = (code: string) => errorLabels[code] ?? "Não foi possível verificar a página agora.";
export default function MonitorPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { mutate: revalidate } = useSWRConfig();
  const [checkPage, setCheckPage] = useState(1);
  const [changePage, setChangePage] = useState(1);
  const [result, setResult] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const period = { from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined, to: to ? new Date(`${to}T23:59:59`).toISOString() : undefined };
  const monitorQuery = useRadarQuery<Monitor>(`/monitors/${id}`);
  const checksQuery = useRadarQuery<Page<Check>>(`/monitors/${id}/checks?${query({ page: checkPage, pageSize: PAGE_SIZE, result: result || undefined, ...period })}`);
  const changesQuery = useRadarQuery<Page<Change>>(`/monitors/${id}/changes?${query({ page: changePage, pageSize: PAGE_SIZE, ...period })}`);
  const monitor = monitorQuery.data;
  const checks = checksQuery.data;
  const changes = changesQuery.data;
  const error = actionError || monitorQuery.error?.message || checksQuery.error?.message || changesQuery.error?.message;
  const refreshing = monitorQuery.isValidating || checksQuery.isValidating || changesQuery.isValidating;
  const refresh = () => { setActionError(""); void Promise.all([monitorQuery.mutate(), checksQuery.mutate(), changesQuery.mutate()]); };

  async function action(kind: "check" | "status" | "delete") {
    if (!monitor) return;
    if (kind === "delete" && !window.confirm("Excluir este monitoramento e todo o seu histórico?")) return;
    setBusy(true); setActionError(""); setNotice("");
    try {
      if (kind === "delete") {
        await mutate<void>(`/monitors/${id}`, "DELETE");
        router.replace("/dashboard");
        await invalidatePaths(revalidate, ["/dashboard", "/monitors", "/notifications"]);
        return;
      }
      if (kind === "check") {
        await mutate(`/monitors/${id}/checks`, "POST");
        setNotice("Verificação enfileirada. O resultado aparecerá após o processamento agendado.");
      } else {
        const updated = await mutate<Monitor>(`/monitors/${id}/status`, "PATCH", { status: monitor.status === "active" ? "paused" : "active" });
        setNotice(updated.status === "active" ? "Monitoramento reativado." : "Monitoramento pausado.");
      }
      await invalidatePaths(revalidate, kind === "check" ? ["/dashboard", `/monitors/${id}`, `/monitors/${id}/checks`, `/monitors/${id}/changes`] : ["/dashboard", "/monitors", `/monitors/${id}`]);
    } catch (cause) { setActionError(userError(cause, "A ação não pôde ser concluída.")); }
    finally { setBusy(false); }
  }

  return <>
    <Link className="mb-5 inline-flex items-center gap-2 text-sm text-slate-500 hover:text-slate-900" href="/dashboard"><ArrowLeft className="h-4 w-4" /> Voltar ao painel</Link>
    {!monitor ? <>{error ? <ErrorState message={error} retry={refresh} /> : <Loading />}</> : <>
      <div className="flex flex-wrap items-start justify-between gap-4"><div className="min-w-0"><h1 className="text-2xl font-semibold">{monitor.name}</h1><a href={monitor.url} target="_blank" rel="noopener noreferrer" className="mt-1 inline-flex max-w-full items-center gap-1 truncate text-sm text-slate-500 hover:underline">{monitor.url}<ExternalLink className="h-3 w-3 shrink-0" /></a><p className="mt-2 text-sm text-slate-500">{monitor.status === "active" ? "Ativo" : "Pausado"} &middot; A cada {monitor.intervalMinutes === 60 ? "1 hora" : monitor.intervalMinutes === 360 ? "6 horas" : "24 horas"}</p><div className="mt-2"><MonitorSchedule monitor={monitor} /></div></div><div className="flex flex-wrap gap-2"><RefreshButton refreshing={refreshing} onClick={refresh} /><Button variant="outline" disabled={busy || monitor.status !== "active"} onClick={() => void action("check")}>Verificar agora</Button><Button variant="outline" disabled={busy} onClick={() => void action("status")}>{monitor.status === "active" ? "Pausar" : "Reativar"}</Button><Button variant="outline" asChild><Link href={`/monitors/${id}/edit`}>Editar</Link></Button><Button variant="destructive" disabled={busy} onClick={() => void action("delete")}>Excluir</Button></div></div>
      <p className="mt-3 text-sm text-slate-500">{monitor.contentSelector ? `Área: ${monitor.contentSelector}` : "Página inteira"} · {monitor.keywords?.length ? `Alertas com: ${monitor.keywords.join(", ")}` : "Alertas para qualquer mudança"} · E-mail {monitor.emailAlertsEnabled ? "ativado" : "desativado"}</p>
      {notice && <p role="status" className="mt-5 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">{notice}</p>}
      {error && <div className="mt-5"><ErrorState message={error} retry={refresh} /></div>}
      <div className="mt-8 grid gap-8 xl:grid-cols-2"><section><div className="mb-4 flex flex-wrap items-end justify-between gap-3"><h2 className="text-lg font-semibold">Verificações</h2><label className="text-xs text-slate-500">Resultado <select className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" value={result} onChange={event => { setResult(event.target.value); setCheckPage(1); }}><option value="">Todos</option><option value="pending">Pendente</option><option value="running">Em andamento</option><option value="success">Sucesso</option><option value="failed">Falha</option><option value="stale">Expirado</option></select></label></div><HistoryPeriod from={from} to={to} onFrom={value => { setFrom(value); setCheckPage(1); setChangePage(1); }} onTo={value => { setTo(value); setCheckPage(1); setChangePage(1); }} />{!checks ? <Loading /> : !checks.items.length ? <EmptyState title="Sem verificações" description="As verificações realizadas aparecerão aqui." /> : <Card className="divide-y divide-slate-100">{checks.items.map(check => <div key={check.id} className="flex flex-wrap items-start justify-between gap-2 p-4 text-sm"><div><p className="font-medium">{check.status === "success" ? "Sucesso" : check.status === "failed" ? "Falha" : check.status === "pending" ? "Pendente" : check.status === "running" ? "Em andamento" : "Expirado"}</p><p className="mt-1 text-slate-500">{dateTime(check.requestedAt)} · {check.triggerType === "manual" ? "Manual" : "Agendada"}</p>{check.errorCode && <p className="mt-1 text-red-700">Erro: {errorLabel(check.errorCode)}</p>}</div><span className="text-xs text-slate-500">{check.httpStatusCode ? `HTTP ${check.httpStatusCode}` : ""}</span></div>)}</Card>}<Pagination page={checkPage} pageSize={PAGE_SIZE} total={checks?.totalCount ?? 0} onChange={setCheckPage} /></section>
      <section><h2 className="mb-4 text-lg font-semibold">Alterações</h2>{!changes ? <Loading /> : !changes.items.length ? <EmptyState title="Nenhuma alteração" description="A primeira leitura cria uma linha de base. Mudanças posteriores aparecem aqui." /> : <Card className="divide-y divide-slate-100">{changes.items.map(change => <Link key={change.id} href={`/changes/${change.id}`} className="flex items-center justify-between gap-3 p-4 text-sm hover:bg-slate-50"><div><p className="font-medium">Conteúdo alterado</p><p className="mt-1 text-slate-500">{dateTime(change.detectedAt)}</p></div><span className="text-slate-700">Comparar →</span></Link>)}</Card>}<Pagination page={changePage} pageSize={PAGE_SIZE} total={changes?.totalCount ?? 0} onChange={setChangePage} /></section></div>
    </>}
  </>;
}

function HistoryPeriod({ from, to, onFrom, onTo }: { from: string; to: string; onFrom: (value: string) => void; onTo: (value: string) => void }) {
  return <div className="mb-4 flex flex-wrap gap-3 text-xs text-slate-500"><label>De <input className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" type="date" value={from} max={to || undefined} onChange={event => onFrom(event.target.value)} /></label><label>Até <input className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" type="date" value={to} min={from || undefined} onChange={event => onTo(event.target.value)} /></label></div>;
}
