export const CARD_LONG_PRESS_DELAY_MS = 500;
export const CARD_LONG_PRESS_MOVE_THRESHOLD_PX = 8;
export const CARD_SYNTHETIC_CLICK_SUPPRESSION_MS = 1_000;
export const CARD_TOUCH_CONTEXT_MENU_SUPPRESSION_MS = 1_500;

const SYNTHETIC_CLICK_DISTANCE_PX = 24;
const CONTEXT_MENU_DISTANCE_PX = 32;
const MOUSE_CONTEXT_MENU_DISTANCE_PX = 4;

export interface CardPointerPoint {
  pointerId: number;
  clientX: number;
  clientY: number;
}

interface TimedPoint {
  clientX: number;
  clientY: number;
  at: number;
}

interface ActivePress extends CardPointerPoint {
  cardIdentity: string;
  longPressTriggered: boolean;
  timer: unknown;
}

export type CardPointerEndResult = "ignored" | "short-press" | "long-press";

export interface CardLongPressScheduler {
  now: () => number;
  setTimeout: (callback: () => void, delayMs: number) => unknown;
  clearTimeout: (timer: unknown) => void;
}

const defaultScheduler: CardLongPressScheduler = {
  now: () => Date.now(),
  setTimeout: (callback, delayMs) => setTimeout(callback, delayMs),
  clearTimeout: (timer) => clearTimeout(timer as ReturnType<typeof setTimeout>),
};

function distanceBetween(
  first: Pick<CardPointerPoint, "clientX" | "clientY">,
  second: Pick<CardPointerPoint, "clientX" | "clientY">,
) {
  return Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY);
}

/**
 * 管理单张对局卡牌的触控长按状态。
 * DOM 事件仍由 CardItem 负责，以便窗口捕获阶段能覆盖 HandArea 的 pointer capture。
 */
export function createCardLongPressGesture({
  onLongPress,
  scheduler = defaultScheduler,
}: {
  onLongPress: (cardIdentity: string) => void;
  scheduler?: CardLongPressScheduler;
}) {
  let activePress: ActivePress | null = null;
  let suppressedClick: TimedPoint | null = null;
  let lastNonMousePress: TimedPoint | null = null;
  let lastMouseContextPress: TimedPoint | null = null;

  const clearActiveTimer = () => {
    if (activePress?.timer != null) scheduler.clearTimeout(activePress.timer);
    if (activePress) activePress.timer = null;
  };

  const discardActivePress = () => {
    clearActiveTimer();
    activePress = null;
  };

  const rememberClickSuppression = (point: Pick<CardPointerPoint, "clientX" | "clientY">) => {
    suppressedClick = {
      clientX: point.clientX,
      clientY: point.clientY,
      at: scheduler.now() + CARD_SYNTHETIC_CLICK_SUPPRESSION_MS,
    };
  };

  const start = (point: CardPointerPoint, cardIdentity: string) => {
    discardActivePress();
    // 新的一次真实触控应拥有自己的短按语义，不继承上一手势的点击抑制。
    suppressedClick = null;
    lastMouseContextPress = null;
    lastNonMousePress = {
      clientX: point.clientX,
      clientY: point.clientY,
      at: scheduler.now(),
    };

    const press: ActivePress = {
      ...point,
      cardIdentity,
      longPressTriggered: false,
      timer: null,
    };
    activePress = press;
    press.timer = scheduler.setTimeout(() => {
      if (activePress !== press || press.longPressTriggered) return;
      press.timer = null;
      press.longPressTriggered = true;
      lastNonMousePress = { ...press, at: scheduler.now() };
      rememberClickSuppression(press);
      onLongPress(press.cardIdentity);
    }, CARD_LONG_PRESS_DELAY_MS);
  };

  const move = (point: CardPointerPoint) => {
    const press = activePress;
    if (!press || press.pointerId !== point.pointerId || press.longPressTriggered) return false;
    if (distanceBetween(press, point) < CARD_LONG_PRESS_MOVE_THRESHOLD_PX) return false;
    discardActivePress();
    return true;
  };

  const endActivePress = (point: CardPointerPoint): CardPointerEndResult => {
    const press = activePress;
    if (!press || press.pointerId !== point.pointerId) return "ignored";
    const longPressTriggered = press.longPressTriggered;
    discardActivePress();
    if (!longPressTriggered) return "short-press";
    // 从松手时重新计时，覆盖用户长按后继续停留超过一秒的情况。
    lastNonMousePress = { ...point, at: scheduler.now() };
    rememberClickSuppression(point);
    return "long-press";
  };

  const finish = (point: CardPointerPoint) => endActivePress(point);
  const cancelPointer = (point: CardPointerPoint) => endActivePress(point);

  const cancelActive = () => {
    discardActivePress();
  };

  const noteMousePointerDown = (
    point: Pick<CardPointerPoint, "clientX" | "clientY">,
    button: number,
  ) => {
    // 真实鼠标按下不是触摸产生的合成 click，不能被上一手势误拦截。
    suppressedClick = null;
    lastMouseContextPress = button === 2
      ? { ...point, at: scheduler.now() }
      : null;
  };

  const consumeSuppressedClick = (
    point: Pick<CardPointerPoint, "clientX" | "clientY">,
  ) => {
    if (!suppressedClick || scheduler.now() > suppressedClick.at) {
      suppressedClick = null;
      return false;
    }
    if (distanceBetween(suppressedClick, point) > SYNTHETIC_CLICK_DISTANCE_PX) return false;
    suppressedClick = null;
    return true;
  };

  const shouldSuppressContextMenu = (
    point: Pick<CardPointerPoint, "clientX" | "clientY">,
  ) => {
    const now = scheduler.now();
    if (
      lastMouseContextPress
      && now - lastMouseContextPress.at <= CARD_TOUCH_CONTEXT_MENU_SUPPRESSION_MS
      && distanceBetween(lastMouseContextPress, point) <= MOUSE_CONTEXT_MENU_DISTANCE_PX
    ) {
      return false;
    }
    return !!lastNonMousePress
      && now - lastNonMousePress.at <= CARD_TOUCH_CONTEXT_MENU_SUPPRESSION_MS
      && distanceBetween(lastNonMousePress, point) <= CONTEXT_MENU_DISTANCE_PX;
  };

  const hasActivePress = () => activePress !== null;

  const dispose = () => {
    discardActivePress();
    suppressedClick = null;
    lastNonMousePress = null;
    lastMouseContextPress = null;
  };

  return {
    start,
    move,
    finish,
    cancelPointer,
    cancelActive,
    noteMousePointerDown,
    consumeSuppressedClick,
    shouldSuppressContextMenu,
    hasActivePress,
    dispose,
  };
}
