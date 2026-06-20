using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-100 杰丽·邦妮（领航）
/// 【我方的回合中】【每回合1次】当我方拥有【触发】效果的角色登场时，可以发动：
///   赋予我方 1 张领袖或角色最多 2 张休息状态的咚!!。
///
/// 实现：用 Wave3 的 OnAllyCharEnter watcher。payload ctx.Vars["cardId"]=登场卡、["owner"]=登场方。
///   仅我方回合、登场方为我方、且登场卡为非自身且拥有【触发】效果的角色时发动；每回合 1 次。
/// </summary>
public class OP13_100_Bonney : IScriptedEffect
{
    public string CardNumber => "OP13-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyCharEnter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅我方的回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // payload：登场方与登场卡
        int owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return;
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is null || cardId == ctx.Source.Id.ToString()) return;

        // 登场卡须为拥有【触发】效果的角色
        var entered = me.Characters.FirstOrDefault(c => c.Id.ToString() == cardId);
        if (entered is null) return;
        if (string.IsNullOrEmpty(entered.Info.Trigger)) return;

        // 每回合 1 次
        var key = self_KeyOf(ctx);
        if (me.TurnOnceUsed.Contains(key)) return;

        // 可选发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "邦妮：赋予我方 1 张领袖或角色最多 2 张休息状态的咚!!？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，赋予最多 2 张休息状态的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count == 0) return;

        var target = targets.First(c => c.Id.ToString() == pick[0]);
        int restDon = me.CostArea.Count(d => d.State == DonState.Rest);
        int n = Math.Min(2, restDon);
        if (n <= 0) return;
        AtomicOps.AttachDonFromCost(me, target.Id, n, DonState.Rest);
    }

    private static string self_KeyOf(EffectContext ctx) => ctx.Source.Info.Number + "-onenter";
}
