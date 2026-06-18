using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-093 橡皮橡皮大猿王枪（事件 / 地 / 德莱斯罗兹·草帽一伙）
/// 【主要】本回合中，我方最多 1 张拥有《德莱斯罗兹》特征的角色力量+6000。
///   之后，我方废弃区中有 15 张或更多卡牌的场合，本回合中，该卡牌获得【双重攻击】。
///
/// 实现说明：
///   - 【主要】= EventMain 时机。
///   - 选 1 张《德莱斯罗兹》角色 +6000（AddPowerThisTurn）。
///   - 若我方废弃区 ≥15 张，则同一张角色本回合获得【双重攻击】（GiveKeyword ThisTurn）。
/// </summary>
public class OP04_093_GumGumKingKongGun : IScriptedEffect
{
    public string CardNumber => "OP04-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c => c.Info.HasKeyword("德莱斯罗兹")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "本回合中，选择最多 1 张《德莱斯罗兹》角色力量+6000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(tgt, 6000);

        // 之后：废弃区 ≥15 张 → 该角色本回合获得【双重攻击】
        if (me.Trash.Count >= 15)
        {
            AtomicOps.GiveKeyword(tgt, "双重攻击", KeywordDuration.ThisTurn);
        }
    }
}
