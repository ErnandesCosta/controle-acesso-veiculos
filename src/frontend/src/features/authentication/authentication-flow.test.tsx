import { StrictMode } from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const humanActivityHarness = vi.hoisted(() => ({
  onActivity: undefined as
    ((occurredAtEpochMilliseconds: number) => void) | undefined,
  onResume: undefined as (() => void) | undefined,
}));

vi.mock("./session/humanActivity", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("./session/humanActivity")>();
  return {
    ...actual,
    subscribeToHumanActivity: vi.fn(({ onActivity, onResume }) => {
      humanActivityHarness.onActivity = onActivity;
      humanActivityHarness.onResume = onResume;
      return () => {
        if (humanActivityHarness.onActivity === onActivity) {
          humanActivityHarness.onActivity = undefined;
          humanActivityHarness.onResume = undefined;
        }
      };
    }),
  };
});

import { AppLayout } from "../../components/layout/AppLayout";
import { LoginPage } from "../../pages/LoginPage";
import { PasswordChangePage } from "../../pages/PasswordChangePage";
import { ProfileRoute } from "../../routes/ProfileRoute";
import { ProtectedRoute } from "../../routes/ProtectedRoute";
import { RouteTransitionManager } from "../../routes/RouteTransitionManager";
import {
  api,
  setApiAccessToken,
  setApiSessionRefreshHandler,
  setApiUnauthorizedHandler,
} from "../../services/api";
import { SessionProvider } from "./session/SessionProvider";
import {
  sessionContinuityStorageKey,
  writeSessionContinuity,
} from "./session/sessionContinuity";
import { useAuthenticatedSession } from "./session/useSession";
import type { AuthenticatedSession, ProfileName } from "./types";

function sessionFor(
  profileName: ProfileName,
  expiresInMilliseconds = 60_000,
  requiresPasswordChange = false,
): AuthenticatedSession {
  return {
    absoluteExpiresAtUtc: new Date(Date.now() + 12 * 60 * 60_000).toISOString(),
    accessToken: "test-only-access-token",
    expiresAtUtc: new Date(Date.now() + expiresInMilliseconds).toISOString(),
    inactivityExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
    serverTimeUtc: new Date(Date.now()).toISOString(),
    user: {
      email: "operator@example.test",
      id: 42,
      profileName,
      requiresPasswordChange,
    },
  };
}

