using System.Collections.Concurrent;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>现场待复核项的正反边界。通过只表示当前场景正常，不自动关闭原反馈。</summary>
public sealed class PlayerFeedback20261020InvestigationTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, "OP13-004", 2)]
    [InlineData(1, "OP13-004", 2)]
    [InlineData(0, "OP17-079", 14)]
    [InlineData(1, "OP17-079", 14)]
    public async Task 葛尔兹从手牌实际登场后费用按所属领袖判定(int owner, string leader, int expected)
    {
        var s = TestScene.New(leader, leader).Build();
        var me = s.Players[owner]; var target = Card("OP17-081"); me.Hand.Add(target);
        await AtomicOps.PlayFromHandFree(s, owner, target);
        await EffectRuntime.DrainPendingEnterFields(s, new MockPromptService().QueueConfirm(false));
        Assert.Equal(expected, s.CurrentCostOf(owner, target));
        Assert.Equal(expected, s.CurrentCostOf(target));
    }

    [Theory]
    [InlineData(0, "OP08-058", true)]
    [InlineData(1, "OP08-058", true)]
    [InlineData(0, "OP09-062", false)]
    [InlineData(1, "OP09-062", false)]
    public async Task 佩洛斯佩罗登场卡塔库栗的回血减攻仍须满足大妈领袖(int owner, string leader, bool eligible)
    {
        var s = TestScene.New(leader, leader).Build(); s.CurrentTurnPlayer = owner;
        var me = s.Players[owner]; var opp = s.Players[1-owner];
        var source = Card("OP17-110"); var target = Card("OP17-103");
        var life = Card("OP15-003"); var victim = Card("OP15-004");
        me.Hand.AddRange([source, target]); me.Deck.AddRange([life, Card("OP15-005")]); opp.Characters.Add(victim);
        opp.Deck.Add(Card("OP15-006"));
        var p = new MockPromptService().QueueChoose(target.Id.ToString()).QueueChoose(victim.Id.ToString()).QueueConfirm(true);
        await AtomicOps.PlayFromHandFree(s, owner, source);
        await EffectRuntime.DrainPendingEnterFields(s, p);
        Assert.Contains(target, me.Characters); Assert.Equal(eligible, me.LifeArea.Contains(life));
        Assert.Equal(victim.Info.Power - (eligible ? 3000 : 0), s.CurrentPowerOf(1-owner, victim));
        Assert.Equal(eligible ? 1 : 0, p.ConfirmHistory.Count);
    }

    [Theory]
    [InlineData("OP15-004", true)]
    [InlineData("OP16-054", false)]
    public async Task 蒙道尔公开两张触发牌后只KO一费以下角色(string number, bool eligible)
    {
        var s = TestScene.New().MyHandAdd("OP17-103").MyHandAdd("OP17-104").OppCharacter(number).Build();
        var me = s.Players[0]; var target = s.Players[1].Characters[0];
        await EffectRuntime.Resolve(s, 0, Card("OP17-111"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueChoose(me.Hand.Select(c => c.Id.ToString()).ToArray())
                .QueueChoose(target.Id.ToString()));
        Assert.Equal(2, me.Hand.Count);
        Assert.Equal(eligible, s.Players[1].Trash.Contains(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 六费黄基德只有生命端点存在正面牌才可支付转移攻击成本(bool faceUp)
    {
        var s = TestScene.New().MyCharacter("ST36-005").Build(); var me = s.Players[0]; var kid = me.Characters[0];
        var life = Card("OP15-003"); life.IsLifeFaceUp = faceUp; me.LifeArea.Add(life);
        s.CurrentTurnPlayer = 1;
        s.CurrentBattle = new BattleContext { AttackerPlayerIndex = 1, DefenderPlayerIndex = 0,
            AttackerCardId = s.Players[1].Leader.Id, TargetIsLeader = true };
        var p = new MockPromptService().QueueConfirm(true).QueueChoose(kid.Id.ToString());
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnOppAttackDeclare, p,
            new Dictionary<string, object?> { ["AttackerIdx"] = 1 });
        Assert.Equal(!faceUp, s.CurrentBattle.TargetIsLeader);
        Assert.Equal(faceUp ? 1 : 0, p.ConfirmHistory.Count);
        Assert.False(life.IsLifeFaceUp);
    }

    [Fact]
    public async Task 草帽船检索的所选牌在双方实际快照中公开()
    {
        _ = TestScene.New().Build();
        const string deck = "OP01-001\nOP15-003\nOP15-004\nOP15-005\nOP15-006\nOP15-007";
        var engine = new GameEngine("feedback-st31-005", ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 31005);
        var queues = new[] { new ConcurrentQueue<string>(), new ConcurrentQueue<string>() };
        engine.OnSendToPlayer = (side, payload) => queues[side].Enqueue(JsonSerializer.Serialize(payload));
        var me = engine.State.Players[0]; me.Hand.Clear(); me.Deck.Clear();
        var target = Card("OP01-001"); me.Deck.AddRange([target, Card("OP15-003"), Card("OP15-004"), Card("OP15-005"), Card("OP15-006"), Card("OP15-007")]);
        var source = Card("ST31-005"); me.StageCard = source;
        var task = EffectRuntime.Resolve(engine.State, 0, source, EffectTrigger.OnEnterField, engine.Prompts);
        var look = await WaitFor("LookTopReveal"); engine.Prompts.Resolve(look.PromptId, [target.Id.ToString()]);
        var order = await WaitFor("ReorderToBottom"); engine.Prompts.Resolve(order.PromptId, order.ValidChoices.ToList());
        await task;
        Assert.Contains(target, me.Hand);
        foreach (var queue in queues)
            Assert.Contains(queue, text => {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.TryGetProperty("reveal", out var reveal) && reveal.ValueKind == JsonValueKind.Object
                    && reveal.GetProperty("cardNumbers").EnumerateArray().Any(x => x.GetString() == target.Info.Number);
            });
        async Task<PendingPrompt> WaitFor(string kind)
        {
            for (int i=0;i<300;i++) {
                if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
                await Task.Delay(10);
            }
            throw new TimeoutException($"等待草帽船{kind}超时");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SMILE命中角色可选择登场或放弃且余牌顺序完整(bool play)
    {
        var s = TestScene.New().MyDeckTop("OP01-104", "OP15-003", "OP15-004", "OP15-005", "OP15-006", "OP15-007").Build();
        var me = s.Players[0]; var top = me.Deck.Take(5).ToArray(); var target = top[0]; var untouched = me.Deck[5];
        var rest = top.Where(c => !play || c != target).Reverse().ToArray();
        var p = new MockPromptService().QueueChoose(play ? [target.Id.ToString()] : [])
            .QueueChoose(rest.Select(c => c.Id.ToString()).ToArray());
        await EffectRuntime.Resolve(s, 0, Card("OP01-116"), EffectTrigger.EventMain, p);
        Assert.Equal(play, me.Characters.Contains(target));
        Assert.Equal(new[] { untouched }.Concat(rest), me.Deck);
        Assert.Equal(6, me.Deck.Count + me.Characters.Count);
    }

    [Fact]
    public async Task 烬拒绝两段追加后仍消费本回合次数()
    {
        var s = TestScene.New("OP01-091").MyCharacter("EB04-031").Build(); var me=s.Players[0];
        me.DonDeck.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard()));
        var p = new MockPromptService().QueueConfirm(false).QueueConfirm(false);
        await EffectRuntime.Resolve(s,0,me.Characters[0],EffectTrigger.ActivatedMain,p);
        await EffectRuntime.Resolve(s,0,me.Characters[0],EffectTrigger.ActivatedMain,p);
        Assert.Equal(2,p.ConfirmHistory.Count); Assert.Empty(me.CostArea);
    }
}
