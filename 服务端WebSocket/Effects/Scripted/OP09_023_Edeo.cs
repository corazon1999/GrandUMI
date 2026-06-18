using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-023 艾狄欧（角色 / 时光旅诗）
/// 【登场时】我方领袖拥有《时光旅诗》特征的场合，将我方最多 3 张咚!!转为活跃状态。
/// 【对方的攻击时】【每回合1次】可以将我方的 1 张咚!!转为休息状态：
///   本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///
/// 实现说明 / 简化点：
///   - 登场时：仅当领袖拥有《时光旅诗》特征时，把费用区最多 3 张休息咚转为活跃。
///   - 对方的攻击时：每回合 1 次，可选成本"将 1 张活跃咚转为休息"，支付后我方最多 1 张
///     领袖/角色本次战斗 +2000；无活跃咚则无法支付。
/// </summary>
public class OP09_023_Edeo : IScriptedEffect
{
    public string CardNumber => "OP09-023";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 登场时：领袖拥有《时光旅诗》→ 最多 3 张休息咚转为活跃
            if (!me.Leader.Info.HasKeyword("时光旅诗")) return;
            int activated = 0;
            foreach (var d in me.CostArea)
            {
                if (activated >= 3) break;
                if (d.State == DonState.Rest) { d.State = DonState.Active; activated++; }
            }
            return;
        }

        // 【对方的攻击时】【每回合1次】
        var self = ctx.Source;
        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：需要至少 1 张活跃咚
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾狄欧【对方的攻击时】：将我方 1 张咚!!转为休息状态，本次战斗我方 1 张领袖/角色 +2000？");
        if (!use) return;

        // 支付成本：1 张活跃咚 → 休息
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Active) { d.State = DonState.Rest; break; }
        }

        me.TurnOnceUsed.Add(key);

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }
    }
}
