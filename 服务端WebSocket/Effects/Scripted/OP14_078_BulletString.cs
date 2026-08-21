using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP14-078 子弹线：咚-1后为我方领袖或角色提供本次战斗及本回合力量加成。</summary>
public sealed class OP14_078_BulletString : IScriptedEffect
{
    public string CardNumber => "OP14-078";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1, optional: false)) return;

        var candidates = new List<CardInstance> { me.Leader };
        candidates.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多1张领袖或角色：本次战斗力量+2000，且本回合力量+2000",
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(card => card.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisBattle(target, 2000);
        AtomicOps.AddPowerThisTurn(target, 2000);
    }
}
