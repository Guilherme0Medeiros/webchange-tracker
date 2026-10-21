"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Radar } from "lucide-react";
import { mutate, type User } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PasswordField } from "@/components/password-field";

export default function RegisterPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [repeatPassword, setRepeatPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    if (!email.trim()) { setError("Informe seu e-mail."); return; }
    if (!password) { setError("Informe uma senha."); return; }
    if (!repeatPassword) { setError("Repita a senha."); return; }
    if (password !== repeatPassword) { setError("As senhas não coincidem."); return; }

    setBusy(true);
    try {
      await mutate<User>("/auth/register", "POST", { email, password });
      router.replace("/dashboard");
      router.refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível criar a conta.");
    } finally {
      setBusy(false);
    }
  }

  return <main className="flex min-h-screen items-center justify-center px-4 py-12">
    <div className="w-full max-w-md">
      <div className="mb-8 flex items-center justify-center gap-2 text-xl font-bold"><Radar className="h-6 w-6" /> WebChange Radar</div>
      <Card className="p-7 sm:p-9">
        <h1 className="text-2xl font-semibold">Criar conta</h1>
        <p className="mt-2 text-sm text-slate-500">Comece a monitorar até três páginas públicas.</p>
        <form className="mt-7 space-y-5" onSubmit={submit} noValidate>
          <label className="block text-sm font-medium">E-mail
            <Input className="mt-2" type="email" autoComplete="email" required value={email} onChange={event => setEmail(event.target.value)} />
          </label>
          <PasswordField id="password" label="Senha" value={password} onChange={event => setPassword(event.target.value)}
            autoComplete="new-password" describedBy="password-requirements" />
          <PasswordField id="repeat-password" label="Repetir senha" value={repeatPassword}
            onChange={event => setRepeatPassword(event.target.value)} autoComplete="new-password"
            visibilityLabel="confirmação de senha" />
          <div id="password-requirements" className="rounded-lg border border-slate-200 bg-slate-100 p-3 text-sm text-slate-700">
            <p className="font-medium">Sua senha precisa ter:</p>
            <ul className="mt-1 list-inside list-disc space-y-0.5">
              <li>Pelo menos 8 caracteres</li>
              <li>Uma letra maiúscula e uma minúscula</li>
              <li>Um número</li>
              <li>Um símbolo, como ! ou #</li>
            </ul>
          </div>
          {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
          <Button className="w-full" disabled={busy}>{busy ? "Criando..." : "Criar conta"}</Button>
        </form>
        <p className="mt-6 text-center text-sm text-slate-500">Já tem conta? <Link className="font-medium text-slate-900 underline" href="/login">Entre</Link></p>
      </Card>
    </div>
  </main>;
}
