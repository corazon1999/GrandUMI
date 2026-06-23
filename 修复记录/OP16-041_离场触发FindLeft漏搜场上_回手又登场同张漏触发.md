---
卡号: OP16-041 (附带 OP13-078)
日期: 2026-06-21
现象: 自己回合，OP16-045 克洛克达尔【登场时】回手一张费用≥2角色作成本、收益又把同一张≤2《因佩尔地狱》角色登场回场上后，领航 OP16-041 巴奇的离场触发不弹「登场囚犯」提示
根因: OP16_041 的 FindLeft 只在 废弃/手牌/卡组/生命 找离场卡读特征，不搜场上角色区/舞台；当离场卡被同一效果链(OP16-045收益)重新登场回场上时，FindLeft 返回 null → left is null → 静默 return 不触发
修复: FindLeft 末尾补搜 me.Characters + me.StageCard（OnCharLeaveField 已证明它确实离场过，全区域定位读特征是安全的）
波及卡牌: 全后端审查后同款共4张一并修——OP16-041 巴奇、OP13-078 黄金梅利号、OP08-056 莫比·迪克号、OP09-080 千里·阳光号(均 OnCharLeaveField 读离场卡却只搜非场上区)；OnAnyCharKOd 类(OP13-002/OP14-041等读废弃区被KO卡)风险低未改
预防: 见下
---

# OP16-041 — 离场触发卡的 FindLeft 漏搜场上，"回手又登场同一张"时漏触发

## 现象
- 单人测试：领航 OP16-041 巴奇(附咚≥1)、手牌有"因佩尔地狱的囚犯"(OP16-042)、场上有费用≥2《因佩尔地狱》角色。
- 打出 OP16-045 克洛克达尔【登场时】：回手 OP16-044 伊万科夫(cost2,《因佩尔地狱》) 作成本 → 收益又把**同一张** OP16-044 登场回场上。
- 结算完，领航 OP16-041 应弹"登场最多1张囚犯"提示，但**什么都没发生**。

## 诊断关键
- 对局日志 `MatchLogs/2026-06-20/ab8d52efa851.jsonl`：OP16-045 的 3 个 prompt(确认/回手/收益) 都创建了；回手 chosen=OP16-044(id 000a)、收益候选与 chosen **同为 id 000a**——同一张卡场上→回手→又登场回场上；全局 `OwnHandPrisoner`(巴奇prompt) 创建次数 = 0。
- 之前多轮误判：先以为旧二进制(确实也需重新 publish 部署)、再以为【每回合1次】(那局确实是 turn1 内重复触发)，最终真 bug 是 FindLeft。

## 根因
`OP16_041_Buggy.cs` 的 `FindLeft` 只查 `Trash ?? Hand ?? Deck ?? LifeArea`。OP16-045 收益用 `PlayFromHandFree` 把回手回来的 OP16-044 又登场到 `me.Characters`(场上)。巴奇在排空 OnCharLeaveField 时 `FindLeft(cardId)` 这几个区都找不到它(它在 Characters) → `left is null` → `return`。

## 修复(2 处脚本)
`FindLeft`/等价跨区查找末尾补：
```csharp
?? me.Characters.FirstOrDefault(c => c.Id.ToString() == cardId)
?? (me.StageCard is { } st && st.Id.ToString() == cardId ? st : null);
```
- `服务端WebSocket/Effects/Scripted/OP16_041_Buggy.cs`
- `服务端WebSocket/Effects/Scripted/OP13_078_OroJackson.cs`(同款 OnCharLeaveField + 不搜场上)

OnCharLeaveField 事件本身已证明该卡确实离场过，故全区域定位它读 Info 特征是安全的。

## 波及/未改（全后端系统审查）
- **同款一并修复，共 4 张**（均监听 `OnCharLeaveField` 读离场卡特征却只搜废弃/手牌/卡组/生命）：
  - OP16-041 巴奇（`FindLeft`）、OP13-078 黄金梅利号（内联跨区查找）、OP08-056 莫比·迪克号（`FindCard`）、OP09-080 千里·阳光号（`FindCard`）。
- **未改（确认无此风险）**：
  - `OnAnyCharKOd` 类(OP13-002 艾斯、OP14-041 汉库克)读 `me.Trash` 的被KO卡——KO 是终态、不会被同链登场回场上。
  - `OnAllyCharEnter` 类(OP02-026/OP13-100/OP16-079)找场上 `Characters`——登场卡本就在场上，正确。
  - EB01-047/OP03-076/OP04-086 的 `Hand.FirstOrDefault` 是"从手牌选自己要打出的牌"，与离场查找无关。
  - DSL 解释器不处理离场读卡；`AtomicOps`/`PromptSystem` 为全区域查找或各自独立操作，无漏场上问题。

## 测试(全过 64/64)
- `OP16LeaderTests.OP16_041_Triggers_When_BounceTarget_IsReSummonedAsBenefit`(精确复现，修复前失败、修复后通过)
- 另有 OP16-041 同步/直接/真实GameEngine异步(`OP16_045_RealPathTests`)三路径回归测试。

## 预防
- 写"当角色离场时X"类卡(OnCharLeaveField)，若需读离场卡 CardInstance 的特征/费用，跨区查找**必须含场上角色区+舞台**——离场卡可能被同一效果链后续操作再次登场回场上。
- 调"效果不触发"先读 match log 的 prompt_created（见记忆 debug-effect-not-triggering），区分后端没发 vs 前端没显示，再逐条对触发条件。
