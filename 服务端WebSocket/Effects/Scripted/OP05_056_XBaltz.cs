using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-056 X·巴雷尔兹（角色）
/// 【登场时】可以将我方 1 张此角色以外的角色放回卡组最下方：抽取 1 张卡牌。
///
/// 实现说明：
///   - 可选成本与收益耦合：仅当玩家选择支付成本（放回 1 张其他角色）时才抽 1。
///   - 先 ConfirmOptional，再强制选 1 张我方其他角色作为成本；选满成本后抽 1。
/// </summary>
public class OP05_056_XBaltz : IScriptedEffect
{
    public string CardNumber => "OP05-056";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var cands = me.Characters.Where(c => c.Id != self.Id).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "X·巴雷尔兹【登场时】：将我方 1 张其他角色放回卡组最下方以抽取 1 张？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张此角色以外的我方角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count < 1) return; // 成本未支付 → 不抽

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, picked);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
