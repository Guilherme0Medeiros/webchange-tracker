"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Bell, LayoutDashboard, LogOut, Radar } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ErrorState, Loading } from "@/components/feedback";
import { get, mutate, type User } from "@/lib/api";
import { cn } from "@/lib/utils";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<User | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let live = true;
    get<User>("/auth/me").then(value => { if (live) setUser(value); }).catch(cause => {
      if (!live) return;
      if (cause.status === 401) router.replace("/login");
      else setError(cause instanceof Error ? cause.message : "Não foi possível verificar a sessão.");
    });
    return () => { live = false; };
  }, [router]);
  async function logout() {
    setBusy(true); setError("");
    try { await mutate<void>("/auth/logout", "POST"); router.replace("/login"); router.refresh(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Não foi possível sair."); setBusy(false); }
  }
  if (error && !user) return <main className="mx-auto max-w-lg p-6"><ErrorState message={error} retry={() => window.location.reload()} /></main>;
  if (!user) return <main className="mx-auto max-w-lg p-6"><Loading /></main>;
  const nav = [{ href: "/dashboard", label: "Painel", icon: LayoutDashboard }, { href: "/notifications", label: "Notificações", icon: Bell }];
  return <div className="min-h-screen md:flex"><aside className="border-b border-slate-200 bg-white md:min-h-screen md:w-64 md:border-b-0 md:border-r"><div className="mx-auto max-w-6xl p-4 md:p-6"><Link href="/dashboard" className="flex items-center gap-2 font-bold"><Radar className="h-6 w-6" /> WebChange Radar</Link><nav className="mt-5 flex gap-2 md:mt-10 md:flex-col" aria-label="Principal">{nav.map(item => <Link key={item.href} href={item.href} className={cn("flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-slate-600 hover:bg-slate-100", pathname.startsWith(item.href) && "bg-slate-100 font-medium text-slate-900")}><item.icon className="h-4 w-4" />{item.label}</Link>)}</nav></div></aside><div className="min-w-0 flex-1"><header className="flex min-h-16 items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 sm:px-8"><span className="min-w-0 truncate text-sm text-slate-500">{user.email}</span><Button variant="ghost" size="sm" onClick={logout} disabled={busy}><LogOut className="h-4 w-4" /> Sair</Button></header><main className="mx-auto max-w-6xl p-4 sm:p-8">{error && <div className="mb-5"><ErrorState message={error} /></div>}{children}</main></div></div>;
}
