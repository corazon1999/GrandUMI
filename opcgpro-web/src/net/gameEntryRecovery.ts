interface GameEntryRecovery {
  isPending: () => boolean;
  isConnected: () => boolean;
  requestState: () => void;
  retryNavigation: () => void;
  reloadGame: () => void;
}

/** 匹配已经建房后，补取遗漏的开局状态；页面跳转仍未完成时恢复当前账号的原对局。 */
export function startGameEntryRecovery(recovery: GameEntryRecovery) {
  const resyncTimer = setTimeout(() => {
    if (!recovery.isPending() || !recovery.isConnected()) return;
    recovery.requestState();
    if (recovery.isPending()) recovery.retryNavigation();
  }, 4_000);
  const reloadTimer = setTimeout(() => {
    if (!recovery.isPending() || !recovery.isConnected()) return;
    recovery.reloadGame();
  }, 12_000);
  return () => {
    clearTimeout(resyncTimer);
    clearTimeout(reloadTimer);
  };
}
