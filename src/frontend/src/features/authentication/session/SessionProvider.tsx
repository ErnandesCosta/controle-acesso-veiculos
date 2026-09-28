import axios from "axios";
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

import {
  setApiAccessToken,
  setApiSessionRefreshHandler,
  setApiUnauthorizedHandler,
} from "../../../services/api";
import {
  AuthenticationContractError,
  authenticate,
  logoutAuthentication,
  refreshAuthentication,
} from "../services/authenticationService";
import type {
  AuthenticatedSession,
  AuthenticatedUser,
  LoginCredentials,
  SessionEndReason,
  SessionNotice,
} from "../types";
import { SessionContext, type SessionContextValue } from "./SessionContext";
import { subscribeToHumanActivity } from "./humanActivity";
import {
  clearSessionContinuity,
  readSessionContinuity,
  writeSessionContinuity,
} from "./sessionContinuity";
import {
  broadcastHumanActivity,
  broadcastSessionEnded,
  runWithSessionRefreshLock,
  subscribeToSessionEvents,
} from "./sessionCoordination";
import {
  InvalidSessionDeadlineError,
  SessionInactiveError,
  SessionInactivityMonitor,
} from "./sessionInactivity";

interface SessionState {
  expiresAtUtc: string | null;
  sessionEndReason: SessionEndReason;
  sessionNotice: SessionNotice;
  status: SessionContextValue["status"];
  user: AuthenticatedUser | null;
}

const unauthenticatedState: SessionState = {
  expiresAtUtc: null,
  sessionEndReason: null,
  sessionNotice: null,
  status: "unauthenticated",
  user: null,
};

const initialState: SessionState = {
  ...unauthenticatedState,
  status: "restoring",
};

const renewalLeadMilliseconds = 60_000;
const renewalRetryMilliseconds = 15_000;

