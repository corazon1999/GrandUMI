using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-118 希尔巴兹·雷利（角色 / 炎 8 费 8000，原罗杰海盗团）
/// 【登场时】选择对方最多 2 张角色，直到下个对方的回合结束时为止，其中 1 张角色力量 -3000，
///   且剩下的角色力量 -2000。之后，将对方最多 1 张力量不高于 3000 的角色 KO。
///
/// 实现说明 / 简化点：
///   - 先选最多 2 张对方角色；若选 ≥1 张，第 1 张 -3000，第 2 张 -2000。
///   - "直到下个对方的回合结束时为止" 的精确力量时效引擎无对应字段，按惯例用
///     AddPowerThisTurn（本回合内有效；过早清除，已知简化，参见 OP13-026 注释）。
///   - 力量削减完成后再以 "当前力量（含持续/本次削减后）" 评估 ≤3000 的对方角色供 KO，
///     与文本 "之后" 的结算顺序一致。
/// </summary>
public class OP08_118_Rayleigh : IScriptedEffect
{
    public string CardNumber => "OP08-118";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ① 选择对方最多 2 张角色，分别 -3000 / -2000
        if (opp.Characters.Count > 0)
        {
            var down = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 2 张角色（第 1 张 -3000，第 2 张 -2000）",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 2);
            for (int i = 0; i < down.Count; i++)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == down[i]);
                AtomicOps.AddPowerThisTurn(tgt, i == 0 ? -3000 : -2000);
            }
        }

        // ② 之后：将对方最多 1 张力量不高于 3000 的角色 KO
        var koCands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 3000)
            .ToList();
        if (koCands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量不高于 3000 的角色 KO",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
