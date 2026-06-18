using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-009 千里·阳光号（舞台 / 炎 / 草帽一伙）
/// 【启动主要】可以将此舞台转为休息状态：将我方最多 1 张被赋予中的咚!! 赋予我方 1 张拥有《草帽一伙》特征的角色。
///
/// 实现说明：
///   - 成本「将此舞台转为休息状态」用 RestCard(ctx.Source)；已休息或无被赋予中的咚/无草帽角色则无意义，按需提示。
///   - 「将一张被赋予中的咚转移给另一张草帽角色」：在费用区找到处于 Attached 状态的咚，改其 AttachedToCardId
///     指向所选草帽角色（保持 Attached 状态，相当于在角色间转移）。源咚可贴在任意角色/领袖上。
///   - 玩家先选 1 张目标草帽角色，再从 Attached 咚中转移 1 张过去（取第 1 张被赋予中的咚）。
/// </summary>
public class EB02_009_ThousandSunny : IScriptedEffect
{
    public string CardNumber => "EB02-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 已休息无法再支付成本
        if (self.IsTapped) return;

        // 必须存在被赋予中的咚 与 草帽角色，否则无意义
        var attachedDon = me.CostArea.Where(d => d.State == DonState.Attached).ToList();
        if (attachedDon.Count == 0) return;
        var strawTargets = me.Characters.Where(c => c.Info.HasKeyword("草帽一伙")).ToList();
        if (strawTargets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "千里·阳光号【启动主要】：将此舞台转为休息状态，把 1 张被赋予中的咚!! 转移给 1 张《草帽一伙》角色？");
        if (!use) return;

        // 成本：将此舞台转为休息状态
        AtomicOps.RestCard(self);

        // 选择 1 张草帽角色作为转移目标
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张《草帽一伙》角色，赋予其 1 张被赋予中的咚!!",
            strawTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var tgt = strawTargets.First(c => c.Id.ToString() == chosen[0]);

        // 转移 1 张被赋予中的咚到目标角色（取第 1 张 Attached 咚）
        var don = attachedDon[0];
        don.AttachedToCardId = tgt.Id;
    }
}
