using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-034 拉奥·G（角色 / 风 / 堂吉诃德海盗团）
/// 【我方的回合结束时】我方场上存在 3 张或更多处于活跃状态的咚!! 的场合，
///   将对方最多 1 张处于休息状态且费用不高于 3 的角色 KO。
///
/// 实现说明：
///   - 活跃咚数量用 me.ActiveDonCount 判定（≥3）。
///   - 选对方处于休息状态且费用≤3 的角色 KO。
/// </summary>
public class OP04_034_LaoG : IScriptedEffect
{
    public string CardNumber => "OP04-034";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方活跃咚 ≥3
        if (me.ActiveDonCount < 3) return;

        // 对方休息状态且费用≤3 的角色
        var cands = opp.Characters.Where(c => c.IsTapped && c.CurrentCost() <= 3).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张休息状态且费用≤3 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
