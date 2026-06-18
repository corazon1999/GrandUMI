using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-001 特拉法尔加·罗（领航 5 费 5000，王下七武海/超新星/心脏海盗团）
/// 【启动主要】【每回合1次】选择我方 2 张拥有《超新星》或《心脏海盗团》特征的角色。
///   本回合中，将所选角色各自原本的力量互换。
///
/// 实现说明：
///   - "本回合中原本的力量互换"用 CardInstance.OriginalPowerOverride 实现：把 A 的原本力量
///     设为 B 的原本力量、把 B 的原本力量设为 A 的原本力量。该字段在回合结束时由 TurnEngine
///     自动清零，符合"本回合中"语义。
///   - 取"原本的力量"为各自当前生效的原本值（OriginalPowerOverride ?? Info.Power），
///     以正确处理同回合内的连续/叠加互换。
/// </summary>
public class OP14_001_Law : IScriptedEffect
{
    public string CardNumber => "OP14-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "OP14-001-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var cands = me.Characters
            .Where(c => c.Info.HasKeyword("超新星") || c.Info.HasKeyword("心脏海盗团"))
            .ToList();
        if (cands.Count < 2) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 2 张《超新星》或《心脏海盗团》角色，本回合互换原本力量",
            cands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (chosen.Count < 2) return;

        var a = cands.First(c => c.Id.ToString() == chosen[0]);
        var b = cands.First(c => c.Id.ToString() == chosen[1]);
        if (a.Id == b.Id) return;

        me.TurnOnceUsed.Add(key);

        int aBase = a.OriginalPowerOverride ?? a.Info.Power;
        int bBase = b.OriginalPowerOverride ?? b.Info.Power;
        a.OriginalPowerOverride = bBase;
        b.OriginalPowerOverride = aBase;
    }
}
