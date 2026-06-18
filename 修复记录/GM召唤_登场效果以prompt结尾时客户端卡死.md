# GM 召唤 — 登场效果以 prompt 结尾时客户端卡死

## 现象
- 单人测试（GM）召唤 OP16-026 安普里奥·伊万科夫，其【登场时】第二段「将我方手牌中最多 1 张费用≤2 的角色登场」(PlayCharFromHand) 弹出选择后，**点确认无反应、界面卡死**。
- 不止 OP16-026：任何「登场效果链以 prompt 结尾」的卡，用 GM 召唤都会卡。

## 定位（靠对战日志 MatchLogs）
日志 `2026-06-17/1344660ba35b.jsonl` 序列：
- seq 91 prompt_created p4(PlayCharFromHand) → seq 96 **prompt_response p4（已成功处理）**
- seq 96 之后**再无任何 public/private snapshot 广播**，客户端反复重发 p4 响应（seq 98/100/102…全部因 `_pending` 已无 p4 而被静默丢弃），直到用户放弃。

## 根因
`Broadcast(...)` 全是显式调用：handler 里、或 `PromptSystem.ChooseCards` 创建每个 prompt 时各广播一次。**效果链结束时没有自动广播**。

- 正常打出路径 `ResolveEffectAsync` 在 `await Resolve(OnEnterField)` 之后有收尾广播 `Broadcast("EffectResolved")`（GameEngine.cs:423），清空客户端 PendingPrompt。
- GM 召唤 `HandleDebugSummonAsync` 在 `await EffectRuntime.Resolve(OnEnterField)`（GameEngine.cs:257）之后**缺这一步收尾广播**。
- 连续 prompt 中间步骤靠「下一个 prompt 的创建广播」自然刷新；但**最后一个 prompt** 响应后效果完成、无新 prompt → 无广播 → 客户端 PendingPrompt 永不清空 → 卡死。

对比：`HandleDebugKoAllAsync` 的 `Broadcast("DebugKoAll")` 在 KO 循环之后，故 GM 全体 KO 不卡。

## 修复
`服务端WebSocket/Game/GameEngine.cs` `HandleDebugSummonAsync`：登场效果触发处加 try/catch 兜底 + 收尾广播，与 `ResolveEffectAsync` 一致。

```csharp
if ((info.Kind == CardKind.Character || info.Kind == CardKind.Stage) && targetIndex == playerIndex)
{
    try { await EffectRuntime.Resolve(State, targetIndex, card, EffectTrigger.OnEnterField, Prompts); }
    catch (Exception ex) { Console.Error.WriteLine($"[GM] 登场效果结算异常: {ex.Message}"); }
    Broadcast("EffectResolved", new { cardNumber = number });   // 收尾广播，清空 PendingPrompt
}
```

## 影响面
- 仅 GM 单人测试召唤路径；正常对战打出不受影响（走 `ResolveEffectAsync` 本就有收尾广播）。
- 一处修全部带登场效果的卡。try/catch 保证效果抛异常时也广播，不留死局。

## 验证
- `dotnet build --no-incremental`：0 错误 0 警告。
