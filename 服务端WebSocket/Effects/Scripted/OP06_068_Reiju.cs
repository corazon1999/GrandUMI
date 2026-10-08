namespace GrandUMI.Effects.Scripted;

/// <summary>OP06-068 温思默克·丽久：先支付变身费用，再结算最多一张同名角色登场。</summary>
public class OP06_068_Reiju : IScriptedEffect
{
    public string CardNumber => "OP06-068";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => GermaTransformationEffect.Resolve(ctx, "温思默克·丽久", 4);
}
