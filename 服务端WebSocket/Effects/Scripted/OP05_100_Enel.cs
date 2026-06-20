using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-100 艾尼路（角色 / 光）
/// 【速攻】【每回合1次】此角色将要离开场上的场合，可以改为将我方生命区最上方的1张卡牌放置到废弃区，使此角色不离场。
///   场上存在角色"蒙奇·D·路飞"的场合，此效果无效。
/// 实现：OnAllyWillLeaveField 自我置换（victim==自身）；每回合1次；双方场上无路飞且生命≥1时可付生命顶入废弃使自身不离场。
/// </summary>
public class OP05_100_Enel : IScriptedEffect
{
    public string CardNumber => "OP05-100";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        if (vId != self.Id.ToString()) return;                      // 仅自身将离场
        // 场上存在"蒙奇·D·路飞"则此效果无效
        if (me.Characters.Concat(opp.Characters).Any(c => c.MatchesName("蒙奇·D·路飞"))) return;
        var key = "OP05-100-leaveguard" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾尼路：将我方生命顶1张放入废弃区，使此角色不离场？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Trash.Add(top);
        ctx.State.MarkPreventLeave(self.Id);
    }
}
