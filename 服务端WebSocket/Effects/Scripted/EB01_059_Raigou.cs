using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-059 雷迎（事件 / 光 / 空岛）
/// 【主要】将对方最多 1 张角色KO。之后，将我方生命区最上方的卡牌放置到废弃区直到我方生命卡牌变为 1 张。
///
/// 实现说明：
///   - 先 KO 对方最多 1 张角色（玩家选择）。
///   - 之后循环将我方生命区最上方卡牌放入废弃区，直到生命剩 1 张（若已 ≤1 张则不操作）。
///   - 【触发】另由触发系统处理，本脚本仅实现【主要】。
/// </summary>
public class EB01_059_Raigou : IScriptedEffect
{
    public string CardNumber => "EB01-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // KO 对方最多 1 张角色
        var cands = opp.Characters.ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, oppIdx, tgt);
            }
        }

        // 之后：将我方生命区最上方卡牌放入废弃区，直到生命变为 1 张
        while (me.LifeArea.Count > 1)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Trash.Add(top);
        }
    }
}
