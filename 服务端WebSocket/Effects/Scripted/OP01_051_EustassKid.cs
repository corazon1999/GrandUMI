using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-051 尤斯塔斯·基德（角色 / 风 / 超新星·基德海盗团 / 8 费 8000）
/// 【咚!!×1】【对方的回合中】此角色处于休息状态的场合，对方不能攻击"尤斯塔斯·基德"以外的角色。
/// 【启动主要】【每回合1次】可以将此角色转为休息状态：将我方手牌中最多 1 张费用不高于 3 的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 仅实现【启动主要】部分：每回合1次，可选支付成本（横置自身），随后从手牌登场最多 1 张费用≤3 角色。
///   - 持续限制"【对方的回合中】此角色休息时对方不能攻击本卡以外的角色"属"攻击目标限制/守护型持续"，
///     引擎暂无对应持续通道，未实现（不影响启动主要部分）。
/// </summary>
public class OP01_051_EustassKid : IScriptedEffect
{
    public string CardNumber => "OP01-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：手牌中费用≤3 的角色
        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost <= 3).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "基德【启动主要】：将此角色转为休息状态，登场手牌中最多 1 张费用≤3 的角色？");
        if (!use) return;

        // 成本：将此角色转为休息状态
        AtomicOps.RestCard(self);
        me.TurnOnceUsed.Add(key);

        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用≤3 的角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
