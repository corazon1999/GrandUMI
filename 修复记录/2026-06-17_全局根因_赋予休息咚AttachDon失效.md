---
卡号: 全局机制(AttachDonFromCost) + ST17-004 波尔·汉库珂 直接受益
日期: 2026-06-17
对应反馈: #73 ST17-004（登场未执行）；并修复全仓约50处「赋予休息咚」失效
---

## 全局根因：赋予休息状态的咚!! 集体失效

**现象**：大量「【登场时/启动主要】赋予…休息状态的咚!!」的卡，效果像"没发动"（用户多报"登场时未执行"）。

**根因**：DSL `AttachDon from:"rest"` → `AtomicOps.AttachDonFromCost(p, target, n, DonState.Rest)`，
原实现只 attach 费用区中 `State==Rest`（已经横置）的咚。但登场/启动主要时玩家费用区的咚
**通常是活跃状态**，没有现成休息咚 → attached=0 → 效果静默失败。

**影响面**：`Definitions/` 下 28 个文件、约 50 处 `"from":"rest"`（ST17/ST21/ST01/OP14/OP15… 广泛）。

**修复**（`Effects/AtomicOps.cs` AttachDonFromCost）：取休息咚不足时，**回退取活跃咚补足**。
依据：引擎 Attached 状态不分横竖、休息赋予与活跃赋予力量贡献等价（见 AttachDonFromDeck 原注释），
故回退安全。仅在请求 `Rest` 时回退，**不改默认 Active 行为**。

**直接修复**：#73 ST17-004 波尔·汉库珂【登场时】赋予《王下七武海》领袖/角色1张休息咚，现可正常执行。

## 连带：OP15-019 测试适配（#86 改动的回归）

#86 给 OP15-019 main 加 `Draw 1` 后，单测 `OP15_019_..._GivesLeaderPlus1000` 失败（actual 0）。
根因：`TestScene.New().Build()` 的 `Build()` 仅 `=> _state`、**不填充卡组**；空卡组抽牌触发败北
(`WinnerIndex` 置位)，`RunSteps` 遇 `IsGameOver` 提前 return → 后续加力被跳过。
**这是测试场景问题，非实现 bug**（实战空卡组抽牌本就该败北）。已给测试加 `MyDeckTop("OP15-050")`
准备卡组并加断言验证抽牌。

## 验证

- `dotnet test` → **59 通过 0 失败**（AttachDon 全局改动未破坏任何现有行为）。
- ⚠️ 需重启后端生效。建议实机抽验一批赋休息咚卡（ST17-004 登场赋咚；ST21/OP14 等）。

## 注意

「登场时未执行」是**多根因**类：本次修掉"赋休息咚"一支（#73）。其余如 ST25-001(抽3丢2)、
ST25-003(抽2丢1+登场)、EB04-058(生命操作) 等不涉及赋咚，需各自诊断。
