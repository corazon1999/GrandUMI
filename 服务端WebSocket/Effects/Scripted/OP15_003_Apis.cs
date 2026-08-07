using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-003 爱比达（置换 KO 效果）
/// 此角色将要被 KO 的场合，可以改为丢弃我方手牌中 1 张力量不高于 6000 的角色卡牌，使此角色不会被 KO。
///
/// 通过 PreKO 触发实现：BattleEngine.KOCardAsync 在 KO 卡牌前先 Resolve PreKO，
/// 此脚本写入 ctx.State.MarkPreventKO(ctx.Source.Id) 即可取消本次 KO。
/// </summary>
public class OP15_003_Apis : IScriptedEffect
{
    public string CardNumber => "OP15-003";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            string key = $"OP15-003-act:{ctx.Source.Id}";
            if (me.TurnOnceUsed.Contains(key)) return;
            int opponent = 1 - ctx.OwnerIndex;
            var opp = ctx.State.Players[opponent];
            if (!opp.CostArea.Any(don => don.State == DonState.Rest)
                || !me.CostArea.Any(don => don.State == DonState.Rest)
                || opp.Characters.Count == 0) return;
            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    "爱比达【启动主要】：赋予对方角色 1 张对方休息咚，再赋予 1 张领袖或角色 1 张其持有者休息咚？")) return;
            var opponentTarget = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方 1 张角色，赋予 1 张对方休息咚",
                opp.Characters.Select(card => card.Id.ToString()).ToList(), 1, 1);
            if (opponentTarget.Count == 0) return;
            var costTarget = opp.Characters.First(card => card.Id.ToString() == opponentTarget[0]);
            if (AtomicOps.AttachDonFromCost(opp, costTarget.Id, 1, DonState.Rest) == 0) return;
            me.TurnOnceUsed.Add(key);

            var possible = new List<(int Owner, CardInstance Card)>();
            for (int owner = 0; owner < 2; owner++)
            {
                var side = ctx.State.Players[owner];
                if (!side.CostArea.Any(don => don.State == DonState.Rest)) continue;
                possible.Add((owner, side.Leader));
                possible.AddRange(side.Characters.Select(card => (owner, card)));
            }
            if (possible.Count == 0) return;
            var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LeaderOrCharacter",
                "选择最多 1 张领袖或角色，赋予 1 张其持有者的休息咚",
                possible.Select(item => item.Card.Id.ToString()).ToList(), 0, 1,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = possible.Select(item => new { id = item.Card.Id.ToString(), number = item.Card.Info.Number }).ToList(),
                });
            if (selected.Count > 0)
            {
                var target = possible.First(item => item.Card.Id.ToString() == selected[0]);
                AtomicOps.AttachDonFromCost(ctx.State.Players[target.Owner], target.Card.Id, 1, DonState.Rest);
            }
            return;
        }

        var candidates = me.Hand
            .Where(c => c.Info.Kind == Cards.CardKind.Character && c.Info.Power <= 6000)
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "爱比达：是否丢弃 1 张手牌（力量≤6000 的角色）以避免被 KO？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardHand",
            "选择 1 张要丢弃的角色卡",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var toDiscard = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, toDiscard);
        ctx.State.MarkPreventKO(ctx.Source.Id);
    }
}
