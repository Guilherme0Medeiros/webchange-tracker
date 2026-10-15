"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowRight, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState, ErrorState, Loading, Pagination } from "@/components/feedback";
import { RefreshButton } from "@/components/refresh-button";
import { query, type Dashboard, type Monitor, type Page } from "@/lib/api";
import { useRadarQuery } from "@/lib/radar-query";
import { MonitorSchedule } from "@/components/monitor-schedule";

const PAGE_SIZE = 10;
export default function DashboardPage() {
  const [filter, setFilter] = useState("");
  const [page, setPage] = useState(1);
  const stats = useRadarQuery<Dashboard>("/dashboard");
  const monitors = useRadarQuery<Page<Monitor>>(`/monitors?${query({ page, pageSize: PAGE_SIZE, status: filter || undefined })}`);
  const error = stats.error || monitors.error;
  const loading = !stats.data || !monitors.data;
  const refreshing = stats.isValidating || monitors.isValidating;
  const refresh = () => { void Promise.all([stats.mutate(), monitors.mutate()]); };

  return <>
    <div className="mb-8 flex flex-wrap items-end justify-between gap-4"><div><h1 className="text-2xl font-semibold">Painel</h1><p className="mt-1 text-sm text-slate-500">Acompanhe suas páginas monitoradas.</p></div><div className="flex flex-wrap gap-2"><RefreshButton refreshing={refreshing} onClick={refresh} /><Button asChild><Link href="/monitors/new"><Plus className="h-4 w-4" /> Novo monitoramento</Link></Button></div></div>
    {error && <div className="mb-5"><ErrorState message={error.message} retry={refresh} /></div>}
    {loading ? (!error && <Loading />) : <>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{([ ["Monitores ativos", stats.data?.activeMonitors], ["Verificações", stats.data?.checks], ["Alterações", stats.data?.changes], ["Falhas", stats.data?.failures] ] as const).map(([label, value]) => <Card key={label} className="p-5"><p className="text-sm text-slate-500">{label}</p><p className="mt-2 text-3xl font-semibold">{value ?? 0}</p></Card>)}</div>
      <section className="mt-8"><div className="mb-4 flex flex-wrap items-center justify-between gap-3"><h2 className="text-lg font-semibold">Monitoramentos</h2><label className="text-sm text-slate-500">Status <select className="ml-2 h-9 rounded-lg border border-slate-200 bg-white px-3 text-slate-900" value={filter} onChange={event => { setFilter(event.target.value); setPage(1); }}><option value="">Todos</option><option value="active">Ativos</option><option value="paused">Pausados</option></select></label></div>
      {!monitors.data?.items.length ? <EmptyState title="Nenhum monitoramento aqui" description={filter ? "Não há monitoramentos com este status." : "Cadastre sua primeira página para começar."} action={!filter && <Button asChild><Link href="/monitors/new">Criar monitoramento</Link></Button>} /> : <Card className="overflow-hidden"><div className="divide-y divide-slate-100">{monitors.data.items.map(monitor => <Link key={monitor.id} href={`/monitors/${monitor.id}`} className="flex items-center justify-between gap-4 p-5 hover:bg-slate-50"><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><span className="font-medium">{monitor.name}</span><span className={monitor.status === "active" ? "rounded-full bg-emerald-50 px-2 py-0.5 text-xs text-emerald-700" : "rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600"}>{monitor.status === "active" ? "Ativo" : "Pausado"}</span></div><p className="mt-1 truncate text-sm text-slate-500">{monitor.url}</p><div className="mt-1"><MonitorSchedule monitor={monitor} /></div></div><ArrowRight className="h-4 w-4 shrink-0 text-slate-400" /></Link>)}</div></Card>}
      <Pagination page={page} pageSize={PAGE_SIZE} total={monitors.data?.totalCount ?? 0} onChange={setPage} /></section>
    </>}
  </>;
}
