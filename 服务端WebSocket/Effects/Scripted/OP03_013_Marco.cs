using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-013 马尔高（角色 / 炎 5 费 6000，白胡子海盗团）
/// 【我方的回合中】【登场时】将对方最多1张力量不高于3000的角色KO。
/// 【KO时】可以从我方的手牌中丢弃1张事件：从废弃区中以休息状态登场此角色卡牌。
///
/// 实现说明：
///   - 【登场时】仅在我方回合生效：KO对方最多1张力量≤3000角色（当前力量用 CurrentPowerOf）。
///   - 【KO时】（OnKO，此卡已进入废弃区，ctx.Source 即本卡）：可选成本=丢弃手牌中1张事件，
///     之后用 PlayFromTrashFree(restState:true) 从废弃区以休息状态登场此角色。
/// </summary>
public class OP03_013_Marco : IScriptedEffect
{
    public string CardNumber => "OP03-013";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 仅我方回合中生效
            if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

            var cands = opp.Characters
                .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 3000).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张力量≤3000的角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
            return;
        }

        // ── OnKO ──
        // 此角色须确实在废弃区（被KO后由引擎放入）
        if (!me.Trash.Contains(ctx.Source)) return;

        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "马尔高被KO：丢弃手牌中1张事件，从废弃区以休息状态登场此角色？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "成本：丢弃手牌中1张事件",
            events.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (discardPick.Count < 1) return;
        var discard = events.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, discard);

        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: true);
    }
}
