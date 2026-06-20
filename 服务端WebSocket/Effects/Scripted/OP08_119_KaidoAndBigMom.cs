using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-119 盖德&玲玲（角色 / 四皇・百兽海盗团・大妈海盗团）
/// 【攻击时】咚!!-10：将此角色以外的所有角色 KO。之后，将我方卡组最上方的最多 1 张卡牌
///   加入生命区最上方，将对方生命区最上方的最多 1 张卡牌放置到废弃区。
///
/// 实现说明 / 简化点：
///   - DSL 的 triggers 不支持成本，且"除自身外全部角色 KO + 生命连锁"难以稳妥表达，故用脚本实现。
///   - 成本"咚!!-10"为强制成本：触发节惯例为无法支付（可放回咚 < 10）则不发动；
///     这里要求费用区可放回的咚（活跃/休息）≥10 才继续。
///   - "此角色以外的所有角色" = 双方所有角色，排除自身。先快照列表再逐张 KO，避免遍历中修改集合。
///   - "最多 1 张"加生命 / 入废弃：受卡组/生命余量约束，空则跳过。
/// </summary>
public class OP08_119_KaidoAndBigMom : IScriptedEffect
{
    public string CardNumber => "OP08-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：咚!!-10，需费用区有 10 张可放回的咚
        if (me.CostArea.Count < 10) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 10)) return;

        // 将此角色以外的所有角色 KO（双方）。先快照，避免遍历中集合被修改。
        var myTargets = me.Characters.Where(c => c.Id != self.Id).ToList();
        var oppTargets = opp.Characters.ToList();
        foreach (var c in myTargets) AtomicOps.KO(ctx.State, ctx.OwnerIndex, c);
        foreach (var c in oppTargets) AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, c);

        // 之后：将我方卡组最上方的最多 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 将对方生命区最上方的最多 1 张放置到废弃区
        if (opp.LifeArea.Count > 0)
        {
            var oppLifeTop = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(oppLifeTop);
        }
    }
}
