"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState, ErrorState, Loading, Pagination } from "@/components/feedback";
import { get, mutate, query, type Change, type Check, type Monitor, type Page } from "@/lib/api";
import { dateTime } from "@/lib/utils";

const PAGE_SIZE = 10;
export default function MonitorPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [monitor, setMonitor] = useState<Monitor | null>(null);
  const [checks, setChecks] = useState<Page<Check> | null>(null);
  const [changes, setChanges] = useState<Page<Change> | null>(null);
  const [checkPage, setCheckPage] = useState(1);
  const [changePage, setChangePage] = useState(1);
  const [result, setResult] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const load = useCallback(async () => {
    setLoading(true); setError("");
    const period = { from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined, to: to ? new Date(`${to}T23:59:59`).toISOString() : undefined };
    try {
      const [m, c, changes] = await Promise.all([
        get<Monitor>(`/monitors/${id}`),
        get<Page<Check>>(`/monitors/${id}/checks?${query({ page: checkPage, pageSize: PAGE_SIZE, result: result || undefined, ...period })}`),
        get<Page<Change>>(`/monitors/${id}/changes?${query({ page: changePage, pageSize: PAGE_SIZE, ...period })}`),
      ]);
      setMonitor(m); setChecks(c); setChanges(changes);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Não foi possível carregar os dados."); }
    finally { setLoading(false); }
  }, [id, checkPage, changePage, result, from, to]);
  useEffect(() => { void load(); }, [load]);
  async function action(kind: "check" | "status" | "delete") {
    if (!monitor) return;
    if (kind === "delete" && !window.confirm("Excluir este monitoramento e todo o seu histórico?")) return;
    setBusy(true); setError(""); setNotice("");
    try {
      if (kind === "delete") { await mutate<void>(`/monitors/${id}`, "DELETE"); router.replace("/dashboard"); return; }
      if (kind === "check") { await mutate(`/monitors/${id}/checks`, "POST"); setNotice("Verificação enfileirada. O resultado aparecerá após o processamento agendado."); }
      if (kind === "status") { const updated = await mutate<Monitor>(`/monitors/${id}/status`, "PATCH", { status: monitor.status === "active" ? "paused" : "active" }); setMonitor(updated); setNotice(updated.status === "active" ? "Monitoramento reativado." : "Monitoramento pausado."); }
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "A ação não pôde ser concluída."); }
    finally { setBusy(false); }
  }
  return <><Link className="mb-5 inline-flex items-center gap-2 text-sm text-slate-500 hover:text-slate-900" href="/dashboard"><ArrowLeft className="h-4 w-4" /> Voltar ao painel</Link>{loading && !monitor ? <Loading /> : !monitor ? <ErrorState message={error || "Monitoramento não encontrado."} retry={() => void load()} /> : <><div className="flex flex-wrap items-start justify-between gap-4"><div className="min-w-0"><h1 className="text-2xl font-semibold">{monitor.name}</h1><a href={monitor.url} target="_blank" rel="noopener noreferrer" className="mt-1 inline-flex max-w-full items-center gap-1 truncate text-sm text-slate-500 hover:underline">{monitor.url}<ExternalLink className="h-3 w-3 shrink-0" /></a><p className="mt-2 text-sm text-slate-500">{monitor.status === "active" ? "Ativo" : "Pausado"} · A cada {monitor.intervalMinutes === 60 ? "1 hora" : monitor.intervalMinutes === 360 ? "6 horas" : "24 horas"} · Próxima verificação: {dateTime(monitor.nextCheckAt)}</p></div><div className="flex flex-wrap gap-2"><Button variant="outline" disabled={busy || monitor.status !== "active"} onClick={() => void action("check")}>Verificar agora</Button><Button variant="outline" disabled={busy} onClick={() => void action("status")}>{monitor.status === "active" ? "Pausar" : "Reativar"}</Button><Button variant="outline" asChild><Link href={`/monitors/${id}/edit`}>Editar</Link></Button><Button variant="destructive" disabled={busy} onClick={() => void action("delete")}>Excluir</Button></div></div>{notice && <p role="status" className="mt-5 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">{notice}</p>}{error && <div className="mt-5"><ErrorState message={error} retry={() => void load()} /></div>}<div className="mt-8 grid gap-8 xl:grid-cols-2"><section><div className="mb-4 flex flex-wrap items-end justify-between gap-3"><h2 className="text-lg font-semibold">Verificações</h2><label className="text-xs text-slate-500">Resultado <select className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" value={result} onChange={event => { setResult(event.target.value); setCheckPage(1); }}><option value="">Todos</option><option value="pending">Pendente</option><option value="running">Em andamento</option><option value="success">Sucesso</option><option value="failed">Falha</option><option value="stale">Expirado</option></select></label></div><HistoryPeriod from={from} to={to} onFrom={value => { setFrom(value); setCheckPage(1); setChangePage(1); }} onTo={value => { setTo(value); setCheckPage(1); setChangePage(1); }} />{loading ? <Loading /> : !checks?.items.length ? <EmptyState title="Sem verificações" description="As verificações realizadas aparecerão aqui." /> : <Card className="divide-y divide-slate-100">{checks.items.map(check => <div key={check.id} className="flex flex-wrap items-start justify-between gap-2 p-4 text-sm"><div><p className="font-medium">{check.status === "success" ? "Sucesso" : check.status === "failed" ? "Falha" : check.status === "pending" ? "Pendente" : check.status === "running" ? "Em andamento" : "Expirado"}</p><p className="mt-1 text-slate-500">{dateTime(check.requestedAt)} · {check.triggerType === "manual" ? "Manual" : "Agendada"}</p>{check.errorCode && <p className="mt-1 text-red-700">Erro: {check.errorCode}</p>}</div><span className="text-xs text-slate-500">{check.httpStatusCode ? `HTTP ${check.httpStatusCode}` : ""}</span></div>)}</Card>}<Pagination page={checkPage} pageSize={PAGE_SIZE} total={checks?.totalCount ?? 0} onChange={setCheckPage} /></section><section><h2 className="mb-4 text-lg font-semibold">Alterações</h2>{loading ? <Loading /> : !changes?.items.length ? <EmptyState title="Nenhuma alteração" description="A primeira leitura cria uma linha de base. Mudanças posteriores aparecem aqui." /> : <Card className="divide-y divide-slate-100">{changes.items.map(change => <Link key={change.id} href={`/changes/${change.id}`} className="flex items-center justify-between gap-3 p-4 text-sm hover:bg-slate-50"><div><p className="font-medium">Conteúdo alterado</p><p className="mt-1 text-slate-500">{dateTime(change.detectedAt)}</p></div><span className="text-slate-700">Comparar →</span></Link>)}</Card>}<Pagination page={changePage} pageSize={PAGE_SIZE} total={changes?.totalCount ?? 0} onChange={setChangePage} /></section></div></>}</>;
}

function HistoryPeriod({ from, to, onFrom, onTo }: { from: string; to: string; onFrom: (value: string) => void; onTo: (value: string) => void }) {
  return <div className="mb-4 flex flex-wrap gap-3 text-xs text-slate-500"><label>De <input className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" type="date" value={from} max={to || undefined} onChange={event => onFrom(event.target.value)} /></label><label>Até <input className="ml-1 h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-900" type="date" value={to} min={from || undefined} onChange={event => onTo(event.target.value)} /></label></div>;
}
