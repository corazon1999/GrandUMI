using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-011 鳞片（角色 / 风 / 海王类）
/// 【速攻：角色】（关键词，由引擎处理）
/// 【登场时】我方每有 1 张拥有《海王类》特征的角色，抽取 1 张卡牌。
///   之后，丢弃我方与抽取张数相同数量的手牌。
///
/// 实现说明：
///   - 抽取张数 = 我方场上《海王类》角色数量（含本卡自身，登场时已在场）。
///   - 抽 n 张后，玩家从手牌中选择 n 张丢弃（数量动态，用 ChooseCards 强制选满 n）；
///     手牌不足 n 时按实际可弃数处理（min=max=实际张数）。
/// </summary>
public class EB04_011_Scaler : IScriptedEffect
{
    public string CardNumber => "EB04-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int n = me.Characters.Count(c => c.Info.HasKeyword("海王类"));
        if (n <= 0) return;

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, n);

        // 之后：丢弃相同张数手牌（手牌不足则全弃）
        int toDiscard = Math.Min(n, me.Hand.Count);
        if (toDiscard <= 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            $"丢弃 {toDiscard} 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), toDiscard, toDiscard);

        foreach (var id in pick)
        {
            var dc = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (dc is not null) AtomicOps.DiscardHand(me, dc);
        }
    }
}
