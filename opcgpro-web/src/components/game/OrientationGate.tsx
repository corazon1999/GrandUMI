export default function OrientationGate() {
  return (
    <div className="game-orientation-gate" role="status" aria-live="polite">
      <div
        className="flex h-16 w-10 rotate-90 items-center justify-center rounded-lg border-2 border-orange-400 text-2xl text-orange-300"
        aria-hidden="true"
      >
        ↻
      </div>
      <p className="text-lg font-bold">请将设备横屏</p>
      <p className="max-w-xs text-sm leading-6 text-gray-400">
        牌桌需要更宽的显示空间。返回大厅后仍可继续竖屏使用。
      </p>
    </div>
  );
}
