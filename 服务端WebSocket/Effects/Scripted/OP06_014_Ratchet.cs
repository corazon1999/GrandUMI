using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-014 拉切特（角色，炎 1 费 0，FILM/机械岛）
/// 【对方的攻击时】可以丢弃我方手牌中任意张数的拥有《FILM》特征的卡牌。
///   每丢弃 1 张卡牌，本次战斗中，我方的 1 张领袖或角色力量 +1000。
///
/// 实现说明：
///   - 可变成本"任意张数 FILM 手牌"：玩家从手牌中 FILM 卡里选 0..N 张丢弃。
///   - 收益按张数缩放：每丢 1 张 +1000，本次战斗中（AddPowerThisBattle）。
///     文本为"我方的 1 张领袖或角色" → 全部加成集中给玩家所选的同 1 张目标。
///   - 触发节(OnOppAttackDeclare)无 cost 通道且无法表达可变张数，故用脚本。
/// </summary>
public class OP06_014_Ratchet : IScriptedEffect
{
    public string CardNumber => "OP06-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var filmCards = me.Hand.Where(c => c.Info.HasKeyword("FILM")).ToList();
        if (filmCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "拉切特【对方的攻击时】：丢弃任意张数 FILM 手牌，每丢 1 张令我方 1 张领袖/角色本次战斗 +1000？");
        if (!use) return;

        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = filmCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var toDiscard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃任意张数拥有《FILM》特征的手牌",
            filmCards.Select(c => c.Id.ToString()).ToList(), 0, filmCards.Count, discardExtra);
        if (toDiscard.Count == 0) return;

        foreach (var cid in toDiscard)
        {
            var card = filmCards.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }

        int delta = toDiscard.Count * 1000;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            $"选择我方 1 张领袖或角色，本次战斗中力量 +{delta}",
            targets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count > 0)
        {
            var target = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(target, delta);
        }
    }
}
