using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST21-001 路飞：咚×1，启动主要每回合1次，赋予我方1张角色最多2张休息咚。</summary>
public sealed class ST21_001_Luffy : IScriptedEffect
{
    public string CardNumber => "ST21-001";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = $"ST21-001-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || me.AttachedDonCount(ctx.Source.Id) < 1) return;
        var targets = me.Characters.ToList();
        if (targets.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "赋予我方最多1张角色最多2张休息咚!!",
            targets.Select(card => card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var target = targets.First(card => card.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Rest);
        me.TurnOnceUsed.Add(key);
    }
}
