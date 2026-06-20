# GrandUMI 手写 C# 卡牌脚本编写规范（权威）

目标：为 effectText 写一个手写脚本类，放到 `服务端WebSocket/Effects/Scripted/` 下。引擎用反射自动发现并注册，无需改其他文件。

## 一、文件 / 类骨架
文件名：`OPxx_yyy_英文名.cs`（如 `OP10_006_Caesar.cs`）。内容：
```csharp
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP10-006 凯撒·库朗  —— 一句话效果说明 + 简化点</summary>
public class OP10_006_Caesar : IScriptedEffect
{
    public string CardNumber => "OP10-006";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        // ... 效果逻辑 ...
    }
}
```
类名必须用下划线形式 `OPxx_yyy_Name`（不能有连字符）。CardNumber 用连字符形式 `"OPxx-yyy"`。
若方法体没有 await（纯同步），用 `public Task Resolve(EffectContext ctx){ ...; return Task.CompletedTask; }`。

## 二、EffectTrigger（HandlesTrigger 返回的时机）
OnEnterField(登场时)、OnAttackDeclare(攻击时)、OnOppAttackDeclare(对方的攻击时)、OnBlockDeclare(阻挡时)、OnKO(KO时)、PreKO(将要被KO/不会被KO)、OnMyTurnEnd(我方回合结束时)、OnOppTurnEnd(对方回合结束时)、OnTurnStart、ActivatedMain(启动主要)、EventMain(事件主要)、EventCounter(事件反击)、OnLifeRevealTrigger(生命触发)。
多时机：`HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;` 然后在 Resolve 里用 `ctx.Trigger` 分支。

## 三、EffectContext
- `ctx.State` (GameState)、`ctx.OwnerIndex` (int)、`ctx.Source` (CardInstance 自身)、`ctx.Trigger`、`ctx.Prompts` (IPromptService)、`ctx.Engine` (GameEngine?，对手弃牌/检索洗牌等需要它)、`ctx.Vars`。
- `ctx.State.Players[ctx.OwnerIndex]` = 我方；`ctx.State.Players[1-ctx.OwnerIndex]` = 对方。

## 四、IPromptService（玩家交互）
- `Task<List<string>> ChooseCards(int playerIdx, string kind, string text, IReadOnlyList<string> cardIds, int min, int max, Dictionary<string,object?>? extra = null)` 返回选中的卡 Id 字符串列表。
- `Task<bool> ConfirmOptional(int playerIdx, string text)` 询问"可以…"型可选效果是否发动。
- `Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options)` 多选一（返回下标），用于"二选一"分支。

选卡规范：候选若是**非公开区**(卡组/对手手牌)或需要展示卡面，要传 `extra["choiceCards"] = list.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList();`。场上角色/我方手牌可不传。

## 五、PlayerState / CardInstance / CardInfo 关键成员
PlayerState：`Leader`(CardInstance)、`Hand`/`Characters`(≤5)/`Trash`/`Deck`/`LifeArea`(List<CardInstance>)、`StageCard`(CardInstance?)、`CostArea`/`DonDeck`(List<DonCard>)、`ActiveDonCount`、`RestDonCount`、`AttachedDonCount(Guid)`、`TotalDonInCostArea`、`LifeCount`、`DeckCount`、`TurnOnceUsed`(HashSet<string>)。
CardInstance：`Id`(Guid)、`Info`(CardInfo)、`IsTapped`、`CurrentPower(int donAttached, bool ownerTurn)`、`CurrentCost()`、`MatchesName(string)`、`PowerModThisTurn/ThisBattle/Persistent`、`GainedKeywords`、`Restrictions`、`OncePerTurnUsedKeys`。
CardInfo：`Number`、`Name`、`Color`(如"炎"或"炎/水")、`ColorList`(string[])、`Kind`(CardKind)、`Property`、`Power`(int)、`Cost`(int)、`Counter`、`Keywords`(string[])、`HasKeyword(string)`、`EffectText`、`Trigger`。
枚举：CardKind{Leader,Character,Event,Stage,Don}；DonState{InDeck,Active,Rest,Attached}；KeywordDuration{ThisTurn,ThisBattle,UntilNextOpponentEndPhase}；RestrictionKind{CannotAttack,CannotBeKOd,CannotBeBlocker,CannotBeChosen}。
当前力量评估请用 `ctx.State.CurrentPowerOf(sideIdx, card)`（含持续效果）。判断是否我方回合：`ctx.State.CurrentTurnPlayer == ctx.OwnerIndex`。回合数 `ctx.State.TurnCount`。