function responseFor(
  profileName: ProfileName,
  expiresInMilliseconds = 60_000,
  requiresPasswordChange = false,
) {
  const data = sessionFor(
    profileName,
    expiresInMilliseconds,
    requiresPasswordChange,
  );
  const {
    absoluteExpiresAtUtc,
    inactivityExpiresAtUtc,
    serverTimeUtc,
    ...responseData
  } = data;
  return {
    data: responseData,
    headers: {
      "x-session-absolute-expires-at": absoluteExpiresAtUtc,
      "x-session-inactivity-expires-at": inactivityExpiresAtUtc,
      "x-session-server-time": serverTimeUtc,
    },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

function renderRestrictedApplication(
  initialEntry: string | { pathname: string; state?: unknown } = "/login",
) {
  return render(
    <SessionProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route element={<RouteTransitionManager />}>
            <Route element={<LoginPage />} path="/login" />
            <Route element={<ProtectedRoute />}>
              <Route element={<AppLayout />}>
                <Route element={<ProfileRoute />}>
                  <Route
                    element={<h1>Painel operacional fictício</h1>}
                    path="/visao-geral"
                  />
                  <Route
                    element={<h1>Operação fictícia</h1>}
                    path="/acessos/novo"
                  />
                  <Route element={<PasswordChangePage />} path="/conta/senha" />
                </Route>
              </Route>
            </Route>
          </Route>
        </Routes>
      </MemoryRouter>
    </SessionProvider>,
  );
}

function SessionIdentity() {
  const { completePasswordChange, logout, sessionNotice, user } =
    useAuthenticatedSession();
  return (
    <div>
      <p>
        {user.email} — {user.profileName}
      </p>
      <button onClick={logout} type="button">
        Sair
      </button>
      <button onClick={completePasswordChange} type="button">
        Concluir troca de senha
      </button>
      <label>
        Observação
        <input name="note" />
      </label>
      {sessionNotice === "renewal-unavailable" ? (
        <p role="status">
          Não foi possível renovar a sessão agora. Seus dados foram mantidos.
        </p>
      ) : null}
    </div>
  );
}

function AppFrame() {
  return <Outlet />;
}

function renderAuthenticationFlow(
  initialEntry: string | { pathname: string; state?: unknown } = "/login",
  strictMode = false,
) {
  const application = (
    <SessionProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route element={<LoginPage />} path="/login" />
          <Route element={<ProtectedRoute />}>
            <Route element={<AppFrame />}>
              <Route element={<SessionIdentity />} path="/visao-geral" />
              <Route element={<ProfileRoute />}>
                <Route element={<p>Administração</p>} path="/administracao" />
              </Route>
            </Route>
          </Route>
        </Routes>
      </MemoryRouter>
    </SessionProvider>
  );
  return render(
    strictMode ? <StrictMode>{application}</StrictMode> : application,
  );
}

async function submitCredentials() {
  const user = userEvent.setup();
  await user.type(
    await screen.findByLabelText("E-mail:"),
    "operator@example.test",
  );
  await user.type(screen.getByLabelText("Senha:"), "test-only-password");
  await user.click(screen.getByRole("button", { name: "Entrar" }));
}

beforeEach(() => {
  humanActivityHarness.onActivity = undefined;
  humanActivityHarness.onResume = undefined;
  window.localStorage.clear();
  window.sessionStorage.clear();
  Object.defineProperty(navigator, "locks", {
    configurable: true,
    value: {
      request: vi.fn(
        async (_name: string, _options: object, operation: () => unknown) =>
          operation(),
      ),
    },
  });
  vi.spyOn(api, "get").mockRejectedValue({
    isAxiosError: true,
    response: { status: 401 },
  });
});

afterEach(() => {
  setApiAccessToken(null);
  setApiSessionRefreshHandler(null);
  setApiUnauthorizedHandler(null);
  window.localStorage.clear();
  window.sessionStorage.clear();
});

function seedRestorableSession() {
  const now = Date.now();
  writeSessionContinuity({
    absoluteDeadlineEpochMilliseconds: now + 12 * 60 * 60_000,
    humanDeadlineEpochMilliseconds: now + 15 * 60_000,
    observedAtEpochMilliseconds: now,
  });
}

describe("authentication flow", () => {
  it("restores a renewable session before rendering protected content", async () => {
    seedRestorableSession();
    vi.mocked(api.get).mockResolvedValue({
      data: { requestToken: "test-only-csrf-token" },
    });
    const post = vi
      .spyOn(api, "post")
      .mockResolvedValue(responseFor("Porteiro"));

    renderAuthenticationFlow("/visao-geral");

    expect(screen.getByText("Validando sua sessão…")).toBeInTheDocument();
    expect(
      screen.queryByText("operator@example.test — Porteiro"),
    ).not.toBeInTheDocument();
    expect(
      await screen.findByText("operator@example.test — Porteiro"),
    ).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith("/auth/refresh", null, {
      headers: { "X-CSRF-TOKEN": "test-only-csrf-token" },
      skipSessionRefresh: true,
    });
  });

  it("does not duplicate refresh or continuity writes during a StrictMode remount", async () => {
    seedRestorableSession();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    vi.mocked(api.get).mockResolvedValue({
      data: { requestToken: "test-only-csrf-token" },
    });
    const post = vi
      .spyOn(api, "post")
      .mockResolvedValue(responseFor("Porteiro"));

    renderAuthenticationFlow("/visao-geral", true);

    expect(
      await screen.findByText("operator@example.test — Porteiro"),
    ).toBeInTheDocument();
    expect(
      post.mock.calls.filter(([url]) => url === "/auth/refresh"),
    ).toHaveLength(1);
    expect(
      setItem.mock.calls.filter(([key]) => key === sessionContinuityStorageKey),
    ).toHaveLength(1);
  });

  it("does not request refresh when temporal continuity metadata is absent", async () => {
    const post = vi.spyOn(api, "post");

    renderAuthenticationFlow("/visao-geral");

    expect(
      await screen.findByRole("heading", { name: "Bem-vindo," }),
    ).toBeInTheDocument();
    expect(post.mock.calls.some(([url]) => url === "/auth/refresh")).toBe(
      false,
    );
  });

  it("does not request refresh with expired continuity metadata", async () => {
    const now = Date.now();
    window.localStorage.setItem(
      sessionContinuityStorageKey,
      JSON.stringify({
        absoluteDeadlineEpochMilliseconds: now + 60_000,
        humanDeadlineEpochMilliseconds: now - 1,
        observedAtEpochMilliseconds: now - 15 * 60_000,
        version: 1,
      }),
    );
    const post = vi.spyOn(api, "post");

    renderAuthenticationFlow("/visao-geral");

    expect(
      await screen.findByRole("heading", { name: "Bem-vindo," }),
    ).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
    expect(window.localStorage).toHaveLength(0);
  });

  it("does not restore a discarded application while it is in the background", async () => {
    seedRestorableSession();
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "hidden",
    });
    const post = vi.spyOn(api, "post");

    try {
      renderAuthenticationFlow("/visao-geral");
      expect(
        await screen.findByRole("heading", { name: "Bem-vindo," }),
      ).toBeInTheDocument();
      expect(post).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
    }
  });

  it("uses the identity and profile returned by the API", async () => {
    const post = vi
      .spyOn(api, "post")
      .mockResolvedValue(responseFor("Porteiro"));

    renderAuthenticationFlow();
    await submitCredentials();

    expect(
      await screen.findByText("operator@example.test — Porteiro"),
    ).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith(
      "/auth/login",
      {
        email: "operator@example.test",
        password: "test-only-password",
      },
      { skipSessionRefresh: true },
    );
    const continuity = window.localStorage.getItem(sessionContinuityStorageKey);
    expect(continuity).not.toBeNull();
    expect(continuity).not.toMatch(/token|email|profile|password|user/i);
    expect(window.sessionStorage).toHaveLength(0);
  });

  it("routes a restricted login directly to mandatory password change", async () => {
    vi.spyOn(api, "post").mockResolvedValue(
      responseFor("Porteiro", 60_000, true),
    );

    renderRestrictedApplication();
    await submitCredentials();

    const heading = await screen.findByRole("heading", {
      name: "Crie sua senha permanente",
    });
    expect(heading).toHaveFocus();
    expect(
      screen.getByText(/credencial temporária usada para entrar/),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Senha atual")).toHaveValue("");
    expect(
      screen.queryByText("Painel operacional fictício"),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Abrir menu" }),
    ).not.toBeInTheDocument();
  });

  it("routes a normal login to the requested application page", async () => {
    vi.spyOn(api, "post").mockResolvedValue(
      responseFor("Porteiro", 60_000, false),
    );

    renderRestrictedApplication();
    await submitCredentials();

    expect(
      await screen.findByRole("heading", {
        name: "Painel operacional fictício",
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("navigation")).toBeInTheDocument();
  });

  it("restores a restricted session into password change before rendering a manual operational URL", async () => {
    seedRestorableSession();
    vi.mocked(api.get).mockResolvedValue({
      data: { requestToken: "test-only-csrf-token" },
    });
    vi.spyOn(api, "post").mockResolvedValue(
      responseFor("Vigilante", 60_000, true),
    );

    renderRestrictedApplication("/acessos/novo");

    expect(screen.getByText("Validando sua sessão…")).toBeInTheDocument();
    expect(screen.queryByText("Operação fictícia")).not.toBeInTheDocument();
    expect(
      await screen.findByRole("heading", {
        name: "Crie sua senha permanente",
      }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Operação fictícia")).not.toBeInTheDocument();
  });

  it("allows logout while the account is restricted", async () => {
    const post = vi
      .spyOn(api, "post")
      .mockImplementation(async (url) =>
        url === "/auth/login"
          ? responseFor("SetorTransporte", 60_000, true)
          : { data: undefined },
      );

    renderRestrictedApplication();
    await submitCredentials();
    await screen.findByRole("heading", { name: "Crie sua senha permanente" });
    vi.mocked(api.get).mockResolvedValueOnce({
      data: { requestToken: "logout-test-csrf-token" },
    });
    await userEvent.click(screen.getByRole("button", { name: "Sair" }));

    expect(
      await screen.findByRole("heading", { name: "Bem-vindo," }),
    ).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith(
      "/auth/logout",
      null,
      expect.objectContaining({ skipSessionRefresh: true }),
    );
  });

  it("ends a restricted session after one successful password change and requires a new login", async () => {
    const currentInput = "fictional-current-input";
    const permanentInput = "fictional-permanent-input";
    const consoleSpies = [
      vi.spyOn(console, "error").mockImplementation(() => undefined),
      vi.spyOn(console, "info").mockImplementation(() => undefined),
      vi.spyOn(console, "log").mockImplementation(() => undefined),
      vi.spyOn(console, "warn").mockImplementation(() => undefined),
    ];
    const post = vi
      .spyOn(api, "post")
      .mockImplementation(async (url) =>
        url === "/auth/login"
          ? responseFor("Administrador", 60_000, true)
          : { data: undefined },
      );

    renderRestrictedApplication();
    await submitCredentials();
    await screen.findByRole("heading", { name: "Crie sua senha permanente" });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Senha atual"), currentInput);
    await user.type(screen.getByLabelText("Nova senha"), permanentInput);
    await user.type(
      screen.getByLabelText("Confirmar nova senha"),
      permanentInput,
    );
    await user.dblClick(screen.getByRole("button", { name: "Alterar senha" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Senha alterada com segurança. Entre novamente usando a nova senha.",
    );
    expect(
      post.mock.calls.filter(([url]) => url === "/auth/password"),
    ).toHaveLength(1);
    expect(
      screen.queryByText("Painel operacional fictício"),
    ).not.toBeInTheDocument();
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
    expect(window.location.href).not.toContain(currentInput);
    expect(window.location.href).not.toContain(permanentInput);
    for (const consoleSpy of consoleSpies) {
      expect(JSON.stringify(consoleSpy.mock.calls)).not.toContain(currentInput);
      expect(JSON.stringify(consoleSpy.mock.calls)).not.toContain(
        permanentInput,
      );
    }
  });

  it("keeps invalid, expired, reused, inactive and temporarily blocked credentials indistinguishable", async () => {
    vi.spyOn(api, "post").mockRejectedValue({
      isAxiosError: true,
      response: { data: { message: "Credenciais inválidas." }, status: 401 },
    });

    renderAuthenticationFlow();
    await submitCredentials();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "E-mail ou senha inválidos, ou a conta está temporariamente indisponível.",
    );
  });

  it("presents backend unavailability without confirming a login", async () => {
    vi.spyOn(api, "post").mockRejectedValue(new Error("connection failed"));

    renderAuthenticationFlow();
    await submitCredentials();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Não foi possível conectar ao sistema. Verifique a rede e tente novamente.",
    );
    expect(
      screen.queryByText(/operator@example.test —/),
    ).not.toBeInTheDocument();
  });

  it("redirects unauthenticated users to the login page", async () => {
    renderAuthenticationFlow("/visao-geral");

    expect(
      await screen.findByRole("heading", { name: "Bem-vindo," }),
    ).toBeInTheDocument();
    expect(window.localStorage).toHaveLength(0);
  });

  it("shows an explicit access denied state for an incompatible profile", async () => {
    vi.spyOn(api, "post").mockResolvedValue(responseFor("SetorTransporte"));

    renderAuthenticationFlow({
      pathname: "/login",
      state: { from: { pathname: "/administracao" } },
    });
    await submitCredentials();

    expect(
      await screen.findByRole("heading", { name: "Acesso negado" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Administração")).not.toBeInTheDocument();
  });

  it("clears the local session on logout", async () => {
    vi.spyOn(api, "post").mockImplementation(async (url) =>
      url === "/auth/login"
        ? responseFor("Administrador")
        : { data: undefined },
    );

    renderAuthenticationFlow();
    await submitCredentials();
    await screen.findByText("operator@example.test — Administrador");
    vi.mocked(api.get).mockResolvedValueOnce({
      data: { requestToken: "test-only-csrf-token" },
    });
    await userEvent.click(screen.getByRole("button", { name: "Sair" }));

    expect(
      await screen.findByRole("heading", { name: "Bem-vindo," }),
    ).toBeInTheDocument();
    expect(window.localStorage).toHaveLength(0);
  });

  it("clears the local session and announces a completed password change", async () => {
    vi.spyOn(api, "post").mockResolvedValue(responseFor("Porteiro"));

    renderAuthenticationFlow();
    await submitCredentials();
    await screen.findByText("operator@example.test — Porteiro");
    await userEvent.click(
      screen.getByRole("button", { name: "Concluir troca de senha" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Senha alterada com segurança. Entre novamente usando a nova senha.",
    );
    expect(screen.getByLabelText("E-mail:")).toHaveAttribute(
      "aria-describedby",
      "login-status-message",
    );
    expect(
      screen.queryByText("operator@example.test — Porteiro"),
    ).not.toBeInTheDocument();
  });

  it("clears the local session even when server logout cannot be confirmed", async () => {
    vi.spyOn(api, "post").mockResolvedValue(responseFor("Administrador"));

    renderAuthenticationFlow();
    await submitCredentials();
    await screen.findByText("operator@example.test — Administrador");
    vi.mocked(api.get).mockRejectedValueOnce(new Error("network unavailable"));
    await userEvent.click(screen.getByRole("button", { name: "Sair" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "A sessão foi encerrada neste dispositivo, mas não foi possível confirmar a saída no servidor.",
    );
    expect(screen.getByLabelText("E-mail:")).toHaveAttribute(
      "aria-describedby",
      "login-status-message",
    );
    expect(
      screen.queryByText("operator@example.test — Administrador"),
    ).not.toBeInTheDocument();
  });

  it("keeps form values during a transient transparent renewal failure", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.mocked(api.get).mockRejectedValue(new Error("network unavailable"));
      vi.spyOn(api, "post").mockResolvedValue(responseFor("Porteiro", 120_000));
      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });
      fireEvent.change(screen.getByLabelText("Observação"), {
        target: { value: "Dado fictício em preenchimento" },
      });

      await act(async () => vi.advanceTimersByTimeAsync(60_000));

      expect(screen.getByLabelText("Observação")).toHaveValue(
        "Dado fictício em preenchimento",
      );
      expect(
        screen.getByText(/Não foi possível renovar a sessão agora/),
      ).toHaveAttribute("role", "status");
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps a renewable session when only the access token expires", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.mocked(api.get).mockRejectedValue(new Error("connection failed"));
      vi.spyOn(api, "post").mockResolvedValue(responseFor("Vigilante", 60_000));

      renderAuthenticationFlow();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      const email = screen.getByLabelText("E-mail:");
      fireEvent.change(email, {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(
        screen.getByText("operator@example.test — Vigilante"),
      ).toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(60_000);
      });

      expect(
        screen.getByText("operator@example.test — Vigilante"),
      ).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(window.localStorage).not.toHaveLength(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it("waits for human activity after the access token expires in the background", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.mocked(api.get).mockResolvedValue({
        data: { requestToken: "test-only-csrf-token" },
      });
      const post = vi
        .spyOn(api, "post")
        .mockResolvedValue(responseFor("Porteiro", 120_000));
      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "hidden",
      });

      await act(async () => vi.advanceTimersByTimeAsync(120_000));

      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(0);
      expect(
        screen.getByText("operator@example.test — Porteiro"),
      ).toBeInTheDocument();
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      act(() => humanActivityHarness.onResume?.());
      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(0);
      expect(
        screen.getByText("operator@example.test — Porteiro"),
      ).toBeInTheDocument();

      await act(async () => {
        humanActivityHarness.onActivity?.(Date.now());
        humanActivityHarness.onActivity?.(Date.now());
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(1);
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      vi.useRealTimers();
    }
  });

  it("reconciles newer human activity persisted by another tab after suspension", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.spyOn(api, "post").mockResolvedValue(
        responseFor("Vigilante", 30 * 60_000),
      );
      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
        await vi.advanceTimersByTimeAsync(10 * 60_000);
      });
      const previous = JSON.parse(
        window.localStorage.getItem(sessionContinuityStorageKey) ?? "null",
      ) as {
        absoluteDeadlineEpochMilliseconds: number;
        version: number;
      };
      window.localStorage.setItem(
        sessionContinuityStorageKey,
        JSON.stringify({
          absoluteDeadlineEpochMilliseconds:
            previous.absoluteDeadlineEpochMilliseconds,
          humanDeadlineEpochMilliseconds: Date.now() + 15 * 60_000,
          observedAtEpochMilliseconds: Date.now(),
          version: previous.version,
        }),
      );

      act(() => humanActivityHarness.onResume?.());

      const canonical = JSON.parse(
        window.localStorage.getItem(sessionContinuityStorageKey) ?? "null",
      ) as {
        absoluteDeadlineEpochMilliseconds: number;
        humanDeadlineEpochMilliseconds: number;
      };
      expect(canonical.humanDeadlineEpochMilliseconds).toBe(
        Date.now() + 15 * 60_000,
      );
      expect(canonical.absoluteDeadlineEpochMilliseconds).toBe(
        previous.absoluteDeadlineEpochMilliseconds,
      );
      expect(
        screen.getByText("operator@example.test — Vigilante"),
      ).toBeInTheDocument();
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      vi.useRealTimers();
    }
  });

  it("does not let an HTTP 401 bypass a renewal awaiting human activity", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      const post = vi
        .spyOn(api, "post")
        .mockResolvedValue(responseFor("Porteiro", 120_000));
      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "hidden",
      });
      await act(async () => vi.advanceTimersByTimeAsync(60_000));
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      act(() => humanActivityHarness.onResume?.());
      vi.mocked(api.get).mockRestore();

      await expect(
        api.get("/protected", {
          adapter: async (config) =>
            Promise.reject({
              config,
              isAxiosError: true,
              response: { config, data: null, headers: {}, status: 401 },
            }),
        }),
      ).rejects.toMatchObject({ isAxiosError: true });

      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(0);
      vi.spyOn(api, "get").mockResolvedValue({
        data: { requestToken: "test-only-csrf-token" },
      });
      await act(async () => {
        humanActivityHarness.onActivity?.(Date.now());
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(1);
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      vi.useRealTimers();
    }
  });

  it("clears a pending background renewal when the operator logs out", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.mocked(api.get).mockResolvedValue({
        data: { requestToken: "test-only-csrf-token" },
      });
      const post = vi
        .spyOn(api, "post")
        .mockImplementation(async (url) =>
          url === "/auth/login"
            ? responseFor("Administrador", 120_000)
            : { data: undefined },
        );
      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });
      const staleActivityCallback = humanActivityHarness.onActivity;
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "hidden",
      });
      await act(async () => vi.advanceTimersByTimeAsync(60_000));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Sair" }));
        await vi.advanceTimersByTimeAsync(0);
      });
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      act(() => staleActivityCallback?.(Date.now()));

      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(0);
      expect(window.localStorage).toHaveLength(0);
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      vi.useRealTimers();
    }
  });

  it("ends safely when resumed continuity metadata is corrupt", async () => {
    vi.spyOn(api, "post").mockResolvedValue(responseFor("Porteiro"));
    renderAuthenticationFlow();
    await submitCredentials();
    await screen.findByText("operator@example.test — Porteiro");
    window.localStorage.setItem(sessionContinuityStorageKey, "not-json");

    act(() => humanActivityHarness.onResume?.());

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Sua sessão terminou por inatividade. Entre novamente para continuar.",
    );
    expect(window.localStorage).toHaveLength(0);
  });

  it("ends and revokes the session after fifteen minutes without human activity", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));

    try {
      vi.mocked(api.get).mockResolvedValue({
        data: { requestToken: "test-only-csrf-token" },
      });
      const post = vi
        .spyOn(api, "post")
        .mockImplementation(async (url) =>
          url === "/auth/login"
            ? responseFor("Porteiro", 20 * 60_000)
            : { data: undefined },
        );

      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(
        screen.getByText("operator@example.test — Porteiro"),
      ).toBeInTheDocument();
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "hidden",
      });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15 * 60_000);
      });

      expect(screen.getByRole("alert")).toHaveTextContent(
        "Sua sessão terminou por inatividade. Entre novamente para continuar.",
      );
      expect(screen.getByLabelText("E-mail:")).toHaveAttribute(
        "aria-describedby",
        "login-status-message",
      );
      expect(
        post.mock.calls.filter(([url]) => url === "/auth/logout"),
      ).toHaveLength(1);
      expect(window.localStorage).toHaveLength(0);
      act(() => humanActivityHarness.onActivity?.(Date.now()));
      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(0);
    } finally {
      Object.defineProperty(document, "visibilityState", {
        configurable: true,
        value: "visible",
      });
      vi.useRealTimers();
    }
  });

  it("does not restore a session when refresh finishes after inactivity", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-06-10T12:00:00Z"));
    const refreshResponse = deferred<ReturnType<typeof responseFor>>();

    try {
      vi.mocked(api.get).mockResolvedValue({
        data: { requestToken: "test-only-csrf-token" },
      });
      const post = vi.spyOn(api, "post").mockImplementation(async (url) => {
        if (url === "/auth/login") {
          return responseFor("Vigilante", 15 * 60_000 + 30_000);
        }
        if (url === "/auth/refresh") return refreshResponse.promise;
        return { data: undefined };
      });

      renderAuthenticationFlow();
      await act(async () => vi.advanceTimersByTimeAsync(0));
      fireEvent.change(screen.getByLabelText("E-mail:"), {
        target: { value: "operator@example.test" },
      });
      fireEvent.change(screen.getByLabelText("Senha:"), {
        target: { value: "test-only-password" },
      });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Entrar" }));
        await vi.advanceTimersByTimeAsync(0);
      });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(14 * 60_000 + 30_000);
      });
      expect(
        post.mock.calls.filter(([url]) => url === "/auth/refresh"),
      ).toHaveLength(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(30_000);
      });
      refreshResponse.resolve(responseFor("Vigilante", 15 * 60_000));
      await act(async () => vi.advanceTimersByTimeAsync(0));

      expect(screen.getByRole("alert")).toHaveTextContent(
        "Sua sessão terminou por inatividade. Entre novamente para continuar.",
      );
      expect(
        screen.queryByText("operator@example.test — Vigilante"),
      ).not.toBeInTheDocument();
    } finally {
      refreshResponse.reject(new Error("test cleanup"));
      vi.useRealTimers();
    }
  });

  it("ends the session when a retried authenticated request returns 401", async () => {
    vi.mocked(api.get).mockRestore();
    const unauthorizedHandler = vi.fn();
    setApiAccessToken("test-only-access-token");
    setApiSessionRefreshHandler(async () => "renewed-test-token");
    setApiUnauthorizedHandler(unauthorizedHandler);

    let attempts = 0;

    await expect(
      api.get("/protected", {
        adapter: async (config) => {
          attempts += 1;
          return Promise.reject({
            config,
            isAxiosError: true,
            response: { config, data: null, headers: {}, status: 401 },
          });
        },
      }),
    ).rejects.toMatchObject({ isAxiosError: true });

    expect(attempts).toBe(2);
    expect(unauthorizedHandler).toHaveBeenCalledOnce();
  });
});
