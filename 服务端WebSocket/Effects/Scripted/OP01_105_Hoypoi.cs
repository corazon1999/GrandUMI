using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-105 保皇（角色 / 暗）
/// 【登场时】选择对方的2张手牌公开。
///
/// 实现：OnEnterField。从对方手牌（非公开区，传 choiceCards 让客户端显示卡面）由我方选最多2张，
/// 用 BroadcastReveal 向双方公开其牌面。
/// </summary>
public class OP01_105_Hoypoi : IScriptedEffect
{
    public string CardNumber => "OP01-105";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.Hand.Count == 0) return;

        int n = Math.Min(2, opp.Hand.Count);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentHand",
            $"选择对方{n}张手牌公开", opp.Hand.Select(c => c.Id.ToString()).ToList(), n, n, extra);
        if (chosen.Count == 0) return;

        var nums = chosen
            .Select(id => opp.Hand.FirstOrDefault(c => c.Id.ToString() == id)?.Info.Number)
            .Where(x => x is not null).Select(x => x!).ToList();
        if (nums.Count > 0) ctx.Engine?.BroadcastReveal(1 - ctx.OwnerIndex, nums);
    }
}
