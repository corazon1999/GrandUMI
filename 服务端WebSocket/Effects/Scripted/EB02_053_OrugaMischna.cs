using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-053 奥尔嘉·米斯奇那（角色 / 光 / 阿鲁凯米，cost3 power4000）
/// 【登场时】/【KO时】确认我方或对方生命区最上方的最多 1 张卡牌，将其放置到生命区的最上方或最下方。
///
/// 实现说明：
///   - 登场时与 KO 时同效，HandlesTrigger 含两者。
///   - 先二选一选择查看哪一方生命区（我方/对方），再二选一选择放回最上方/最下方。
///   - "确认最上方 1 张"：把生命顶卡向发动者展示（广播牌面）。放最上方=不动；放最下方=移到底。
/// </summary>
public class EB02_053_OrugaMischna : IScriptedEffect
{
    public string CardNumber => "EB02-053";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 选择查看哪一方生命区（仅在该方有生命牌时为有效目标）
        var sides = new List<(string label, PlayerState p)>();
        if (me.LifeArea.Count > 0) sides.Add(("我方生命区", me));
        if (opp.LifeArea.Count > 0) sides.Add(("对方生命区", opp));
        if (sides.Count == 0) return;

        int sideIdx = sides.Count == 1
            ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "确认哪一方生命区最上方 1 张卡牌？",
                sides.Select(s => s.label).ToList());
        var target = sides[sideIdx].p;
        if (target.LifeArea.Count == 0) return;

        // 确认最上方 1 张：向发动者广播牌面
        var top = target.LifeArea[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { top.Info.Number });

        // 放置到最上方或最下方
        int place = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "将其放置到生命区的哪一处？",
            new[] { "最上方", "最下方" });
        if (place == 1)
        {
            target.LifeArea.RemoveAt(0);
            target.LifeArea.Add(top);
        }
        // place == 0：放回最上方，即保持不动
    }
}
