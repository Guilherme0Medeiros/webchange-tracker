import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SWRConfig } from "swr";
import { useRadarQuery } from "@/lib/radar-query";
import DashboardPage from "@/app/(app)/dashboard/page";
import MonitorPage from "@/app/(app)/monitors/[id]/page";
import NotificationsPage from "@/app/(app)/notifications/page";
import { MonitorForm } from "@/components/monitor-form";
import { get, mutate } from "@/lib/api";

vi.mock("@/lib/api", async importOriginal => {
  const actual = await importOriginal<typeof import("@/lib/api")>();
  return { ...actual, get: vi.fn(), mutate: vi.fn() };
});
vi.mock("next/navigation", () => ({ useParams: () => ({ id: "m1" }), useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));

const mockedGet = vi.mocked(get);
const mockedMutate = vi.mocked(mutate);
const monitor = { id: "m1", name: "Página A", url: "https://example.com", intervalMinutes: 60, contentSelector: null, keywords: [], emailAlertsEnabled: false, status: "active" as const, nextCheckAt: null, lastCheckedAt: null, createdAt: "2026-10-15T12:00:00Z", updatedAt: "2026-10-15T12:00:00Z" };
const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 10, totalCount: items.length });

function withCache(element: React.ReactElement) {
  return render(<SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0, focusThrottleInterval: 0 }}>{element}</SWRConfig>);
}

function Probe() {
  const { data, error, mutate: refresh } = useRadarQuery<{ count: number }>("/probe");
  return <div>{data?.count ?? "loading"}{error && <span role="alert">{error.message}</span>}<button onClick={() => void refresh()}>Atualizar</button></div>;
}

beforeEach(() => {
  vi.useRealTimers();
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  mockedGet.mockReset();
  mockedMutate.mockReset();
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe("automatic refresh", () => {
  it("polls every 30 seconds, pauses while hidden, and refreshes on return", async () => {
    mockedGet.mockResolvedValue({ count: 1 });
    vi.useFakeTimers();
    withCache(<Probe />);
    await act(async () => { await Promise.resolve(); });
    expect(mockedGet).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000); });
    expect(mockedGet).toHaveBeenCalledTimes(2);
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(mockedGet).toHaveBeenCalledTimes(2);
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
    await act(async () => { document.dispatchEvent(new Event("visibilitychange")); window.dispatchEvent(new Event("focus")); await vi.advanceTimersByTimeAsync(1_000); });
    expect(mockedGet).toHaveBeenCalledTimes(3);
  });

  it("keeps existing data visible when a refresh fails", async () => {
    mockedGet.mockResolvedValueOnce({ count: 1 }).mockRejectedValueOnce(new Error("Falha de rede"));
    withCache(<Probe />);
    await screen.findByText("1");
    fireEvent.click(screen.getByRole("button", { name: "Atualizar" }));
    await waitFor(() => expect(screen.getByRole("alert").textContent).toBe("Falha de rede"));
    expect(screen.getByText("1")).toBeTruthy();
  });

  it("refreshes dashboard data with the button without navigating", async () => {
    mockedGet.mockImplementation(async path => path === "/dashboard" ? { activeMonitors: 1, checks: 0, changes: 0, failures: 0 } : page([monitor]));
    withCache(<DashboardPage />);
    await screen.findByText("Página A");
    mockedGet.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Atualizar" }));
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2));
    expect(screen.getByText("Página A")).toBeTruthy();
  });

  it("revalidates check history after requesting a manual check", async () => {
    mockedGet.mockImplementation(async path => path === "/monitors/m1" ? monitor : page([]));
    mockedMutate.mockResolvedValue({});
    withCache(<MonitorPage />);
    await screen.findByText("Página A");
    mockedGet.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Verificar agora" }));
    await waitFor(() => expect(mockedMutate).toHaveBeenCalledWith("/monitors/m1/checks", "POST"));
    await waitFor(() => expect(mockedGet.mock.calls.some(([path]) => String(path).startsWith("/monitors/m1/checks?"))).toBe(true));
  });

  it("updates notifications after marking one as read", async () => {
    const notification = { id: "n1", changeEventId: "c1", monitorId: "m1", createdAt: "2026-10-15T12:00:00Z", readAt: null };
    mockedGet.mockResolvedValue(page([notification]));
    mockedMutate.mockResolvedValue({});
    withCache(<NotificationsPage />);
    await screen.findByRole("button", { name: "Marcar como lida" });
    mockedGet.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Marcar como lida" }));
    await waitFor(() => expect(mockedMutate).toHaveBeenCalledWith("/notifications/n1/read", "PATCH"));
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(1));
  });

  it.each([false, true])("invalidates the dashboard after %s monitor form submission", async editing => {
    mockedGet.mockImplementation(async path => path === "/dashboard" ? { activeMonitors: 1, checks: 0, changes: 0, failures: 0 } : page([monitor]));
    mockedMutate.mockResolvedValue(monitor);
    withCache(<><DashboardPage /><MonitorForm monitor={editing ? monitor : undefined} /></>);
    await screen.findByText("Página A");
    mockedGet.mockClear();
    fireEvent.submit(screen.getByRole("button", { name: editing ? "Salvar alterações" : "Criar monitoramento" }).closest("form")!);
    await waitFor(() => expect(mockedMutate).toHaveBeenCalledWith(editing ? "/monitors/m1" : "/monitors", editing ? "PUT" : "POST", expect.any(Object)));
    await waitFor(() => expect(mockedGet.mock.calls.some(([path]) => path === "/dashboard")).toBe(true));
  });

  it("updates the status after pausing a monitor", async () => {
    let status: "active" | "paused" = "active";
    mockedGet.mockImplementation(async path => path === "/monitors/m1" ? { ...monitor, status } : page([]));
    mockedMutate.mockImplementation(async () => { status = "paused"; return { ...monitor, status }; });
    withCache(<MonitorPage />);
    await screen.findByRole("button", { name: "Pausar" });
    fireEvent.click(screen.getByRole("button", { name: "Pausar" }));
    await screen.findByRole("button", { name: "Reativar" });
  });

  it("removes a deleted monitor from the dashboard", async () => {
    let deleted = false;
    mockedGet.mockImplementation(async path => path === "/dashboard" ? { activeMonitors: deleted ? 0 : 1, checks: 0, changes: 0, failures: 0 } : path === "/monitors/m1" ? monitor : page(deleted ? [] : [monitor]));
    mockedMutate.mockImplementation(async () => { deleted = true; return undefined; });
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    withCache(<><DashboardPage /><MonitorPage /></>);
    await screen.findByRole("button", { name: "Excluir" });
    fireEvent.click(screen.getByRole("button", { name: "Excluir" }));
    await screen.findByText("Nenhum monitoramento aqui");
    expect(mockedMutate).toHaveBeenCalledWith("/monitors/m1", "DELETE");
    confirm.mockRestore();
  });
});
