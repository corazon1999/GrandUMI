using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST08-013 Mr.2·盆·岁末（本萨姆）（角色）
/// 【咚!!×1】当此角色与对方角色进行战斗的战斗结束时，可以将进行战斗的对方角色KO。那样做的场合，将此角色KO。
/// （在 EndBattle 清场前的 OnBattleEnd 时机触发，CurrentBattle 仍可读以判定参战对象）
/// </summary>
public class ST08_013_BonClay : IScriptedEffect
{
    public string CardNumber => "ST08-013";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnBattleEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var b = s.CurrentBattle;
        if (b is null) return;
        var me = s.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        if (me.AttachedDonCount(self.Id) < 1) return; // 【咚!!×1】

        // 找出与此角色进行战斗的"对方角色"（须为角色，非领袖）
        CardInstance? foe = null;
        int foeOwner = -1;
        var defTargetId = b.ReplacedByBlockerCardId ?? b.TargetCardId;
        if (b.AttackerCardId == self.Id)
        {
            if (b.TargetIsLeader || defTargetId is null) return;
            foeOwner = b.DefenderPlayerIndex;
            foe = s.Players[foeOwner].Characters.FirstOrDefault(c => c.Id == defTargetId);
        }
        else if (defTargetId == self.Id)
        {
            foeOwner = b.AttackerPlayerIndex;
            foe = s.Players[foeOwner].Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
        }
        else return; // 此角色未参战
        if (foe is null) return; // 对手是领袖或已不在场 → 不发动

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "本萨姆：将进行战斗的对方角色KO（随后此角色也被KO）？")) return;
        AtomicOps.KO(s, foeOwner, foe);
        AtomicOps.KO(s, ctx.OwnerIndex, self);
    }
}

/// <summary>
/// ST15-001 阿特摩斯（角色）
/// 【攻击时】我方领袖为"爱德华·纽哥特"的场合，本回合中，我方无法通过我方的效果将生命卡牌加入手牌。
/// （置位 GameState.NoEffectLifeToHandThisTurn；各生命入手通道据此跳过）
/// </summary>
public class ST15_001_Atmos : IScriptedEffect
{
    public string CardNumber => "ST15-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Leader.Info.NameIs("爱德华·纽哥特"))
            ctx.State.NoEffectLifeToHandThisTurn.Add(ctx.OwnerIndex);
        return Task.CompletedTask;
    }
}
