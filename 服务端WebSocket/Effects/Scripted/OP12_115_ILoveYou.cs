using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-115 我爱你哦!!（1 费 事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，我方生命卡牌不多于 2 张的场合，将我方废弃区中最多 1 张
///   "特拉法尔加·罗"加入手牌。
///
/// 实现：先让玩家最多选 1 张领袖或角色 +2000（本次战斗）；
///   随后若我方生命≤2，从废弃区筛出"特拉法尔加·罗"让玩家最多选 1 张入手。
///   两步均为可选（min=0）。废弃区候选通过 extra.choiceCards 显示卡面。
/// </summary>
public class OP12_115_ILoveYou : IScriptedEffect
{
    public string CardNumber => "OP12-115";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 第一步：我方最多 1 张领袖或角色 +2000（本次战斗）
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (target is not null)
                AtomicOps.AddPowerThisBattle(target, 2000);
        }

        // 第二步：我方生命≤2 时，废弃区最多 1 张"特拉法尔加·罗"加入手牌
        if (me.LifeCount > 2) return;

        var lawCards = me.Trash.Where(c => c.MatchesName("特拉法尔加·罗")).ToList();
        if (lawCards.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = lawCards
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "将废弃区中最多 1 张\"特拉法尔加·罗\"加入手牌",
            lawCards.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count == 0) return;

        var picked = lawCards.FirstOrDefault(c => c.Id.ToString() == pick[0]);
        if (picked is not null)
            AtomicOps.TrashToHand(me, picked);
    }
}
