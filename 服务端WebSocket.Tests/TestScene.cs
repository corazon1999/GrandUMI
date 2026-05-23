using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Tests;

/// <summary>
/// 测试场景构造器：流式 API 快速构造 GameState 用于卡牌效果断言
/// </summary>
public class TestScene
{
    private readonly GameState _state;
    private static bool _dbLoaded;

    public static TestScene New()
    {
        EnsureCardDbLoaded();
        var leader0 = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);
        var leader1 = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Leader);
        var state = new GameState { RoomId = "test-room", FirstPlayer = 0 };
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

    public TestScene MyActiveDon(int n)
    {
        for (int i = 0; i < n; i++) _state.Players[0].CostArea.Add(new DonCard { State = DonState.Active });
        return this;
    }

    static void EnsureCardDbLoaded()
    {
        if (_dbLoaded) return;
        // 查找 卡牌数据 目录
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
}