## 六、AtomicOps 动作库（签名）
- `Draw(GameState s, int playerIdx, int n)`
- `DiscardHand(PlayerState p, CardInstance card)` / `MillTop(PlayerState p, int n)`
- `AddPowerThisTurn(CardInstance c, int delta)` / `AddPowerThisBattle(c,delta)` / `AddPowerPersistent(c,delta)`
- `RestCard(c)` / `ActivateCard(c)` / `PreventActivateNextReset(c)`
- `KO(GameState s, int ownerIdxOfTarget, CardInstance card)`  ← ownerIdx 是**目标所属方**的下标
- `GiveKeyword(CardInstance c, string keyword, KeywordDuration dur)`
- `AttachDonFromCost(PlayerState p, Guid targetId, int n, DonState fromState = Active)` 贴咚
- `ReturnDonToDeck(PlayerState p, int n)` 咚!!-n
- `BounceToHand(GameState s, int ownerIdxOfTarget, CardInstance card)` 退回手牌
- `PlayFromHandFree(GameState s, int playerIdx, CardInstance card)` 手牌登场
- `PlayFromTrashFree(GameState s, int playerIdx, CardInstance card, bool restState=false)` 废弃登场
- `TrashToHand(PlayerState p, CardInstance card)` / `ReturnHandToDeckBottom(p,card)` / `ReturnTrashToDeckBottom(p,card)` / `ReturnFieldToDeckBottom(GameState s,int ownerIdx,card)`
- `SetPowerThisTurn(CardInstance c, int absoluteValue, int donAttached, bool ownerTurn)`
- `AddCostModifier(CardInstance c, int delta, KeywordDuration dur)` 费用增减
- `NullifyEffects(CardInstance c, KeywordDuration dur)` 效果无效
- `AddRestriction(CardInstance c, RestrictionKind kind, KeywordDuration dur)`
- `MoveCharToLife(GameState s, int ownerIdx, CardInstance card, bool toTop=true)`
- `AddLifeFromDeckTop(PlayerState p, int n)` / `RefreshDonFromDeck(PlayerState p,int n,DonState=Active)`
- `await OpponentDiscardChosen(GameEngine engine, int opponentIdx, int n)` （需 ctx.Engine 非空）
- `await SearchDeck(GameEngine engine, int playerIdx, Func<CardInstance,bool> filter, string prompt)` 检索1张入手并洗牌
- `AddPowerToAllThisTurn(GameState s, int sideIdx, Func<CardInstance,bool> filter, int delta, bool includeLeader=true)`
- 检索看顶 N：手动 `var top = me.Deck.Take(n).ToList();` 后按需 `me.Deck.Remove(x); me.Hand.Add(x);` 其余 `me.Deck.Remove` 再 `me.Deck.AddRange` 放底。

## 七、持续/静态效果 → ContinuousEffect（重点！这类不要判 complex）
"【对方的回合中】我方所有X +N"、"我方拥有《Y》特征的角色 +M"、"自身在某条件下 +K" 等**持续力量修正**用注册实现：
```csharp
var selfId = self.Id; int owner = ctx.OwnerIndex;
ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString()); // 防重复
ctx.State.ContinuousEffects.Add(new ContinuousEffect {
    SourceCardId = selfId.ToString(),
    Scope = new ContinuousScope {
        Side = 0,                 // 0=源卡同方 1=对方 -1=双方
        IncludeLeader = true, IncludeCharacters = true,
        Filter = c => c.Info.HasKeyword("黑胡子海盗团"),  // 可空
    },
    PowerDelta = 1000,
    Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer != owner,  // 仅对方回合中生效; 恒定生效用 (s,i,c)=>true
});
```
注册型持续效果应在 `OnEnterField` 时机注册（HandlesTrigger 含 OnEnterField）。来源卡离场时引擎自动清理。
注意：ContinuousEffect 只能改**力量**。持续的"费用-X(手牌中)"、"获得关键词"、"效果无效"等非力量持续修正引擎暂无持续通道 → 这类判 complex。

## 八、每回合1次 / 成本
- 每回合1次：`var key = self.Info.Number + "-act"; if (me.TurnOnceUsed.Contains(key)) return; ... me.TurnOnceUsed.Add(key);`
- 启动主要类成本(横置自身/弃牌/咚-N/自身放回卡组底)直接用对应 AtomicOps 在效果前执行；"可以…"用 `ConfirmOptional` 先问。

