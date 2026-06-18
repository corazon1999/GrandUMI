using GrandUMI.Cards;

namespace GrandUMI.Game;

/// <summary>
/// 出牌（角色/事件/舞台）+ 费用支付 + 登场
/// 事件卡发动效果 / 登场时效果 由 EffectRuntime 在 M3 接入
/// </summary>
public static class CardPlayer
{
    /// <summary>从手牌打出</summary>
    public static PlayResult Play(GameState s, int playerIdx, int handIndex)
    {
        var p = s.Players[playerIdx];
        var card = p.Hand[handIndex];
        var info = card.Info;
        int cost = s.HandPlayCost(playerIdx, card);   // 含持续费用光环（如"从手牌登场X角色费用-1"）

        // 消费一次性"下次登场减费"（OP02-025）：cost 已含其额度，此处移除该次性折扣
        var oneShot = s.OneShotPlayDiscounts.FirstOrDefault(x => x.Matches(playerIdx, card));
        if (oneShot is not null) s.OneShotPlayDiscounts.Remove(oneShot);

        // 支付费用：活跃咚 → 休息咚
        PayCost(p, cost);
        // 从手牌移除
        p.Hand.RemoveAt(handIndex);

        switch (info.Kind)
        {
            case CardKind.Character:
                if (p.Characters.Count >= 5)
                {
                    // 满员处理：先废弃一张（规则处理，简化为废弃最早一张）
                    var sacrifice = p.Characters[0];
                    p.Characters.RemoveAt(0);
                    p.Trash.Add(sacrifice);
                }
                card.TurnPlayed = s.TurnCount;
                p.Characters.Add(card);
                return new PlayResult(PlayKind.Character, card);

            case CardKind.Stage:
                if (p.StageCard is not null)
                {
                    p.Trash.Add(p.StageCard);
                    p.StageCard = null;
                }
                p.StageCard = card;
                return new PlayResult(PlayKind.Stage, card);

            case CardKind.Event:
                // 事件：发动效果（M3 接入），先直接进废弃区
                p.Trash.Add(card);
                return new PlayResult(PlayKind.Event, card);

            default:
                throw new InvalidOperationException($"无法打出 {info.Kind}");
        }
    }

    /// <summary>支付 n 点费用：选 n 张活跃咚转休息</summary>
    public static void PayCost(PlayerState p, int n)
    {
        int paid = 0;
        foreach (var d in p.CostArea)
        {
            if (paid >= n) break;
            if (d.State == DonState.Active)
            {
                d.State = DonState.Rest;
                paid++;
            }
        }
    }

    /// <summary>把一张活跃咚附给目标卡</summary>
    public static bool AttachDon(PlayerState p, Guid targetId)
    {
        var don = p.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return false;
        don.State = DonState.Attached;
        don.AttachedToCardId = targetId;
        return true;
    }
}

public enum PlayKind { Character, Stage, Event }
public record PlayResult(PlayKind Kind, CardInstance Card);
