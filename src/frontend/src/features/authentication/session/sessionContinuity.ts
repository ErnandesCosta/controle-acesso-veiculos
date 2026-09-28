import {
  clockRollbackToleranceMilliseconds,
  sessionAbsoluteLifetimeMilliseconds,
  sessionInactivityWindowMilliseconds,
  type SessionContinuitySnapshot,
} from "./sessionInactivity";

export const sessionContinuityStorageKey =
  "controle-acesso-veiculos:session-continuity:v1";

interface StoredSessionContinuity extends SessionContinuitySnapshot {
  version: 1;
}

export function readSessionContinuity(
  nowEpochMilliseconds = Date.now(),
): SessionContinuitySnapshot | null {
  let parsed: unknown;
  try {
    const serialized = window.localStorage.getItem(sessionContinuityStorageKey);
    if (serialized === null) return null;
    parsed = JSON.parse(serialized);
  } catch {
    clearSessionContinuity();
    return null;
  }

  if (!isValidContinuity(parsed, nowEpochMilliseconds)) {
    clearSessionContinuity();
    return null;
  }

  const {
    absoluteDeadlineEpochMilliseconds,
    humanDeadlineEpochMilliseconds,
    observedAtEpochMilliseconds,
  } = parsed;
  return {
    absoluteDeadlineEpochMilliseconds,
    humanDeadlineEpochMilliseconds,
    observedAtEpochMilliseconds,
  };
}

export function writeSessionContinuity(snapshot: SessionContinuitySnapshot) {
  if (
    !isValidContinuity(
      { ...snapshot, version: 1 },
      snapshot.observedAtEpochMilliseconds,
    )
  ) {
    clearSessionContinuity();
    return false;
  }

  try {
    window.localStorage.setItem(
      sessionContinuityStorageKey,
      JSON.stringify({
        ...snapshot,
        version: 1,
      } satisfies StoredSessionContinuity),
    );
    return true;
  } catch {
    clearSessionContinuity();
    return false;
  }
}

export function clearSessionContinuity() {
  try {
    window.localStorage.removeItem(sessionContinuityStorageKey);
  } catch {
    // Storage availability must never keep a session unlocked.
  }
}

function isValidContinuity(
  value: unknown,
  nowEpochMilliseconds: number,
): value is StoredSessionContinuity {
  if (typeof value !== "object" || value === null) return false;
  const record = value as Record<string, unknown>;
  const absoluteDeadline = record.absoluteDeadlineEpochMilliseconds;
  const humanDeadline = record.humanDeadlineEpochMilliseconds;
  const observedAt = record.observedAtEpochMilliseconds;

  return (
    record.version === 1 &&
    typeof absoluteDeadline === "number" &&
    Number.isFinite(absoluteDeadline) &&
    typeof humanDeadline === "number" &&
    Number.isFinite(humanDeadline) &&
    typeof observedAt === "number" &&
    Number.isFinite(observedAt) &&
    humanDeadline > nowEpochMilliseconds &&
    absoluteDeadline > nowEpochMilliseconds &&
    observedAt <= humanDeadline &&
    humanDeadline <= absoluteDeadline &&
    humanDeadline - observedAt <= sessionInactivityWindowMilliseconds &&
    absoluteDeadline - observedAt <= sessionAbsoluteLifetimeMilliseconds &&
    nowEpochMilliseconds + clockRollbackToleranceMilliseconds >= observedAt
  );
}
