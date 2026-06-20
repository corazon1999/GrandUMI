using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-115 大地……不会失败!!（事件，光）
/// 【反击】我方领袖拥有《山迪亚战士》特征的场合，本次战斗中，我方最多 1 张领袖或角色力量 +3000。
///         之后，将我方手牌中最多 1 张"神之岛"登场。
/// 【触发】抽取 2 张卡牌，丢弃我方的 1 张手牌。（触发节由引擎处理，此处实现【反击】）
///
/// 实现说明 / 简化点：
///   - "神之岛"为舞台卡，按名称匹配；PlayFromHandFree 支持舞台卡登场。
///   - 仅当领袖拥有《山迪亚战士》特征时本【反击】才有 +3000 收益。
/// </summary>
public class OP08_115_TheEarthWontFail : IScriptedEffect
{
    public string CardNumber => "OP08-115";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：领袖拥有《山迪亚战士》特征
        if (!me.Leader.Info.HasKeyword("山迪亚战士")) return;

        // 本次战斗中，我方最多 1 张领袖或角色力量 +3000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本次战斗力量 +3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var buffTarget = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(buffTarget, 3000);
        }

        // 之后：将我方手牌中最多 1 张"神之岛"登场
        var playable = me.Hand.Where(c => c.MatchesName("神之岛")).ToList();
        if (playable.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "将手牌中最多 1 张\"神之岛\"登场",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = playable.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, p);
            }
        }
    }
}
