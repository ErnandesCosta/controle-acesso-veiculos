import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode, useState } from "react";
import {
  Link,
  MemoryRouter,
  Outlet,
  Route,
  RouterProvider,
  Routes,
  createMemoryRouter,
  useNavigate,
} from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { SessionContext } from "../features/authentication/session/SessionContext";
import type { ProfileName } from "../features/authentication/types";
import { ProfileRoute } from "./ProfileRoute";
import { RouteTransitionManager } from "./RouteTransitionManager";
import { getRouteTitle } from "./routeMetadata";

const routeTitles = [
  ["/login", "Entrar"],
  ["/visao-geral", "Visão geral"],
  ["/acessos/novo", "Registrar entrada"],
  ["/acessos/abertos", "Acessos em aberto"],
  ["/acessos/historico", "Histórico de acessos"],
  ["/utilizacoes-institucionais", "Utilizações da frota"],
  ["/frota", "Frota institucional"],
  ["/motoristas-institucionais", "Motoristas autorizados"],
  ["/eventos", "Eventos e autorizações"],
  ["/administracao", "Administração"],
] as const;

function sessionValue(profileName: ProfileName = "Administrador") {
  return {
    completePasswordChange: vi.fn(),
    expiresAtUtc: "2030-09-10T23:59:59.000Z",
    login: vi.fn(),
    logout: vi.fn(),
    sessionEndReason: null,
    status: "authenticated" as const,
    user: {
      email: "route-test@example.test",
      id: 42,
      profileName,
      requiresPasswordChange: false,
    },
  };
}

function MainLayout() {
  return (
    <main>
      <Outlet />
    </main>
  );
}

function OverviewPage() {
  const navigate = useNavigate();

  return (
    <>
      <h1>Visão geral</h1>
      <Link to="/acessos/historico">Abrir histórico</Link>
      <button onClick={() => navigate(1)} type="button">
        Avançar
      </button>
    </>
  );
}

function HistoryPage() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState("");

  return (
    <>
      <h1>Histórico de acessos</h1>
      <label htmlFor="route-filter">Pesquisar</label>
      <input
        id="route-filter"
        onChange={(event) => setFilter(event.target.value)}
        value={filter}
      />
      <p>Filtro atual: {filter}</p>
      <button onClick={() => navigate(-1)} type="button">
        Voltar
      </button>
    </>
  );
}

function renderTransitions(
  initialEntries: string[] = ["/visao-geral"],
  initialIndex = initialEntries.length - 1,
  profileName: ProfileName = "Administrador",
) {
  return render(
    <StrictMode>
      <SessionContext.Provider value={sessionValue(profileName)}>
        <MemoryRouter
          initialEntries={initialEntries}
          initialIndex={initialIndex}
        >
          <Routes>
            <Route element={<RouteTransitionManager />}>
              <Route
                element={
                  <main>
                    <h1>Bem-vindo</h1>
                    <label htmlFor="email">E-mail</label>
                    <input id="email" />
                  </main>
                }
                path="/login"
              />
              <Route element={<MainLayout />}>
                <Route element={<OverviewPage />} path="/visao-geral" />
                <Route element={<HistoryPage />} path="/acessos/historico" />
                <Route element={<ProfileRoute />}>
                  <Route
                    element={<h1>Usuários e permissões</h1>}
                    path="/administracao"
                  />
                </Route>
                <Route element={<h1>Página não encontrada</h1>} path="*" />
              </Route>
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionContext.Provider>
    </StrictMode>,
  );
}

afterEach(() => {
  document.title = "";
});

describe("route metadata", () => {
  it.each(routeTitles)("defines the title for %s", (pathname, pageTitle) => {
    expect(getRouteTitle(pathname, "Administrador")).toBe(
      `${pageTitle} | Controle de Acesso de Veículos`,
    );
  });

  it("provides titles for access denied and not found states", () => {
    expect(getRouteTitle("/administracao", "Vigilante")).toBe(
      "Acesso negado | Controle de Acesso de Veículos",
    );
    expect(getRouteTitle("/rota-inexistente", "Administrador")).toBe(
      "Página não encontrada | Controle de Acesso de Veículos",
    );
  });
});

