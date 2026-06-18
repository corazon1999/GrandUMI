using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-101 卡彭·班吉（角色 / 光）
/// 【阻挡者】【每回合1次】我方"卡彭·班吉"以外的拥有《超新星》特征的角色因对方的效果将要离开场上的场合，
///   可以改为正面朝下加入我方生命区最上方，以代替离场。
/// 置换：将该角色正面朝下置入我方生命区最上方（代替原本的离场）。
/// </summary>
public class OP11_101_CaponeBege : IScriptedEffect
{
    public string CardNumber => "OP11-101";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        if (vId == self.Id.ToString()) return;                      // 卡彭以外
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || !victim.Info.HasKeyword("超新星")) return;
        var key = "OP11-101-leaveguard" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"卡彭·班吉：将「{victim.Info.Name}」正面朝下加入生命区顶以代替离场？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);
        ctx.State.MarkPreventLeave(victim.Id);
        // 置换：从场上移除，正面朝下置入生命区最上方
        foreach (var d in me.CostArea)
            if (d.State == DonState.Attached && d.AttachedToCardId == victim.Id)
            { d.State = DonState.Rest; d.AttachedToCardId = null; }
        if (me.Characters.Remove(victim)) { victim.IsLifeFaceUp = false; me.LifeArea.Insert(0, victim); }
    }
}
