using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-117 拿破仑（角色）
/// 【启动主要】可以将此角色转为休息状态：直到下个我方的回合开始时为止，
///   我方最多 1 张"夏洛特·玲玲"力量+1000。
///
/// 说明 / 简化点：
///   - 成本：将此角色转为休息状态（RestCard）。
///   - "直到下个我方回合开始时为止" 的跨回合持续力量：用 ContinuousEffect 注册，
///     Predicate 以注册时回合数为基准，在下个我方回合到来前有效。
///     具体：本回合(我方回合)注册，对方回合仍生效，我方下个回合开始即失效。
///     近似实现为 TurnCount <= 基准回合数+1（覆盖本回合与紧随的对方回合）。
///   - 目标"夏洛特·玲玲"在注册时由玩家选定 1 张，效果绑定该卡 Id。
/// </summary>
public class OP03_117_Napoleon : IScriptedEffect
{
    public string CardNumber => "OP03-117";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (self.IsTapped) return; // 已休息无法支付成本

        var targets = me.Characters.Where(c => c.MatchesName("夏洛特·玲玲")).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "拿破仑【启动主要】：将此角色转为休息状态，使我方 1 张『夏洛特·玲玲』力量+1000（直到下个我方回合开始）？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张『夏洛特·玲玲』",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        // 成本：转为休息状态
        AtomicOps.RestCard(self);

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        var tgtId = tgt.Id;
        var selfId = self.Id;
        int baseTurn = ctx.State.TurnCount; // 注册时的回合数（我方回合）

        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            // 有效期至下个我方回合开始前：本回合 + 紧随的对方回合
            Predicate = (s, sideIdx, card) =>
                card.Id == tgtId && s.TurnCount <= baseTurn + 1,
        });
    }
}
