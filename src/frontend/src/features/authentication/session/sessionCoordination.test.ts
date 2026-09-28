import { afterEach, describe, expect, it, vi } from "vitest";

import {
  SessionCoordinationUnavailableError,
  broadcastSessionEnded,
  broadcastHumanActivity,
  runWithSessionRefreshLock,
  subscribeToSessionEvents,
} from "./sessionCoordination";

afterEach(() => vi.unstubAllGlobals());

describe("cross-tab session coordination", () => {
  it("uses an exclusive browser lock for refresh", async () => {
    const request = vi.fn(
      async (_name: string, _options: object, operation: () => unknown) =>
        operation(),
    );
    Object.defineProperty(navigator, "locks", {
      configurable: true,
      value: { request },
    });
    const operation = vi.fn().mockResolvedValue("renewed-token");

    await expect(runWithSessionRefreshLock(operation)).resolves.toBe(
      "renewed-token",
    );
    expect(request).toHaveBeenCalledWith(
      "controle-acesso-veiculos:session-refresh",
      { mode: "exclusive" },
      operation,
    );
  });

  it("fails closed when the browser cannot coordinate refreshes", async () => {
    Object.defineProperty(navigator, "locks", {
      configurable: true,
      value: undefined,
    });

    await expect(
      runWithSessionRefreshLock(async () => "token"),
    ).rejects.toBeInstanceOf(SessionCoordinationUnavailableError);
  });

  it("broadcasts only a session-ended event and never a token", () => {
    const postedMessages: unknown[] = [];
    let receiveMessage: ((message: { data: unknown }) => void) | undefined;
    class TestBroadcastChannel {
      addEventListener(
        _type: string,
        listener: (message: { data: unknown }) => void,
      ) {
        receiveMessage = listener;
      }
      postMessage(message: unknown) {
        postedMessages.push(message);
      }
      close() {}
    }
    vi.stubGlobal("BroadcastChannel", TestBroadcastChannel);
    const listener = vi.fn();
    const unsubscribe = subscribeToSessionEvents(listener);

    broadcastSessionEnded();
    receiveMessage?.({ data: { type: "session-ended" } });

    expect(postedMessages).toEqual([{ type: "session-ended" }]);
    expect(JSON.stringify(postedMessages)).not.toContain("token");
    expect(listener).toHaveBeenCalledWith({ type: "session-ended" });
    unsubscribe();
  });

  it("coordinates human activity with non-sensitive metadata only", () => {
    const postedMessages: unknown[] = [];
    let receiveMessage: ((message: { data: unknown }) => void) | undefined;
    class TestBroadcastChannel {
      addEventListener(
        _type: string,
        listener: (message: { data: unknown }) => void,
      ) {
        receiveMessage = listener;
      }
      postMessage(message: unknown) {
        postedMessages.push(message);
      }
      close() {}
    }
    vi.stubGlobal("BroadcastChannel", TestBroadcastChannel);
    const listener = vi.fn();
    const unsubscribe = subscribeToSessionEvents(listener);

    broadcastHumanActivity(1_907_502_000_000);
    receiveMessage?.({
      data: {
        occurredAtEpochMilliseconds: 1_907_502_000_000,
        type: "human-activity",
      },
    });

    expect(postedMessages).toEqual([
      {
        occurredAtEpochMilliseconds: 1_907_502_000_000,
        type: "human-activity",
      },
    ]);
    expect(JSON.stringify(postedMessages)).not.toMatch(
      /token|credential|password|email|operator/i,
    );
    expect(listener).toHaveBeenCalledWith(postedMessages[0]);
    unsubscribe();
  });

  it("tells every subscribed tab to end for inactivity", () => {
    let receiveMessage: ((message: { data: unknown }) => void) | undefined;
    class TestBroadcastChannel {
      addEventListener(
        _type: string,
        listener: (message: { data: unknown }) => void,
      ) {
        receiveMessage = listener;
      }
      postMessage() {}
      close() {}
    }
    vi.stubGlobal("BroadcastChannel", TestBroadcastChannel);
    const firstTab = vi.fn();
    const secondTab = vi.fn();
    const unsubscribeFirst = subscribeToSessionEvents(firstTab);
    const unsubscribeSecond = subscribeToSessionEvents(secondTab);

    receiveMessage?.({ data: { reason: "inactivity", type: "session-ended" } });

    expect(firstTab).toHaveBeenCalledWith({
      reason: "inactivity",
      type: "session-ended",
    });
    expect(secondTab).toHaveBeenCalledWith({
      reason: "inactivity",
      type: "session-ended",
    });
    unsubscribeFirst();
    unsubscribeSecond();
  });
});
