using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-006 罗罗诺亚·佐罗（角色 / 风 / 超新星・草帽一伙 / cost4 power4000）【阻挡者】
/// 【对方的回合中】此角色因对方角色的效果将要转为休息状态的场合，
///   可以改为将我方 1 张其他的角色转为休息状态，使此角色不会转为休息状态。
///
/// 实现说明 / 取舍：
///   - 引擎无"将要转为休息"的前置钩子（PreRest），改为用后置 watcher OnCharRested「事后撤销」等效实现：
///     自身因效果被横置后触发 → ActivateCard(self) 撤销自身休息 + 改为休息我方 1 张其他活跃角色。
///   - "因对方角色的效果"近似为：对方回合中（CurrentTurnPlayer != OwnerIndex）且被横置者为本卡自身。
///     OnCharRested 仅在效果横置(RestCard)时派发，已排除攻击宣言的横置；无法区分来源是角色效果还是事件，按近似处理。
///   - "可以"为可选效果，用 ConfirmOptional；需存在我方其他活跃角色作为代替对象，否则不发动。
///   - 局限：撤销为事后操作，OnCharRested 此前已派发给其他 watcher（基于"自身已休息"的连锁罕见），按边界忽略。
///   - 【阻挡者】为纯关键词，由引擎处理。
/// </summary>
public class PRB02_006_Zoro : IScriptedEffect
{
    public string CardNumber => "PRB02-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【对方的回合中】（近似"因对方角色的效果"）
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 仅本卡自身被横置时触发
        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != self.Id.ToString()) return;

        // 需存在我方其他活跃角色作为代替对象
        var others = me.Characters.Where(c => c != self && !c.IsTapped).ToList();
        if (others.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佐罗：改为将我方 1 张其他的角色转为休息状态，使此角色不会转为休息状态？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = others.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张其他的角色转为休息状态（代替佐罗）",
            others.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count == 0) return;
        var target = others.First(c => c.Id.ToString() == chosen[0]);

        // 撤销自身休息 + 改为休息所选角色
        AtomicOps.ActivateCard(self);
        AtomicOps.RestCard(target);
    }
}