describe("RouteTransitionManager", () => {
  it("focuses the login field on the direct login route", async () => {
    renderTransitions(["/login"]);

    await waitFor(() =>
      expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveFocus(),
    );
    expect(document.title).toBe("Entrar | Controle de Acesso de Veículos");
  });

  it("sets the title and focuses the main heading on a direct route", async () => {
    renderTransitions(["/acessos/historico"]);

    const heading = screen.getByRole("heading", {
      name: "Histórico de acessos",
    });

    await waitFor(() => expect(heading).toHaveFocus());
    expect(heading).toHaveAttribute("tabindex", "-1");
    expect(document.title).toBe(
      "Histórico de acessos | Controle de Acesso de Veículos",
    );
  });

  it("updates title and focus across links, back, and forward navigation", async () => {
    const user = userEvent.setup();
    renderTransitions();

    const overviewHeading = screen.getByRole("heading", {
      name: "Visão geral",
    });
    await waitFor(() => expect(overviewHeading).toHaveFocus());

    await user.click(screen.getByRole("link", { name: "Abrir histórico" }));
    const historyHeading = screen.getByRole("heading", {
      name: "Histórico de acessos",
    });
    await waitFor(() => expect(historyHeading).toHaveFocus());
    expect(document.title).toBe(
      "Histórico de acessos | Controle de Acesso de Veículos",
    );

    await user.click(screen.getByRole("button", { name: "Voltar" }));
    const restoredOverviewHeading = screen.getByRole("heading", {
      name: "Visão geral",
    });
    await waitFor(() => expect(restoredOverviewHeading).toHaveFocus());
    expect(document.title).toBe("Visão geral | Controle de Acesso de Veículos");

    await user.click(screen.getByRole("button", { name: "Avançar" }));
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Histórico de acessos" }),
      ).toHaveFocus(),
    );
  });

  it("focuses the heading after a lazy route module finishes loading", async () => {
    let resolveLazyRoute: (() => void) | undefined;
    const lazyRouteReady = new Promise<void>((resolve) => {
      resolveLazyRoute = resolve;
    });
    const lazyRouter = createMemoryRouter(
      [
        {
          element: <RouteTransitionManager />,
          children: [
            {
              element: (
                <main>
                  <h1>Visão geral</h1>
                  <Link to="/acessos/historico">Abrir histórico</Link>
                </main>
              ),
              path: "/visao-geral",
            },
            {
              lazy: async () => {
                await lazyRouteReady;

                return {
                  Component: () => (
                    <main>
                      <h1>Histórico de acessos</h1>
                    </main>
                  ),
                };
              },
              path: "/acessos/historico",
            },
          ],
        },
      ],
      { initialEntries: ["/visao-geral"] },
    );
    const user = userEvent.setup();

    render(
      <SessionContext.Provider value={sessionValue()}>
        <RouterProvider router={lazyRouter} />
      </SessionContext.Provider>,
    );

    await user.click(screen.getByRole("link", { name: "Abrir histórico" }));

    expect(
      screen.getByRole("heading", { name: "Visão geral" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Histórico de acessos" }),
    ).not.toBeInTheDocument();

    await act(async () => resolveLazyRoute?.());

    const historyHeading = await screen.findByRole("heading", {
      name: "Histórico de acessos",
    });
    await waitFor(() => expect(historyHeading).toHaveFocus());
    expect(document.title).toBe(
      "Histórico de acessos | Controle de Acesso de Veículos",
    );
  });

  it("does not steal focus during updates within the current route", async () => {
    const user = userEvent.setup();
    renderTransitions(["/acessos/historico"]);
    const filter = screen.getByRole("textbox", { name: "Pesquisar" });

    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Histórico de acessos" }),
      ).toHaveFocus(),
    );
    await user.type(filter, "placa fictícia");

    expect(filter).toHaveFocus();
    expect(
      screen.getByText("Filtro atual: placa fictícia"),
    ).toBeInTheDocument();
    expect(document.title).toBe(
      "Histórico de acessos | Controle de Acesso de Veículos",
    );
  });

  it("uses the not found title and focus target", async () => {
    renderTransitions(["/rota-inexistente"]);
    const heading = screen.getByRole("heading", {
      name: "Página não encontrada",
    });

    await waitFor(() => expect(heading).toHaveFocus());
    expect(document.title).toBe(
      "Página não encontrada | Controle de Acesso de Veículos",
    );
  });

  it("uses the access denied title and focuses its heading", async () => {
    renderTransitions(["/administracao"], 0, "Vigilante");
    const heading = screen.getByRole("heading", { name: "Acesso negado" });

    await waitFor(() => expect(heading).toHaveFocus());
    expect(document.title).toBe(
      "Acesso negado | Controle de Acesso de Veículos",
    );
  });
});
