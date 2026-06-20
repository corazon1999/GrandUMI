using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-119 波特卡斯·D·艾斯（角色）
/// 【登场时】将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///   之后，我方生命卡牌不多于 2 张的场合，本回合中，此角色获得【速攻】效果。
///
/// 说明 / 简化点：
///   - "最多 1 张加入生命区"在卡组非空时执行 1 张（AddLifeFromDeckTop(1)），空则跳过。
///   - "之后，生命≤2 时本回合获得速攻"为带独立条件的连锁分句，在 C# 中按顺序门控：
///     先加生命后再判 me.LifeCount<=2，满足才赋予速攻（GiveKeyword，本回合）。
/// </summary>
public class OP07_119_Ace : IScriptedEffect
{
    public string CardNumber => "OP07-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 将卡组最上方最多 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 之后：我方生命≤2 的场合，本回合中此角色获得【速攻】
        if (me.LifeCount <= 2)
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);

        return Task.CompletedTask;
    }
}
