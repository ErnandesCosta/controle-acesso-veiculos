export const sessionInactivityWindowMilliseconds = 15 * 60 * 1_000;
export const sessionAbsoluteLifetimeMilliseconds = 12 * 60 * 60 * 1_000;

export const clockRollbackToleranceMilliseconds = 5_000;
const maximumTimerDelayMilliseconds = 2_147_483_647;

export interface SessionDeadlines {
  absoluteExpiresAtUtc: string;
  inactivityExpiresAtUtc: string;
  serverTimeUtc: string;
}

export interface SessionContinuitySnapshot {
  absoluteDeadlineEpochMilliseconds: number;
  humanDeadlineEpochMilliseconds: number;
  observedAtEpochMilliseconds: number;
}

interface ClockReading {
  monotonic: number;
  wall: number;
}

interface SessionInactivityState {
  absoluteDeadline: ClockReading;
  humanDeadline: ClockReading;
  observation: ClockReading;
}

interface SessionInactivityMonitorOptions {
  clock?: () => ClockReading;
  onExpired: () => void;
}

export class InvalidSessionDeadlineError extends Error {
  constructor() {
    super("The session deadlines are invalid.");
    this.name = "InvalidSessionDeadlineError";
  }
}

export class SessionInactiveError extends Error {
  constructor() {
    super("The session is no longer active.");
    this.name = "SessionInactiveError";
  }
}

export class SessionInactivityMonitor {
  private readonly clock: () => ClockReading;
  private readonly onExpired: () => void;
  private state: SessionInactivityState | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor({
    clock = readClock,
    onExpired,
  }: SessionInactivityMonitorOptions) {
    this.clock = clock;
    this.onExpired = onExpired;
  }

  start(deadlines: SessionDeadlines) {
    const durations = parseDeadlineDurations(deadlines);
    const observation = this.readValidClock();

    this.state = {
      absoluteDeadline: addDuration(observation, durations.absolute),
      humanDeadline: addDuration(observation, durations.inactivity),
      observation,
    };
    this.schedule();
  }

  restore(snapshot: SessionContinuitySnapshot) {
    const observation = this.readValidClock();
    const inactivityRemaining =
      snapshot.humanDeadlineEpochMilliseconds - observation.wall;
    const absoluteRemaining =
      snapshot.absoluteDeadlineEpochMilliseconds - observation.wall;

    if (
      !isValidSnapshot(snapshot) ||
      observation.wall + clockRollbackToleranceMilliseconds <
        snapshot.observedAtEpochMilliseconds ||
      inactivityRemaining <= 0 ||
      absoluteRemaining <= 0 ||
      inactivityRemaining > sessionInactivityWindowMilliseconds ||
      absoluteRemaining > sessionAbsoluteLifetimeMilliseconds
    ) {
      throw new InvalidSessionDeadlineError();
    }

    this.state = {
      absoluteDeadline: {
        monotonic: observation.monotonic + absoluteRemaining,
        wall: snapshot.absoluteDeadlineEpochMilliseconds,
      },
      humanDeadline: {
        monotonic: observation.monotonic + inactivityRemaining,
        wall: snapshot.humanDeadlineEpochMilliseconds,
      },
      observation,
    };
    this.schedule();
  }

  reconcile(deadlines: SessionDeadlines) {
    const durations = parseDeadlineDurations(deadlines);
    if (!this.state) {
      this.start(deadlines);
      return;
    }

    if (!this.checkNow()) return;
    const observation = this.readValidClock();
    this.state.absoluteDeadline = earlierReading(
      this.state.absoluteDeadline,
      addDuration(observation, durations.absolute),
    );
    this.state.humanDeadline = earlierReading(
      this.state.humanDeadline,
      addDuration(observation, durations.inactivity),
      this.state.absoluteDeadline,
    );
    this.state.observation = observation;

    if (!this.checkNow()) return;
    this.schedule();
  }

