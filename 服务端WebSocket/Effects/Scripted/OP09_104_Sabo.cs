using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-104 萨波（角色 / 光 / 革命军）
/// 【登场时】将我方手牌中最多 1 张拥有《革命军》特征的角色卡牌正面朝上加入生命区最上方。
///   之后，我方生命卡牌为 2 张或更多的场合，将我方生命区最上方或最下方的 1 张卡牌加入手牌。
///
/// 实现说明：
///   - "正面朝上加入生命区最上方"：引擎生命区无正/反面区分，手动 me.Hand.Remove + LifeArea.Insert(0,..)。
///   - "之后…生命≥2 时取顶/底 1 张入手"：判断在第一段执行之后的生命数量（含刚加入的牌）。
///   - 生命牌私有，经 extra.choiceCards 下发卡面供玩家选择顶 / 底。
/// </summary>
public class OP09_104_Sabo : IScriptedEffect
{
    public string CardNumber => "OP09-104";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 第一段：将手牌中最多 1 张《革命军》角色加入生命区最上方
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.HasKeyword("革命军")).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多 1 张《革命军》角色加入生命区最上方（可不选）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                me.Hand.Remove(picked);
                me.LifeArea.Insert(0, picked);
            }
        }

        // 第二段：生命≥2 时，将生命区最上方或最下方 1 张加入手牌
        if (me.LifeArea.Count < 2) return;

        var top = me.LifeArea[0];
        var bottom = me.LifeArea[me.LifeArea.Count - 1];
        var lifeCands = new List<CardInstance> { top };
        if (!ReferenceEquals(top, bottom)) lifeCands.Add(bottom);

        var lifeExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = lifeCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var lifeChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LifeTopOrBottom",
            "将生命区最上方或最下方的 1 张卡牌加入手牌",
            lifeCands.Select(c => c.Id.ToString()).ToList(), 1, 1, lifeExtra);
        if (lifeChoice.Count == 0) return;
        var taken = lifeCands.First(c => c.Id.ToString() == lifeChoice[0]);
        me.LifeArea.Remove(taken);
        me.Hand.Add(taken);
    }
}