class BackgroundSessionRefreshError extends Error {}
class PendingHumanActivityError extends Error {}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState(initialState);
  const expirationTimer = useRef<number | null>(null);
  const refreshInFlight = useRef<Promise<AuthenticatedSession> | null>(null);
  const renewalPending = useRef(false);
  const renewalTimer = useRef<number | null>(null);
  const sessionGeneration = useRef(0);
  const currentExpiration = useRef<number | null>(null);
  const mounted = useRef(false);
  const inactivityExpiredRef = useRef<() => void>(() => undefined);
  const inactivityMonitor = useRef<SessionInactivityMonitor | null>(null);
  const endSessionRef = useRef<
    (reason: SessionEndReason, broadcast: boolean) => void
  >(() => undefined);
  const refreshAccessTokenRef = useRef<
    (authorizedByHumanActivity?: boolean) => Promise<string>
  >(async () => {
    throw new Error("Session refresh is not initialized.");
  });

  useEffect(() => {
    const monitor = new SessionInactivityMonitor({
      onExpired: () => inactivityExpiredRef.current(),
    });
    inactivityMonitor.current = monitor;

    return () => {
      monitor.stop();
      if (inactivityMonitor.current === monitor) {
        inactivityMonitor.current = null;
      }
    };
  }, []);

  const clearTimers = useCallback(() => {
    if (expirationTimer.current !== null) {
      window.clearTimeout(expirationTimer.current);
      expirationTimer.current = null;
    }
    if (renewalTimer.current !== null) {
      window.clearTimeout(renewalTimer.current);
      renewalTimer.current = null;
    }
    currentExpiration.current = null;
    renewalPending.current = false;
  }, []);

  const endSession = useCallback(
    (reason: SessionEndReason, broadcast: boolean) => {
      sessionGeneration.current += 1;
      clearTimers();
      inactivityMonitor.current?.stop();
      clearSessionContinuity();
      setApiAccessToken(null);
      setState({ ...unauthenticatedState, sessionEndReason: reason });
      if (broadcast) {
        broadcastSessionEnded(reason === "inactive" ? "inactivity" : undefined);
      }
    },
    [clearTimers],
  );

  const scheduleRenewalRetry = useCallback(() => {
    if (
      currentExpiration.current === null ||
      currentExpiration.current <= Date.now() + 1_000
    ) {
      return;
    }

    const delay = Math.min(
      renewalRetryMilliseconds,
      currentExpiration.current - Date.now() - 1_000,
    );
    renewalTimer.current = window.setTimeout(() => {
      renewalTimer.current = null;
      if (document.visibilityState !== "visible") {
        renewalPending.current = true;
        return;
      }
      if (renewalPending.current) return;
      void refreshAccessTokenRef.current().catch(() => undefined);
    }, delay);
  }, []);

  const applySession = useCallback(
    (session: AuthenticatedSession, refreshed = false) => {
      const expiresAt = Date.parse(session.expiresAtUtc);
      const serverTime = Date.parse(session.serverTimeUtc);
      const expiresInMilliseconds = expiresAt - serverTime;

      if (
        !Number.isFinite(expiresAt) ||
        !Number.isFinite(serverTime) ||
        expiresInMilliseconds <= 0
      ) {
        throw new AuthenticationContractError();
      }

      try {
        const deadlines = {
          absoluteExpiresAtUtc: session.absoluteExpiresAtUtc,
          inactivityExpiresAtUtc: session.inactivityExpiresAtUtc,
          serverTimeUtc: session.serverTimeUtc,
        };
        const monitor = inactivityMonitor.current;
        if (!monitor) throw new InvalidSessionDeadlineError();
        if (refreshed) monitor.reconcile(deadlines);
        else monitor.start(deadlines);
        const snapshot = monitor.getSnapshot();
        if (!snapshot || !writeSessionContinuity(snapshot)) {
          throw new InvalidSessionDeadlineError();
        }
      } catch (error) {
        if (error instanceof InvalidSessionDeadlineError) {
          throw new AuthenticationContractError();
        }
        throw error;
      }

      clearTimers();
      currentExpiration.current = Date.now() + expiresInMilliseconds;
      setApiAccessToken(session.accessToken);
      expirationTimer.current = window.setTimeout(() => {
        expirationTimer.current = null;
        renewalPending.current = true;
        setApiAccessToken(null);
      }, expiresInMilliseconds);
      const leadTime = Math.min(
        renewalLeadMilliseconds,
        Math.max(1_000, Math.floor(expiresInMilliseconds / 2)),
      );
      renewalTimer.current = window.setTimeout(
        () => {
          renewalTimer.current = null;
          if (document.visibilityState !== "visible") {
            renewalPending.current = true;
            return;
          }
          if (renewalPending.current) return;
          void refreshAccessTokenRef.current().catch(() => undefined);
        },
        Math.max(0, expiresInMilliseconds - leadTime),
      );
      setState({
        expiresAtUtc: session.expiresAtUtc,
        sessionEndReason: null,
        sessionNotice: null,
        status: "authenticated",
        user: session.user,
      });
    },
    [clearTimers],
  );

  const requestRefresh = useCallback((authorizedByHumanActivity = false) => {
    if (refreshInFlight.current) return refreshInFlight.current;
    if (document.visibilityState !== "visible") {
      return Promise.reject(new BackgroundSessionRefreshError());
    }
    if (renewalPending.current && !authorizedByHumanActivity) {
      return Promise.reject(new PendingHumanActivityError());
    }

    const request = runWithSessionRefreshLock(async () => {
      if (document.visibilityState !== "visible") {
        throw new BackgroundSessionRefreshError();
      }
      if (renewalPending.current && !authorizedByHumanActivity) {
        throw new PendingHumanActivityError();
      }
      if (!inactivityMonitor.current?.checkNow()) {
        throw new SessionInactiveError();
      }
      return refreshAuthentication();
    }).finally(() => {
      if (refreshInFlight.current === request) {
        refreshInFlight.current = null;
      }
    });
    refreshInFlight.current = request;
    return request;
  }, []);

  const refreshAccessToken = useCallback(
    async (authorizedByHumanActivity = false) => {
      const generation = sessionGeneration.current;

      try {
        const session = await requestRefresh(authorizedByHumanActivity);
        if (generation !== sessionGeneration.current) {
          throw new Error("Session changed while refresh was in progress.");
        }
        if (mounted.current) applySession(session, true);
        else setApiAccessToken(session.accessToken);
        return session.accessToken;
      } catch (error) {
        const rejected =
          axios.isAxiosError(error) && error.response?.status === 401;
        const invalidContract = error instanceof AuthenticationContractError;
        const inactive = error instanceof SessionInactiveError;
        const backgrounded = error instanceof BackgroundSessionRefreshError;
        const awaitingHumanActivity =
          error instanceof PendingHumanActivityError;

        if (inactive) {
          // The inactivity monitor already cleared and revoked the session.
        } else if (backgrounded) {
          // Background tabs cannot keep a shared tablet session alive.
          renewalPending.current = true;
        } else if (awaitingHumanActivity) {
          // Automatic requests cannot bypass a renewal awaiting human activity.
        } else if (rejected || invalidContract) {
          if (mounted.current) endSession("unauthorized", true);
          else setApiAccessToken(null);
        } else if (mounted.current) {
          setState((current) =>
            current.status === "authenticated"
              ? { ...current, sessionNotice: "renewal-unavailable" }
              : current,
          );
          scheduleRenewalRetry();
        }

        throw error;
      }
    },
    [applySession, endSession, requestRefresh, scheduleRenewalRetry],
  );

  useEffect(() => {
    endSessionRef.current = endSession;
    refreshAccessTokenRef.current = refreshAccessToken;
    inactivityExpiredRef.current = () => {
      endSession("inactive", true);
      void logoutAuthentication().catch(() => undefined);
    };
  }, [endSession, refreshAccessToken]);

  useEffect(() => {
    mounted.current = true;
    setApiSessionRefreshHandler(refreshAccessToken);
    setApiUnauthorizedHandler(() => endSession("unauthorized", true));
    const unsubscribe = subscribeToSessionEvents((event) => {
      if (event.type === "human-activity") {
        const monitor = inactivityMonitor.current;
        if (monitor?.recordHumanActivity(event.occurredAtEpochMilliseconds)) {
          const snapshot = monitor.getSnapshot();
          if (snapshot) writeSessionContinuity(snapshot);
        }
        return;
      }
      endSession(
        event.reason === "inactivity" ? "inactive" : "unauthorized",
        false,
      );
    });
    let active = true;
    const restorationGeneration = sessionGeneration.current;
    const continuity = readSessionContinuity();
    let canRestore =
      document.visibilityState === "visible" && continuity !== null;
    const finishWithoutRestoration = () => {
      queueMicrotask(() => {
        if (active) setState(unauthenticatedState);
      });
    };

    if (!canRestore) {
      setApiAccessToken(null);
      finishWithoutRestoration();
    } else {
      try {
        if (!inactivityMonitor.current || !continuity) {
          throw new InvalidSessionDeadlineError();
        }
        inactivityMonitor.current.restore(continuity);
      } catch {
        canRestore = false;
        clearSessionContinuity();
        setApiAccessToken(null);
        finishWithoutRestoration();
      }
    }

    if (canRestore && inactivityMonitor.current?.checkNow()) {
      void requestRefresh()
        .then((session) => {
          if (active && restorationGeneration === sessionGeneration.current) {
            applySession(session, true);
          }
        })
        .catch((error: unknown) => {
          if (!active) return;
          const noRenewableSession =
            axios.isAxiosError(error) && error.response?.status === 401;
          setApiAccessToken(null);
          clearSessionContinuity();
          setState({
            ...unauthenticatedState,
            sessionEndReason: noRenewableSession
              ? null
              : "restoration-unavailable",
          });
        });
    }

    return () => {
      active = false;
      mounted.current = false;
      unsubscribe();
      setApiSessionRefreshHandler(null);
      setApiUnauthorizedHandler(null);
      setApiAccessToken(null);
      clearTimers();
      inactivityMonitor.current?.stop();
    };
  }, [
    applySession,
    clearTimers,
    endSession,
    refreshAccessToken,
    requestRefresh,
  ]);

  useEffect(() => {
    if (state.status !== "authenticated") return;

    return subscribeToHumanActivity({
      onActivity: (occurredAtEpochMilliseconds) => {
        if (
          inactivityMonitor.current?.recordHumanActivity(
            occurredAtEpochMilliseconds,
          )
        ) {
          const snapshot = inactivityMonitor.current.getSnapshot();
          if (snapshot) writeSessionContinuity(snapshot);
          broadcastHumanActivity(occurredAtEpochMilliseconds);
          const expiration = currentExpiration.current;
          const shouldRenew =
            renewalPending.current ||
            (expiration !== null &&
              expiration <= Date.now() + renewalLeadMilliseconds);
          if (shouldRenew && !refreshInFlight.current) {
            renewalPending.current = false;
            void refreshAccessTokenRef.current(true).catch(() => undefined);
          }
        }
      },
      onResume: () => {
        const continuity = readSessionContinuity();
        const expiration = currentExpiration.current;
        if (!continuity) {
          endSession("inactive", true);
          return;
        }
        if (expiration === null) {
          endSession("expired", true);
          return;
        }
        if (expiration <= Date.now()) {
          setApiAccessToken(null);
          renewalPending.current = true;
        }
        try {
          const canonical =
            inactivityMonitor.current?.reconcileSnapshot(continuity);
          if (!canonical || !writeSessionContinuity(canonical)) {
            throw new InvalidSessionDeadlineError();
          }
          if (expiration <= Date.now() + renewalLeadMilliseconds) {
            if (renewalTimer.current !== null) {
              window.clearTimeout(renewalTimer.current);
              renewalTimer.current = null;
            }
            renewalPending.current = true;
          }
        } catch {
          endSession("inactive", true);
        }
      },
    });
  }, [endSession, state.status]);

  const login = useCallback(
    async (credentials: LoginCredentials) => {
      sessionGeneration.current += 1;
      clearTimers();
      setApiAccessToken(null);
      setState({ ...unauthenticatedState, status: "authenticating" });

      try {
        const session = await authenticate(credentials);
        applySession(session);
        return session.user;
      } catch (error) {
        endSession(null, false);
        throw error;
      }
    },
    [applySession, clearTimers, endSession],
  );

  const logout = useCallback(async () => {
    endSession(null, true);
    const logoutGeneration = sessionGeneration.current;

    try {
      await logoutAuthentication();
    } catch {
      if (mounted.current && sessionGeneration.current === logoutGeneration) {
        setState({
          ...unauthenticatedState,
          sessionEndReason: "logout-unconfirmed",
        });
      }
    }
  }, [endSession]);

  const completePasswordChange = useCallback(() => {
    endSession("password-changed", true);
  }, [endSession]);

  const value = useMemo<SessionContextValue>(
    () => ({ ...state, completePasswordChange, login, logout }),
    [completePasswordChange, login, logout, state],
  );

  return (
    <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
  );
}
