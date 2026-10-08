namespace GrandUMI.Effects.Scripted;

/// <summary>OP06-066 温思默克·勇智：先支付变身费用，再结算最多一张同名角色登场。</summary>
public class OP06_066_Yonji : IScriptedEffect
{
    public string CardNumber => "OP06-066";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => GermaTransformationEffect.Resolve(ctx, "温思默克·勇智", 4);
}
