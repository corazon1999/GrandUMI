using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-096 我出五亿……五亿贝里……（事件）
/// 【主要】将对方最多 1 张费用不高于 1 的角色 KO，或放回其持有者的手牌，或正面朝上放置到
///   其生命区的最上方或最下方。之后，我方场上存在拥有《天龙人》特征的角色的场合，抽取 1 张卡牌。
///
/// 实现说明 / 简化点：
///   - 处置方式用 ChooseOption 四选一：KO / 退回手牌 / 放置到生命区最上方 / 放置到生命区最下方。
///     放置生命区用 MoveCharToLife(目标所属方下标, card, toTop)。
///   - "之后"条件抽牌：我方场上（领袖或角色）存在《天龙人》特征时抽 1 张。
/// </summary>
public class OP05_096_FiveHundredMillion : IScriptedEffect
{
    public string CardNumber => "OP05-096";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 1).ToList();
        if (cands.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张费用不高于 1 的角色进行处置",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == ch[0]);
                int mode = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "选择处置方式",
                    new[] { "KO", "放回持有者手牌", "正面朝上放到生命区最上方", "正面朝上放到生命区最下方" });
                switch (mode)
                {
                    case 0: AtomicOps.KO(ctx.State, oppIdx, tgt); break;
                    case 1: AtomicOps.BounceToHand(ctx.State, oppIdx, tgt); break;
                    case 2: AtomicOps.MoveCharToLife(ctx.State, oppIdx, tgt, true); break;
                    default: AtomicOps.MoveCharToLife(ctx.State, oppIdx, tgt, false); break;
                }
            }
        }

        // 之后：我方场上存在《天龙人》特征角色 → 抽 1 张
        bool hasTenryuubito = me.Characters.Any(c => c.Info.HasKeyword("天龙人"))
            || me.Leader.Info.HasKeyword("天龙人");
        if (hasTenryuubito)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }
    }
}
