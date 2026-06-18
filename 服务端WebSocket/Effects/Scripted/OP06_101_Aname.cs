using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-101 阿奈美（角色 / 光 2 费 3000，草帽一伙）
/// 【登场时】本回合中，我方最多1张领袖或角色获得【流放】效果。
///   （此卡牌给予伤害的场合，不发动触发效果将该卡牌放置到废弃区）
///
/// 实现说明 / 简化点：
///   - "本回合中获得【流放】"为本回合临时关键词：用 AtomicOps.GiveKeyword(c,"流放",ThisTurn)。
///     【流放】已被引擎消费（给予伤害时不发动触发直接废弃），无需持续效果。
///   - "最多1张"=可选，先 ConfirmOptional 询问。
/// </summary>
public class OP06_101_Aname : IScriptedEffect
{
    public string CardNumber => "OP06-101";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿奈美【登场时】：本回合中，使我方1张领袖或角色获得【流放】效果？");
        if (!use) return;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多1张我方领袖或角色，使其本回合获得【流放】",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.GiveKeyword(tgt, "流放", KeywordDuration.ThisTurn);
    }
}
