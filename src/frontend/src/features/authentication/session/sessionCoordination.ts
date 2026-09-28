const refreshLockName = "controle-acesso-veiculos:session-refresh";
const channelName = "controle-acesso-veiculos:session-events";

export type SessionEvent =
  | {
      occurredAtEpochMilliseconds: number;
      type: "human-activity";
    }
  | { reason?: "inactivity"; type: "session-ended" };
type SessionEventListener = (event: SessionEvent) => void;

let channel: BroadcastChannel | null = null;
const listeners = new Set<SessionEventListener>();

export class SessionCoordinationUnavailableError extends Error {
  constructor() {
    super("Cross-tab session coordination is unavailable.");
    this.name = "SessionCoordinationUnavailableError";
  }
}

export async function runWithSessionRefreshLock<T>(
  operation: () => Promise<T>,
): Promise<T> {
  if (typeof navigator === "undefined" || !navigator.locks) {
    throw new SessionCoordinationUnavailableError();
  }

  return navigator.locks.request(
    refreshLockName,
    { mode: "exclusive" },
    operation,
  );
}

export function broadcastHumanActivity(occurredAtEpochMilliseconds: number) {
  if (!Number.isFinite(occurredAtEpochMilliseconds)) return;
  getSessionChannel()?.postMessage({
    occurredAtEpochMilliseconds,
    type: "human-activity",
  } satisfies SessionEvent);
}

export function broadcastSessionEnded(reason?: "inactivity") {
  getSessionChannel()?.postMessage({
    ...(reason ? { reason } : {}),
    type: "session-ended",
  } satisfies SessionEvent);
}

export function subscribeToSessionEvents(listener: SessionEventListener) {
  listeners.add(listener);
  getSessionChannel();

  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      channel?.close();
      channel = null;
    }
  };
}

function getSessionChannel() {
  if (channel || typeof BroadcastChannel === "undefined") return channel;

  channel = new BroadcastChannel(channelName);
  channel.addEventListener("message", (message: MessageEvent<unknown>) => {
    if (!isSessionEvent(message.data)) return;
    const event = message.data;
    listeners.forEach((listener) => listener(event));
  });
  return channel;
}

function isSessionEvent(value: unknown): value is SessionEvent {
  if (typeof value !== "object" || value === null || !("type" in value)) {
    return false;
  }

  if (value.type === "session-ended") {
    return (
      !("reason" in value) ||
      value.reason === undefined ||
      value.reason === "inactivity"
    );
  }

  return (
    value.type === "human-activity" &&
    "occurredAtEpochMilliseconds" in value &&
    typeof value.occurredAtEpochMilliseconds === "number" &&
    Number.isFinite(value.occurredAtEpochMilliseconds)
  );
}
