import { RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";

export function RefreshButton({ refreshing, onClick }: { refreshing: boolean; onClick: () => void }) {
  return <div className="flex items-center gap-2"><Button type="button" size="sm" variant="outline" onClick={onClick} disabled={refreshing}><RefreshCw className="h-4 w-4" /> Atualizar</Button>{refreshing && <span className="text-xs text-slate-500" role="status">Atualizando...</span>}</div>;
}
