using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-007 托尼托尼·乔巴（角色）
/// 【我方的回合中】【登场时】/【攻击时】确认我方卡组最上方的5张卡牌，
///   将其中最多1张力量不高于4000且拥有《动物》特征的角色卡牌以休息状态登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 时机：OnEnterField（登场时）/ OnAttackDeclare（攻击时），二者均发生在我方回合，
///     额外校验 CurrentTurnPlayer == OwnerIndex 以满足【我方的回合中】。
///   - 看顶 5 张，公开候选（力量≤4000 且《动物》角色），玩家选最多 1 张并通过统一的卡组登场入口以休息状态登场。
///   - 无论有无候选都展示全部顶牌，剩余卡牌按玩家选择的顺序放回卡组底。
/// </summary>
public class OP08_007_Chopper : IScriptedEffect
{
    public string CardNumber => "OP08-007";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        await DeckTopCharacterPlay.Resolve(ctx, 5, 1,
            card => card.Info.Kind == CardKind.Character && card.Info.Power <= 4000
                && card.Info.HasKeyword("动物"),
            "确认卡组顶5张，登场最多1张力量不高于4000的《动物》角色（休息状态）",
            restState: true);
    }
}