  reconcileSnapshot(snapshot: SessionContinuitySnapshot) {
    if (!this.state) throw new InvalidSessionDeadlineError();

    const observation = this.readValidClock();
    const wallElapsed = observation.wall - this.state.observation.wall;
    const monotonicElapsed =
      observation.monotonic - this.state.observation.monotonic;
    const snapshotHumanRemaining =
      snapshot.humanDeadlineEpochMilliseconds - observation.wall;
    const snapshotAbsoluteRemaining =
      snapshot.absoluteDeadlineEpochMilliseconds - observation.wall;
    const absoluteDeadlineEpochMilliseconds = Math.min(
      this.state.absoluteDeadline.wall,
      snapshot.absoluteDeadlineEpochMilliseconds,
    );
    const absoluteRemaining =
      absoluteDeadlineEpochMilliseconds - observation.wall;

    if (
      !isValidSnapshot(snapshot) ||
      wallElapsed < -clockRollbackToleranceMilliseconds ||
      monotonicElapsed < 0 ||
      wallElapsed + clockRollbackToleranceMilliseconds < monotonicElapsed ||
      observation.wall + clockRollbackToleranceMilliseconds <
        snapshot.observedAtEpochMilliseconds ||
      snapshotHumanRemaining <= 0 ||
      snapshotHumanRemaining > sessionInactivityWindowMilliseconds ||
      snapshotAbsoluteRemaining <= 0 ||
      snapshotAbsoluteRemaining > sessionAbsoluteLifetimeMilliseconds ||
      absoluteRemaining <= 0
    ) {
      throw new InvalidSessionDeadlineError();
    }

    const humanDeadlineEpochMilliseconds = Math.min(
      Math.max(
        this.state.humanDeadline.wall,
        snapshot.humanDeadlineEpochMilliseconds,
      ),
      absoluteDeadlineEpochMilliseconds,
    );
    const humanRemaining = humanDeadlineEpochMilliseconds - observation.wall;

    if (humanRemaining <= 0) throw new InvalidSessionDeadlineError();

    this.state = {
      absoluteDeadline: addDuration(observation, absoluteRemaining),
      humanDeadline: addDuration(observation, humanRemaining),
      observation,
    };
    this.schedule();
    return this.getSnapshot();
  }

  recordHumanActivity(activityAtEpochMilliseconds = this.clock().wall) {
    if (!this.state || !this.checkNow()) return false;

    const observation = this.readValidClock();
    const oldestAcceptedActivity =
      observation.wall - sessionInactivityWindowMilliseconds;
    if (
      !Number.isFinite(activityAtEpochMilliseconds) ||
      activityAtEpochMilliseconds < oldestAcceptedActivity ||
      activityAtEpochMilliseconds >
        observation.wall + clockRollbackToleranceMilliseconds
    ) {
      return false;
    }

    const activityOffset = activityAtEpochMilliseconds - observation.wall;
    const candidate = addDuration(
      observation,
      activityOffset + sessionInactivityWindowMilliseconds,
    );
    this.state.humanDeadline = earlierReading(
      laterReading(candidate, this.state.humanDeadline),
      this.state.absoluteDeadline,
    );
    this.state.observation = observation;
    this.schedule();
    return true;
  }

  checkNow() {
    if (!this.state) return false;

    let observation: ClockReading;
    try {
      observation = this.readValidClock();
    } catch {
      this.expire();
      return false;
    }

    const wallElapsed = observation.wall - this.state.observation.wall;
    const monotonicElapsed =
      observation.monotonic - this.state.observation.monotonic;
    const clockMovedBackwards =
      wallElapsed < -clockRollbackToleranceMilliseconds ||
      monotonicElapsed < 0 ||
      wallElapsed + clockRollbackToleranceMilliseconds < monotonicElapsed;
    const deadlineReached =
      hasReached(observation, this.state.humanDeadline) ||
      hasReached(observation, this.state.absoluteDeadline);

    if (clockMovedBackwards || deadlineReached) {
      this.expire();
      return false;
    }

    this.state.observation = observation;
    return true;
  }

  stop() {
    this.clearTimer();
    this.state = null;
  }

