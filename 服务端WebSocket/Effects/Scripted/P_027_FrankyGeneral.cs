using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-027 弗兰奇将军（角色 / 炎 / 草帽一伙，cost2 power4000）
/// 规则上，此卡牌的卡牌名称也视为"弗兰奇"。
/// 【对方的回合中】我方所有原本力量不高于 3000 的角色力量 +1000。
///
/// 实现说明：
///   - 名称替身：登场时为本卡追加 NameAlias "弗兰奇"。
///   - 持续力量：ContinuousEffect(PowerDelta=1000)，Scope=我方角色，
///     Predicate=对方回合中 && 该角色"原本力量"(c.Info.Power)≤3000。
/// </summary>
public class P_027_FrankyGeneral : IScriptedEffect
{
    public string CardNumber => "P-027";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 名称也视为"弗兰奇"
        if (!self.NameAliases.Contains("弗兰奇")) self.NameAliases.Add("弗兰奇");

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.Power <= 3000,
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer != owner, // 仅对方回合中
        });
        return Task.CompletedTask;
    }
}
