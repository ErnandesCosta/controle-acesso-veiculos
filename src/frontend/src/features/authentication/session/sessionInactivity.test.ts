import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi,
  type Mock,
} from "vitest";

import {
  SessionInactivityMonitor,
  sessionAbsoluteLifetimeMilliseconds,
  sessionInactivityWindowMilliseconds,
} from "./sessionInactivity";

const serverTime = Date.parse("2030-06-10T12:00:00Z");

function deadlines(
  inactivityDuration = sessionInactivityWindowMilliseconds,
  absoluteDuration = sessionAbsoluteLifetimeMilliseconds,
) {
  return {
    absoluteExpiresAtUtc: new Date(serverTime + absoluteDuration).toISOString(),
    inactivityExpiresAtUtc: new Date(
      serverTime + inactivityDuration,
    ).toISOString(),
    serverTimeUtc: new Date(serverTime).toISOString(),
  };
}

describe("session inactivity monitor", () => {
  let monotonic: number;
  let wall: number;
  let expired: Mock<() => void>;
  let monitor: SessionInactivityMonitor;

  beforeEach(() => {
    vi.useFakeTimers();
    monotonic = 0;
    wall = serverTime;
    expired = vi.fn();
    monitor = new SessionInactivityMonitor({
      clock: () => ({ monotonic, wall }),
      onExpired: expired,
    });
    monitor.start(deadlines());
  });

  afterEach(() => {
    monitor.stop();
    vi.useRealTimers();
  });

  async function advance(milliseconds: number, runTimers = true) {
    wall += milliseconds;
    monotonic += milliseconds;
    if (runTimers) await vi.advanceTimersByTimeAsync(milliseconds);
  }

  it.each([-10, 10])(
    "keeps fifteen-minute and twelve-hour durations with a tablet clock offset by %s minutes",
    (offsetMinutes) => {
      monitor.start(deadlines());
      wall = serverTime + offsetMinutes * 60_000;
      monitor.start(deadlines());

      expect(monitor.getSnapshot()).toEqual({
        absoluteDeadlineEpochMilliseconds:
          wall + sessionAbsoluteLifetimeMilliseconds,
        humanDeadlineEpochMilliseconds:
          wall + sessionInactivityWindowMilliseconds,
        observedAtEpochMilliseconds: wall,
      });
    },
  );

  it("renews the local period only after accepted human activity", async () => {
    await advance(10 * 60_000);
    expect(monitor.recordHumanActivity(wall)).toBe(true);
    await advance(5 * 60_000);
    expect(expired).not.toHaveBeenCalled();
    await advance(10 * 60_000);
    expect(expired).toHaveBeenCalledOnce();
  });

  it("expires after fifteen minutes without activity", async () => {
    await advance(sessionInactivityWindowMilliseconds);
    expect(expired).toHaveBeenCalledOnce();
    expect(monitor.checkNow()).toBe(false);
  });

  it("never extends beyond the absolute deadline", async () => {
    monitor.start(deadlines(15 * 60_000, 20 * 60_000));
    await advance(10 * 60_000);
    monitor.recordHumanActivity(wall);
    await advance(10 * 60_000);
    expect(expired).toHaveBeenCalledOnce();
  });

  it("rejects deadline durations beyond the server limits", () => {
    expect(() =>
      monitor.start(
        deadlines(
          sessionInactivityWindowMilliseconds,
          sessionAbsoluteLifetimeMilliseconds + 1,
        ),
      ),
    ).toThrow("The session deadlines are invalid.");
  });

  it("does not let an automatic refresh extend human inactivity", async () => {
    await advance(14 * 60_000);
    const before = monitor.getSnapshot();
    monitor.reconcile(deadlines());
    const after = monitor.getSnapshot();
    await advance(60_000);

    expect(before?.humanDeadlineEpochMilliseconds).toBe(
      serverTime + sessionInactivityWindowMilliseconds,
    );
    expect(after?.humanDeadlineEpochMilliseconds).toBe(
      before?.humanDeadlineEpochMilliseconds,
    );
    expect(expired).toHaveBeenCalledOnce();
  });

  it("restores only an unexpired continuity snapshot", async () => {
    await advance(5 * 60_000, false);
    monitor.restore({
      absoluteDeadlineEpochMilliseconds:
        serverTime + sessionAbsoluteLifetimeMilliseconds,
      humanDeadlineEpochMilliseconds:
        serverTime + sessionInactivityWindowMilliseconds,
      observedAtEpochMilliseconds: serverTime,
    });
    await advance(10 * 60_000);
    expect(expired).toHaveBeenCalledOnce();
  });

  it("accepts a newer valid human deadline persisted by another tab", async () => {
    await advance(10 * 60_000, false);

    const canonical = monitor.reconcileSnapshot({
      absoluteDeadlineEpochMilliseconds:
        serverTime + sessionAbsoluteLifetimeMilliseconds,
      humanDeadlineEpochMilliseconds:
        wall + sessionInactivityWindowMilliseconds,
      observedAtEpochMilliseconds: wall,
    });

    expect(canonical?.humanDeadlineEpochMilliseconds).toBe(
      wall + sessionInactivityWindowMilliseconds,
    );
    await advance(sessionInactivityWindowMilliseconds);
    expect(expired).toHaveBeenCalledOnce();
  });

  it("never lets another tab's snapshot extend the known absolute deadline", async () => {
    monitor.start(deadlines(15 * 60_000, 20 * 60_000));
    await advance(5 * 60_000, false);

    const canonical = monitor.reconcileSnapshot({
      absoluteDeadlineEpochMilliseconds: wall + 30 * 60_000,
      humanDeadlineEpochMilliseconds: wall + 15 * 60_000,
      observedAtEpochMilliseconds: wall,
    });

    expect(canonical?.absoluteDeadlineEpochMilliseconds).toBe(
      serverTime + 20 * 60_000,
    );
  });

  it("rejects an expired or corrupt snapshot from another tab", async () => {
    await advance(16 * 60_000, false);

    expect(() =>
      monitor.reconcileSnapshot({
        absoluteDeadlineEpochMilliseconds:
          serverTime + sessionAbsoluteLifetimeMilliseconds,
        humanDeadlineEpochMilliseconds:
          serverTime + sessionInactivityWindowMilliseconds,
        observedAtEpochMilliseconds: serverTime,
      }),
    ).toThrow("The session deadlines are invalid.");
    expect(() =>
      monitor.reconcileSnapshot({
        absoluteDeadlineEpochMilliseconds: Number.NaN,
        humanDeadlineEpochMilliseconds: wall + 60_000,
        observedAtEpochMilliseconds: wall,
      }),
    ).toThrow("The session deadlines are invalid.");
  });

  it("expires immediately after a suspended tab resumes past its deadline", async () => {
    await advance(16 * 60_000, false);
    expect(monitor.checkNow()).toBe(false);
    expect(expired).toHaveBeenCalledOnce();
  });

  it("fails closed when the wall clock or monotonic clock regresses", () => {
    monotonic += 60_000;
    wall -= 60_000;
    expect(monitor.checkNow()).toBe(false);
    expect(expired).toHaveBeenCalledOnce();

    monitor.start(deadlines());
    monotonic = -1;
    expect(monitor.checkNow()).toBe(false);
    expect(expired).toHaveBeenCalledTimes(2);
  });
});
