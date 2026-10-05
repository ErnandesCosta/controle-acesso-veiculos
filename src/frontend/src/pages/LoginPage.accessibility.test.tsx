import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { SessionProvider } from "../features/authentication";
import { expectNoSeriousAccessibilityViolations } from "../test/accessibility";
import { LoginPage } from "./LoginPage";

describe("LoginPage accessibility", () => {
  it("keeps the page vertically scrollable on short viewports", async () => {
    const { container } = render(
      <SessionProvider>
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>
      </SessionProvider>,
    );

    await screen.findByRole("heading", { name: /bem-vindo/i });

    const main = container.querySelector("main");

    expect(main).toHaveClass("login-page", "overflow-x-hidden");
    expect(main).not.toHaveClass("overflow-hidden");
    expect(container.querySelector(".login-shell")).toBeInTheDocument();
  });

  it("describes session restoration without outdated browser-storage guidance", async () => {
    render(
      <SessionProvider>
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>
      </SessionProvider>,
    );

    expect(
      await screen.findByText(
        "Sua sessão é protegida e pode ser restaurada com segurança enquanto estiver válida.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/será encerrada ao atualizar ou fechar/i),
    ).not.toBeInTheDocument();
  });

  it("has no serious automated accessibility violations", async () => {
    const { container } = render(
      <SessionProvider>
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>
      </SessionProvider>,
    );

    await expectNoSeriousAccessibilityViolations(container);
  });
});
