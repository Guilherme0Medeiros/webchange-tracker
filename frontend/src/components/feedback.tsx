import { Button } from "@/components/ui/button";

export function Loading() { return <div className="rounded-xl border border-slate-200 bg-white p-8 text-sm text-slate-500" role="status">Carregando...</div>; }
export function ErrorState({ message, retry }: { message: string; retry?: () => void }) {
  return <div className="rounded-xl border border-red-200 bg-red-50 p-6 text-sm text-red-800" role="alert"><p>{message}</p>{retry && <Button className="mt-4" variant="outline" onClick={retry}>Tentar novamente</Button>}</div>;
}
export function EmptyState({ title, description, action }: { title: string; description: string; action?: React.ReactNode }) {
  return <div className="rounded-xl border border-dashed border-slate-300 bg-white p-10 text-center"><h2 className="font-semibold">{title}</h2><p className="mt-2 text-sm text-slate-500">{description}</p>{action && <div className="mt-5">{action}</div>}</div>;
}
export function Pagination({ page, pageSize, total, onChange }: { page: number; pageSize: number; total: number; onChange: (page: number) => void }) {
  if (total <= pageSize) return null;
  return <nav aria-label="Paginação" className="flex items-center justify-between gap-3 pt-4 text-sm text-slate-500"><span>Página {page} de {Math.ceil(total / pageSize)}</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={page === 1} onClick={() => onChange(page - 1)}>Anterior</Button><Button size="sm" variant="outline" disabled={page * pageSize >= total} onClick={() => onChange(page + 1)}>Próxima</Button></div></nav>;
}
