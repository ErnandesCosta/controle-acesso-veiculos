import { afterEach, describe, expect, it, vi } from "vitest";

import { api } from "../../../services/api";
import {
  AuthenticationContractError,
  authenticate,
  logoutAuthentication,
  refreshAuthentication,
} from "./authenticationService";

const session = {
  accessToken: "test-only-access-token",
  expiresAtUtc: "2030-06-10T13:00:00Z",
  user: {
    email: "operator@example.test",
    id: 42,
    profileName: "Porteiro",
    requiresPasswordChange: false,
  },
};

const sessionHeaders = {
  "x-session-absolute-expires-at": "2030-06-10T23:00:00Z",
  "x-session-inactivity-expires-at": "2030-06-10T12:15:00Z",
  "x-session-server-time": "2030-06-10T12:00:00Z",
};

afterEach(() => vi.restoreAllMocks());

describe("authentication session service", () => {
  it("gets a fresh CSRF token before refreshing with cookies enabled globally", async () => {
    const get = vi
      .spyOn(api, "get")
      .mockResolvedValue({ data: { requestToken: "test-only-csrf-token" } });
    const post = vi
      .spyOn(api, "post")
      .mockResolvedValue({ data: session, headers: sessionHeaders });

    await expect(refreshAuthentication()).resolves.toEqual({
      ...session,
      absoluteExpiresAtUtc: sessionHeaders["x-session-absolute-expires-at"],
      inactivityExpiresAtUtc: sessionHeaders["x-session-inactivity-expires-at"],
      serverTimeUtc: sessionHeaders["x-session-server-time"],
    });
    expect(api.defaults.withCredentials).toBe(true);
    expect(get).toHaveBeenCalledWith("/auth/csrf", {
      skipSessionRefresh: true,
    });
    expect(post).toHaveBeenCalledWith("/auth/refresh", null, {
      headers: { "X-CSRF-TOKEN": "test-only-csrf-token" },
      skipSessionRefresh: true,
    });
    expect(get.mock.invocationCallOrder[0]).toBeLessThan(
      post.mock.invocationCallOrder[0],
    );
  });

  it("gets a fresh CSRF token before server logout", async () => {
    vi.spyOn(api, "get").mockResolvedValue({
      data: { requestToken: "logout-test-csrf-token" },
    });
    const post = vi.spyOn(api, "post").mockResolvedValue({ data: undefined });

    await logoutAuthentication();

    expect(post).toHaveBeenCalledWith("/auth/logout", null, {
      headers: { "X-CSRF-TOKEN": "logout-test-csrf-token" },
      skipSessionRefresh: true,
      timeout: 4_000,
    });
    expect(api.get).toHaveBeenCalledWith("/auth/csrf", {
      skipSessionRefresh: true,
      timeout: 4_000,
    });
  });

  it("rejects an invalid CSRF response without calling refresh", async () => {
    vi.spyOn(api, "get").mockResolvedValue({ data: {} });
    const post = vi.spyOn(api, "post");

    await expect(refreshAuthentication()).rejects.toBeInstanceOf(
      AuthenticationContractError,
    );
    expect(post).not.toHaveBeenCalled();
  });

  it("rejects a login response without the mandatory password-change indicator", async () => {
    const incompleteUser = {
      email: session.user.email,
      id: session.user.id,
      profileName: session.user.profileName,
    };
    vi.spyOn(api, "post").mockResolvedValue({
      data: { ...session, user: incompleteUser },
      headers: sessionHeaders,
    });

    await expect(
      authenticate({
        email: "operator@example.test",
        password: "fictional-input-only",
      }),
    ).rejects.toBeInstanceOf(AuthenticationContractError);
  });

  it("rejects a refresh response without the mandatory password-change indicator", async () => {
    const incompleteUser = {
      email: session.user.email,
      id: session.user.id,
      profileName: session.user.profileName,
    };
    vi.spyOn(api, "get").mockResolvedValue({
      data: { requestToken: "test-only-csrf-token" },
    });
    vi.spyOn(api, "post").mockResolvedValue({
      data: { ...session, user: incompleteUser },
      headers: sessionHeaders,
    });

    await expect(refreshAuthentication()).rejects.toBeInstanceOf(
      AuthenticationContractError,
    );
  });

  it.each([
    {},
    {
      "x-session-absolute-expires-at":
        sessionHeaders["x-session-absolute-expires-at"],
      "x-session-inactivity-expires-at":
        sessionHeaders["x-session-inactivity-expires-at"],
    },
    {
      "x-session-inactivity-expires-at":
        sessionHeaders["x-session-inactivity-expires-at"],
      "x-session-server-time": sessionHeaders["x-session-server-time"],
    },
    {
      "x-session-absolute-expires-at":
        sessionHeaders["x-session-absolute-expires-at"],
      "x-session-inactivity-expires-at": "not-a-timestamp",
      "x-session-server-time": sessionHeaders["x-session-server-time"],
    },
    {
      "x-session-absolute-expires-at": "2030-06-10T12:10:00Z",
      "x-session-inactivity-expires-at":
        sessionHeaders["x-session-inactivity-expires-at"],
      "x-session-server-time": sessionHeaders["x-session-server-time"],
    },
    {
      ...sessionHeaders,
      "x-session-server-time": "2030-06-10T12:16:00Z",
    },
    {
      ...sessionHeaders,
      "x-session-inactivity-expires-at": "2030-06-10T12:15:00.001Z",
    },
    {
      ...sessionHeaders,
      "x-session-absolute-expires-at": "2030-06-11T00:00:00.001Z",
    },
  ])(
    "rejects missing, invalid, or incoherent session deadline headers",
    async (headers) => {
      vi.spyOn(api, "post").mockResolvedValue({ data: session, headers });

      await expect(
        authenticate({
          email: "operator@example.test",
          password: "fictional-input-only",
        }),
      ).rejects.toBeInstanceOf(AuthenticationContractError);
    },
  );
});
