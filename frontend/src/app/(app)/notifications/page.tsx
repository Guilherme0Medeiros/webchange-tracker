"use client";

import { useState } from "react";
import Link from "next/link";
import { useSWRConfig } from "swr";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState, ErrorState, Loading, Pagination } from "@/components/feedback";
import { RefreshButton } from "@/components/refresh-button";
import { userError, mutate, query, type EmailStatus, type Notification, type Page } from "@/lib/api";
import { invalidatePaths, useRadarQuery } from "@/lib/radar-query";
import { dateTime } from "@/lib/utils";

const PAGE_SIZE = 10;
export default function NotificationsPage() {
  const { mutate: revalidate } = useSWRConfig();
  const [page, setPage] = useState(1);
  const [unread, setUnread] = useState(false);
  const [actionError, setActionError] = useState("");
  const [busyId, setBusyId] = useState("");
  const list = useRadarQuery<Page<Notification>>(`/notifications?${query({ page, pageSize: PAGE_SIZE, unread: unread ? "true" : undefined })}`);
  const email = useRadarQuery<EmailStatus>("/notifications/email-status");
  const refresh = () => { setActionError(""); void list.mutate(); void email.mutate(); };

  async function markRead(id: string) {
    setBusyId(id); setActionError("");
    try { await mutate(`/notifications/${id}/read`, "PATCH"); await invalidatePaths(revalidate, ["/notifications"]); }
    catch (cause) { setActionError(userError(cause, "Não foi possível marcar como lida.")); }
    finally { setBusyId(""); }
  }
  return <>
    <div className="mb-6 flex flex-wrap items-end justify-between gap-3"><div><h1 className="text-2xl font-semibold">Notificações</h1><p className="mt-1 text-sm text-slate-500">Avisos de mudanças detectadas nas suas páginas.</p></div><div className="flex flex-wrap items-center gap-3"><RefreshButton refreshing={list.isValidating} onClick={refresh} /><label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={unread} onChange={event => { setUnread(event.target.checked); setPage(1); }} /> Apenas não lidas</label></div></div>
    {email.data && <p className="mb-5 text-sm text-slate-600">E-mail: {!email.data.available ? "indisponível" : !email.data.confirmed ? "aguardando confirmação do endereço" : "disponível"}{email.data.pending > 0 && ` · ${email.data.pending} pendente(s)`}{email.data.failed > 0 && ` · ${email.data.failed} falha(s)`}</p>}
    {(actionError || list.error) && <div className="mb-5"><ErrorState message={actionError || list.error?.message || ""} retry={refresh} /></div>}
    {!list.data ? (!list.error && <Loading />) : !list.data.items.length ? <EmptyState title="Nenhuma notificação" description={unread ? "Todas as notificações estão lidas." : "Quando uma página mudar após a primeira leitura, você verá um aviso aqui."} /> : <Card className="divide-y divide-slate-100">{list.data.items.map(notification => <div key={notification.id} className="flex flex-wrap items-center justify-between gap-4 p-5"><div><div className="flex items-center gap-2"><p className="font-medium">Mudança detectada</p>{!notification.readAt && <span className="h-2 w-2 rounded-full bg-blue-600" aria-label="Não lida" />}</div><p className="mt-1 text-sm text-slate-500">{dateTime(notification.createdAt)}</p><Link className="mt-2 inline-block text-sm font-medium text-slate-900 underline" href={`/changes/${notification.changeEventId}`}>Comparar versões</Link></div>{!notification.readAt && <Button size="sm" variant="outline" disabled={busyId === notification.id} onClick={() => void markRead(notification.id)}>{busyId === notification.id ? "Salvando..." : "Marcar como lida"}</Button>}</div>)}</Card>}
    <Pagination page={page} pageSize={PAGE_SIZE} total={list.data?.totalCount ?? 0} onChange={setPage} />
  </>;
}
