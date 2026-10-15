"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { Card } from "@/components/ui/card";
import { MonitorForm } from "@/components/monitor-form";
import { ErrorState, Loading } from "@/components/feedback";
import { get, type Monitor } from "@/lib/api";

export default function EditMonitorPage() {
  const { id } = useParams<{ id: string }>();
  const [monitor, setMonitor] = useState<Monitor | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { get<Monitor>(`/monitors/${id}`).then(setMonitor).catch(cause => setError(cause instanceof Error ? cause.message : "Não foi possível carregar o monitoramento.")); }, [id]);
  return <><h1 className="mb-6 text-2xl font-semibold">Editar monitoramento</h1>{error ? <ErrorState message={error} /> : !monitor ? <Loading /> : <Card className="p-6"><MonitorForm monitor={monitor} /></Card>}</>;
}
