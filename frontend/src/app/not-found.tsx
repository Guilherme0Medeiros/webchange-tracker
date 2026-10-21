import Link from "next/link";

export default function NotFound() {
  return <main className="mx-auto flex min-h-screen max-w-md flex-col items-center justify-center px-6 text-center">
    <h1 className="text-2xl font-semibold">Página não encontrada</h1>
    <p className="mt-3 text-slate-600">O endereço informado não existe ou não está mais disponível.</p>
    <Link className="mt-6 font-medium underline" href="/dashboard">Voltar ao painel</Link>
  </main>;
}
