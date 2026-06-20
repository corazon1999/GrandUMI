# OP12 卡牌效果实现简报（给实现子智能体）

你要为 GrandUMI（海贼王 OPTCG 的复刻对战引擎，C#/.NET 服务端）实现指定卡牌的效果。
- **script 类**：直接把 .cs 文件写到 `D:\Self\GrandUMI\服务端WebSocket\Effects\Scripted\`（文件名唯一，不会与他人冲突）。
- **dsl 类**：不写文件，把 JSON 条目作为结构化结果返回，主控统一合并进 OP12.json。
- **engine 类**：不实现，返回 engineNeed 描述。
- **不要编译、不要碰 OP12.json、不要碰其他人的文件**。整合与编译由主控负责。

## 必读参考文件（动手前先 Read）
- 卡牌数据：`D:\Self\GrandUMI\_op12_cards.json`（含你负责卡的 number/effect 等）
- DSL 解释器（权威 op/条件/候选清单）：`D:\Self\GrandUMI\服务端WebSocket\Effects\Dsl\DslInterpreter.cs`
- 原子操作库：`D:\Self\GrandUMI\服务端WebSocket\Effects\AtomicOps.cs`
- 现成 DSL 定义范例：`D:\Self\GrandUMI\服务端WebSocket\Effects\Definitions\OP15.json` 与 `OP16.json`
- 现成手写脚本范例：`D:\Self\GrandUMI\服务端WebSocket\Effects\Scripted\` 目录下各 .cs（尤其 OP16_067_Otsuru.cs、OP15_029_Kuma.cs、OP12_108_Rosinante.cs）
- 触发枚举：`D:\Self\GrandUMI\服务端WebSocket\Effects\EffectTrigger.cs`

## 三种实现方式（按卡选最简者）
1. **dsl**：能用现有 DSL op 表达 → 产出该卡在 OP12.json 里的 JSON 条目（一个对象）。
2. **script**：DSL 表达不了（多步组合、选项分支、复杂条件）→ 产出一个 C# 脚本文件（实现 IScriptedEffect）。
3. **engine**：依赖尚不存在的引擎机制（见下"引擎缺口"）→ 不实现，产出 engineNeed 描述即可，主控后续处理。

## 颜色映射（务必使用）
效果文本写"红/绿/蓝/紫/黑/黄"，数据 color 字段是元素色：
红=炎、绿=风、蓝=水、紫=地、黑=暗、黄=光。
DSL 的 filter/match 里 `"color":"红"` 会自动按此映射；写脚本时用 `ColorMatches` 思路或 `c.Info.ColorList.Contains("炎")`。

## DSL 定义结构
```json
"OP12-XXX": {
  "_name": "卡名",
  "triggers": [
    { "on": "OnEnterField", "if": {<条件>}, "then": [ {<op>}, ... ] }
  ],
  "main":      { "if": {...}, "then": [...] },                // 事件【主要】（EventMain）
  "counter":   [ {<op>}, ... ],                                // 事件【反击】（EventCounter）
  "trigger":   [ {<op>}, ... ],                                // 生命牌【触发】
  "activated": { "oncePerTurn": true, "cost": {...}, "if": {...}, "then": [...] }  // 【启动主要】
}
```
- `triggers[].on` 取值：`OnEnterField`(登场时)、`OnAttackDeclare`(攻击时)、`OnKO`(KO时)、`OnBlockDeclare`(阻挡时)、`OnMyTurnEnd`、`OnOppTurnEnd`。
- 一张卡可同时有 triggers / main / counter / trigger / activated 多节。

## 可用 op（op 名 + 关键字段）—— 以 DslInterpreter.cs 的 RunOp 为准
- `Draw` n
- `MillTop` n（卡组顶 n 张入废弃区）
- `AddPowerThisTurn` target delta / `AddPowerThisBattle` target delta
- `AddPowerAll` side("own"/"opp") delta excludeLeader? filter（范围加力）
- `KO` target / `Rest` target / `Activate` target
- `GiveKeyword` target keyword duration（keyword 如"阻挡者"/"速攻"/"双重攻击"；duration: ThisTurn/ThisBattle/UntilNextOpponentEndPhase）
- `AttachDon` target n from("rest"/"active")（赋予咚）
- `RefreshDon` n state("active"/"rest")（从咚卡组追加咚到费用区）
- `ReturnDonToDeck` n（自己咚放回咚卡组）
- `Choose` prompt(候选种类) max min as("$var") text → 选中写入 ctx.Vars[$var]，后续 op 用 "target":"$var"
- `BounceToHand` target（场上卡回手）
- `ReturnToDeckBottom` target from("field"/"hand"/"trash")
- `PlayFromTrash` target rest? / `TrashToHand` target / `DiscardHand` target
- `SetPower` target value（本回合设为绝对值）
- `OpponentDiscard` n（对手自选弃 n 张）
- `DiscardOwnChosen` n（我方自选弃 n 张手牌）★新增
- `LookTopReveal` count max match restTo ★新增（见下）
- `AddLifeFromDeck` n（卡组顶 n 张入生命区顶）
- `MoveCharToLife` target / `SearchDeck` filter text as
- `AddCostMod` target delta duration / `Nullify` target duration / `AddRestriction` target kind duration
- `MarkPreventKO` target

### ★ LookTopReveal（通用"探顶"，覆盖大量"确认卡组顶N张公开…放回底部"）
```json
{ "op": "LookTopReveal", "count": 5, "max": 1, "restTo": "bottom",
  "match": { "anyOf": [ {"nameEquals":"特拉法尔加·罗"}, {"color":"红","kind":"Event"} ], "excludeName":"战国" } }
