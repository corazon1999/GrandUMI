using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-090 蒙奇·D·路飞（角色 / 地 / 德莱斯罗兹·草帽一伙）
/// 此角色也可以攻击处于活跃状态的角色。
/// 【启动主要】【每回合1次】可以将我方废弃区的 7 张卡牌自选顺序放回卡组最下方：
///   将此角色转为活跃状态。之后，在下个我方的重置阶段中，此角色不会转为活跃状态。
///
/// 实现说明：
///   - "也可以攻击活跃状态角色" = ContinuousEffect.GrantKeyword="可攻击活跃"（仅此角色，恒定生效）。
///   - 成本 = 将废弃区 7 张放回卡组最下方（ReturnTrashToDeckBottom ×7，简化为原相对顺序）。需废弃区 ≥7 张。
///   - 收益 = ActivateCard(self) + PreventActivateNextReset(self)（下个我方重置阶段不活跃）。
///   - 每回合 1 次用 TurnOnceUsed 标记。
/// </summary>
public class OP04_090_Luffy : IScriptedEffect
{
    public string CardNumber => "OP04-090";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 此角色也可以攻击活跃状态的角色（恒定）
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "可攻击活跃",
                Predicate = (s, sideIdx, c) => c.Id == selfId && sideIdx == owner,
            });
            return;
        }

        // 【启动主要】【每回合1次】
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本可支付性：废弃区须有 7 张
        if (me.Trash.Count < 7) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【启动主要】：将废弃区 7 张卡放回卡组最下方，将此角色转为活跃状态（下个我方重置阶段不活跃）？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：将废弃区 7 张放回卡组最下方
        var toReturn = me.Trash.Take(7).ToList();
        foreach (var c in toReturn) AtomicOps.ReturnTrashToDeckBottom(me, c);

        // 收益：转为活跃 + 下个我方重置阶段不活跃
        AtomicOps.ActivateCard(self);
        AtomicOps.PreventActivateNextReset(self);
    }
}
