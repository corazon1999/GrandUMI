using System.Text.Json;
using GrandUMI.Game.Ranked;

namespace GrandUMI.Game;

/// <summary>聊天语录只影响展示，不参与动作、胜负或结算事务。</summary>
public static partial class GameRoomManager
{
    internal sealed class ChatDecorationCinematicRuntime
    {
        private readonly object _gate = new();
        private readonly ChatDecorationDefinition?[,] _loadout = new ChatDecorationDefinition?[2, 2];
        private bool _initialized;

        internal void Initialize(IReadOnlyList<string> accounts, string?[,]? restoredIds)
        {
            lock (_gate)
            {
                if (_initialized) return;
                for (var seat = 0; seat < 2; seat++)
                {
                    if (restoredIds is not null)
                    {
                        _loadout[seat, 0] = ChatDecorationCatalog.Find(restoredIds[seat, 0]);
                        _loadout[seat, 1] = ChatDecorationCatalog.Find(restoredIds[seat, 1]);
                        continue;
                    }

                    var account = seat < accounts.Count ? accounts[seat] : string.Empty;
                    var accountLoadout = ResolveWithoutAffectingMatch(account);
                    _loadout[seat, 0] = accountLoadout.Opening;
                    _loadout[seat, 1] = accountLoadout.Victory;
                }
                _initialized = true;
            }
        }

        internal object[] ExportForJournal()
        {
            lock (_gate)
            {
                return Enumerable.Range(0, 2)
                    .Select(seat => (object)new
                    {
                        seat,
                        opening = _loadout[seat, 0]?.Id,
                        victory = _loadout[seat, 1]?.Id,
                    })
                    .ToArray();
            }
        }

        internal object BuildSnapshot(RoomEntry room, int perspectivePlayerIndex)
        {
            lock (_gate)
            {
                var perspective = Math.Clamp(perspectivePlayerIndex, 0, 1);
                var openingEvents = room.Engine.State.MulliganBothDone
                    ? Enumerable.Range(0, 2)
                        .Where(seat => _loadout[seat, 0] is not null)
                        .Select(seat => BuildPhraseEvent(
                            room,
                            perspective,
                            seat,
                            $"{room.RoomId}:opening:{seat}",
                            _loadout[seat, 0]!))
                        .ToArray()
                    : [];

                object? terminal = null;
                if (room.Engine.State.IsGameOver)
                {
                    var winnerSeat = room.Engine.State.WinnerIndex is 0 or 1
                        ? room.Engine.State.WinnerIndex
                        : null;
                    var loserSeat = winnerSeat.HasValue ? 1 - winnerSeat.Value : (int?)null;
                    object? victory = null;
                    if (winnerSeat.HasValue && _loadout[winnerSeat.Value, 1] is { } definition)
                    {
                        victory = BuildPhraseEvent(
                            room,
                            perspective,
                            winnerSeat.Value,
                            $"{room.RoomId}:victory:{winnerSeat.Value}",
                            definition);
                    }

                    terminal = new
                    {
                        eventId = $"{room.RoomId}:terminal",
                        winnerSeat,
                        loserSeat,
                        winnerSide = SideOf(winnerSeat, perspective),
                        loserSide = SideOf(loserSeat, perspective),
                        reason = room.Engine.State.GameOverReason,
                        victory,
                    };
                }

                return new
                {
                    matchId = room.RoomId,
                    openingEvents,
                    terminal,
                };
            }
        }

        private static object BuildPhraseEvent(
            RoomEntry room,
            int perspective,
            int seat,
            string eventId,
            ChatDecorationDefinition definition)
            => new
            {
                eventId,
                sourceSeat = seat,
                displaySide = SideOf(seat, perspective),
                displayName = room.PlayerDisplayNames[seat],
                id = definition.Id,
                name = definition.Name,
                text = definition.Text,
                rarity = definition.Rarity,
                styleToken = definition.StyleToken,
            };

        private static string? SideOf(int? seat, int perspective)
            => seat is 0 or 1 ? seat == perspective ? "self" : "opponent" : null;

        private static (ChatDecorationDefinition? Opening, ChatDecorationDefinition? Victory)
            ResolveWithoutAffectingMatch(string account)
        {
            if (string.IsNullOrWhiteSpace(account)) return (null, null);
            try
            {
                return RankedStore.Default.ResolveEquippedChatDecorationLoadout(account);
            }
            catch (Exception ex)
            {
                // 装饰存储故障必须降级为空演出，绝不能阻塞开局或终局结算。
                Console.Error.WriteLine($"[聊天语录] 读取账号装配失败，已降级为空：{ex.Message}");
                return (null, null);
            }
        }
    }

    private static void ConfigureChatDecorationCinematics(RoomEntry room, string?[,]? restoredIds = null)
    {
        room.ChatDecorationCinematics.Initialize(room.PlayerAccounts, restoredIds);
        room.Engine.CinematicSnapshotProvider = perspective =>
            room.ChatDecorationCinematics.BuildSnapshot(room, perspective);
    }

    internal static string?[,]? ReadChatDecorationJournalLoadout(JsonElement header)
    {
        if (!header.TryGetProperty("chatDecorations", out var loadout)
            || loadout.ValueKind != JsonValueKind.Array)
            return null;

        var result = new string?[2, 2];
        foreach (var item in loadout.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("seat", out var seatElement)
                || seatElement.ValueKind != JsonValueKind.Number
                || !seatElement.TryGetInt32(out var seat)
                || seat is not (0 or 1))
                continue;
            result[seat, 0] = ReadOptionalString(item, "opening");
            result[seat, 1] = ReadOptionalString(item, "victory");
        }
        return result;
    }

    private static string? ReadOptionalString(JsonElement value, string property)
        => value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
