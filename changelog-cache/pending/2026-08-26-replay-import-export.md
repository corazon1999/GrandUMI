# 对局回放支持导入与导出

- 日期：2026-08-26
- 分类：新增
- 影响范围：大厅对局记录、本地回放存储与回放文件
- 状态：已完成

## 玩家可见说明

- 对局记录现在可以逐局导出为回放文件，也可以在另一台设备的对局记录中导入并直接观看；重复导入不会覆盖原有记录，损坏或超限文件会显示明确提示。

## 技术说明

- 新增带格式标识和版本号的 JSON 回放格式，完整携带对局元信息与快照流，并对文件体积、快照数量、必要字段、关键数组和快照顺序执行运行时校验。
- 导入时始终生成新的本地记录 ID，元信息与快照分块在同一 IndexedDB 事务中使用 `add` 写入，失败时整笔回滚；旧版整块快照读取与原有 30 局修剪策略保持兼容。
- 对局记录加入可重复选择同一文件的导入入口、逐局导出按钮和可访问的成功/错误反馈，手机端操作区保持至少 44×44px。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/match-replay-import-export.test.mjs tests/match-history-opening-summary.test.mjs tests/disconnect-loss-history.test.mjs tests/replay-hands.test.mjs`：19 项通过。
- `npx tsc --noEmit --incremental false`：通过。
- 已核对录制功能首次上线时的旧版 `MsgGameState` / `PlayerSnapshot` 必要字段，旧元信息缺少后加可选字段的兼容用例通过。
- 浏览器验证 `390×844` 与 `360×780` 两档手机竖屏：导入、清空、播放、导出与删除操作完整可见，触控区均不小于 `44×44px`，页面无横向溢出；同一 JSON 文件连续两次导入均成功生成独立记录，导出流程正常完成并显示成功反馈。
