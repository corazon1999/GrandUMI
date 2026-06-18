using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-083 绵羊头（角色 / 地 2 费 3000，百兽海盗团/SMILE）
/// 【咚!!×1】【我方的回合中】对方所有角色费用-1。
///
/// 实现说明：
///   - 纯持续费用修正，用 ContinuousEffect.CostDelta 注册（OnEnterField 注册，离场引擎自动清理）。
///   - 作用方 Side=1（源卡对方），仅角色（IncludeLeader=false）。
///   - 激活条件：本卡被赋予 ≥1 张咚!!（【咚!!×1】）且当前为我方回合。
/// </summary>
public class OP08_083_SheepsHead : IScriptedEffect
{
    public string CardNumber => "OP08-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = -1,
            // 【咚!!×1】= 本卡被赋予至少 1 张咚!!；且【我方的回合中】
            Predicate = (s, sideIdx, c) =>
                s.CurrentTurnPlayer == owner
                && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
