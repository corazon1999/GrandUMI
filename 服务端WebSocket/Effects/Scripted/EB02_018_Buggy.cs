using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-018 巴奇（角色 / 风 / 东海·巴奇海盗团）
/// 【登场时】我方场上不存在其他的角色"巴奇"的场合，本回合中，我方最多 1 张领袖获得【双重攻击】效果。
/// 【触发】将对方最多 1 张费用不高于 4 的角色转为休息状态。（关键词触发，由引擎/触发流程处理）
///
/// 实现说明：
///   - 条件「我方场上不存在其他的角色『巴奇』」：统计我方角色中名为"巴奇"且非自身的数量为 0 才成立，用 C# 直接判断。
///   - 「本回合中我方最多 1 张领袖获得【双重攻击】」：领袖唯一，玩家可选 0..1 张领袖，对其 GiveKeyword(双重攻击, ThisTurn)。
///   - 仅实现【登场时】主动效果；【触发】KO/横置由引擎触发流程处理。
/// </summary>
public class EB02_018_Buggy : IScriptedEffect
{
    public string CardNumber => "EB02-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：我方场上不存在其他角色"巴奇"
        bool hasOtherBuggy = me.Characters.Any(c => c.Id != self.Id && c.MatchesName("巴奇"));
        if (hasOtherBuggy) return;

        // 本回合中，我方最多 1 张领袖获得【双重攻击】
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
            "选择我方最多 1 张领袖，本回合获得【双重攻击】",
            new List<string> { me.Leader.Id.ToString() }, 0, 1);
        if (chosen.Count > 0)
        {
            AtomicOps.GiveKeyword(me.Leader, "双重攻击", KeywordDuration.ThisTurn);
        }
    }
}
