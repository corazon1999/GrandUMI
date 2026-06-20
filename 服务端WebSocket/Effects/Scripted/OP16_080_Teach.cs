using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-080 马歇尔·D·提奇（领航）
/// 持续：【对方的回合中】我方所有角色费用 +1（永续效果，需 ContinuousEffect 注册）
/// 【对方的攻击时】每回合 1 次：丢弃 1 张带【触发】的手牌 → 攻击对象变为此领袖或我方《黑胡子海盗团》角色
/// 当前实现：仅启动主要部分（攻击重定向需 BattleEngine 扩展）
/// </summary>
public class OP16_080_Teach : IScriptedEffect
{
    public string CardNumber => "OP16-080";
    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnGameStart || t == EffectTrigger.OnOppAttackDeclare;
    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // 持续：【对方的回合中】我方所有角色费用 +1。
        // 本卡是领袖（开局即在场、不经 OnEnterField），故在 OnGameStart 注册一次永续效果；
        // 领袖恒在场，故不会被 TurnEngine 结束阶段的"来源离场清理"移除。
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            var selfId = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                CostDelta = 1,
                // 仅在对方回合、作用于我方【场上】角色。
                // 必须限定"在场上角色区"，否则 HandPlayCost 也走 ContinuousCostBonus → 手牌角色费用被误+1（反馈#113）。
                Predicate = (s, sideIdx, c) =>
                    sideIdx == owner
                    && s.CurrentTurnPlayer != owner
                    && s.Players[owner].Characters.Any(ch => ch.Id == c.Id),
            });
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP16-080-OncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var triggerCards = me.Hand.Where(c => !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (triggerCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "提奇：弃 1 张带【触发】手牌，把攻击对象变为本领袖或《黑胡子海盗团》角色？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandWithTrigger",
            "选 1 张带【触发】的手牌丢弃",
            triggerCards.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = triggerCards.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);
        me.TurnOnceUsed.Add(key);

        // ── 重定向攻击目标：变为此领袖 或 我方《黑胡子海盗团》角色 ──
        // OnOppAttackDeclare 触发时 CurrentBattle 已建立(我方为防守方)，
        // 伤害结算读 TargetIsLeader/TargetCardId，直接改这两字段即可重定向(同阻挡者机制)。
        var battle = ctx.State.CurrentBattle;
        if (battle is null) return;

        var candidates = new List<CardInstance> { me.Leader };
        candidates.AddRange(me.Characters.Where(c => c.Info.HasKeyword("黑胡子海盗团")));

        // 领袖不在 fieldCards 中，客户端需靠 extra.choiceCards 显示卡面
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RedirectAttackTarget",
            "选择将这次攻击的对象转移到：本领袖或我方《黑胡子海盗团》角色",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (pick.Count == 0) return;

        var newTarget = candidates.First(c => c.Id.ToString() == pick[0]);
        if (newTarget.Id == me.Leader.Id)
        {
            battle.TargetIsLeader = true;
            battle.TargetCardId = null;
        }
        else
        {
            battle.TargetIsLeader = false;
            battle.TargetCardId = newTarget.Id;
        }
    }
}
