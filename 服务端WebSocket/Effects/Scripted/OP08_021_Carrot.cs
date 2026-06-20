using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-021 凯罗特（领航）
/// 【启动主要】【每回合1次】我方场上存在拥有《纯毛族》特征的角色的场合，
///   将对方最多 1 张费用不高于 5 的角色转为休息状态。
///
/// 实现说明：
///   - 发动条件：我方角色中存在拥有《纯毛族》特征者（本领航本身也在 me.Characters 中，按文本以场上角色判定）。
///   - 每回合 1 次：用 TurnOnceUsed 记录。
///   - 收益：选择对方最多 1 张费用≤5 的角色横置（RestCard）。
/// </summary>
public class OP08_021_Carrot : IScriptedEffect
{
    public string CardNumber => "OP08-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 发动条件：我方场上存在拥有《纯毛族》特征的角色
        bool hasMink = me.Characters.Any(c => c.Info.HasKeyword("纯毛族"));
        if (!hasMink) return;

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 5 && !c.IsTapped).ToList();
        if (cands.Count == 0) { me.TurnOnceUsed.Add(key); return; }

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤5 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);

        me.TurnOnceUsed.Add(key);

        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
