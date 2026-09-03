using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-119 蒙奇·D·路飞。
/// 【登场时】可以将我方场上 1 张或更多咚!!放回咚!!卡组：抽 1 张，本回合此角色获得【速攻】。
/// 选择 0 张即放弃；选中数量及实例在提交前由原子操作重新验证。
/// </summary>
public sealed class OP09_119_Luffy : IScriptedEffect
{
    public string CardNumber => "OP09-119";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (!await AtomicOps.PromptReturnAtLeastOneDonToDeck(ctx)) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }
}
