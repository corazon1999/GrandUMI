using System.Text.Json;
using System.Collections.Concurrent;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Tournaments;
using GrandUMI.Game.Validation;
using GrandUMI.Persistence;

namespace GrandUMI;

public static partial class WebSocketBridge
{
    private sealed record TournamentContactsCache(string Signature, long ExpiresAt, IReadOnlyList<TournamentContact> Contacts);
    private static readonly ConcurrentDictionary<string, TournamentContactsCache> TournamentContactsByRoom = new();

    private static async Task OnTournamentAsync(WsSession session, Dictionary<string, JsonElement> message)
    {
        var request = new TournamentCommand { RequestId = Str(message, "requestId") ?? "" };
        try
        {
            request = JsonSerializer.Deserialize<TournamentCommand>(JsonSerializer.Serialize(message), TournamentStore.JsonOptions) ?? request;
            if (!TryRequirePlayer(session)) return;
            if (!session.TryConsumeRateLimit("tournaments", 15, 3)) throw new InvalidOperationException("赛事操作过于频繁，请稍后重试。");
            var store = TournamentRuntime.Store ?? throw new InvalidOperationException("赛事服务尚未就绪。");
            var actor = session.Account!; var admin = AdministratorPolicy.IsAuthorized(actor);
            if (request.Action == "list")
            {
                var visible = store.ReadAll().Where(t => TournamentStore.CanView(t, actor, admin)).ToArray();
                var owners = _qqAccessStore.ReadTournamentContacts(visible.Select(t => t.Owner));
                var list = visible.Select(t => new
                {
                    t.Id, t.Name, t.OwnerName, t.Owner, t.Status, t.Capacity, t.Format, t.StartsAt, t.RegistrationOpensAt, t.RegistrationClosesAt,
                    ownerQq = owners.GetValueOrDefault(t.Owner)?.Qq,
                    registered = t.Entrants.Count(e => e.Status is "registered" or "active"),
                    mine = t.Owner.Equals(actor, StringComparison.OrdinalIgnoreCase) || t.Entrants.Any(e => e.Account.Equals(actor, StringComparison.OrdinalIgnoreCase)),
                    canManage = TournamentStore.CanManage(t, actor, admin), t.Revision,
                }).ToArray();
                SendTournamentWire(session.SessionId, new { proto = "MsgTournament", request.RequestId, result = true, items = list, canReview = admin }); return;
            }
            if (request.Action is "detail" or "previewEnd")
            {
                var current = store.Read(request.Id);
                if (!TournamentStore.CanView(current, actor, admin)) throw new InvalidOperationException("没有查看本场赛事的权限。");
                if (request.Action == "previewEnd" && (!TournamentStore.CanManage(current, actor, admin) || current.CurrentRound is not { Stage: "swiss", Status: "live" }))
                    throw new InvalidOperationException("当前不能主动结束瑞士轮。");
                SendTournament(session, current, request.RequestId, request.Action == "previewEnd" ? TournamentStore.EndToken(current, actor) : null); return;
            }
            StoredDeck? deck = null;
            if (request.Action == "register")
            {
                deck = _playerDataStore.GetPlayerData(actor).Decks.FirstOrDefault(d => d.Name == request.DeckName) ?? throw new InvalidOperationException("云端卡组不存在，请先保存卡组。");
                var current = store.Read(request.Id);
                var raw = string.Join('\n', new[] { deck.Leader }.Concat(deck.Cards));
                var validation = DeckValidator.Validate(raw, current.Format == "standard" ? DeckValidator.FormatStandard : DeckValidator.FormatPublicUnrestricted);
                if (!validation.Ok) throw new InvalidOperationException(validation.Reason);
            }
            if (request.Action is "apply" or "edit" or "approve")
            {
                var directory = _playerDataStore.GetPlayerDirectoryEntries();
                foreach (var account in request.Managers.Concat(request.Judges))
                    if (!directory.Any(p => p.Account.Equals(account.Trim(), StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"管理或裁判账号不存在：{account}");
            }
            if (request.Action == "openRound")
            {
                var current = store.Read(request.Id);
                foreach (var member in current.Entrants.Where(e => e.CheckedIn && e.Status is "registered" or "waitlist").OrderBy(e => e.Order).Take(current.Capacity))
                {
                    var validation = DeckValidator.Validate(member.DeckRaw, current.Format == "standard" ? DeckValidator.FormatStandard : DeckValidator.FormatPublicUnrestricted);
                    if (!validation.Ok) throw new InvalidOperationException($"{member.Name}的卡组不符合本场规则：{validation.Reason}");
                }
            }
            var changed = await TournamentRuntime.ExecuteAsync(() =>
            {
                if (!IsCurrentAccountSession(session)) throw new InvalidOperationException("登录状态已变化，请重新登录。");
                return store.Execute(actor, session.PlayerName ?? actor, admin, request, deck,
                    rulesetId: CardRulesetManager.Current.Id, rulesetHash: CardRulesetManager.Current.ManifestHash);
            });
            SendTournament(session, store.Read(changed.Id), request.RequestId);
            SendTournamentNotice(store.Read(changed.Id), request.Action, request.TableId);
        }
        catch (Exception ex)
        {
            SendTournamentWire(session.SessionId, new { proto = "MsgTournament", request.RequestId, result = false,
                logStr = ex is InvalidOperationException or ArgumentException ? ex.Message : "赛事操作暂未完成，请刷新后重试。" });
            if (ex is not (InvalidOperationException or ArgumentException)) LogErr($"赛事操作失败：{ex}");
        }
    }

    private static void SendTournament(WsSession session, Tournament t, string requestId, string? confirmationToken = null)
        => Send(session.SessionId, BuildTournamentResponse(t, session.Account!, AdministratorPolicy.IsAuthorized(session.Account), requestId, confirmationToken,
            ReadTournamentContacts(t, includeAudit: TournamentStore.CanJudge(t, session.Account!, AdministratorPolicy.IsAuthorized(session.Account))
                || t.Owner.Equals(session.Account, StringComparison.OrdinalIgnoreCase)),
            TournamentRuntime.Store!.ReadReputation(session.Account!), TournamentRuntime.Store.ReadReputationHistory(session.Account!)));

    private static IReadOnlyList<TournamentContact> ReadTournamentContacts(Tournament t, IEnumerable<WsSession>? audience = null, bool includeAudit = false)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [t.Owner] = t.OwnerName };
        foreach (var entrant in t.Entrants) names[entrant.Account] = entrant.Name;
        foreach (var account in t.Managers.Concat(t.Judges).Concat(includeAudit ? t.Audit.Select(a => a.Actor) : []))
            names.TryAdd(account, account == "system" ? "系统" : account);
        if (audience is not null)
            foreach (var session in audience.Where(s => s.Account is not null)) names.TryAdd(session.Account!, session.PlayerName ?? session.Account!);
        var contacts = _qqAccessStore.ReadTournamentContacts(names.Keys);
        return names.Select(pair => new TournamentContact(pair.Key,
            contacts.TryGetValue(pair.Key, out var contact) && string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase)
                ? contact.Name : pair.Value,
            contacts.GetValueOrDefault(pair.Key)?.Qq)).ToArray();
    }

    private static void SendTournamentWire(string sessionId, object payload)
        => Send(sessionId, JsonSerializer.SerializeToElement(payload, TournamentStore.JsonOptions));

    internal static void SendTournamentOverdueNotice(Tournament t, TournamentTable table)
    {
        var accounts = t.Managers.Concat(t.Judges).Append(t.Owner).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in SnapshotCurrentAccountSessions().Where(s => s.Account is not null && accounts.Contains(s.Account)))
            SendTournamentWire(target.SessionId, new { proto = "MsgTournamentNotice", tournamentId = t.Id, tableId = table.Id,
                kind = "timeExpired", logStr = $"【{t.Name}】第{table.Number}桌{(table.StartedAt is null ? "落座" : "对局")}已超时，请安排继续比赛、调整时间或人工判定；系统不会自动判负。" });
    }

    private static void SendTournamentNotice(Tournament t, string action, string tableId = "")
    {
        var table = t.CurrentRound?.Tables.FirstOrDefault(m => m.Id == tableId);
        var text = action switch
        {
            "apply" or "edit" => "举办申请已提交，等待平台管理员审核。",
            "approve" => "赛事已通过审核，现已公开。",
            "reject" => "赛事申请未通过，请查看审核说明。",
            "openRound" => $"第{t.CurrentRound?.Number}轮对阵已公布，请在5分钟内落座；同桌双方落座即可开始。",
            "endRound" => "当前瑞士轮已主动结束，等待管理员逐桌人工确认结果。",
            "announce" => $"赛事公告：{t.Announcement}",
            "cancel" => $"赛事已取消：{t.Announcement}",
            "reschedule" => $"计划开赛时间已调整为{DateTimeOffset.FromUnixTimeMilliseconds(t.StartsAt).ToOffset(TimeSpan.FromHours(8)):MM-dd HH:mm}（北京时间），请查看最新报名截止时间。",
            "removeEntrant" => "参赛名单已调整，请查看选手状态及移出或退赛原因。",
            "adjustTableTime" when table is not null => $"第{table.Number}桌计时已调整为{(table.TimeLimitMs == 0 ? "不限时" : $"剩余{table.TimeLimitMs / 60_000.0:0.##}分钟")}，请查看最新计时。",
            "adjudicate" when table is not null => $"第{table.Number}桌结果已由赛事管理员人工判定，请查看最新结果。",
            _ => null,
        };
        if (text is null) return;
        var players = table is not null && action is "adjustTableTime" or "adjudicate"
            ? new[] { table.Player0, table.Player1 }.OfType<string>() : t.Entrants.Select(e => e.Account);
        var accounts = players.Concat(t.Managers).Concat(t.Judges).Append(t.Owner).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in SnapshotCurrentAccountSessions().Where(s => s.Account is not null &&
            (accounts.Contains(s.Account) || action is "apply" or "edit" && AdministratorPolicy.IsAuthorized(s.Account))))
            SendTournamentWire(target.SessionId, new { proto = "MsgTournamentNotice", logStr = $"【{t.Name}】{text}" });
    }

    internal static JsonElement BuildTournamentResponse(Tournament t, string actor, bool admin, string requestId, string? confirmationToken = null,
        IReadOnlyList<TournamentContact>? contacts = null, int reputation = 100, IReadOnlyList<TournamentReputationEntry>? reputationHistory = null)
    {
        var canManage = TournamentStore.CanManage(t, actor, admin); var canJudge = TournamentStore.CanJudge(t, actor, admin);
        var own = t.Entrants.FirstOrDefault(e => e.Account.Equals(actor, StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.SerializeToElement(new { proto = "MsgTournament", requestId, result = true, confirmationToken,
            tournament = new
            {
                t.Id, t.Revision, t.Name, t.Description, t.Owner, t.OwnerName, t.Status, t.Capacity, t.Format,
                t.RegistrationOpensAt, t.RegistrationClosesAt, t.StartsAt, t.Rewards, t.Announcement, t.ReviewReason,
                t.SwissRounds, t.CutSize, t.ConfiguredSwissRounds, t.ConfiguredCutSize, t.Managers, t.Judges, t.RulesetId, t.Champion,
                t.StartedEntrantCount, t.ActualStartedAt,
                canHost = TournamentStore.CanHost(t, actor, admin), reputation, reputationHistory = reputationHistory ?? [],
                contacts = contacts ?? [],
                canManage, canJudge, canReview = admin, canEdit = t.Owner.Equals(actor, StringComparison.OrdinalIgnoreCase) && t.Rounds.Count == 0,
                mine = own is null ? null : new { own.Account, own.Status, own.CheckedIn, own.LeaveReason, deckName = own.Deck.Name, own.DeckRaw },
                entrants = t.Entrants.Select(e => new { e.Account, e.Name, e.Status, e.CheckedIn, e.LeaveReason, e.LeftAt, leader = e.Deck.Leader,
                    deckRaw = canJudge ? e.DeckRaw : null }),
                standings = TournamentStore.Standings(t), rounds = t.Rounds,
                appeals = t.Appeals.Where(a => canJudge || a.Account.Equals(actor, StringComparison.OrdinalIgnoreCase)),
                audit = canJudge || t.Owner.Equals(actor, StringComparison.OrdinalIgnoreCase) ? t.Audit.TakeLast(100) : [],
            }, serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, TournamentStore.JsonOptions);
    }

    internal static void StartTournamentTable(string tournamentId, string tableId)
    {
        var store = TournamentRuntime.Store!;
        var t = store.Read(tournamentId); var round = t.CurrentRound;
        var table = round?.Tables.FirstOrDefault(m => m.Id == tableId);
        if (round?.Status != "live" || table?.Status != "starting") return;
        if (table.RoomId is { } existingId && GameRoomManager.GetRoom(existingId) is not null)
        {
            store.SystemUpdate(t.Id, state => { var m = state.CurrentRound!.Tables.First(m => m.Id == tableId); m.Status = "playing"; m.StartedAt ??= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); return true; }); return;
        }
        try
        {
            if (File.Exists(RoomJournal.PathOf(table.RoomId!))) throw new InvalidOperationException("对局恢复材料尚未处理，请裁判检查后重试。");
            var p0 = t.Entrants.First(e => e.Account == table.Player0); var p1 = t.Entrants.First(e => e.Account == table.Player1);
            var s0 = SnapshotCurrentAccountSessions().FirstOrDefault(s => string.Equals(s.Account, p0.Account, StringComparison.OrdinalIgnoreCase));
            var s1 = SnapshotCurrentAccountSessions().FirstOrDefault(s => string.Equals(s.Account, p1.Account, StringComparison.OrdinalIgnoreCase));
            if (s0 is null || s1 is null) throw new InvalidOperationException("双方需保持在线，请重新落座。");
            if (s0.IsMatching || s1.IsMatching || s0.CurrentRoomCode is not null || s1.CurrentRoomCode is not null)
                throw new InvalidOperationException("请先退出普通匹配或好友房间，再重新落座。");
            var ruleset = CardRulesetManager.GetRequired(t.RulesetId);
            if (ruleset.ManifestHash != t.RulesetHash) throw new InvalidOperationException("赛事锁定的规则版本不可用，请裁判处理。");
            var room = _qqAccessStore.ExecuteNewGameAdmission(new[] { p0.Account, p1.Account }, () => GameRoomManager.CreateRoom(
                s0.SessionId, p0.Account, p0.DeckRaw, s1.SessionId, p1.Account, p1.DeckRaw,
                p0AlwaysPrompt: s0.AlwaysPromptOnLifeReveal, p1AlwaysPrompt: s1.AlwaysPromptOnLifeReveal,
                p0CardBackId: s0.CardBackId, p1CardBackId: s1.CardBackId, p0SpriteMap: p0.Deck.SpriteMap, p1SpriteMap: p1.Deck.SpriteMap,
                matchKind: MatchKind.Tournament, broadcastInitialState: false, p0DisplayName: p0.Name, p1DisplayName: p1.Name,
                p0SpectateMode: SpectatingRules.Open, p1SpectateMode: SpectatingRules.Open,
                openingReadinessMask: (s0.SupportsOpeningPreparation ? 1 : 0) | (s1.SupportsOpeningPreparation ? 2 : 0),
                assignedRoomId: table.RoomId, pinnedRuleset: ruleset));
            var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            store.SystemUpdate(t.Id, state => { var m = state.CurrentRound!.Tables.First(m => m.Id == tableId); m.Status = "playing"; m.StartedAt = started; return true; });
            GameOpponent[s0.SessionId] = s1.SessionId; GameOpponent[s1.SessionId] = s0.SessionId;
            Send(s0.SessionId, new { proto = "MsgMatchFound", opponentName = p1.Name });
            Send(s1.SessionId, new { proto = "MsgMatchFound", opponentName = p0.Name });
            Send(s0.SessionId, new { proto = "MsgGameStart", MainDeck = p0.DeckRaw });
            Send(s1.SessionId, new { proto = "MsgGameStart", MainDeck = p1.DeckRaw });
            SendTournamentClock(t, round, store.Read(t.Id).CurrentRound!.Tables.First(m => m.Id == tableId));
            GameRoomManager.BroadcastInitialState(room);
        }
        catch (Exception ex)
        {
            store.SystemUpdate(t.Id, state => { var m = state.CurrentRound!.Tables.First(m => m.Id == tableId); if (state.CurrentRound.Status != "live" || m.Status != "starting") return false; m.Status = "blocked"; m.Reason = ex.Message; m.Seated0 = m.Seated1 = false; return true; });
        }
    }

    internal static void SendTournamentClock(Tournament t, TournamentRound round, TournamentTable table)
    {
        var room = table.RoomId is null ? null : GameRoomManager.GetRoom(table.RoomId);
        var audience = SnapshotCurrentAccountSessions().Where(s => s.Account == table.Player0 || s.Account == table.Player1
            || room?.Spectators.ContainsKey(s.SessionId) == true).ToArray();
        if (audience.Length == 0) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = table.RoomId ?? table.Id;
        var signature = t.Revision + ":" + string.Join(',', audience.Select(s => s.Account).OfType<string>().Order(StringComparer.OrdinalIgnoreCase));
        if (!TournamentContactsByRoom.TryGetValue(key, out var cached) || cached.Signature != signature || cached.ExpiresAt <= now)
        {
            cached = new(signature, now + 10_000, ReadTournamentContacts(t, audience));
            TournamentContactsByRoom[key] = cached;
            if (TournamentContactsByRoom.Count > 512)
                foreach (var entry in TournamentContactsByRoom.Where(entry => entry.Value.ExpiresAt <= now)) TournamentContactsByRoom.TryRemove(entry.Key, out _);
        }
        foreach (var session in audience)
        {
            var moderator = GameChatRouting.CanModerate(t, session.Account, AdministratorPolicy.IsAuthorized(session.Account));
            var player = room?.PlayerSessionIds.Contains(session.SessionId, StringComparer.Ordinal) == true
                || string.Equals(session.Account, table.Player0, StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.Account, table.Player1, StringComparison.OrdinalIgnoreCase);
            SendTournamentWire(session.SessionId, BuildTournamentClock(t, round, table, cached.Contacts, now,
                GameChatRouting.Channels(true, player, moderator)));
        }
    }

    private static Tournament? FindChatTournament(string? roomId)
        => roomId is null ? null : TournamentRuntime.Store?.ReadAll()
            .FirstOrDefault(t => t.Rounds.Any(round => round.Tables.Any(table => table.RoomId == roomId)));

    internal static JsonElement BuildTournamentClock(Tournament t, TournamentRound round, TournamentTable table,
        IReadOnlyList<TournamentContact> contacts, long now, string[]? chatChannels = null)
        => JsonSerializer.SerializeToElement(new { proto = "MsgTournamentClock", tournamentId = t.Id, t.Name, round = round.Number,
            round.Stage, isFinal = TournamentStore.IsFinal(round), table = table.Number, table.StartedAt,
            deadline = TournamentStore.TableDeadline(round, table), extraTurnLimit = (int?)null,
            table.RoomId, contacts, chatChannels, manual = round.Status == "manual", serverNow = now }, TournamentStore.JsonOptions);
}
