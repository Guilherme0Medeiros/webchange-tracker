"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { Card } from "@/components/ui/card";
import { EmptyState, ErrorState, Loading } from "@/components/feedback";
import { userError, get, type ChangeDetail } from "@/lib/api";
import { dateTime } from "@/lib/utils";

export default function ChangePage() {
  const { id } = useParams<{ id: string }>();
  const [change, setChange] = useState<ChangeDetail | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { get<ChangeDetail>(`/changes/${id}`).then(setChange).catch(cause => setError(userError(cause, "Não foi possível carregar a comparação."))); }, [id]);
  return <>{change && <Link href={`/monitors/${change.monitorId}`} className="mb-5 inline-flex items-center gap-2 text-sm text-slate-500 hover:text-slate-900"><ArrowLeft className="h-4 w-4" /> Voltar ao monitoramento</Link>}<h1 className="text-2xl font-semibold">Comparar versões</h1><p className="mb-6 mt-1 text-sm text-slate-500">{change ? dateTime(change.detectedAt) : "Trechos removidos e adicionados"}</p>{error ? <ErrorState message={error} /> : !change ? <Loading /> : <><div className="grid gap-4 lg:grid-cols-2"><Card className="overflow-hidden"><h2 className="border-b border-slate-200 bg-red-50 px-5 py-3 font-medium text-red-800">Removido</h2>{change.removed.length ? <pre className="whitespace-pre-wrap break-words p-5 font-mono text-sm leading-6 text-red-800">{change.removed.map(line => `− ${line}`).join("\n")}</pre> : <div className="p-5"><EmptyState title="Nada removido" description="Esta mudança apenas adicionou texto." /></div>}</Card><Card className="overflow-hidden"><h2 className="border-b border-slate-200 bg-emerald-50 px-5 py-3 font-medium text-emerald-800">Adicionado</h2>{change.added.length ? <pre className="whitespace-pre-wrap break-words p-5 font-mono text-sm leading-6 text-emerald-800">{change.added.map(line => `+ ${line}`).join("\n")}</pre> : <div className="p-5"><EmptyState title="Nada adicionado" description="Esta mudança apenas removeu texto." /></div>}</Card></div><details className="mt-6 rounded-xl border border-slate-200 bg-white p-5"><summary className="cursor-pointer font-medium">Ver versões completas</summary><div className="mt-5 grid gap-5 lg:grid-cols-2"><div><h3 className="mb-2 text-sm font-medium">Antes</h3><pre className="max-h-96 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-slate-50 p-4 text-xs">{change.oldText}</pre></div><div><h3 className="mb-2 text-sm font-medium">Depois</h3><pre className="max-h-96 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-slate-50 p-4 text-xs">{change.newText}</pre></div></div></details></>}</>;
}
