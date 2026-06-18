using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-098 解放（事件 / 黑(暗) / 6 费 / 黑胡子海盗团）
/// 【主要】我方角色比对方角色少 2 张或更多的场合，将对方最多 1 张原本的费用不高于 6 的角色和
///   最多 1 张原本的费用不高于 4 的角色 KO。
/// 【触发】本回合中，对方领袖和角色各最多 1 张的效果无效。
///
/// 说明 / 简化点：
///   - 【主要】发动条件为差值比较（我方角色数 ≤ 对方角色数 - 2），DSL if 节仅有绝对值键，故脚本实现。
///   - KO 两个目标：先选原本费用 ≤6 的角色，再从剩余中选原本费用 ≤4 的角色（各最多 1 张，min=0）。
///   - 【触发】效果无效用 AtomicOps.NullifyEffects（统一在回合结束阶段清理，等价"本回合中"）。
/// </summary>
public class OP10_098_Liberation : IScriptedEffect
{
    public string CardNumber => "OP10-098";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】本回合中，对方领袖和角色各最多 1 张效果无效
            if (opp.Leader != null) AtomicOps.NullifyEffects(opp.Leader, KeywordDuration.ThisTurn);
            var oppChars = opp.Characters.ToList();
            if (oppChars.Count > 0)
            {
                var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择对方最多 1 张角色，本回合效果无效",
                    oppChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (pick.Count > 0)
                {
                    var c = oppChars.First(x => x.Id.ToString() == pick[0]);
                    AtomicOps.NullifyEffects(c, KeywordDuration.ThisTurn);
                }
            }
            return;
        }

        // 【主要】条件：我方角色数 ≤ 对方角色数 - 2
        if (me.Characters.Count > opp.Characters.Count - 2) return;

        // 第一目标：原本费用 ≤6 的对方角色
        var cands6 = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        CardInstance? koTarget1 = null;
        if (cands6.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张原本费用≤6 的角色 KO",
                cands6.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) koTarget1 = cands6.First(c => c.Id.ToString() == ch[0]);
        }

        // 第二目标：原本费用 ≤4 的对方角色（须不同于第一目标）
        var cands4 = opp.Characters.Where(c => c.Info.Cost <= 4 && c != koTarget1).ToList();
        CardInstance? koTarget2 = null;
        if (cands4.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张原本费用≤4 的角色 KO",
                cands4.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) koTarget2 = cands4.First(c => c.Id.ToString() == ch[0]);
        }

        if (koTarget1 != null) AtomicOps.KO(s, oppIdx, koTarget1);
        if (koTarget2 != null) AtomicOps.KO(s, oppIdx, koTarget2);
    }
}
