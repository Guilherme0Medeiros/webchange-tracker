export type User = { id: string; email: string };
export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type Monitor = { id: string; name: string; url: string; intervalMinutes: number; status: "active" | "paused"; nextCheckAt: string | null; createdAt: string; updatedAt: string };
export type MonitorInput = Pick<Monitor, "name" | "url" | "intervalMinutes">;
export type Dashboard = { activeMonitors: number; checks: number; changes: number; failures: number };
export type Check = { id: string; triggerType: string; status: string; requestedAt: string; startedAt: string | null; finishedAt: string | null; httpStatusCode: number | null; errorCode: string | null; durationMs: number | null };
export type Change = { id: string; monitorId: string; oldSnapshotId: string; newSnapshotId: string; detectedAt: string };
export type ChangeDetail = { id: string; monitorId: string; detectedAt: string; oldText: string; newText: string; removed: string[]; added: string[] };
export type Notification = { id: string; changeEventId: string; monitorId: string; createdAt: string; readAt: string | null };

type Problem = { title?: string; detail?: string; errors?: Record<string, string[]> };

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api${path}`, { ...init, credentials: "same-origin", cache: "no-store", headers: { ...init.headers } });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as Problem;
    const validation = problem.errors && Object.values(problem.errors).flat().join(" ");
    throw new ApiError(response.status, validation || problem.detail || problem.title || `Erro HTTP ${response.status}`);
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
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
