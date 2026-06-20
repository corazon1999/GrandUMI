using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-081 马歇尔·D·提奇（领航）
/// 持续：我方的【登场时】效果无效。（永续，领袖开局即在场，OnGameStart 注册）
/// 【启动主要】可以丢弃我方的 1 张手牌：
///   直到下个对方的回合结束时为止，对方的【登场时】效果无效。
///
/// 实现说明：
///   - 两段都用 Wave4 的 ContinuousEffect.NullifyOnlyTrigger = OnEnterField。
///   - 我方持续段：Side=0、恒定生效（仅无效化【登场时】，其它效果照常）。
///   - 启动主要段：丢 1 张手牌为成本，注册对方 Side=1 的限时无效化；
///     有效期"直到下个对方回合结束"用 TurnCount 上限近似：注册时记录基准回合数，
///     谓词在 baseTurn..baseTurn+2 区间生效（覆盖紧随的对方回合结束）。
///     用固定 SourceId 后缀去重，避免重复发动叠加。
/// </summary>
public class OP09_081_Teach : IScriptedEffect
{
    public string CardNumber => "OP09-081";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnGameStart || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];

        // ── 持续：我方的【登场时】效果无效（领袖永续，OnGameStart 注册一次）──
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            var selfId = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
                NullifyOnlyTrigger = EffectTrigger.OnEnterField,
                // 仅作用于我方一侧的卡牌
                Predicate = (s, sideIdx, c) => sideIdx == owner,
            });
            return;
        }

        // ── 【启动主要】可以丢弃 1 张手牌：限时无效化对方【登场时】──
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "提奇：丢弃 1 张手牌，直到下个对方回合结束时，对方的【登场时】效果无效？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OwnHand",
            "选择 1 张手牌丢弃",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        // 注册对方限时无效化（仅【登场时】）。有效期至下个对方回合结束。
        int baseTurn = ctx.State.TurnCount;
        var srcId = ctx.Source.Id.ToString() + "-oppnullify";
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == srcId);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = srcId,
            Scope = new ContinuousScope { Side = 1, IncludeLeader = true, IncludeCharacters = true },
            NullifyOnlyTrigger = EffectTrigger.OnEnterField,
            // 作用于对方一侧；有效期覆盖到下个对方回合结束（近似为 +2 回合内）
            Predicate = (s, sideIdx, c) => sideIdx == (1 - owner) && s.TurnCount <= baseTurn + 2,
        });
    }
}
