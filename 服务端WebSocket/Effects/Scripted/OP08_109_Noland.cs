using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-109 蒙布朗·诺兰度（角色，光）
/// 【登场时】我方领袖拥有《山迪亚战士》特征，且我方场上存在角色"卡尔加拉"的场合，
///           将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 条件 1：领袖拥有《山迪亚战士》特征 → me.Leader.Info.HasKeyword("山迪亚战士")。
///   - 条件 2：我方场上存在名为"卡尔加拉"的角色 → me.Characters.Any(MatchesName)。
///   - 效果：从卡组顶取最多 1 张加入生命区最上方 → AddLifeFromDeckTop(me, 1)。
/// </summary>
public class OP08_109_Noland : IScriptedEffect
{
    public string CardNumber => "OP08-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool leaderOk = me.Leader.Info.HasKeyword("山迪亚战士");
        bool hasKalgara = me.Characters.Any(c => c.MatchesName("卡尔加拉"));
        if (!leaderOk || !hasKalgara) return Task.CompletedTask;

        AtomicOps.AddLifeFromDeckTop(me, 1);
        return Task.CompletedTask;
    }
}
