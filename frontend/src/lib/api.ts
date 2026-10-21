export type User = { id: string; email: string };
export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type Monitor = { id: string; name: string; url: string; intervalMinutes: number; contentSelector: string | null; keywords: string[]; emailAlertsEnabled: boolean; status: "active" | "paused"; nextCheckAt: string | null; lastCheckedAt: string | null; createdAt: string; updatedAt: string };
export type MonitorInput = Pick<Monitor, "name" | "url" | "intervalMinutes" | "contentSelector" | "keywords" | "emailAlertsEnabled">;
export type EmailStatus = { available: boolean; confirmed: boolean; pending: number; failed: number };
export type Dashboard = { activeMonitors: number; checks: number; changes: number; failures: number };
export type Check = { id: string; triggerType: string; status: string; requestedAt: string; startedAt: string | null; finishedAt: string | null; httpStatusCode: number | null; errorCode: string | null; durationMs: number | null };
export type Change = { id: string; monitorId: string; oldSnapshotId: string; newSnapshotId: string; detectedAt: string };
export type ChangeDetail = { id: string; monitorId: string; detectedAt: string; oldText: string; newText: string; removed: string[]; added: string[] };
export type Notification = { id: string; changeEventId: string; monitorId: string; createdAt: string; readAt: string | null };

type Problem = { title?: string; detail?: string; errors?: Record<string, string[]> };
const unexpectedError = "Não foi possível concluir a solicitação. Tente novamente mais tarde.";

function friendlyError(status: number, problem: Problem): string {
  if (status >= 500 && status !== 503) return unexpectedError;
  const validation = problem.errors && Object.values(problem.errors)
    .filter(Array.isArray).flat().filter((message): message is string => typeof message === "string").join(" ");
  const message = validation || problem.detail || problem.title;
  if (typeof message === "string" && message.trim()) return message;
  if (status === 401) return "Entre na sua conta para continuar.";
  if (status === 403) return "Você não tem permissão para acessar este recurso.";
  if (status === 404) return "Recurso não encontrado.";
  if (status === 429) return "Muitas solicitações. Aguarde e tente novamente.";
  return unexpectedError;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}
export const userError = (cause: unknown, fallback = unexpectedError) =>
  cause instanceof ApiError ? cause.message : fallback;

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, { ...init, credentials: "same-origin", cache: "no-store", headers: { ...init.headers } });
  } catch (cause) {
    console.error("API network request failed", cause);
    throw new ApiError(0, "Não foi possível conectar ao serviço. Verifique sua conexão e tente novamente.");
  }
  if (!response.ok) {
    const isProblem = response.headers.get("Content-Type")?.toLowerCase().includes("application/problem+json");
    const parsed = isProblem ? await response.json().catch(() => null) : null;
    const problem: Problem = parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : {};
    throw new ApiError(response.status, friendlyError(response.status, problem));
  }
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  try {
    return body ? JSON.parse(body) as T : undefined as T;
  } catch (cause) {
    console.error("API returned invalid JSON", cause);
    throw new ApiError(response.status, unexpectedError);
  }
}

export const get = <T>(path: string) => request<T>(path);

export async function mutate<T>(path: string, method: "POST" | "PUT" | "PATCH" | "DELETE", body?: object): Promise<T> {
  const { requestToken } = await get<{ requestToken: string }>("/auth/csrf");
  return request<T>(path, {
    method,
    headers: { "X-CSRF-TOKEN": requestToken, ...(body ? { "Content-Type": "application/json" } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
}

export function query(params: Record<string, string | number | undefined>) {
  const values = Object.entries(params).filter((entry): entry is [string, string | number] => entry[1] !== undefined);
  return new URLSearchParams(values.map(([key, value]) => [key, String(value)])).toString();
}
