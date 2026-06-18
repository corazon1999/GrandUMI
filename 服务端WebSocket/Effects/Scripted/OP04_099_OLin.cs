using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-099 阿玲（角色 / 光 / 和之国·四皇·大妈海盗团）
/// 规则上，此卡牌的卡牌名称也视为"夏洛特·玲玲"。
/// 【触发】我方生命卡牌不多于 1 张的场合，此卡牌登场。
///
/// 实现说明：
///   - "卡名也视为夏洛特·玲玲"用 NameAliases 实现：登场时为自身追加别名，MatchesName 会同时命中。
///   - 【触发】(OnLifeRevealTrigger)：此卡作为被翻开的生命牌发动触发后已入废弃区；
///     我方生命≤1 时将其从废弃区登场（PlayFromTrashFree）。同时为登场体补上别名。
/// </summary>
public class OP04_099_OLin : IScriptedEffect
{
    public string CardNumber => "OP04-099";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (!self.NameAliases.Contains("夏洛特·玲玲"))
            self.NameAliases.Add("夏洛特·玲玲");

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 我方生命≤1 时，此卡从废弃区登场
            if (me.LifeArea.Count <= 1 && me.Trash.Contains(self))
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        }

        return Task.CompletedTask;
    }
}
