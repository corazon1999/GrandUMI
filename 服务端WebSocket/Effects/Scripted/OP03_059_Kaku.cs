using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-059 卡古（角色）
/// 【攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   本次战斗中，此角色获得【流放】效果。
///
/// 实现说明：
///   - 可选成本咚!!-1（ReturnDonToDeck 1）；支付后本次战斗赋予【流放】关键词。
///   - 【流放】已被引擎消费（给予伤害时直接废弃不触发），可正常赋予。
/// </summary>
public class OP03_059_Kaku : IScriptedEffect
{
    public string CardNumber => "OP03-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡古【攻击时】：咚!!-1，本次战斗此角色获得【流放】？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.GiveKeyword(ctx.Source, "流放", KeywordDuration.ThisBattle);
    }
}
