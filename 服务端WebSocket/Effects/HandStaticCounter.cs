using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>手牌中随场况变化的反击值。</summary>
public static class HandStaticCounter
{
    public static int Value(GameState state, int playerIdx, CardInstance card)
    {
        int value = card.Info.Counter;
        if (card.Info.Kind != CardKind.Character) return value;

        var me = state.Players[playerIdx];

        // OP17-063 盖德：我方没有反击值的角色手牌获得反击+1000；同名光环不叠加。
        if (value == 0 && me.Characters.Any(c =>
                c.Info.Number == "OP17-063" && !state.IsContinuouslyNullified(c)))
            value = 1000;

        // OP17-118 洛克斯：我方场上仅存在没有反击值的角色时，手牌中的自身反击+2000。
        if (card.Info.Number == "OP17-118"
            && me.Characters.All(c => c.Info.Counter == 0))
            value = Math.Max(value, 2000);

        return value;
    }
}
