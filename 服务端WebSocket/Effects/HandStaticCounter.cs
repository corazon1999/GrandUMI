using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>手牌中随场况变化的反击值。</summary>
public static class HandStaticCounter
{
    public static int Value(GameState state, int playerIdx, CardInstance card)
    {
        int value = card.Info.Counter;
        var me = state.Players[playerIdx];

        // OP18-021 弗兰奇：领袖效果生效时，手牌中的所有舞台卡牌变为反击+3000。
        if (card.Info.Kind == CardKind.Stage
            && me.Leader.Info.Number == "OP18-021"
            && !me.Leader.IsEffectsNullified
            && !state.IsContinuouslyNullified(me.Leader))
            return 3000;

        if (card.Info.Kind != CardKind.Character) return value;

        // EB01-001 光月御殿：这是“规则上”的卡牌规则，不属于可被效果无效化的领袖效果。
        // 我方所有原本没有反击值的《和之国》角色卡牌均变为拥有反击+1000。
        if (card.Info.Counter == 0
            && card.Info.HasKeyword("和之国")
            && me.Leader.Info.Number == "EB01-001")
            value = Math.Max(value, 1000);

        // OP17-063 盖德：我方没有反击值的角色手牌获得反击+1000；同名光环不叠加。
        if (value == 0 && me.Characters.Any(c =>
                c.Info.Number == "OP17-063" && !state.IsContinuouslyNullified(c)))
            value = 1000;

        // OP16-118 艾斯：我方手牌中所有印刷力量为8000的角色卡牌，变为反击+2000；同名光环不叠加。
        if (card.Info.Power == 8000 && me.Characters.Any(c =>
                c.Info.Number == "OP16-118"
                && !c.IsEffectsNullified
                && !state.IsContinuouslyNullified(c)))
            value = 2000;

        // OP17-118 洛克斯：我方场上存在角色，且仅存在没有印刷反击值的角色时，
        // 手牌中的自身反击+2000。空角色区不满足“仅为……角色”的条件。
        if (card.Info.Number == "OP17-118"
            && me.Characters.Count > 0
            && me.Characters.All(c => c.Info.Counter == 0))
            value = Math.Max(value, 2000);

        return value;
    }
}