  getSnapshot(): SessionContinuitySnapshot | null {
    if (!this.state) return null;
    return {
      absoluteDeadlineEpochMilliseconds: this.state.absoluteDeadline.wall,
      humanDeadlineEpochMilliseconds: this.state.humanDeadline.wall,
      observedAtEpochMilliseconds: this.state.observation.wall,
    };
  }

  private readValidClock() {
    const observation = this.clock();
    if (
      !Number.isFinite(observation.wall) ||
      !Number.isFinite(observation.monotonic) ||
      observation.monotonic < 0
    ) {
      throw new InvalidSessionDeadlineError();
    }
    return observation;
  }

  private expire() {
    const wasActive = this.state !== null;
    this.stop();
    if (wasActive) this.onExpired();
  }

  private schedule() {
    this.clearTimer();
    if (!this.state) return;

    const observation = this.readValidClock();
    const delay = Math.min(
      maximumTimerDelayMilliseconds,
      Math.max(
        0,
        Math.min(
          this.state.humanDeadline.monotonic - observation.monotonic,
          this.state.absoluteDeadline.monotonic - observation.monotonic,
        ),
      ),
    );
    this.timer = setTimeout(() => {
      this.timer = null;
      if (this.checkNow()) this.schedule();
    }, delay);
  }

  private clearTimer() {
    if (this.timer === null) return;
    clearTimeout(this.timer);
    this.timer = null;
  }
}

function parseDeadlineDurations({
  absoluteExpiresAtUtc,
  inactivityExpiresAtUtc,
  serverTimeUtc,
}: SessionDeadlines) {
  const serverTime = Date.parse(serverTimeUtc);
  const inactivityDeadline = Date.parse(inactivityExpiresAtUtc);
  const absoluteDeadline = Date.parse(absoluteExpiresAtUtc);
  const inactivity = inactivityDeadline - serverTime;
  const absolute = absoluteDeadline - serverTime;

  if (
    !Number.isFinite(serverTime) ||
    !Number.isFinite(inactivity) ||
    !Number.isFinite(absolute) ||
    inactivity <= 0 ||
    inactivity > sessionInactivityWindowMilliseconds ||
    absolute <= 0 ||
    absolute > sessionAbsoluteLifetimeMilliseconds ||
    inactivity > absolute
  ) {
    throw new InvalidSessionDeadlineError();
  }

  return { absolute, inactivity };
}

function isValidSnapshot(snapshot: SessionContinuitySnapshot) {
  return (
    Number.isFinite(snapshot.absoluteDeadlineEpochMilliseconds) &&
    Number.isFinite(snapshot.humanDeadlineEpochMilliseconds) &&
    Number.isFinite(snapshot.observedAtEpochMilliseconds) &&
    snapshot.observedAtEpochMilliseconds <
      snapshot.humanDeadlineEpochMilliseconds &&
    snapshot.humanDeadlineEpochMilliseconds <=
      snapshot.absoluteDeadlineEpochMilliseconds &&
    snapshot.humanDeadlineEpochMilliseconds -
      snapshot.observedAtEpochMilliseconds <=
      sessionInactivityWindowMilliseconds &&
    snapshot.absoluteDeadlineEpochMilliseconds -
      snapshot.observedAtEpochMilliseconds <=
      sessionAbsoluteLifetimeMilliseconds
  );
}

function addDuration(reading: ClockReading, duration: number): ClockReading {
  return {
    monotonic: reading.monotonic + duration,
    wall: reading.wall + duration,
  };
}

function earlierReading(...readings: ClockReading[]) {
  return readings.reduce((earlier, candidate) =>
    candidate.monotonic < earlier.monotonic ? candidate : earlier,
  );
}

function laterReading(...readings: ClockReading[]) {
  return readings.reduce((later, candidate) =>
    candidate.monotonic > later.monotonic ? candidate : later,
  );
}

function hasReached(current: ClockReading, deadline: ClockReading) {
  return (
    current.monotonic >= deadline.monotonic || current.wall >= deadline.wall
  );
}

function readClock(): ClockReading {
  return {
    monotonic: performance.now(),
    wall: Date.now(),
  };
}
