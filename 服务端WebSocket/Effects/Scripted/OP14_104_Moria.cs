using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-104 月光·莫利亚（角色 / 光 8 费 10000）
/// 【登场时】将我方废弃区中最多 1 张费用不高于 4 且拥有《恐怖之船海盗团》特征的角色卡牌，
///   正面朝上加入生命区最上方，或登场。
///
/// 实现说明 / 简化点：
///   - 候选为废弃区中"费用≤4 且含《恐怖之船海盗团》特征"的角色卡。
///   - 玩家先选目标，再用 ChooseOption 在"加入生命区最上方 / 登场"间二选一。
///   - 加入生命区：从废弃区移除后插入生命区最上方（正面朝上由客户端按生命卡展示规则处理）。
///   - 登场：用 AtomicOps.PlayFromTrashFree（场上满 5 体时由其内部处理）。
/// </summary>
public class OP14_104_Moria : IScriptedEffect
{
    public string CardNumber => "OP14-104";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            c.Info.HasKeyword("恐怖之船海盗团")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCharacter",
            "选择废弃区中最多 1 张费用≤4 的《恐怖之船海盗团》角色",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择处理方式", new[] { "加入生命区最上方", "登场" });
        if (opt == 0)
        {
            me.Trash.Remove(picked);
            me.LifeArea.Insert(0, picked);
        }
        else
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
