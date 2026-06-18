using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-100 乙姫（角色，光，人鱼族/鱼人岛，1 费 0 力 2000 反击）
/// 【登场时】我方领袖为"白星"的场合，可以将我方生命区最上方的 1 张卡牌翻至正面朝下：抽取 1 张卡牌。
///
/// 实现说明 / 简化点：
///   - 触发条件：我方领袖名为"白星"。
///   - 可选成本"将我方生命区最上方 1 张卡牌翻至正面朝下"：引擎当前无"生命牌正/反面"状态通道，
///     该成本无法表达，按规范惯例（可选成本无法表达时仅实现收益）仅实现收益部分。
///   - 收益：抽取 1 张卡牌（要求生命区至少 1 张，以贴合"需有可翻面的生命牌"的前提）。
/// </summary>
public class OP11_100_Otohime : IScriptedEffect
{
    public string CardNumber => "OP11-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅当领袖为"白星"且生命区有牌时本效果可发动
        if (!me.Leader.Info.NameContains("白星")) return;
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乙姫【登场时】：将生命区最上方 1 张翻至正面朝下，抽取 1 张卡牌？");
        if (!use) return;

        // 成本（翻面）无引擎通道，省略；仅实现收益：抽 1 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
