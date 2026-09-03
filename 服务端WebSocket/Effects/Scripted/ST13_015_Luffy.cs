using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST13-015 路飞：每回合1次，自身+2000直到下个我方回合开始；有生命时抽1并废弃生命顶。</summary>
public sealed class ST13_015_Luffy : IScriptedEffect
{
    public string CardNumber => "ST13-015";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = $"ST13-015-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);
        AtomicOps.AddPowerUntilOppEnd(ctx.Source, 2000, ctx.OwnerIndex);
        if (me.LifeArea.Count > 0)
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            var life = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            life.IsLifeFaceUp = false;
            me.Trash.Add(life);
        }
        return;
    }
}
