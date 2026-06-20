using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-015 日熊（角色）
/// 【登场时】赋予对方 1 张角色最多 1 张对方休息状态的咚!!。
///   之后，本回合中，对方最多 1 张被赋予咚!! 的角色力量 -1000。
///
/// 实现说明 / 简化点：
///   - "赋予对方角色对方休息状态的咚!!"：从对方费用区取一张活跃咚附着到所选对方角色
///     （引擎置为 Attached），与"对方持有的咚贴到对方角色"语义一致。
///   - "之后，对方最多 1 张被赋予咚!! 的角色 -1000(本回合)"：在被赋予咚的对方角色中选 1 张，
///     用 AddPowerThisTurn(-1000)。
/// </summary>
public class OP15_015_Higuma : IScriptedEffect
{
    public string CardNumber => "OP15-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 赋予对方 1 张角色最多 1 张咚!! ──
        if (opp.Characters.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择 1 张对方角色，赋予其持有者 1 张休息状态的咚!!",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(opp, tgt.Id, 1, DonState.Active);
            }
        }

        // ── 之后：对方最多 1 张被赋予咚!! 的角色力量 -1000（本回合） ──
        var donChars = opp.Characters
            .Where(c => opp.CostArea.Any(d => d.State == DonState.Attached && d.AttachedToCardId == c.Id))
            .ToList();
        if (donChars.Count == 0) return;

        var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张被赋予咚!! 的对方角色，本回合 -1000",
            donChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch2.Count > 0)
        {
            var tgt2 = donChars.First(c => c.Id.ToString() == ch2[0]);
            AtomicOps.AddPowerThisTurn(tgt2, -1000);
        }
    }
}
