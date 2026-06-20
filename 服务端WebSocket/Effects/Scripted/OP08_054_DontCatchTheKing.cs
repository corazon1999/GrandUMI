using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-054 总不能让你一下子就把『王』给擒了吧!（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +3000。之后，公开我方卡组最上方的 1 张卡牌，
///   并将最多 1 张费用不高于 3 且拥有 <白胡子海盗团> 特征的角色卡牌登场。
///   之后，将剩余的卡牌放回卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 力量加成走 AddPowerThisBattle（本次战斗）。
///   - 公开顶牌通过 prompt extra.choiceCards 下发卡面；满足条件可登场。
///   - "放回最上方或最下方"：未登场时玩家二选一放顶/放底。
/// </summary>
public class OP08_054_DontCatchTheKing : IScriptedEffect
{
    public string CardNumber => "OP08-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色 +3000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，选择我方最多 1 张领袖或角色 +3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 3000);
        }

        // 公开卡组最上方 1 张
        if (me.Deck.Count == 0) return;
        var top = me.Deck[0];

        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new[] { new { id = top.Id.ToString(), number = top.Info.Number } }.ToList(),
        };

        bool eligible = top.Info.Kind == CardKind.Character
            && top.Info.Cost <= 3
            && top.Info.HasKeyword("白胡子海盗团");

        if (eligible)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开卡组顶 1 张，可将该费用≤3 的《白胡子海盗团》角色登场",
                new List<string> { top.Id.ToString() }, 0, 1, revealExtra);
            if (pick.Count > 0)
            {
                me.Deck.Remove(top);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, top);
                return; // 已登场，无剩余牌
            }
        }

        // 未登场：放回卡组最上方或最下方
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将公开的卡牌放回卡组最上方还是最下方？", new[] { "最上方", "最下方" });
        if (where == 1)
        {
            me.Deck.Remove(top);
            me.Deck.Add(top);
        }
    }
}
