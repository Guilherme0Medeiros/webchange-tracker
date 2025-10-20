import { afterEach, describe, expect, it, vi } from "vitest";
import { mutate } from "@/lib/api";

afterEach(() => vi.unstubAllGlobals());

describe("API responses", () => {
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
