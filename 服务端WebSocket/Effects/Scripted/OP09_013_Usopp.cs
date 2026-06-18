using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-013 耶稣布（角色 / 炎 5 费 6000，红发海盗团）
/// 【登场时】直到下个对方的回合结束时为止，我方最多 1 张领袖力量 +1000。
/// 【咚×1】【攻击时】本回合中，对方最多 1 张角色力量 -1000。
///
/// 实现说明 / 简化点：
///   - "直到下个对方的回合结束时为止 +1000" 的精确力量时效引擎无对应字段，按惯例用
///     AddPowerThisTurn（本回合有效；过早清除，已知简化，参见 OP13-026 注释）。
///   - 【咚×1】为发动条件：仅当此角色附着的咚 ≥1 时发动【攻击时】效果。
/// </summary>
public class OP09_013_Usopp : IScriptedEffect
{
    public string CardNumber => "OP09-013";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 我方最多 1 张领袖力量 +1000（本卡只有 1 张领袖，仍按 "最多 1 张" 给可选）
            bool buff = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "耶稣布【登场时】：我方领袖力量 +1000？");
            if (buff) AtomicOps.AddPowerThisTurn(me.Leader, 1000);
            return;
        }

        // 【攻击时】发动条件【咚×1】：此角色需附着 ≥1 张咚
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        if (opp.Characters.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "本回合中，对方最多 1 张角色力量 -1000",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -1000);
        }
    }
}
