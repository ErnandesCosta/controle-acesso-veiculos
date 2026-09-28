import { afterEach, beforeEach, describe, expect, it } from "vitest";

import {
  clearSessionContinuity,
  readSessionContinuity,
  sessionContinuityStorageKey,
  writeSessionContinuity,
} from "./sessionContinuity";

const now = Date.parse("2030-06-10T12:00:00Z");
const snapshot = {
  absoluteDeadlineEpochMilliseconds: now + 12 * 60 * 60_000,
  humanDeadlineEpochMilliseconds: now + 15 * 60_000,
  observedAtEpochMilliseconds: now,
};

describe("session continuity metadata", () => {
  beforeEach(clearSessionContinuity);
  afterEach(clearSessionContinuity);

  it("persists only versioned non-sensitive temporal metadata", () => {
    expect(writeSessionContinuity(snapshot)).toBe(true);
    const serialized = localStorage.getItem(sessionContinuityStorageKey);

    expect(serialized).not.toBeNull();
    expect(serialized).not.toMatch(
      /access.?token|refresh.?token|email|profile|password|user/i,
    );
    expect(readSessionContinuity(now)).toEqual(snapshot);
  });

  it.each([
    null,
    "not-json",
    JSON.stringify({ version: 1 }),
    JSON.stringify({
      ...snapshot,
      humanDeadlineEpochMilliseconds: now - 1,
      version: 1,
    }),
  ])(
    "fails closed for absent, corrupt, incomplete, or expired metadata",
    (value) => {
      if (value !== null)
        localStorage.setItem(sessionContinuityStorageKey, value);
      expect(readSessionContinuity(now)).toBeNull();
      expect(localStorage.getItem(sessionContinuityStorageKey)).toBeNull();
    },
  );

  it("fails closed after a gross local clock regression", () => {
    writeSessionContinuity(snapshot);
    expect(readSessionContinuity(now - 6_000)).toBeNull();
  });
});
