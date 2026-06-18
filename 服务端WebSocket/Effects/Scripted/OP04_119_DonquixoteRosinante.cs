using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-119 堂吉诃德·罗西南德（角色 / 风 / 海军・堂吉诃德海盗团）
/// 【对方的回合中】此角色处于休息状态的场合，我方处于活跃状态且原本费用为 5 的角色不会因效果而被 KO。
/// 【登场时】可以将此角色转为休息状态：将我方手牌中最多 1 张费用为 5 的绿色（风）角色卡牌登场。
///
/// 实现说明：
///   - 持续防 KO：ContinuousEffect.KoGuard="effect"，
///     Predicate=对方回合中 && 自身休息 && 目标为我方活跃且原本费用(Info.Cost)为 5 的角色。
///     在 OnEnterField 时机注册，离场由引擎清理。
///   - 【登场时】可选成本=横置自身，收益=登场我方手牌中 1 张费用 5 的绿色角色。
/// </summary>
public class OP04_119_DonquixoteRosinante : IScriptedEffect
{
    public string CardNumber => "OP04-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // ── 持续：对方回合中、自身休息时，我方活跃且原本费用为 5 的角色不会因效果被 KO ──
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) =>
                s.CurrentTurnPlayer != owner &&                 // 对方的回合中
                s.Players[owner].Characters.Any(x => x.Id == selfId && x.IsTapped) && // 自身休息
                sideIdx == owner &&
                c.Info.Kind == CardKind.Character &&
                !c.IsTapped &&                                  // 处于活跃状态
                c.Info.Cost == 5,                              // 原本费用为 5
        });

        // ── 【登场时】可选：横置自身，登场手牌中 1 张费用 5 的绿色角色 ──
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 5 &&
            c.Info.ColorList.Contains("绿")
        ).ToList();
        if (playable.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗西南德【登场时】：将此角色转为休息状态，登场手牌中 1 张费用 5 的绿色角色？");
        if (!use) return;

        AtomicOps.RestCard(self);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场手牌中最多 1 张费用 5 的绿色角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