## 九、常用范例

KO 对方选定角色：
```csharp
var cands = opp.Characters.Where(c => c.Info.Power <= 4000).ToList();
if (cands.Count == 0) return;
var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
    "选择1张力量≤4000的对方角色KO", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
if (chosen.Count > 0) {
    var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
    AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
}
```

二选一分支（如"KO对方角色 或 横置1张咚"）：用 `ChooseOption(ctx.OwnerIndex, "选择其一", new[]{"KO对方角色","横置1张咚"})` 返回下标后分支。

看卡组顶 5 张公开 1 张某特征加手牌、其余放底：
```csharp
int k = Math.Min(5, me.Deck.Count);
var top = me.Deck.Take(k).ToList();
var cand = top.Where(c => c.Info.HasKeyword("革命军")).ToList();
if (cand.Count > 0) {
    var extra = new Dictionary<string, object?> { ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList() };
    var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal", "公开最多1张《革命军》加入手牌", cand.Select(c=>c.Id.ToString()).ToList(), 0, 1, extra);
    if (ch.Count > 0) { var p = cand.First(c=>c.Id.ToString()==ch[0]); me.Deck.Remove(p); me.Hand.Add(p); }
}
var rest = top.Where(c => me.Deck.Contains(c)).ToList();
foreach (var c in rest) me.Deck.Remove(c);
me.Deck.AddRange(rest);
```

## 十、何时判 complex（输出 status=complex，不写脚本）
- 非力量的持续修正（持续费用-X在手牌、持续赋予关键词/效果无效/不会被KO等"持续状态"）——引擎无持续通道。
- 改变攻击目标、复制其他卡效果、查看并任意操纵对手手牌做复杂联动、流放(【流放】)等引擎未实现的机制。
- 依赖未提供的事件钩子（如"当此卡因对方效果被KO时""手牌被丢弃时"——EffectTrigger 无对应项）。
判 complex 时给简短 reason。其余绝大多数（KO/抽/登场/检索/力量增减/贴咚/退回/横置/费用增减/条件分支/持续力量）都应写脚本。

## 十一、输出
为每张卡产出一个完整 .cs 文件内容（含 using/namespace/class）。务必保证语法正确、能编译：方法签名、类型、AtomicOps 调用参数都要对得上本规范。

---

# 十二、增强能力（重要！本节优先级最高，覆盖前文保守判定）

以下卡此前被判 complex 多因旧规范保守。引擎现已支持更多，请尽量用以下能力实现，**不要轻易判 complex**。

## 12.1 持续费用修正 ContinuousEffect.CostDelta（"持续费用±N"现已支持）
`ContinuousEffect` 除 `PowerDelta` 外还有 `CostDelta`（正=费用升高，负=降低）。"【我方的回合中】对方所有角色费用-4""手牌中此卡费用-X""我方某特征角色费用+1"等**持续费用修正**用它实现（OnEnterField 或领袖在 OnGameStart 注册）：
```csharp
var selfId = self.Id; int owner = ctx.OwnerIndex;
ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
ctx.State.ContinuousEffects.Add(new ContinuousEffect {
    SourceCardId = selfId.ToString(),
    Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true }, // Side:0同方 1对方 -1双方
    CostDelta = -4,
    Predicate = (s, sideIdx, c) => s.CurrentTurnPlayer == owner,  // 仅我方回合中
});
```
注意：手牌中卡的持续费用修正(影响打出费用)同理可注册，Scope.Filter 限定到该卡。

## 12.2 更多事件钩子（HandlesTrigger 可用）
除前文外，现支持：`OnDrawCard`(抽牌时)、`OnPlayCard`(出牌时/事件也算)、`OnDonAttached`(咚被赋予时)、`OnEnterTrash`(进入废弃区/离场到废弃时)、`OnDamageToLeader`(对对方领袖造成伤害时)、`OnTurnStart`(回合开始时)、`OnGameStart`(开局,领袖注册永续用)。
- "当此卡因对方效果被KO/离场时…从废弃区登场"→ 用 `OnEnterTrash`(在 Resolve 里判 ctx.Source 是否在废弃区即可，简化不区分是否对方效果)。
- "抽牌时""出牌时""咚被赋予时"→ 对应钩子直接写。

