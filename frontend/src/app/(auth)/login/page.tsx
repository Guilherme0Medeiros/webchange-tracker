"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Radar } from "lucide-react";
import { mutate, type User } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    try { await mutate<User>("/auth/login", "POST", { email, password }); router.replace("/dashboard"); router.refresh(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Não foi possível entrar."); }
    finally { setBusy(false); }
  }
  return <main className="flex min-h-screen items-center justify-center px-4 py-12"><div className="w-full max-w-md"><div className="mb-8 flex items-center justify-center gap-2 text-xl font-bold"><Radar className="h-6 w-6" /> WebChange Radar</div><Card className="p-7 sm:p-9"><h1 className="text-2xl font-semibold">Entrar</h1><p className="mt-2 text-sm text-slate-500">Acompanhe suas páginas em um só lugar.</p><form className="mt-7 space-y-5" onSubmit={submit}><label className="block text-sm font-medium">E-mail<Input className="mt-2" type="email" autoComplete="email" required value={email} onChange={event => setEmail(event.target.value)} /></label><label className="block text-sm font-medium">Senha<Input className="mt-2" type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} /></label>{error && <p role="alert" className="text-sm text-red-700">{error}</p>}<Button className="w-full" disabled={busy}>{busy ? "Entrando..." : "Entrar"}</Button></form><p className="mt-6 text-center text-sm text-slate-500">Ainda não tem conta? <Link className="font-medium text-slate-900 underline" href="/register">Cadastre-se</Link></p></Card></div></main>;
}
