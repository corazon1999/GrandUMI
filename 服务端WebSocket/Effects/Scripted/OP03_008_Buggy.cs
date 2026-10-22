using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-008 巴奇（角色 / 炎 1 费 3000，巴奇海盗团）
/// 此角色在与拥有属性（斩）的卡牌的战斗中不会被KO。
/// 【登场时】确认我方卡组最上方的5张卡牌，公开其中最多1张红色事件并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 【登场时】检索：无命中也确认顶5张，公开最多1张红色事件加入手牌，余牌自选顺序放底。
///   - 通过 KoGuard="battle" 读取 CurrentBattle；当本卡作为防守方与斩属性攻击者战斗时不会被KO。
/// </summary>
public class OP03_008_Buggy : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP03-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId || card.IsEffectsNullified || s.IsContinuouslyNullified(card)) return false;
                var battle = s.CurrentBattle;
                if (battle is null || (battle.ReplacedByBlockerCardId ?? battle.TargetCardId) != selfId) return false;
                var attackerSide = s.Players[battle.AttackerPlayerIndex];
                var attacker = attackerSide.Leader.Id == battle.AttackerCardId
                    ? attackerSide.Leader
                    : attackerSide.Characters.FirstOrDefault(c => c.Id == battle.AttackerCardId);
                return attacker is not null && attacker.HasProperty("斩");
            },
        });

        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        await RegisterFieldStatic(ctx);
        var me = ctx.State.Players[ctx.OwnerIndex];

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("红")).ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认卡组顶5张，公开最多1张红色事件加入手牌",
            cand.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(1, cand.Count), extra);
        var picked = ch.Select(id => cand.FirstOrDefault(card => card.Id.ToString() == id)).FirstOrDefault();
        if (picked is not null && me.Deck.Remove(picked))
        {
            me.Hand.Add(picked);
            ctx.BroadcastReveal(picked);
        }

        // 只有选中的牌公开；确认余牌和排序保持为操作者私密信息。
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        var ordered = rest;
        if (rest.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                rest.Select(card => card.Id.ToString()).ToList(), 0, rest.Count,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = rest.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                    ["allowDefaultOrder"] = true,
                });
            ordered = order.Select(id => rest.FirstOrDefault(card => card.Id.ToString() == id))
                .Where(card => card is not null).Cast<CardInstance>().DistinctBy(card => card.Id).ToList();
            ordered.AddRange(rest.Where(card => !ordered.Contains(card)));
        }
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(ordered);
    }
}
