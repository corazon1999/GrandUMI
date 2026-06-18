using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;

namespace GrandUMI.Tests;

/// <summary>
/// 测试场景构造器：流式 API 快速构造 GameState 用于卡牌效果断言
/// </summary>
public class TestScene
{
    private readonly GameState _state;
    private static bool _dbLoaded;
    private static bool _dslLoaded;

    public static TestScene New(string? myLeaderNumber = null, string? oppLeaderNumber = null)
    {
        EnsureCardDbLoaded();
        EnsureDslLoaded();
        var leader0 = myLeaderNumber is not null
            ? CardDatabase.Get(myLeaderNumber)!
            : CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);
        var leader1 = oppLeaderNumber is not null
            ? CardDatabase.Get(oppLeaderNumber)!
            : CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);
        var state = new GameState { RoomId = "test-room", FirstPlayer = 0, RngSeed = 1 };
        state.Players[0] = new PlayerState
        {
            SessionId = "s0", AccountName = "p0",
            Leader = new CardInstance { Info = leader0 },
        };
        state.Players[1] = new PlayerState
        {
            SessionId = "s1", AccountName = "p1",
            Leader = new CardInstance { Info = leader1 },
        };
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 1;
        state.Phase = Phase.Main;
        return new TestScene(state);
    }

    private TestScene(GameState s) { _state = s; }

    public GameState Build() => _state;

    public TestScene MyHandAdd(string number)
    {
        var info = CardDatabase.Get(number)!;
        _state.Players[0].Hand.Add(new CardInstance { Info = info });
        return this;
    }

    public TestScene OppCharacter(string number)
    {
        var info = CardDatabase.Get(number)!;
        var ci = new CardInstance { Info = info, TurnPlayed = 0 };
        _state.Players[1].Characters.Add(ci);
        return this;
    }

    public TestScene MyCharacter(string number)
    {
        var info = CardDatabase.Get(number)!;
        var ci = new CardInstance { Info = info, TurnPlayed = 0 };
        _state.Players[0].Characters.Add(ci);
        return this;
    }

    /// <summary>
    /// 最大宽松场景：10 活跃咚 + 50 张随机卡组 + 3 张对方角色 + 5 张己方手牌/废弃区/场上
    /// 用于冒烟测试，让任何卡的效果都能找到费用/目标/卡组等先决条件。
    /// </summary>
    public static GameState MaxScenario(string? myLeader = null)
    {
        EnsureCardDbLoaded();
        EnsureDslLoaded();
        // 随便选一张 OP15 角色作为通用填充
        var filler = CardDatabase.Get("OP15-003") ?? CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Character);
        var leader0 = myLeader is not null
            ? CardDatabase.Get(myLeader)!
            : CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);
        var leader1 = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);

        var s = new GameState { RoomId = "smoke", FirstPlayer = 0, RngSeed = 1 };
        s.Players[0] = new PlayerState { SessionId = "s0", AccountName = "p0", Leader = new CardInstance { Info = leader0 } };
        s.Players[1] = new PlayerState { SessionId = "s1", AccountName = "p1", Leader = new CardInstance { Info = leader1 } };
        s.CurrentTurnPlayer = 0;
        s.TurnCount = 2;
        s.Phase = Phase.Main;

        var me = s.Players[0];
        var op = s.Players[1];

        // 10 活跃咚 + 给领航附 2 张赋予中（满足咚条件类卡）
        for (int i = 0; i < 10; i++) me.CostArea.Add(new DonCard { State = DonState.Active });
        for (int i = 0; i < 2; i++) me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        for (int i = 0; i < 10; i++) op.CostArea.Add(new DonCard { State = DonState.Active });

        // 50 张卡组（任意填充）
        for (int i = 0; i < 50; i++)
        {
            me.Deck.Add(new CardInstance { Info = filler });
            op.Deck.Add(new CardInstance { Info = filler });
        }
        // 5 张生命
        for (int i = 0; i < 5; i++)
        {
            me.LifeArea.Add(new CardInstance { Info = filler });
            op.LifeArea.Add(new CardInstance { Info = filler });
        }
        // 5 张己方手牌（含一些事件类供"丢弃事件"类效果）
        var event15 = CardDatabase.GetBySet("OP15").FirstOrDefault(c => c.Kind == CardKind.Event) ?? filler;
        me.Hand.Add(new CardInstance { Info = filler });
        me.Hand.Add(new CardInstance { Info = event15 });
        me.Hand.Add(new CardInstance { Info = filler });
        me.Hand.Add(new CardInstance { Info = event15 });
        me.Hand.Add(new CardInstance { Info = filler });
        // 5 张对方手牌（用于强制对方丢手牌等）
        for (int i = 0; i < 5; i++) op.Hand.Add(new CardInstance { Info = filler });

        // 3 张对方角色（一张被赋 2 咚，一张被赋 1 咚，一张休息态）
        for (int i = 0; i < 3; i++)
        {
            var c = new CardInstance { Info = filler };
            op.Characters.Add(c);
            if (i == 0) { op.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = c.Id }); op.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = c.Id }); }
            if (i == 1) { op.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = c.Id }); }
            if (i == 2) c.IsTapped = true;
        }
        // 2 张己方角色
        for (int i = 0; i < 2; i++) me.Characters.Add(new CardInstance { Info = filler });

        // 5 张己方废弃区（含事件供"废弃区有 X 张事件"类条件）
        for (int i = 0; i < 3; i++) me.Trash.Add(new CardInstance { Info = event15 });
        for (int i = 0; i < 2; i++) me.Trash.Add(new CardInstance { Info = filler });

        return s;
    }

    public TestScene MyActiveDon(int n)
    {
        for (int i = 0; i < n; i++) _state.Players[0].CostArea.Add(new DonCard { State = DonState.Active });
        return this;
    }

    public TestScene OppActiveDon(int n)
    {
        for (int i = 0; i < n; i++) _state.Players[1].CostArea.Add(new DonCard { State = DonState.Active });
        return this;
    }

    /// <summary>给己方领航附 N 张赋予中的咚（满足【咚!!×N】条件）</summary>
    public TestScene AttachDonToMyLeader(int n)
    {
        var p = _state.Players[0];
        for (int i = 0; i < n; i++)
            p.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = p.Leader.Id });
        return this;
    }

    /// <summary>给己方场上 charIndex 角色附 N 张赋予中的咚</summary>
    public TestScene AttachDonToOppChar(int charIndex, int n)
    {
        var p = _state.Players[1];
        if (charIndex < 0 || charIndex >= p.Characters.Count) return this;
        var cardId = p.Characters[charIndex].Id;
        for (int i = 0; i < n; i++)
            p.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = cardId });
        return this;
    }

    /// <summary>给己方卡组放 N 张指定卡（按顺序，索引 0 是最顶）</summary>
    public TestScene MyDeckTop(params string[] numbers)
    {
        var p = _state.Players[0];
        for (int i = numbers.Length - 1; i >= 0; i--)
            p.Deck.Insert(0, new CardInstance { Info = CardDatabase.Get(numbers[i])! });
        return this;
    }

    static void EnsureCardDbLoaded()
    {
        if (_dbLoaded) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "卡牌数据");
            if (Directory.Exists(candidate))
            {
                CardDatabase.LoadFrom(candidate);
                _dbLoaded = true;
                return;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到卡牌数据目录");
    }

    static void EnsureDslLoaded()
    {
        if (_dslLoaded) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "服务端WebSocket", "Effects", "Definitions");
            if (Directory.Exists(candidate))
            {
                DslInterpreter.LoadDirectory(candidate);
                _dslLoaded = true;
                return;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到 Effects/Definitions 目录");
    }
}
