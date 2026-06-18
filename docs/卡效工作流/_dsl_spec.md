# GrandUMI 卡牌效果 DSL 编写规范（权威）

效果定义是一个以卡号为键的 JSON 对象：`"OPxx-yyy": { ...定义... }`。定义对象按"触发时机"分节，只写该卡实际拥有的节。

## 一、触发时机节

- `triggers`: 数组，每项 `{ "on": <触发枚举>, "if": {条件}, "then": [op数组] }`。用于【登场时】【攻击时】【KO时】等事件触发。**注意：triggers 不支持 cost 节**。
- `activated`: 对象 `{ "cost": {成本}, "if": {条件}, "oncePerTurn": true, "then": [op数组] }`。用于【启动主要】。
- `main`: 对象 `{ "cost": {成本}, "if": {条件}, "then": [op数组] }`。用于事件卡【主要】。
- `counter`: 数组 `[op数组]`（直接是 op 列表，无 then 包裹，无条件）。用于事件卡【反击】。
- `trigger`: 数组 `[op数组]`（直接是 op 列表）。用于生命牌【触发】（卡牌 trigger 字段非空时）。

`if` 与 `cost`、`oncePerTurn` 均可省略。`main`/`activated`/`counter` 支持 `cost`；`triggers`/`trigger` 不支持 `cost`。

### 触发枚举（on 的取值）
- `OnEnterField` = 【登场时】
- `OnAttackDeclare` = 【攻击时】
- `OnOppAttackDeclare` = 【对方的攻击时】
- `OnBlockDeclare` = 【阻挡时】
- `OnKO` = 【KO时】/【K.O.时】
- `OnMyTurnEnd` = 【我方的回合结束时】
- `OnOppTurnEnd` = 【对方的回合结束时】
- `OnTurnStart` = 回合开始时

## 二、op 列表（动作）

目标参数 `target` 取值：`"self"`(自身)、`"selfLeader"`(我方领袖)、`"oppLeader"`(对方领袖)、`"$tgt"`(前面 Choose/SearchDeck 用 as 存入的变量，名字随意但要 $ 开头)。要指定某个敌方/我方角色，必须先用 `Choose` 选出存入变量，再在后续 op 用 `target:"$tgt"`。

- `Draw` {n} 抽 n 张
- `MillTop` {n} 自己卡组顶废弃 n 张
- `AddPowerThisTurn` {target, delta} 本回合力量±delta（delta 可负）
- `AddPowerThisBattle` {target, delta} 本次战斗力量±delta
- `AddPowerAll` {side:"own"|"opp", delta, excludeLeader:bool, filter:{}} 全体±delta，filter 可限定范围
- `SetPower` {target, value} 设定本回合力量为 value
- `KO` {target} KO 目标
- `Rest` {target} 横置 / `Activate` {target} 活跃
- `GiveKeyword` {target, keyword, duration} 赋予关键词（如 "速攻"/"双重攻击"/"阻挡者"/"不可阻挡"）
- `AttachDon` {target, n, from:"active"|"rest"} 贴 n 张咚
- `Choose` {prompt, max, min, as, filter:{}, text} 玩家选 1 张存入 as 变量（max 一般 1，min 一般 0）
- `BounceToHand` {target} 把目标退回手牌（场上→手牌）
- `ReturnToDeckBottom` {target, from:"field"|"hand"|"trash"} 放回卡组底
- `ReturnDonToDeck` {n} 咚!!-n（活跃咚放回咚卡组）
- `RefreshDon` {n, state:"active"|"rest"} 从咚卡组补 n 张咚
- `PlayCharFromHand` {filter:{}, rest:bool, text} 从手牌按 filter 选≤1张角色免费登场（rest=true 横置登场）
- `PlayCharFromTrash` {filter:{}, rest:bool, text} 从废弃区按 filter 选≤1张角色免费登场
- `PlayFromTrash` {target, rest} 指定卡（$tgt）从废弃区登场
- `TrashToHand` {target} 把废弃区指定卡（$tgt）加入手牌
- `LookTopReveal` {count, max, restTo:"bottom"|"trash", match:{}} 确认卡组顶 count 张、公开≤max 张符合 match 的加入手牌、其余按原序放回底/废弃。检索类标配。
- `SearchDeck` {filter:{}, text, as} 从卡组检索 1 张符合 filter 的加入手牌（会洗牌）
- `AddCostMod` {target, delta, duration} 费用±delta
- `Nullify` {target, duration} 使目标效果无效
- `AddRestriction` {target, kind, duration} 施加限制（kind 如 "CannotBeBlocker"）
- `OpponentDiscard` {n} 对方弃 n 张（对方自选）
- `DiscardHand` {target} 弃掉指定手牌（$tgt）
- `DiscardOwnChosen` {n} 我方自选弃 n 张
- `AddLifeFromDeck` {n} 卡组顶 n 张置入生命
- `MoveCharToLife` {target} 把目标角色置入生命区顶
- `LifeToHand` {} 我方生命顶 1 张入手 / `OppLifeToHand` {} 对方生命顶 1 张入对方手
- `MarkPreventKO` {target} 标记本回合不被 KO
- `RestActiveDon` {n} / `ActiveOwnDon` {n} 横置/活跃 n 张咚
- `SelfToTrash` {} 自身进废弃 / `ShuffleDeck` {}

