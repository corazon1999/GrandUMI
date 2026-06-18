using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-001 斯摩格（领航 / 炎风 4 费 5000，班克禁区/海军）
/// 1.【对方的回合中】我方所有拥有《海军》或《班克禁区》特征的角色力量+1000。
/// 2.【启动主要】【每回合1次】我方场上存在力量为7000或更高的角色的场合，
///    将我方最多2张咚!!转为活跃状态。
///
/// 实现说明：
/// - 第一段为「对方回合中」对我方全体特征角色的持续力量修正 → ContinuousEffect 注册，
///   在 OnEnterField（领袖始终在场，登场即注册）时机注册；Predicate 限定对方回合中。
/// - 第二段启动主要每回合1次：检查我方是否存在当前力量≥7000的角色，满足则把最多 2 张
///   休息状态的咚!!转为活跃状态（咚之间无区别，无需玩家选择）。
/// </summary>
public class OP10_001_Smoker : IScriptedEffect
{
    public string CardNumber => "OP10-001";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope
                {
                    Side = 0,
                    IncludeLeader = false,
                    IncludeCharacters = true,
                    Filter = c => c.Info.HasKeyword("海军") || c.Info.HasKeyword("班克禁区"),
                },
                PowerDelta = 1000,
                // 作用对象=我方〈海军〉/〈班克禁区〉角色：Scope.Filter 仅供显示、引擎忽略，
                // 过滤条件必须写进 Predicate，否则对方回合时全场每张卡都 +1000。
                Predicate = (s, sideIdx, card) =>
                    s.CurrentTurnPlayer != owner &&
                    sideIdx == owner &&
                    card.Info.Kind != CardKind.Leader &&
                    (card.Info.HasKeyword("海军") || card.Info.HasKeyword("班克禁区")),
            });
            return;
        }

        // ── 【启动主要】【每回合1次】 ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方场上存在力量≥7000的角色
        bool hasBigChar = me.Characters.Any(c => ctx.State.CurrentPowerOf(owner, c) >= 7000);
        if (!hasBigChar) return;

        me.TurnOnceUsed.Add(key);

        // 将我方最多 2 张休息状态的咚!!转为活跃状态
        int activated = 0;
        foreach (var d in me.CostArea)
        {
            if (activated >= 2) break;
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                activated++;
            }
        }
        await Task.CompletedTask;
    }
}
