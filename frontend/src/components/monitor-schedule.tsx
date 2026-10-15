import { type Monitor } from "@/lib/api";
import { dateTime } from "@/lib/utils";

export function MonitorSchedule({ monitor }: { monitor: Monitor }) {
  const overdue = monitor.status === "active" && monitor.nextCheckAt !== null &&
    new Date(monitor.nextCheckAt).getTime() <= Date.now();

  return <div className="text-xs text-slate-500">
    <p>{overdue ? "Aguardando verificação" : `Horário previsto: ${dateTime(monitor.nextCheckAt)}`}</p>
    {overdue && <p>Horário previsto: {dateTime(monitor.nextCheckAt)}</p>}
    <p>Última verificação realizada: {dateTime(monitor.lastCheckedAt)}</p>
  </div>;
}
