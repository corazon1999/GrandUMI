using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-036 蒙奇·D·路飞（角色 / 时光旅诗·超新星·草帽一伙）
/// 【登场时】我方场上存在 2 张或更多处于休息状态的角色的场合，
///   将对方最多 1 张费用不高于 6 的角色 或 最多 1 张咚!!转为休息状态。
///
/// 实现说明 / 简化点：
///   - 触发条件：我方场上休息状态角色 ≥ 2 张。
///   - "二选一"用 ChooseOption：分支 A 休息对方 1 张费用≤6 的角色；分支 B 休息对方 1 张活跃咚。
///   - 休息对方角色 = 设其 IsTapped=true；休息对方咚 = 把对方 1 张活跃咚改为 Rest。
/// </summary>
public class OP09_036_Luffy : IScriptedEffect
{
    public string CardNumber => "OP09-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上 ≥2 张休息状态角色
        int restedChars = me.Characters.Count(c => c.IsTapped);
        if (restedChars < 2) return;

        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "路飞【登场时】：选择其一",
            new[] { "将对方 1 张费用≤6 的角色转为休息", "将对方 1 张咚!!转为休息" });

        if (choice == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 6 && !c.IsTapped).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张费用≤6 的角色转为休息",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
        else
        {
            // 将对方 1 张活跃咚转为休息
            foreach (var d in opp.CostArea)
            {
                if (d.State == DonState.Active) { d.State = DonState.Rest; break; }
            }
        }
    }
}
