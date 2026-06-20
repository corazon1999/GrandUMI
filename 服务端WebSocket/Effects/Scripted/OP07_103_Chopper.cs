using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-103 托尼托尼·乔巴（角色）
/// 【触发】本回合中，我方最多1张拥有《艾格赫德》特征的角色获得【阻挡者】效果。
///   之后，将此卡牌加入手牌。
///
/// 实现说明：
///   - 【触发】(OnLifeRevealTrigger)：触发时此卡已被放入废弃区。
///   - 让玩家选择我方最多 1 张《艾格赫德》角色，本回合赋予【阻挡者】(GiveKeyword, ThisTurn)。
///   - 之后将此卡（在废弃区）加入手牌（TrashToHand）。
/// </summary>
public class OP07_103_Chopper : IScriptedEffect
{
    public string CardNumber => "OP07-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本回合中，我方最多 1 张《艾格赫德》角色获得【阻挡者】
        var cands = me.Characters.Where(c => c.Info.HasKeyword("艾格赫德")).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择最多 1 张《艾格赫德》角色，本回合获得【阻挡者】",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.GiveKeyword(tgt, "阻挡者", KeywordDuration.ThisTurn);
            }
        }

        // 之后：将此卡（在废弃区）加入手牌
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.TrashToHand(me, ctx.Source);
    }
}
