using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// M6 引擎机制护栏测试：效果KO来源追踪 / 置换守护(OnAllyWillBeKOd 效果KO派发 / PreKO 自身) / 持续防离场光环。
/// 覆盖 EB01-057、EB04-043、EB04-044、EB04-057，及不回归战斗KO。
/// </summary>
public class EB_M6_Tests
{
    private static CardInstance Filler() => new() { Info = CardDatabase.Get("OP15-003")! };

    // ── EB01-057 白星：因对方效果被KO时卡组顶1张入生命 ──
    [Fact]
    public async Task EB01_057_EffectKO_AddsLifeFromDeck()
    {
        var s = TestScene.New().MyCharacter("EB01-057").Build();
        var me = s.Players[0];
        me.Deck.Add(Filler());
        var shira = me.Characters[0];

        await AtomicOps.KOByEffectAsync(s, 0, shira, new MockPromptService(), actingSide: 1);

        Assert.DoesNotContain(shira, me.Characters);   // 已被KO
        Assert.Single(me.LifeArea);                    // 卡组顶1张入生命
    }

    [Fact]
    public async Task EB01_057_BattleKO_DoesNotAddLife()
    {
        var s = TestScene.New().MyCharacter("EB01-057").Build();
        var me = s.Players[0];
        me.Deck.Add(Filler());
        var shira = me.Characters[0];

        // 战斗KO：KOReason 非 effect → 不应发动
        await BattleEngine.KOCardAsync(s, 0, shira, new MockPromptService());

        Assert.DoesNotContain(shira, me.Characters);
        Assert.Empty(me.LifeArea);                     // 战斗KO不入生命
    }

    // ── EB04-043 卡古：守护我方≤5费黑色角色，效果KO时置换 ──
    [Fact]
    public async Task EB04_043_GuardsAllyAgainstEffectKO()
    {
        var s = TestScene.New().MyCharacter("EB04-043").MyCharacter("OP01-093").Build();
        var me = s.Players[0];
        var victim = me.Characters[1]; // OP01-093 润媞 暗 cost2
        for (int i = 0; i < 3; i++) me.Trash.Add(Filler());

        var p = new MockPromptService().QueueConfirm(true);
        p.QueueChoose(me.Trash.Take(3).Select(c => c.Id.ToString()).ToArray());

        await AtomicOps.KOByEffectAsync(s, 0, victim, p, actingSide: 1);

        Assert.Contains(victim, me.Characters);        // 被守护，未被KO
        Assert.Empty(me.Trash);                        // 废弃区3张放回卡组底
    }

    [Fact]
    public async Task EB04_043_NotGuard_WhenOwnEffect()
    {
        // actingSide 为我方自己（非对方的效果）→ 不守护
        var s = TestScene.New().MyCharacter("EB04-043").MyCharacter("OP01-093").Build();
        var me = s.Players[0];
        var victim = me.Characters[1];
        for (int i = 0; i < 3; i++) me.Trash.Add(Filler());

        await AtomicOps.KOByEffectAsync(s, 0, victim, new MockPromptService(), actingSide: 0);

        Assert.DoesNotContain(victim, me.Characters);  // 非对方效果 → 正常被KO
    }

    // ── EB04-044 可比：领袖含海军时，自身将被KO可弃手牌置换不离场（PreKO） ──
    [Fact]
    public async Task EB04_044_SelfGuard_DiscardToPreventKO()
    {
        var s = TestScene.New("OP02-002").MyCharacter("EB04-044").Build(); // 领袖 蒙奇·D·戈普(海军)
        var me = s.Players[0];
        var koby = me.Characters[0];
        me.Hand.Add(Filler());

        var p = new MockPromptService().QueueConfirm(true);
        p.QueueChoose(me.Hand[0].Id.ToString());

        await AtomicOps.KOByEffectAsync(s, 0, koby, p, actingSide: 1);

        Assert.Contains(koby, me.Characters);          // 弃手牌置换，存活
        Assert.Empty(me.Hand);                         // 弃1张
    }

    // ── EB04-057 贝加班克：生命≤2时我方科学家黄色角色不会因效果离场 ──
    [Fact]
    public async Task EB04_057_LeaveGuard_BlocksEffectKO_WhenLifeLow()
    {
        var s = TestScene.New().MyCharacter("EB04-057").Build();
        var me = s.Players[0];
        var vega = me.Characters[0];
        await EffectRuntime.Resolve(s, 0, vega, EffectTrigger.OnEnterField, new MockPromptService());

        me.LifeArea.Add(Filler()); me.LifeArea.Add(Filler()); // 生命=2

        AtomicOps.KO(s, 0, vega);                      // 效果KO（同步）
        Assert.Contains(vega, me.Characters);          // 被光环守护，未离场

        me.LifeArea.Add(Filler());                     // 生命=3，光环失效
        AtomicOps.KO(s, 0, vega);
        Assert.DoesNotContain(vega, me.Characters);    // 此时正常被KO
    }

    [Fact]
    public async Task EB04_057_LeaveGuard_BlocksBounce_WhenLifeLow()
    {
        var s = TestScene.New().MyCharacter("EB04-057").Build();
        var me = s.Players[0];
        var vega = me.Characters[0];
        await EffectRuntime.Resolve(s, 0, vega, EffectTrigger.OnEnterField, new MockPromptService());
        me.LifeArea.Add(Filler()); me.LifeArea.Add(Filler()); // 生命=2

        AtomicOps.BounceToHand(s, 0, vega);            // 效果退回手牌
        Assert.Contains(vega, me.Characters);          // 被守护，未离场
        Assert.DoesNotContain(vega, me.Hand);
    }
}
