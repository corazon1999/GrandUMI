using GrandUMI;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class DuelLobbyTests
{
    [Fact]
    public void 房间码房间_加入后保留双方卡组并进入统一准备房()
    {
        var room = CreateWaitingRoom();

        var joined = room.TryAddGuest("guest", "访客", "guest-deck", "访客卡组", out var error);

        Assert.True(joined);
        Assert.Null(error);
        Assert.True(room.IsFull);
        Assert.Equal(new[] { "host", "guest" }, room.Accounts);
        Assert.Equal(new[] { "host-deck", "guest-deck" }, room.Decks);
        Assert.Equal("lobby", room.State);
    }

    [Fact]
    public void 房间码房间_并发加入只允许一个访客成功()
    {
        var room = CreateWaitingRoom();
        var successes = 0;

        Parallel.For(0, 20, index =>
        {
            if (room.TryAddGuest($"guest-{index}", $"访客{index}", "deck", "卡组", out _))
                Interlocked.Increment(ref successes);
        });

        Assert.Equal(1, successes);
        Assert.True(room.IsFull);
    }

    [Fact]
    public void 房间码房间_不能加入自己创建的房间()
    {
        var room = CreateWaitingRoom();

        var joined = room.TryAddGuest("HOST", "房主", "deck", "卡组", out var error);

        Assert.False(joined);
        Assert.Equal("不能加入自己创建的房间", error);
        Assert.False(room.IsFull);
    }

    [Fact]
    public void 共用准备房_双方准备后只有一个调用者能取得开局权()
    {
        var room = CreateWaitingRoom();
        Assert.True(room.TryAddGuest("guest", "访客", "guest-deck", "访客卡组", out _));
        room.Ready[0] = true;
        room.Ready[1] = true;
        var starts = 0;

        Parallel.For(0, 20, iteration =>
        {
            if (room.TryBeginStart(out _)) Interlocked.Increment(ref starts);
        });

        Assert.Equal(1, starts);
        Assert.Equal("starting", room.State);
        room.CompleteStart(success: true);
        Assert.Equal("playing", room.State);
        Assert.False(room.Ready[0]);
        Assert.False(room.Ready[1]);
    }

    [Fact]
    public void 房间码过期清理_不能关闭已经加入访客的房间()
    {
        var room = CreateWaitingRoom();
        Assert.True(room.TryAddGuest("guest", "访客", "guest-deck", "访客卡组", out _));

        Assert.False(room.TryClose(onlyIfWaitingForGuest: true));
        Assert.Equal("lobby", room.State);

        Assert.True(room.TryClose());
        Assert.Equal("closed", room.State);
        Assert.False(room.TryAddGuest("late-guest", "迟到访客", "deck", "卡组", out _));
    }

    private static DuelLobby CreateWaitingRoom()
    {
        var room = new DuelLobby
        {
            RoomId = "room-code-test",
            MatchKind = MatchKind.RoomCode,
            JoinCode = "ABC234",
        };
        room.Accounts[0] = "host";
        room.Names[0] = "房主";
        room.Decks[0] = "host-deck";
        room.DeckNames[0] = "房主卡组";
        return room;
    }
}
