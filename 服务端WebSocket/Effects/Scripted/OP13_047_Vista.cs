using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-047 佛萨（角色 / 水 / 白胡子海盗团，cost2 power3000）
/// 我方拥有的特征中包含〈白胡子海盗团〉的角色因对方的效果将要被KO的场合，
///   可以改为将此角色（自身）放置到废弃区，使该角色不会被KO。
///
/// 实现说明：
///   - OnAllyWillBeKOd 守护者钩子；"因对方的效果"判 KOReason=="effect" 且 KOActingSide 为对方。
///   - 被守护目标须含〈白胡子海盗团〉特征。置换成本：自身放置废弃区，MarkPreventKO 取消KO。
/// </summary>
public class OP13_047_Vista : IScriptedEffect
{
    public string CardNumber => "OP13-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        if (victimId == self.Id.ToString()) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;

        if (!victim.Info.HasKeyword("白胡子海盗团")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佛萨：将此角色放置到废弃区，使该〈白胡子海盗团〉角色不会被KO？");
        if (!use) return;

        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        me.Characters.Remove(self);
        me.Trash.Add(self);

        ctx.State.MarkPreventKO(victim.Id);
    }
}
