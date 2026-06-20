using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-061 温思默克·伊智（角色）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   本回合中，对方最多 1 张角色力量 -2000，且此角色获得【速攻】效果。
///
/// 实现说明 / 简化点：
///   - 条件为"我方咚数 ≤ 对方咚数"的相对比较，脚本直接用 TotalDonInCostArea 比较即可。
///   - "对方最多 1 张角色 -2000"为可选，由我方选择 0~1 张目标。
///   - 【速攻】通过 GiveKeyword(self, "速攻", ThisTurn) 本回合赋予自身（引擎据此放行登场回合攻击）。
/// </summary>
public class OP06_061_Niji : IScriptedEffect
{
    public string CardNumber => "OP06-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：我方场上咚数 ≤ 对方场上咚数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        // 本回合中自身获得【速攻】
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);

        // 本回合中对方最多 1 张角色 -2000（可选）
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张对方角色，本回合力量 -2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var target = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(target, -2000);
        }
    }
}
