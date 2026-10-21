import { afterEach, describe, expect, it, vi } from "vitest";
import { get, mutate } from "@/lib/api";

afterEach(() => vi.unstubAllGlobals());

describe("API responses", () => {
  it("shows translated validation details from ProblemDetails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: "Uma ou mais informações são inválidas.",
      errors: { url: ["Informe uma URL pública HTTP ou HTTPS."] }
    }), { status: 400, headers: { "Content-Type": "application/problem+json" } })));
    await expect(get("/monitors")).rejects.toThrow("Informe uma URL pública HTTP ou HTTPS.");
  });

  it("uses a friendly message for unexpected server errors", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: "Database connection failed", detail: "C:\\private\\database.sql"
    }), { status: 500, headers: { "Content-Type": "application/problem+json" } })));
    await expect(get("/dashboard")).rejects.toThrow("Não foi possível concluir a solicitação. Tente novamente mais tarde.");
  });

  it("preserves a known email delivery error", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: "Não foi possível enviar o e-mail agora. Tente novamente mais tarde."
    }), { status: 503, headers: { "Content-Type": "application/problem+json" } })));
    await expect(get("/notifications/email-status")).rejects.toThrow(
      "Não foi possível enviar o e-mail agora. Tente novamente mais tarde.");
  });

  it("does not display HTML or framework error text", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("<h1>Internal Server Error</h1>", {
      status: 404, headers: { "Content-Type": "text/html" }
    })));
    await expect(get("/monitors/missing")).rejects.toThrow("Recurso não encontrado.");
  });

  it("translates connection failures", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    await expect(get("/dashboard")).rejects.toThrow("Não foi possível conectar ao serviço. Verifique sua conexão e tente novamente.");
    vi.restoreAllMocks();
  });
  it("accepts an empty 202 response after requesting email confirmation", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: "csrf-token" }), {
        status: 200, headers: { "Content-Type": "application/json" }
      }))
      .mockResolvedValueOnce(new Response(null, { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(mutate<void>("/notifications/request-email-confirmation", "POST"))
      .resolves.toBeUndefined();
    expect(fetchMock).toHaveBeenNthCalledWith(2,
      "/api/notifications/request-email-confirmation",
      expect.objectContaining({ method: "POST", headers: { "X-CSRF-TOKEN": "csrf-token" } }));
  });
});
