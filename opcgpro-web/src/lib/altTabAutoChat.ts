export const ALT_TAB_AUTO_CHAT_MESSAGE = "老板来了，等我一会";

const ALT_TAB_INTENT_TIMEOUT_MS = 1_500;

type EventTargetLike = Pick<EventTarget, "addEventListener" | "removeEventListener">;

interface AltTabAutoChatOptions {
  windowTarget: EventTargetLike;
  documentTarget: EventTargetLike & { visibilityState: DocumentVisibilityState };
  canSend: () => boolean;
  send: (message: string) => void;
  now?: () => number;
}

interface AltTabAutoChatGameState {
  mode: string;
  viewerKind: string;
  isGameOver: boolean;
  my: unknown | null;
  opponent: unknown | null;
}

export function isAltTabAutoChatEligible(game: AltTabAutoChatGameState): boolean {
  return game.mode === "Player"
    && game.viewerKind === "player"
    && !game.isGameOver
    && game.my !== null
    && game.opponent !== null;
}

/**
 * 浏览器无法直接获知系统是否完成了 Alt+Tab，因此先记录明确的按键意图，
 * 再由紧随其后的失焦或页面隐藏确认切出。返回值用于组件卸载时清理监听器。
 */
export function installAltTabAutoChat({
  windowTarget,
  documentTarget,
  canSend,
  send,
  now = Date.now,
}: AltTabAutoChatOptions): () => void {
  let intentAt: number | null = null;
  let sentForCurrentLeave = false;

  const clearIntent = () => {
    intentAt = null;
  };

  const confirmLeave = () => {
    if (sentForCurrentLeave || intentAt === null) return;
    if (now() - intentAt > ALT_TAB_INTENT_TIMEOUT_MS) {
      clearIntent();
      return;
    }
    if (!canSend()) {
      clearIntent();
      return;
    }

    sentForCurrentLeave = true;
    clearIntent();
    send(ALT_TAB_AUTO_CHAT_MESSAGE);
  };

  const onKeyDown = (event: Event) => {
    const keyboardEvent = event as KeyboardEvent;
    if (
      keyboardEvent.key !== "Tab"
      || !keyboardEvent.altKey
      || keyboardEvent.ctrlKey
      || keyboardEvent.metaKey
      || keyboardEvent.repeat
      || documentTarget.visibilityState !== "visible"
      || !canSend()
    ) {
      return;
    }

    intentAt = now();
  };

  const onKeyUp = (event: Event) => {
    const keyboardEvent = event as KeyboardEvent;
    // 若页面没有切走，浏览器会收到 Tab 抬起事件；清除未被确认的陈旧意图。
    if (keyboardEvent.key === "Tab") clearIntent();
  };

  const onVisibilityChange = () => {
    if (documentTarget.visibilityState === "hidden") {
      confirmLeave();
      return;
    }

    sentForCurrentLeave = false;
    clearIntent();
  };

  const onBlur = () => confirmLeave();
  const onFocus = () => {
    if (documentTarget.visibilityState !== "visible") return;
    sentForCurrentLeave = false;
    clearIntent();
  };

  windowTarget.addEventListener("keydown", onKeyDown, true);
  windowTarget.addEventListener("keyup", onKeyUp, true);
  windowTarget.addEventListener("blur", onBlur);
  windowTarget.addEventListener("focus", onFocus);
  documentTarget.addEventListener("visibilitychange", onVisibilityChange);

  return () => {
    windowTarget.removeEventListener("keydown", onKeyDown, true);
    windowTarget.removeEventListener("keyup", onKeyUp, true);
    windowTarget.removeEventListener("blur", onBlur);
    windowTarget.removeEventListener("focus", onFocus);
    documentTarget.removeEventListener("visibilitychange", onVisibilityChange);
  };
}
