using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-042 维德（角色 / 火焰坦克海盗团）
/// 【登场时】可以丢弃我方手牌中 1 张拥有《火焰坦克海盗团》特征的卡牌：
///   本回合中，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 可选效果，先 ConfirmOptional 询问；发动需手牌中存在《火焰坦克海盗团》特征的牌作为成本。
///   - 成本=弃 1 张《火焰坦克海盗团》手牌；之后此角色本回合获得【速攻】(ThisTurn)。
///   - 弃牌候选经 extra.choiceCards 下发卡面。
/// </summary>
public class OP11_042_Vergo : IScriptedEffect
{
    public string CardNumber => "OP11-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本候选：手牌中拥有《火焰坦克海盗团》特征的卡牌
        var cands = me.Hand.Where(c => c.Info.HasKeyword("火焰坦克海盗团")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "维德【登场时】：丢弃 1 张《火焰坦克海盗团》手牌，使此角色本回合获得【速攻】？");
        if (!use) return;

        // 支付成本：弃 1 张《火焰坦克海盗团》手牌
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "丢弃 1 张《火焰坦克海盗团》手牌作为成本",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var toDiscard = chosen.Count > 0
            ? cands.FirstOrDefault(c => c.Id.ToString() == chosen[0])
            : null;
        if (toDiscard is null) return;
        AtomicOps.DiscardHand(me, toDiscard);

        // 效果：此角色本回合获得【速攻】
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
    }
}
