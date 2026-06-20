using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-110 庞德（角色 / 光 / 蛋糕岛）
/// 【阻挡者】（关键词，由引擎处理）
/// 【KO时】将对方最多 1 张费用不高于 3 的角色正面朝上加入对方生命区的最上方或最下方。
///
/// 实现说明：
///   - 阻挡者为关键词，引擎自动处理，此处仅实现【KO时】主动效果。
///   - 将对方角色置入"对方生命区"用 MoveCharToLife(ctx.State, 1-owner, card, toTop)，
///     ownerIdx 传对方下标即放入对方生命区；toTop 由玩家二选一决定。
/// </summary>
public class OP04_110_Pound : IScriptedEffect
{
    public string CardNumber => "OP04-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3)
            .ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色加入对方生命区",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);

        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "加入对方生命区的位置", new[] { "最上方", "最下方" });
        AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: pos == 0);
    }
}
