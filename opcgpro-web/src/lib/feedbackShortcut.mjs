/**
 * 判断反馈快捷键是否应当交给反馈窗口处理。
 * 优先使用物理键位 code，并兼容少数只提供 key 的浏览器或键盘环境。
 */
export function isFeedbackShortcut(event) {
  const target = event.target;
  const tagName = typeof target?.tagName === "string"
    ? target.tagName.toUpperCase()
    : "";
  const isEditing = tagName === "INPUT"
    || tagName === "TEXTAREA"
    || tagName === "SELECT"
    || target?.isContentEditable === true;

  const isF = event.code === "KeyF"
    || ((!event.code || event.code === "Unidentified")
      && typeof event.key === "string"
      && event.key.toLowerCase() === "f");

  return isF
    && !event.repeat
    && !event.ctrlKey
    && !event.altKey
    && !event.metaKey
    && !event.isComposing
    && !isEditing;
}

/**
 * 在捕获阶段注册局内反馈快捷键，避免牌桌控件在冒泡阶段截断 F。
 * 返回清理函数供 React effect 卸载时调用。
 */
export function installFeedbackShortcut(windowTarget, { onToggle, onClose }) {
  const onKeyDown = (event) => {
    if (event.key === "Escape") {
      onClose();
      return;
    }
    if (!isFeedbackShortcut(event)) return;

    event.preventDefault();
    onToggle();
  };

  windowTarget.addEventListener("keydown", onKeyDown, true);
  return () => windowTarget.removeEventListener("keydown", onKeyDown, true);
}
