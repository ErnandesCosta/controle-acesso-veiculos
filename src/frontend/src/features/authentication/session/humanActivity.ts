interface HumanActivitySubscription {
  onActivity: (occurredAtEpochMilliseconds: number) => void;
  onResume: () => void;
}

export function subscribeToHumanActivity({
  onActivity,
  onResume,
}: HumanActivitySubscription) {
  let lastAcceptedGestureAt = Number.NEGATIVE_INFINITY;
  const handleActivity = (event: Event) => {
    if (!isRelevantHumanActivity(event)) return;
    const occurredAt = Date.now();
    if (occurredAt - lastAcceptedGestureAt < 500) {
      return;
    }
    lastAcceptedGestureAt = occurredAt;
    onActivity(occurredAt);
  };
  const handleVisibilityChange = () => {
    if (document.visibilityState === "visible") onResume();
  };

  document.addEventListener("keydown", handleActivity, true);
  document.addEventListener("pointerdown", handleActivity, true);
  document.addEventListener("touchstart", handleActivity, true);
  document.addEventListener("click", handleActivity, true);
  document.addEventListener("visibilitychange", handleVisibilityChange);
  window.addEventListener("pageshow", onResume);

  return () => {
    document.removeEventListener("keydown", handleActivity, true);
    document.removeEventListener("pointerdown", handleActivity, true);
    document.removeEventListener("touchstart", handleActivity, true);
    document.removeEventListener("click", handleActivity, true);
    document.removeEventListener("visibilitychange", handleVisibilityChange);
    window.removeEventListener("pageshow", onResume);
  };
}

export function isRelevantHumanActivity(
  event: Pick<Event, "isTrusted" | "type">,
) {
  return (
    event.isTrusted &&
    (event.type === "keydown" ||
      event.type === "pointerdown" ||
      event.type === "touchstart" ||
      event.type === "click")
  );
}