```
- count=看顶几张；max=最多公开取几张入手；restTo="bottom"(放回卡组底，默认) 或 "trash"(放废弃区)。
- `match`：`anyOf` 列表里任一子过滤命中即可（对应"X或Y"）；不写 anyOf 则把 match 自身当单个过滤；`excludeName` 对应"某名以外"。
- 子过滤字段：`nameEquals`、`keyword`(特征)、`color`、`property`(属性 斩/打/特/知…)、`kind`(Character/Event/Stage/Leader)、`originalCostLte/Gte`、`originalPowerLte/Gte`。

## Choose 的候选种类（prompt 字段）
OpponentCharacter / OpponentCharacterWithDon / OpponentCharacterWithDonGe2 / OpponentRestingCharacter / OpponentLeaderOrCharacter / OpponentCharacterCostLe5 / OwnCharacter / OwnLeaderOrCharacter / OwnHand / OwnHandCharacter / OwnHandEvent / OwnTrash / OwnTrashCharacter / OwnTrashEvent / OwnStage / AnyStage / OpponentHand / OpponentLifeAll。
（若需要"费用不高于N的对方角色"等现成种类没有的，改用脚本 + 自建候选列表。）

## 条件（if 节，全部需满足）
leaderPowerNotMoreThan / leaderHasKeyword / leaderNameEquals / leaderHasProperty★ / leaderColorIncludes★ / trashCountGte / trashEventCountGte / donAttachedGte / selfDonAttachedGte / ownCharCountGte / oppCharCountGte / ownLifeCountLte / oppLifeCountLte / oppHandCountGte / isMyTurn / donAttachedGteOpponent / lifeArousal(生命≤N) / selfDonNotMoreThanOpp★(true) / attachedDonTotalGte★。

## 【启动主要】activated 的 cost 字段
`donReturn`:N（咚-N）、`restSelf`:true（自身转休息）、`selfToTrash`:true（自身去废弃）、`handDiscard`:N★（弃N张手牌）。`oncePerTurn`:true 对应【每回合1次】。

## target 引用
"self"(效果源自身) / "selfLeader" / "oppLeader" / "$var"(Choose 选中的)。

## 写脚本（script）时的范式
```csharp
using GrandUMI.Cards;
using GrandUMI.Game;
namespace GrandUMI.Effects.Scripted;
public class OP12_XXX_名字 : IScriptedEffect
{
    public string CardNumber => "OP12-XXX";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField; // 按需
    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        // ... 用 AtomicOps.* + ctx.Prompts.ChooseCards(...) 实现
    }
}
```
- **脚本里 ChooseCards 若候选来自手牌/卡组/废弃区（客户端默认看不到身份），必须传 extra.choiceCards** 让前端显示卡面：
```csharp
var extra = new Dictionary<string, object?> {
    ["choiceCards"] = list.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
};
var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Kind", "提示文案",
    list.Select(c => c.Id.ToString()).ToList(), min, max, extra);
```
- 自身去废弃用 `BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source)`。
- AtomicOps 全部可用方法请 Read AtomicOps.cs 确认签名（Draw/DiscardHand/KO/AttachDonFromCost/RefreshDonFromDeck/PlayFromHandFree/PlayFromTrashFree/BounceToHand/ReturnFieldToDeckBottom/MoveCharToLife/AddLifeFromDeckTop/SetPowerThisTurn/GiveKeyword 等）。
- 脚本会被反射自动注册，无需改注册表。脚本优先级高于 DSL（同卡两者都有时脚本生效）。

## 引擎缺口（遇到判定为 approach="engine"，只写 engineNeed，不实现）
1. **替换类（防离场/防KO）**：形如"将要被KO/离开场上的场合，可以改为转休息/丢手牌/把生命牌某操作，使其不离场"。引擎暂无替换钩子。
2. **全局监听类领航/持续**：形如"当我方某类卡（不限自身）登场/离场/被丢弃时…"（领航被动监听全场事件）。
3. **生命牌朝向/正面朝下加入生命区**等尚未建模的区域操作。
判断标准：若效果需要拦截"别的卡"的状态变化、或监听全场事件、或操作尚不存在的状态，归为 engine。

## 输出要求（每张卡一个结果对象）
- `number`：卡号
- `approach`：dsl / script / engine
- `dslJson`：approach=dsl 时，该卡 JSON 条目的**值对象**序列化成字符串（形如 `{"_name":"…","triggers":[…]}`，可被 JSON.parse）；否则空串
- `scriptFileName`：approach=script 时你实际写入的文件名，形如 `OP12_007_Shanks.cs`（已写入 Scripted 目录）；否则空串
- `engineNeed`：approach=engine 时说明缺什么引擎能力；否则空串
- `summary`：一句话说明实现了什么 / 做了哪些简化
- `confidence`：high / medium / low

文件命名规则：`OP12_<三位号>_<罗马名>.cs`，类名与文件名一致（如 `OP12_007_Shanks`）。罗马名简短即可，避免与已存在文件重名（先 Glob 看 Scripted 目录）。

## 重要原则
- 忠实还原效果文本；做不到的部分在 summary 注明简化点，**不要假装实现**。
- "可以…"是可选效果（min=0 可跳过）；无"可以"的是强制。
- "最多N张"= max=N、min=0。
- 不确定的字段/方法，先 Read 源码确认，**绝不臆造不存在的 op/方法/条件名**。臆造会导致编译失败。
- 若某 op/条件/候选种类不存在但效果需要，改用 script 自行用 AtomicOps 实现。
