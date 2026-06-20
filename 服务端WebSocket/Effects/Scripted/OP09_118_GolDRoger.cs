using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-118 高路·D·罗杰（角色）
/// 【速攻】（关键词，由引擎处理）
/// 当对方发动【阻挡者】效果时，若我方或对方生命卡牌张数为0，则我方获得游戏胜利。
/// 实现说明：用 Wave5 的 OnOppBlocker watcher。仅当确为对方发动阻挡者、且任一方生命为0时令我方获胜。
/// </summary>
public class OP09_118_GolDRoger : IScriptedEffect
{
    public string CardNumber => "OP09-118";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppBlocker;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // 确认确为"对方"发动阻挡者
        var bOwner = ctx.Vars.TryGetValue("blockerOwner", out var v) && v is int bi ? bi : -1;
        if (bOwner == owner) return Task.CompletedTask;

        var me = ctx.State.Players[owner];
        var opp = ctx.State.Players[1 - owner];

        // 我方或对方生命为0 → 我方获胜
        if (me.LifeCount == 0 || opp.LifeCount == 0)
        {
            ctx.State.WinnerIndex = owner;
            ctx.State.GameOverReason = "高路·D·罗杰：对方发动【阻挡者】时生命为0，我方获胜";
        }

        return Task.CompletedTask;
    }
}
