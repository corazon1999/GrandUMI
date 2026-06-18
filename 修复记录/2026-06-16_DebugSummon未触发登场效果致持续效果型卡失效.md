# DebugSummon 调试召唤未触发登场效果，导致持续效果型卡（OP16-017 等）力量不变

日期：2026-06-16

## 问题

用户反馈：场上只有 OP16-017「小奥兹Jr.」时，应满足条件 -4000（显示 4000），但实际显示印刷力量 8000。

## 根因

OP16-017 的「我方场上不存在费用≥8 且含〈白胡子海盗团〉特征的角色时，自身力量-4000」是**靠登场时注册一条 `ContinuousEffect` 实现的**
（`Effects/Scripted/OP16_017_LittleOzJr.cs`，`HandlesTrigger == OnEnterField`）。其 Predicate 逻辑本身正确。

但用户测试走的是 **DebugSummon**（GM 调试召唤，对局日志里全是它）。原 `HandleDebugSummon`（`Game/GameEngine.cs`）
直接 `p.Characters.Add(card)`，注释明确写「不触发登场效果」——因此 OnEnterField 从未派发，
持续减力 `ContinuousEffect` 从未注册，力量恒为印刷值。

影响面：**所有「靠 OnEnterField 注册持续效果」的卡**（OP16-017 条件减力、OP16-003 我方领袖持续增益等）
在 DebugSummon 调试召唤下都无法生效；正常对局打出（走 `ResolveEffectAsync → Resolve(OnEnterField)`）不受影响。

## 修复（方案A：DebugSummon 默认触发登场效果，贴近真实登场）

`Game/GameEngine.cs`：
1. 分发处 `case "DebugSummon"` 改为 fire-and-forget 异步：`_ = HandleDebugSummonAsync(...)`（参照同处 `DebugKoAll`）。
2. `HandleDebugSummon` → `async Task HandleDebugSummonAsync`，更新方法注释。
3. `Broadcast("DebugSummon", …)` 之后，对角色/舞台补触发一次：
   ```csharp
   if (info.Kind == CardKind.Character || info.Kind == CardKind.Stage)
       await EffectRuntime.Resolve(State, targetIndex, card, EffectTrigger.OnEnterField, Prompts);
   ```
   owner 用 `targetIndex`（卡实际归属方），兼容召唤到对手场。

## 机制说明（已与用户确认采用方案A）

DebugSummon 由「不触发登场效果」改为「召唤即触发登场效果」，这是**回归正确机制**而非取舍：
规则上【登场时】效果本就是**强制触发**的，所以调试召唤触发它是正确行为，弹出 prompt 是机制使然。

「最多……」类目标选择无需特殊处理，现有 prompt 已正确区分：
- 「可以……」(可选发动) → `ConfirmOptional` 先问是否发动（如 OP16-003「可以公开手牌 2 张……」）。
- 「最多 N 张」(强制发动、目标数下限为 0) → `ChooseCards(..., min=0, max=N)`，即 `MinChoose=0` 允许不选目标
  （如 OP16-003「对方最多 1 张角色 -6000」用 `0, 1`）。

## 验证

`dotnet build GrandUMIServer.csproj -p:OutputPath=bin\verify\`（输出到临时目录避开运行中服务器文件锁）
→ 已成功生成，0 错误。临时目录已清理。

## 同类核查

OP16-017 自身的 C# 脚本逻辑无需改动（一直是对的）。本次修的是调试通道，受益面是全部「登场注册持续效果」类卡，
无需逐卡改动。后续若新增此类卡，DebugSummon 调试将自动正确生效。
