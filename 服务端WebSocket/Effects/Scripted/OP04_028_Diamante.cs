using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-028 迪亚曼蒂（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【咚!!×1】【我方的回合结束时】我方场上存在 2 张或更多处于活跃状态的咚!! 的场合，将此角色转为活跃状态。
///
/// 实现说明：
///   - 仅实现主动效果部分（【阻挡者】纯关键词由引擎处理）。
///   - 【咚!!×1】= 此角色被赋予中的咚!! ≥1 张时才发动。
///   - 【我方的回合结束时】用 OnMyTurnEnd；条件为我方活跃咚 ≥2 张，满足则将自身转为活跃状态。
/// </summary>
public class OP04_028_Diamante : IScriptedEffect
{
    public string CardNumber => "OP04-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：此角色须有 ≥1 张被赋予中的咚!!
        if (me.AttachedDonCount(self.Id) < 1) return Task.CompletedTask;

        // 条件：我方场上 ≥2 张活跃状态的咚!!
        if (me.ActiveDonCount < 2) return Task.CompletedTask;

        // 效果：将此角色转为活跃状态
        AtomicOps.ActivateCard(self);
        return Task.CompletedTask;
    }
}
