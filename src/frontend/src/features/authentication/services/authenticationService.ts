import { api } from "../../../services/api";
import { loginResponseSchema, loginSchema } from "../schemas/loginSchema";
import type { AuthenticatedSession, LoginCredentials } from "../types";
import { z } from "zod";

const csrfResponseSchema = z
  .object({ requestToken: z.string().min(1) })
  .strict();

const sessionRequestConfig = {
  skipSessionRefresh: true,
} as const;
const logoutConfirmationTimeoutMilliseconds = 4_000;
const inactivityWindowMilliseconds = 15 * 60 * 1_000;
const absoluteLifetimeMilliseconds = 12 * 60 * 60 * 1_000;

export class AuthenticationContractError extends Error {
  constructor() {
    super("The authentication response does not match the expected contract.");
    this.name = "AuthenticationContractError";
  }
}

export async function authenticate(
  credentials: LoginCredentials,
): Promise<AuthenticatedSession> {
  const request = loginSchema.parse(credentials);
  const response = await api.post<unknown>(
    "/auth/login",
    request,
    sessionRequestConfig,
  );
  return parseAuthenticatedSession(response.data, response.headers);
}

export async function refreshAuthentication(): Promise<AuthenticatedSession> {
  const requestToken = await requestCsrfToken();
  const response = await api.post<unknown>("/auth/refresh", null, {
    ...sessionRequestConfig,
    headers: { "X-CSRF-TOKEN": requestToken },
  });
  return parseAuthenticatedSession(response.data, response.headers);
}

export async function logoutAuthentication(): Promise<void> {
  const requestToken = await requestCsrfToken(
    logoutConfirmationTimeoutMilliseconds,
  );
  await api.post("/auth/logout", null, {
    ...sessionRequestConfig,
    headers: { "X-CSRF-TOKEN": requestToken },
    timeout: logoutConfirmationTimeoutMilliseconds,
  });
}

async function requestCsrfToken(timeout?: number): Promise<string> {
  const response = await api.get<unknown>("/auth/csrf", {
    ...sessionRequestConfig,
    ...(timeout === undefined ? {} : { timeout }),
  });
  const parsedResponse = csrfResponseSchema.safeParse(response.data);

  if (!parsedResponse.success) {
    throw new AuthenticationContractError();
  }

  return parsedResponse.data.requestToken;
}

function parseAuthenticatedSession(
  data: unknown,
  headers: unknown,
): AuthenticatedSession {
  const parsedResponse = loginResponseSchema.safeParse(data);
  const inactivityExpiresAtUtc = readHeader(
    headers,
    "X-Session-Inactivity-Expires-At",
  );
  const absoluteExpiresAtUtc = readHeader(
    headers,
    "X-Session-Absolute-Expires-At",
  );
  const serverTimeUtc = readHeader(headers, "X-Session-Server-Time");

  if (
    !parsedResponse.success ||
    !isUtcTimestamp(inactivityExpiresAtUtc) ||
    !isUtcTimestamp(absoluteExpiresAtUtc) ||
    !isUtcTimestamp(serverTimeUtc)
  ) {
    throw new AuthenticationContractError();
  }

  const inactivityExpiresAt = Date.parse(inactivityExpiresAtUtc);
  const absoluteExpiresAt = Date.parse(absoluteExpiresAtUtc);
  const serverTime = Date.parse(serverTimeUtc);
  const accessTokenExpiresAt = Date.parse(parsedResponse.data.expiresAtUtc);
  const inactivityDuration = inactivityExpiresAt - serverTime;
  const absoluteDuration = absoluteExpiresAt - serverTime;

  if (
    inactivityDuration <= 0 ||
    inactivityDuration > inactivityWindowMilliseconds ||
    absoluteDuration <= 0 ||
    absoluteDuration > absoluteLifetimeMilliseconds ||
    inactivityExpiresAt > absoluteExpiresAt ||
    accessTokenExpiresAt <= serverTime ||
    accessTokenExpiresAt > absoluteExpiresAt
  ) {
    throw new AuthenticationContractError();
  }

  return {
    ...parsedResponse.data,
    absoluteExpiresAtUtc,
    inactivityExpiresAtUtc,
    serverTimeUtc,
  };
}

function readHeader(headers: unknown, name: string): string | null {
  if (typeof headers !== "object" || headers === null) return null;

  if ("get" in headers && typeof headers.get === "function") {
    const value = headers.get(name);
    if (typeof value === "string") return value;
  }

  const record = headers as Record<string, unknown>;
  const value = record[name] ?? record[name.toLowerCase()];
  return typeof value === "string" ? value : null;
}

function isUtcTimestamp(value: string | null): value is string {
  if (!value || !/(?:Z|[+-]00:00)$/i.test(value)) return false;
  return Number.isFinite(Date.parse(value));
}