## 三、cost 成本节（仅 activated/main/counter 可用）
- `donReturn`: n  咚!!-n
- `restSelf`: true  横置自身
- `selfToTrash`: true  自身进废弃
- `handDiscard`: n  弃 n 张手牌（自选）
- `restActiveDon`: n  横置 n 张活跃咚
- `millTop`: n  卡组顶弃 n 张
- `lifeToHand`: true  生命顶 1 张入手

## 四、if 条件节（键: 阈值数字 或 true）
leaderHasKeyword(领袖含特征,字符串)、leaderNameEquals、leaderHasProperty、leaderColorIncludes(颜色字符串如"红"/"绿"/"蓝"/"紫"/"黑"/"黄")、leaderColorCountGte、leaderPowerGte、
selfDonTotalGte/selfDonTotalLte、oppDonTotalGte/oppDonTotalLte、attachedDonTotalGte、donAttachedGte(自身被贴咚≥N)、donAttachedGteOwn/donAttachedLteOwn、donAttachedGteOpponent、
selfHandCountGte/selfHandCountLte、oppHandCountGte、
ownCharCountGte/oppCharCountGte、ownRestedCharCountGte、
selfLifeCountGte、ownLifeCountLte、oppLifeCountGte/oppLifeCountLte、bothLifeTotalLte、lifeArousal(=生命≤N,【激起】)、
ownTrashCountGte/oppTrashCountGte/trashCountGte、ownTrashEventCountGte/trashEventCountGte、
turnCountGte、isMyTurn(true)、isOppTurn(true)、selfPowerGte/selfPowerLte、leaderPowerGte

多个条件写在同一个 if 对象里表示"且"(AND)。

## 五、match / filter 卡牌过滤对象
键：`keyword`(特征,字符串)、`kind`("Character"/"Event"/"Stage")、`originalCostLte`/`originalCostGte`、`originalPowerLte`/`originalPowerGte`、`nameEquals`、`excludeName`(排除某名)、`keywordContains`。
多键=且。`anyOf`: [{...},{...}] 表示"或"。例：`{ "anyOf":[{"keyword":"草帽一伙"},{"keyword":"心脏海盗团"}], "originalCostGte":2 }`

## 六、典型范例

【登场时】检索（卡组顶5张公开1张某特征加手牌）：
```
"OPxx-001": { "triggers": [ { "on":"OnEnterField", "then":[
  { "op":"LookTopReveal","count":5,"max":1,"restTo":"bottom","match":{"keyword":"红发海盗团","excludeName":"自己卡名"} } ] } ] }
```

【登场时】KO对方1张费用≤4角色：
```
"OPxx-002": { "triggers":[ { "on":"OnEnterField","then":[
  { "op":"Choose","prompt":"OpponentCharacterCostLe4","max":1,"as":"$tgt" },
  { "op":"KO","target":"$tgt" } ] } ] }
```

【启动主要】(每回合1次) 休息自身：领袖含某特征时对方1张角色力量-3000：
```
"OPxx-003": { "activated": { "cost":{"restSelf":true}, "oncePerTurn":true, "if":{"leaderHasKeyword":"红发海盗团"}, "then":[
  { "op":"Choose","prompt":"OpponentCharacter","max":1,"as":"$tgt" },
  { "op":"AddPowerThisTurn","target":"$tgt","delta":-3000 } ] } }
```

事件【主要】抽2弃1，【触发】抽1：
```
"OPxx-004": { "main": { "then":[ {"op":"Draw","n":2}, {"op":"DiscardOwnChosen","n":1} ] },
              "trigger": [ {"op":"Draw","n":1} ] }
```

【攻击时】我方1张领袖或角色力量+1000：
```
"OPxx-005": { "triggers":[ { "on":"OnAttackDeclare","then":[
  { "op":"Choose","prompt":"OwnLeaderOrCharacter","max":1,"as":"$t" },
  { "op":"AddPowerThisTurn","target":"$t","delta":1000 } ] } ] }
```

## 七、判定为 complex（需 C# 脚本，本轮不写 DSL）的情形
- 纯持续/静态修正且无事件触发：如"【我方的回合中】本角色力量+X"(常态加成)、"手牌中此卡费用-X"、"我方全体角色获得某持续效果"。
- 需要"按指定名称从手牌/废弃区登场特定卡"且 DSL filter 无法精确表达，或涉及多步复杂条件联动。
- 涉及未在上面 op/condition/prompt 列表中的机制（如复制效果、查看并重排、改变攻击目标、特殊一次性状态等）。
- "之后"分句涉及无法表达的连锁判定。

判 complex 时给出简短 reason。纯关键词（仅【阻挡者】【速攻】【双重攻击】【不可阻挡】无其他主动效果）判 noEffect。

## 八、Choose 的 prompt 取值
OpponentCharacter, OpponentCharacterCostLe0..Le9, OpponentRestingCharacter, OpponentLeaderOrCharacter, OpponentCharacterWithDon, OpponentCharacterWithDonGe2,
OwnCharacter, OwnCharacterCostLe2..Le6, OwnLeaderOrCharacter, OwnHand, OwnHandCharacter, OwnHandEvent, OwnHandCostLe3..Le6,
OwnTrash, OwnTrashCharacter, OwnTrashEvent, OwnTrashCharacterCostLe3..Le7, OwnStage, AnyStage, OpponentHand, OpponentLifeAll。
需要按特征/名称再细分时，配合 `filter` 字段。