## 12.3 攻击目标重定向（"将攻击对象变为X"现已支持）
`OnOppAttackDeclare` 触发时 `ctx.State.CurrentBattle` 已建立(我方为防守方)。直接改其目标字段即可重定向(伤害结算读这两字段)：
```csharp
var b = ctx.State.CurrentBattle; if (b is null) return;
// 改为我方领袖：
b.TargetIsLeader = true; b.TargetCardId = null;
// 或改为我方某角色 chosenChar：
b.TargetIsLeader = false; b.TargetCardId = chosenChar.Id;
```
"对方角色无法发动【阻挡者】本次战斗"→ 对对方所有角色 `AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle)`。

## 12.4 牌堆重排 / 看顶放顶或底
`AtomicOps.ReorderTopK(PlayerState p, IReadOnlyList<Guid> order, bool toBottom)`：看顶 k 张后按 order 顺序放回(顶或底)。"确认顶N张自选顺序放顶/底"可用它（简化：让玩家逐张选序或直接原序）。

## 12.5 对"对方"发起 prompt（对方决策驱动）
prompt 可发给对方：`await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, kind, text, ids, min, max, extra)`、`await ctx.Prompts.ChooseOption(1 - ctx.OwnerIndex, text, options)`、`AtomicOps.OpponentDiscardChosen(ctx.Engine, 1-ctx.OwnerIndex, n)`。
"对方可以弃1张生命牌，否则…"→ 用 ChooseOption 让对方在"弃生命/不弃"间选，再按结果分支。

## 12.6 现仍判 complex 的（确实无通道，才判）
- 持续**赋予关键词 / 持续效果无效化**(非力量非费用的持续状态机)；
- 持续**防止其他角色被KO**(PreKO 只能拦截被KO卡自身，无法持续监听他卡)；
- 【流放】关键词(引擎未实现)；复制其他卡效果。
其余(KO/抽/登场/检索/力量±/费用±持续/贴咚/退回/横置/重定向/牌堆重排/对方prompt/各种事件钩子/每回合1次)都应实现。

---

# 十三、引擎新增持续通道（Wave1，本节能力可用，对应卡不要再判 complex）

## 13.1 持续/条件赋予关键词 ContinuousEffect.GrantKeyword
"我方所有X获得【速攻/双重攻击/不可阻挡/阻挡者】""生命≤N时此卡获得【阻挡者】"等持续/条件关键词：
```csharp
var selfId = self.Id; int owner = ctx.OwnerIndex;
ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
ctx.State.ContinuousEffects.Add(new ContinuousEffect {
    SourceCardId = selfId.ToString(),
    Scope = new ContinuousScope { Side = 0 },
    GrantKeyword = "速攻",   // 或 "阻挡者"/"双重攻击"/"不可阻挡"/"流放"
    Predicate = (s, sideIdx, c) =>
        sideIdx == owner && (c.Id == selfId || c.MatchesName("修罗"))   // 谓词决定授予哪些卡及条件
        && s.Players[owner].LifeCount < s.Players[1-owner].LifeCount,   // 条件示例
});
```
在 OnEnterField 注册(领袖用 OnGameStart)。**本回合临时**获得关键词(如"本回合获得【流放】")直接用 `AtomicOps.GiveKeyword(c,"流放",KeywordDuration.ThisTurn)` 即可，无需持续效果。【流放】已被引擎消费(给伤害时不发动触发直接废弃)，可正常赋予。

## 13.2 持续"不会被KO" ContinuousEffect.KoGuard
"我方所有费用≤7角色在战斗中不会被KO""此角色不会因效果被KO"等：
```csharp
ctx.State.ContinuousEffects.Add(new ContinuousEffect {
    SourceCardId = selfId.ToString(),
    Scope = new ContinuousScope { Side = 0 },
    KoGuard = "battle",   // "battle"=仅战斗中 / "effect"=仅因效果 / "any"=任何KO
    Predicate = (s, sideIdx, c) => sideIdx == owner && ctx.State.CurrentCostOf(sideIdx,c) <= 7,
});
```
"直到下个对方回合结束"的群体防KO：在 EventMain/触发里注册，Predicate 用 TurnCount 限制有效期(如 `s.TurnCount <= 注册时TurnCount+1`，把基准回合数在注册前算好存入局部变量)。

## 13.3 持续"效果无效" ContinuousEffect.NullifyEffect=true
"对方的【登场时】效果无效""领袖及非X角色效果无效"：注册 NullifyEffect=true，Predicate 选中目标。被选中的卡在 EffectRuntime.Resolve 会被跳过。

