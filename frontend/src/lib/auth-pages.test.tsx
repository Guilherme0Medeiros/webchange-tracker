import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import RegisterPage from "@/app/(auth)/register/page";
import LoginPage from "@/app/(auth)/login/page";
import { mutate } from "@/lib/api";

vi.mock("@/lib/api", async importOriginal => {
  const actual = await importOriginal<typeof import("@/lib/api")>();
  return { ...actual, mutate: vi.fn() };
});
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn(), refresh: vi.fn() }) }));

const mockedMutate = vi.mocked(mutate);
beforeEach(() => mockedMutate.mockReset());
afterEach(cleanup);

describe("password forms", () => {
  it("shows the password rules and toggles visibility without revealing the confirmation", () => {
    render(<RegisterPage />);
    expect(screen.getByText("Pelo menos 8 caracteres")).toBeTruthy();
    expect(screen.getByText("Uma letra maiúscula e uma minúscula")).toBeTruthy();
    expect(screen.getByText("Um número")).toBeTruthy();
    expect(screen.getByText("Um símbolo, como ! ou #")).toBeTruthy();
    const password = screen.getByLabelText("Senha") as HTMLInputElement;
    const confirmation = screen.getByLabelText("Repetir senha") as HTMLInputElement;
    expect(password.type).toBe("password");
    expect(confirmation.type).toBe("password");
    fireEvent.click(screen.getByRole("button", { name: "Mostrar senha" }));
    expect(password.type).toBe("text");
    expect(confirmation.type).toBe("password");
    fireEvent.click(screen.getByRole("button", { name: "Ocultar senha" }));
    expect(password.type).toBe("password");
  });

  it("requires matching passwords before requesting registration", () => {
    render(<RegisterPage />);
    fireEvent.change(screen.getByLabelText("E-mail"), { target: { value: "user@example.test" } });
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "Abcdef1!" } });
    fireEvent.change(screen.getByLabelText("Repetir senha"), { target: { value: "Different1!" } });
    fireEvent.submit(screen.getByRole("button", { name: "Criar conta" }).closest("form")!);
    expect(screen.getByRole("alert").textContent).toBe("As senhas não coincidem.");
    expect(mockedMutate).not.toHaveBeenCalled();
  });

  it("sends matching passwords without including the confirmation in the request", async () => {
    mockedMutate.mockResolvedValue({ id: "user-1", email: "user@example.test" });
    render(<RegisterPage />);
    fireEvent.change(screen.getByLabelText("E-mail"), { target: { value: "user@example.test" } });
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "Abcdef12" } });
    fireEvent.change(screen.getByLabelText("Repetir senha"), { target: { value: "Abcdef12" } });
    await act(async () => {
      fireEvent.submit(screen.getByRole("button", { name: "Criar conta" }).closest("form")!);
    });
    expect(mockedMutate).toHaveBeenCalledWith("/auth/register", "POST",
      { email: "user@example.test", password: "Abcdef12" });
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("toggles login password visibility", () => {
    render(<LoginPage />);
    const password = screen.getByLabelText("Senha") as HTMLInputElement;
    fireEvent.click(screen.getByRole("button", { name: "Mostrar senha" }));
    expect(password.type).toBe("text");
    fireEvent.click(screen.getByRole("button", { name: "Ocultar senha" }));
    expect(password.type).toBe("password");
  });
});
