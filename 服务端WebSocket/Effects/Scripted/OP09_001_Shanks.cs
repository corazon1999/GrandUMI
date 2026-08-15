using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP09-001 杰克斯：对方攻击时每回合1次，可令对方最多1张领袖或角色本回合-1000。</summary>
public sealed class OP09_001_Shanks : IScriptedEffect
{
    public string CardNumber => "OP09-001";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = $"OP09-001-opp-attack:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var targets = new[] { opponent.Leader }.Concat(opponent.Characters).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "令对方最多1张领袖或角色本回合力量-1000",
            targets.Select(card => card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var target = targets.First(card => card.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, -1000);
        me.TurnOnceUsed.Add(key);
    }
}
