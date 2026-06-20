using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-038 我们不会把『伙伴』出卖给敌人的!!!（事件 / 风 / 1费 / 纯毛族·赤鞘九人男）
/// 【主要】可以将我方的2张角色转为休息状态：直到下个对方的回合结束时为止，
///   我方所有角色不会因对方的效果而被KO。
///
/// 实现说明：
///   - 成本（可选）："将我方的 2 张角色转为休息状态"。需场上有 ≥2 张角色才能支付，
///     用 ConfirmOptional 询问是否发动，再 ChooseCards 选 2 张转休息（RestCard）。
///   - 收益：直到下个对方回合结束时为止，我方所有角色"不会因对方的效果被KO"，
///     用 ContinuousEffect.KoGuard = "effect"（仅因效果）注册（规范 13.2）。
///   - 事件本身打出后进入废弃区，无法承载跨回合持续效果；故将来源锚定到我方领袖
///     （始终在场，引擎不会因来源离场而清理），用基于 TurnCount 的 Predicate 自动到期。
///   - 到期回合：本卡为【主要】通常在我方回合发动，"下个对方回合"= TurnCount+1，
///     其结束后失效，故 Predicate 在 s.TurnCount <= expireTurn 期间生效。
/// </summary>
public class OP08_038_WeWillNotSellComrades : IScriptedEffect
{
    public string CardNumber => "OP08-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        // 成本：需要有 2 张角色可转为休息状态
        var cands = me.Characters.ToList();
        if (cands.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "将我方 2 张角色转为休息状态：直到下个对方回合结束时为止，我方所有角色不会因对方的效果被KO？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 2 张角色转为休息状态（成本）",
            cands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (chosen.Count < 2) return; // 未完成成本支付

        foreach (var id in chosen)
        {
            var c = cands.First(x => x.Id.ToString() == id);
            AtomicOps.RestCard(c);
        }

        // 收益：直到下个对方回合结束时为止，我方所有角色不会因对方效果被KO
        // 到期回合 = 下个对方回合（其结束后失效）。
        int expireTurn = ctx.State.CurrentTurnPlayer == owner
            ? ctx.State.TurnCount + 1
            : ctx.State.TurnCount;

        var leaderId = me.Leader.Id;
        ctx.State.ContinuousEffects.RemoveAll(e =>
            e.SourceCardId == leaderId.ToString() && e.KoGuard == "effect");
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),   // 锚定领袖，始终在场不被清理
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",                   // 仅"因效果"被KO时受保护
            // 仅保护我方角色，且在到期回合及之前生效，超过后自动失活
            Predicate = (s, sideIdx, card) => sideIdx == owner && s.TurnCount <= expireTurn,
        });
    }
}
