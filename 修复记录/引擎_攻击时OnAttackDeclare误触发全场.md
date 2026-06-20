# 引擎 — 【攻击时】(OnAttackDeclare) 我方任意卡攻击都误触发全场

## 现象
- ST30-012 路飞的【攻击时】（将对方角色转休息）应只在**此卡攻击时**发动，实际我方**任意卡**（含其他角色/领袖）攻击时都会触发。
- 排查发现这不是单卡 bug，而是引擎级缺陷，波及全部 DSL 的【攻击时】卡与约 115 张未自检的脚本【攻击时】卡。

## 根因
派发收集处 `EffectRuntime.CollectListeners` 对 `OnAttackDeclare` **遍历全场、收集所有带该标签的卡**，不区分谁是本次战斗的攻击者。

- 按 OPTCG 规则，【攻击时】恒为「**此卡**攻击时」；对方攻击是单独的 `OnOppAttackDeclare`。
- DSL 解释器（`DslInterpreter.cs`）对【攻击时】完全没有攻击者自检。
- 脚本卡里仅 5 张自带 `b.AttackerCardId != ctx.Source.Id` 校验（OP04-026、OP06-055、OP11-010、ST_GroupC_Misc3、ST_Misc6），其余约 115 张缺这道门。

## 修复（中心化一处，修全部）
`服务端WebSocket/Effects/EffectRuntime.cs` `CollectListeners`：当 `trigger == OnAttackDeclare` 时，直接用 `CurrentBattle.AttackerCardId` 定位本次战斗的攻击者（领袖或角色），只把它加入监听列表并提前返回，不再遍历全场。

```csharp
if (trigger == EffectTrigger.OnAttackDeclare && s.CurrentBattle is { } b)
{
    int ai2 = b.AttackerPlayerIndex;
    var atkP = s.Players[ai2];
    var attacker = atkP.Leader.Id == b.AttackerCardId
        ? atkP.Leader
        : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
    if (attacker != null && HasEffectForTrigger(attacker, trigger))
        list.Add(new(ai2, attacker));
    return list;
}
```

这套过滤与该方法既有的 `OnOppAttackDeclare` / 回合结束时按归属过滤的风格一致。

## 为何不逐个改 120 张卡
脚本与 DSL 数量庞大，逐个补 `AttackerCardId` 校验易漏且重复。`CollectListeners` 是【攻击时】派发的唯一入口（chokepoint），在此过滤即可一次修全，单一真相源、零遗漏。曾先在 DslInterpreter 加局部门控，中心化后已撤销以免双重维护。

## 不受影响
- `OnOppAttackDeclare`（被攻击时）语义不同（源卡是防守方），未触及。
- 5 张已自检的脚本卡：现在只会在自己攻击时被收集，其内部 `AttackerCardId` 判断恒成立，仅提前 return 的死分支，无害。

## 关键约定（后续）
**新写【攻击时】(OnAttackDeclare) 卡无需再自检攻击者**——引擎已保证只有攻击者本身会被派发。

## 验证
- `dotnet build --no-incremental`：0 错误 0 警告。
  （注：增量编译曾对 GameEngine.cs 的 `HandleDebugRestAll` 报幻影 CS0103，清理重建即消失，与本次改动无关。）