## 13.4 持续"无法转为活跃" ContinuousEffect.PreventReset=true
"所有费用≤5角色在重置阶段不会转为活跃"(群体持续)：注册 PreventReset=true + Predicate。
**单目标一次性**"对方1张角色在下个对方重置阶段不活跃"：直接 `targetChar.CannotActivateNextReset = true;`(对对方角色也有效，对方重置时会检查)。

## 13.5 "无法转为休息状态" RestrictionKind.CannotBeRested
"对方1张角色无法转为休息状态"：`AtomicOps.AddRestriction(c, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase);`(RestCard 会对其 no-op)。

## 13.6 仍需 complex 的（Wave2 待做，本轮仍判 complex）
- 反应式事件钩子：当咚放回咚卡组时 / 当角色因效果被横置时 / 当角色因效果离场时 / 当对方发动事件或阻挡者时 / 当我方他角色登场时 / 当生命牌离场时 → 引擎尚无这些 watcher 派发。
- 置换型(他卡)：力量-1000代替被KO、放废弃代替被KO、横置+弃牌使不离场。
- 追加回合；攻击规则修改(可攻击活跃角色/按费用禁攻)；延迟到回合结束执行；手牌打出费用减免。

---

# 十四、引擎新增反应式 watcher（Wave2，可用）

引擎现在会在效果造成"咚放回咚卡组/角色因效果被横置/角色因效果离场"时，向监听卡派发 watcher 触发。监听卡通过 HandlesTrigger 声明，并在 Resolve 里从 ctx.Vars 读 payload。

可用 watcher 触发：
- `OnDonReturnedToDeck` 当(我方)咚放回咚卡组时。payload: ctx.Vars["count"](int 本次放回张数)。监听卡文本含"放回咚!!卡组时"。
- `OnCharRested` 当角色因效果转为休息状态时。payload: ctx.Vars["restedCardId"](string)。监听卡文本含"转为休息状态时"。
- `OnCharLeaveField` 当角色因效果离开场上时(KO/退回手牌/放回卡组/置入生命)。payload: ctx.Vars["cardId"](string), ctx.Vars["owner"](int 离场卡所属方)。监听卡文本含"离开场上时"。

写法示例（"当此角色转为休息状态时…"，自身被横置才触发）：
```csharp
public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;
public async Task Resolve(EffectContext ctx) {
    // 仅在我方回合、且被横置的是本卡自身时触发；每回合1次按需
    if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
    var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
    if (restedId != ctx.Source.Id.ToString()) return;   // 仅本卡被横置
    // …效果…
}
```
"当角色因我方效果转为休息时"(任意卡)：去掉 restedId==self 判断，仅判 `ctx.State.CurrentTurnPlayer==ctx.OwnerIndex`。
"当(我方/对方)X角色因效果离开场上时"：用 OnCharLeaveField，按 ctx.Vars["owner"] 判属哪方；离场卡已不在场，按需用 payload。
"当我方咚放回咚卡组时…"：OnDonReturnedToDeck，需"≥2张"时判 (int)ctx.Vars["count"]>=2；通常只在我方回合有意义，先判回合。
注意：这些 watcher 在"造成事件的效果"完整结算后才派发；只在效果上下文内的 AtomicOps 触发(普通战斗横置/攻击不算)。仍需 complex：当对方发动事件/阻挡者时、当我方他角色登场时、生命牌离场时、追加回合、攻击规则修改、手牌打出费用减免、置换型(力量-1000/放废弃代替被KO/横置使不离场)。

---

# 十五、引擎新增（Wave3，可用）

- **当对方发动事件时** `OnOppEventPlayed`：监听卡(非出牌方)文本含"对方发动事件时"。payload ctx.Vars["owner"]=出牌方下标；脚本判 `(int)owner != ctx.OwnerIndex`(确为对方出牌)再发动，限我方回合/每回合1次按需。
- **当我方(他)角色登场时** `OnAllyCharEnter`：文本含"角色登场时"(不含【登场时】)。payload ctx.Vars["cardId"](登场卡), ["owner"]。脚本判 owner==ctx.OwnerIndex 且 cardId!=自身。需"拥有【触发】的角色"时按 cardId 查到该卡判 Info.Trigger 非空。
- **抽牌时(抽卡阶段以外)** `OnDrawCard`：文本含"抽取卡牌时"。效果内抽牌才派发(抽卡阶段不派发)。payload ["count"],["player"]。
- **可攻击活跃角色**：给攻击者 `AtomicOps.GiveKeyword(c, "可攻击活跃", KeywordDuration.ThisTurn)`(或持续 GrantKeyword="可攻击活跃")，ActionValidator 允许其攻击对方活跃角色。
- **手牌打出费用减免**：现已生效——注册 `ContinuousEffect.CostDelta`(负数) 且 Predicate 对该手牌成立(用 c.Info.HasKeyword/Cost 判，sideIdx 即打出方)，引擎打出时按 `HandPlayCost` 扣减。Scope 随意，Predicate 决定。
- **追加回合**：`ctx.State.ExtraTurnPending = true;` 本回合结束后同一玩家再来一回合。
- **某咚下个重置不活跃**：对目标 DonCard 设 `don.CannotActivateNextReset = true;`(在 CostArea 中找到对方/我方的休息咚设置)。
- **群体赋予关键词+原本力量变为N**：关键词用 GrantKeyword；"原本力量变为6000"可注册 ContinuousEffect，PowerDelta 用 (6000 - c.Info.Power) 并 Scope.Filter/Predicate 选中目标(近似"变为6000")。

