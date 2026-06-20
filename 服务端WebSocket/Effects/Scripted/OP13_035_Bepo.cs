using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-035 贝宝（角色 / 风 / 5 费 / 7000 / 纯毛族・FILM・心脏海盗团）
/// 【我方的回合结束时】将此角色或我方最多 1 张咚!!转为活跃状态。
///
/// 实现：我方回合结束时，二选一：
///   (A) 将此角色转为活跃状态（解除休息）；或
///   (B) 将我方最多 1 张休息状态的咚!!转为活跃状态。
/// 让玩家在"此角色 / 1 张休息咚"中选择 1 个目标转活跃（可不选）。
/// </summary>
public class OP13_035_Bepo : IScriptedEffect
{
    public string CardNumber => "OP13-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 构造候选：此角色（若处于休息）+ 1 张休息状态的咚!!（用占位 id 表示）
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);

        var idList = new List<string>();
        var labels = new List<object>();
        if (ctx.Source.IsTapped)
        {
            idList.Add(ctx.Source.Id.ToString());
            labels.Add(new { id = ctx.Source.Id.ToString(), number = ctx.Source.Info.Number });
        }
        const string donChoice = "DON";
        if (don is not null)
        {
            idList.Add(donChoice);
            labels.Add(new { id = donChoice, number = "DON" });
        }
        if (idList.Count == 0) return;

        var extra = new Dictionary<string, object?> { ["choiceCards"] = labels };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OP13035ActivateChoice",
            "将此角色或我方最多 1 张咚!!转为活跃状态（可不选）",
            idList, 0, 1, extra);
        if (chosen.Count == 0) return;

        if (chosen[0] == donChoice)
        {
            if (don is not null) { don.State = DonState.Active; don.AttachedToCardId = null; }
        }
        else
        {
            AtomicOps.ActivateCard(ctx.Source);
        }
    }
}
