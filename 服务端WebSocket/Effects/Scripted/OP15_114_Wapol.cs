using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-114 瓦帕（角色 / 光 5 费 6000，空岛/山迪亚战士）
/// 【登场时】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   本回合中，对方所有角色力量-2000。之后，将对方所有力量不高于 0 的角色 KO。
/// 【启动主要】【每回合1次】赋予我方 1 张拥有《空岛》特征的领袖或角色最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 两个时机：OnEnterField + ActivatedMain，按 ctx.Trigger 分支。
///   - 【登场时】成本"将生命区最上方 1 张翻至正面朝上"：引擎未区分生命牌朝向，作为可选发动开关。
///   - 降力后用含持续光环的当前力量评估 ≤0 的对方角色并 KO(用快照避免遍历时修改集合)。
///   - 【启动主要】每回合 1 次：仅向含《空岛》特征的我方领袖/角色赋予 1 张休息咚。
/// </summary>
public class OP15_114_Wapol : IScriptedEffect
{
    public string CardNumber => "OP15-114";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.LifeArea.Count == 0) return; // 无法支付成本

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "瓦帕【登场时】：将生命区最上方 1 张翻至正面朝上，本回合对方全角色-2000 并KO力量≤0者?");
            if (!use) return;

            // 收益 1：本回合对方所有角色力量-2000（不含领袖）
            AtomicOps.AddPowerToAllThisTurn(ctx.State, 1 - ctx.OwnerIndex, _ => true, -2000, includeLeader: false);

            // 收益 2：KO 对方所有当前力量≤0 的角色
            var toKO = opp.Characters
                .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 0)
                .ToList();
            foreach (var c in toKO)
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, c);
            return;
        }

        // ── 【启动主要】【每回合1次】 ──
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        int restDon = me.CostArea.Count(d => d.State == DonState.Rest);
        if (restDon <= 0) return;

        var targets = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("空岛")) targets.Add(me.Leader);
        targets.AddRange(me.Characters.Where(c => c.Info.HasKeyword("空岛")));
        if (targets.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacterSkypiea",
            "赋予 1 张《空岛》领袖或角色最多 1 张休息状态的咚!!（可不选）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        me.TurnOnceUsed.Add(key);
        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
    }
}
