using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-042 月光·莫利亚（角色 / 水 / 王下七武海・恐怖之船海盗团）
/// 【每回合1次】我方领袖拥有《王下七武海》特征，且此角色因对方的效果将要离开场上的场合，
///   可以改为将我方"月光·莫利亚"以外的 1 张角色放回其持有者的卡组最下方，使此角色不离场。
///
/// 实现说明：
///   - 离场置换守护用 OnAllyWillBeKOd（效果KO/离场流程派发）；本次须为对方效果(KOReason=="effect"
///     且发起方为对方)、受害者须为本卡自身。
///   - 置换成本：将我方"月光·莫利亚"以外的 1 张角色放回卡组最下方(ReturnFieldToDeckBottom)，
///     再 MarkPreventKO 取消本次对自身的KO。每回合1次用 TurnOnceUsed 记录。
/// </summary>
public class OP07_042_Moria : IScriptedEffect
{
    public string CardNumber => "OP07-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;

        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;
        if (!me.Leader.Info.HasKeyword("王下七武海")) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null || victimId != selfId.ToString()) return;

        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 置换成本目标：我方"月光·莫利亚"以外的角色
        var cands = me.Characters
            .Where(c => c.Id != selfId && !c.Info.NameContains("月光·莫利亚"))
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫利亚【每回合1次】：将我方 1 张其他角色放回卡组最下方，使此角色不离场？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张其他角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, tgt);

        ctx.State.MarkPreventKO(selfId);
        me.TurnOnceUsed.Add(key);
    }
}