仍可能 complex(可判)：置换型他卡(力量-1000/放废弃代替被KO、横置使不离场)、生命牌离场时、"仅【登场时】效果无效"的选择性无效化、"此角色无法攻击"永久自限+本回合自我解除、回合级"无法登场角色"限制。

---
# 十六、引擎新增(Wave4)
- **生命牌离场时** `OnLifeLeaveField`：每张生命牌离场(伤害/触发/入手)派发。payload ctx.Vars["owner"]=失去生命的一方, ["toZero"]=bool 是否变为0张。"当我方生命变为0时"判 owner==ctx.OwnerIndex&&(bool)toZero；"当对方生命牌离场时"判 owner!=ctx.OwnerIndex；"当我方生命牌加入手牌时"判 owner==ctx.OwnerIndex(近似)。通常限我方回合/每回合1次。
- **仅某类触发无效** `ContinuousEffect.NullifyOnlyTrigger = EffectTrigger.OnEnterField`：Predicate 选中的卡，仅该类触发(如【登场时】)不发动。"我方/对方的【登场时】效果无效"用此：注册两条(Side=0我方持续；Side=对方+TurnCount限有效期)。

---
# 十七、引擎新增(Wave5，可用)
- **本回合我方无法登场角色**：`ctx.State.NoPlayCharacterThisTurn.Add(ctx.OwnerIndex);`(回合结束自动清)。
- **此角色无法攻击(永久自限)**：卡面含"此角色无法攻击"即被引擎自动禁攻；启动效果"本回合此角色效果无效"用 `AtomicOps.NullifyEffects(ctx.Source, KeywordDuration.ThisTurn);`(本回合解除该限制)。
- **当对方发动【阻挡者】时** `OnOppBlocker`：payload ctx.Vars["blockerOwner"]；脚本判 (int)blockerOwner!=ctx.OwnerIndex(确为对方阻挡)。文本含"对方发动【阻挡者】"。常与 OnOppEventPlayed 同写(同一卡两 HandlesTrigger)。
- **按费用禁攻**：`attacker.NoAttackCostLeThisTurn = 7;`(本回合该领袖/角色无法攻击对方原本费用≤7角色，回合末清)。
- **延迟到回合结束**：`ctx.State.EndOfTurnTasks.Add(new EndTurnTask{ Kind="TrashFilm", SourceCardId=ctx.Source.Id.ToString(), Owner=ctx.OwnerIndex });`(回合结束将该来源/任一FILM角色废弃)。
- **攻击税(令对方攻击需弃牌)**：`ctx.State.AttackTaxDiscard[1-ctx.OwnerIndex] = 2;`(对方所有角色攻击前须弃2张手牌，到对方回合结束自动清)。
- **KO守护者(他卡置换"代替被KO/使其不被KO")** `OnAllyWillBeKOd`(战斗KO时派发)：payload ctx.Vars["victimId"](string),["victimOwner"](int)。在 s.Players[victimOwner] 的 Characters/Leader 按 id 找到 victim，校验条件(力量/休息/特征/每回合1次/回合)后：`ctx.State.MarkPreventKO(victim.Id);` 取消KO，并施加置换(如 AddPowerThisTurn(victim,-1000) / 自身进废弃 BattleEngine.KOCard(ctx.State,ctx.OwnerIndex,ctx.Source) / 弃手牌等成本)。文本含"将要被KO的场合""代替被KO"。注意仅战斗KO派发；效果KO/离场的置换守护暂仍可判 complex。
