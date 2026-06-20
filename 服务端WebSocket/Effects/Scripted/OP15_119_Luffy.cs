using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-119 蒙奇·D·路飞（角色 / 光 / 5费 7000 / 空岛・草帽一伙）
/// 1) 我方场上存在 6 张或更多咚!! 的场合，此角色获得【速攻】效果。
///    → ContinuousEffect.GrantKeyword="速攻"，Predicate 限定本卡且我方 TotalDonInCostArea>=6。
/// 2) 当对方发动事件或【阻挡者】效果时，公开我方生命区最上方的最多 1 张卡牌。
///    公开的卡牌每有 1 点费用，本回合中，此角色的力量 +1000。
///    → OnOppEventPlayed / OnOppBlocker 两个 watcher，公开生命顶 1 张并按其费用×1000 加力量（本回合）。
///
/// 实现说明 / 简化点：
///   - 持续【速攻】在 OnEnterField 注册，离场由引擎清理；6 张咚条件由 Predicate 实时判定。
///   - 反应触发只在确认出牌/阻挡方为对方时发动；公开生命顶牌走 BroadcastReveal 给对手看牌面，
///     公开后生命牌仍留在生命区（"公开"≠移动）。
///   - 力量加成用 AddPowerThisTurn(费用×1000)，每次对方发动事件/阻挡者各结算一次（与卡面一致）。
/// </summary>
public class OP15_119_Luffy : IScriptedEffect
{
    public string CardNumber => "OP15-119";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField ||
        t == EffectTrigger.OnOppEventPlayed ||
        t == EffectTrigger.OnOppBlocker;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        // ── 1) 登场时注册持续【速攻】（6 张以上咚条件）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (s, sideIdx, c) =>
                    sideIdx == owner && c.Id == selfId &&
                    s.Players[owner].TotalDonInCostArea >= 6,
            });
            return Task.CompletedTask;
        }

        // ── 2) 对方发动事件 / 阻挡者时：公开生命顶 1 张，每费用 +1000（本回合）──
        if (ctx.Trigger == EffectTrigger.OnOppEventPlayed)
        {
            var owId = ctx.Vars.TryGetValue("owner", out var v) ? v : null;
            if (owId is int evOwner && evOwner == owner) return Task.CompletedTask; // 自己出牌不触发
        }
        else if (ctx.Trigger == EffectTrigger.OnOppBlocker)
        {
            var bwId = ctx.Vars.TryGetValue("blockerOwner", out var v) ? v : null;
            if (bwId is int bOwner && bOwner == owner) return Task.CompletedTask; // 自己阻挡不触发
        }

        if (me.LifeArea.Count == 0) return Task.CompletedTask;

        var topLife = me.LifeArea[0];
        // 向对手公开该生命牌牌面（仍留在生命区）
        ctx.Engine?.BroadcastReveal(owner, new List<string> { topLife.Info.Number });

        int cost = topLife.Info.Cost;
        if (cost > 0)
        {
            AtomicOps.AddPowerThisTurn(self, cost * 1000);
        }

        return Task.CompletedTask;
    }
}
