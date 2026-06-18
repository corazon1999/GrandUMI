using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-105 特拉法尔加·拉米（1 费 0）
/// 【我方的回合中】【登场时】本回合中，我方最多 1 张"特拉法尔加·罗"力量 +2000。
///
/// 实现：登场时，仅在我方回合生效。从我方场上名为"特拉法尔加·罗"的角色中
/// 让玩家最多选 1 张赋予本回合 +2000（可选，min=0）。
/// （Choose DSL 候选无法按卡名过滤，故用脚本自建候选列表。）
/// </summary>
public class OP12_105_Lami : IScriptedEffect
{
    public string CardNumber => "OP12-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 【我方的回合中】：仅在我方回合登场时生效
        if (s.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 候选：我方场上名为"特拉法尔加·罗"的角色
        var candidates = me.Characters
            .Where(c => c.MatchesName("特拉法尔加·罗"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选最多 1 张\"特拉法尔加·罗\"本回合力量 +2000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AddPowerThisTurn(target, 2000);
    }
}
