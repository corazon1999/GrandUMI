using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-059 S-蛇女（角色 / 光 / 炽天使·艾格赫德 cost6 power7000）
/// 【登场时】我方领袖拥有《艾格赫德》特征，且我方生命卡牌为 2 张或更多的场合，
///   将我方手牌中最多 1 张拥有【触发】效果的角色卡牌正面朝上加入生命区最上方。
///
/// 实现说明：
///   - 条件：领袖含《艾格赫德》且我方生命 ≥2 张。
///   - "拥有【触发】效果的角色卡牌"用 c.Info.Trigger 非空判定（Kind==Character）。
///   - 手牌牌移入生命区最上方：手动 Hand.Remove + LifeArea.Insert(0,...)
///     （生命牌无正/反面字段，"正面朝上"按惯例省略，仅做位置转移）。
/// </summary>
public class EB03_059_SSnakeWoman : IScriptedEffect
{
    public string CardNumber => "EB03-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("艾格赫德")) return;
        if (me.LifeArea.Count < 2) return;

        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张拥有【触发】的角色加入生命区最上方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        me.Hand.Remove(picked);
        me.LifeArea.Insert(0, picked);
    }
}
