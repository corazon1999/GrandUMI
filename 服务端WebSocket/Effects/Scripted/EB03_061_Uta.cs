using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-061 乌塔（角色 / 风 / FILM，cost7 power8000）
/// 【启动主要】【每回合1次】将我方最多 1 张咚!! 转为活跃状态。之后，将对方最多 1 张
///   费用不高于 4 的角色或咚!! 转为休息状态。
/// 【我方的回合结束时】可以将我方的 1 张咚!! 转为休息状态：
///   将我方最多 1 张拥有《FILM》特征的角色转为活跃状态。
///
/// 实现说明：
///   - 启动主要：从我方休息咚(DonState.Rest)中取 1 张转活跃(State=Active)；之后让玩家在
///     「对方角色」或「对方咚!!」间二选一，分别将对方费用≤4角色横置(IsTapped=true)或将对方
///     活跃咚(DonState.Active)转休息(DonState.Rest)。每回合 1 次用 TurnOnceUsed。
///   - 回合结束：可选成本=将我方 1 张活跃咚转休息；收益=将我方 1 张休息中的《FILM》角色
///     转为活跃(IsTapped=false)。
/// </summary>
public class EB03_061_Uta : IScriptedEffect
{
    public string CardNumber => "EB03-061";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
            if (me.TurnOnceUsed.Contains(key)) return;
            me.TurnOnceUsed.Add(key);

            // 将我方最多 1 张咚!! 转为活跃状态（取一张休息咚）
            var restDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
            if (restDon != null) restDon.State = DonState.Active;

            // 之后：将对方最多 1 张费用≤4 的角色或咚!! 转为休息状态
            var charCands = opp.Characters.Where(c => !c.IsTapped && c.Info.Cost <= 4).ToList();
            var donCands  = opp.CostArea.Where(d => d.State == DonState.Active).ToList();
            // 对方咚!! 无固有费用，按文本"费用不高于 4 的角色或咚!!"将咚!! 视为可选目标。

            bool hasChar = charCands.Count > 0;
            bool hasDon  = donCands.Count > 0;
            if (!hasChar && !hasDon) return;

            int choice;
            if (hasChar && hasDon)
                choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "将对方 1 张转为休息状态：选择目标类型",
                    new[] { "对方费用≤4的角色", "对方的咚!!" });
            else
                choice = hasChar ? 0 : 1;

            if (choice == 0 && hasChar)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe4",
                    "选择最多 1 张对方费用不高于 4 的角色转为休息状态",
                    charCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = charCands.First(c => c.Id.ToString() == chosen[0]);
                    tgt.IsTapped = true;
                }
            }
            else if (choice == 1 && hasDon)
            {
                // 将对方 1 张活跃咚!! 转为休息状态
                donCands[0].State = DonState.Rest;
            }
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            // 成本：将我方 1 张活跃咚!! 转为休息状态
            if (me.ActiveDonCount < 1) return;

            // 收益候选：我方 1 张休息中的《FILM》角色
            var filmCands = me.Characters.Where(c => c.IsTapped && c.Info.HasKeyword("FILM")).ToList();
            if (filmCands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "乌塔【我方的回合结束时】：将我方 1 张咚!! 转为休息状态，将我方最多 1 张《FILM》角色转为活跃状态？");
            if (!use) return;

            // 支付成本：将 1 张活跃咚转休息
            var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
            if (activeDon == null) return;
            activeDon.State = DonState.Rest;

            // 收益：将最多 1 张《FILM》角色转为活跃状态
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnFilmRestingCharacter",
                "选择最多 1 张我方《FILM》角色转为活跃状态",
                filmCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = filmCands.First(c => c.Id.ToString() == chosen[0]);
                tgt.IsTapped = false;
            }
            return;
        }
    }
}
