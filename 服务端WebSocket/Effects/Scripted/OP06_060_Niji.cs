namespace GrandUMI.Effects.Scripted;

/// <summary>OP06-060 温思默克·伊智：先支付变身费用，再结算最多一张同名角色登场。</summary>
public class OP06_060_Niji : IScriptedEffect
{
    public string CardNumber => "OP06-060";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => GermaTransformationEffect.Resolve(ctx, "温思默克·伊智", 7);
}
