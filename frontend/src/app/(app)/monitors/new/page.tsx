import { Card } from "@/components/ui/card";
import { MonitorForm } from "@/components/monitor-form";

export default function NewMonitorPage() {
  return <><div className="mb-6"><h1 className="text-2xl font-semibold">Novo monitoramento</h1><p className="mt-1 text-sm text-slate-500">Cadastre uma página HTML pública para acompanhar mudanças no texto.</p></div><Card className="p-6"><MonitorForm /></Card></>;
}
