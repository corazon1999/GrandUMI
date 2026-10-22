using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using GrandUMI.Persistence;

namespace GrandUMI.Game.Tournaments;

/// <summary>每场赛事作为一个事务聚合持久化；所有推进均重新检查数据库中的权威状态。</summary>
public sealed class TournamentStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly object _gate = new();
    public TournamentStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, DefaultTimeout = 10 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS tournaments (id TEXT PRIMARY KEY, revision INTEGER NOT NULL, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS tournament_commands (actor TEXT NOT NULL, request_id TEXT NOT NULL, tournament_id TEXT NOT NULL,
              PRIMARY KEY(actor,request_id));
            CREATE TABLE IF NOT EXISTS tournament_disconnect_grace (room_id TEXT PRIMARY KEY, p0 INTEGER NOT NULL, p1 INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS tournament_reputation (account TEXT PRIMARY KEY, score INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS tournament_reputation_ledger (id INTEGER PRIMARY KEY AUTOINCREMENT,
              account TEXT NOT NULL, tournament_id TEXT NOT NULL, kind TEXT NOT NULL, operation_id TEXT NOT NULL,
              delta INTEGER NOT NULL, score INTEGER NOT NULL, reason TEXT NOT NULL, at INTEGER NOT NULL,
              UNIQUE(account,tournament_id,kind,operation_id));
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
    internal long[] ReadDisconnectGrace(string roomId)
    {
        lock (_gate)
        {
            using var c = Open(); using var command = c.CreateCommand();
            command.CommandText = "SELECT p0,p1 FROM tournament_disconnect_grace WHERE room_id=$id";
            command.Parameters.AddWithValue("$id", roomId); using var reader = command.ExecuteReader();
            return reader.Read() ? [reader.GetInt64(0), reader.GetInt64(1)] : [120_000, 120_000];
        }
    }
    internal void SaveDisconnectGrace(string roomId, long p0, long p1)
    {
        lock (_gate)
        {
            using var c = Open(); using var command = c.CreateCommand();
            // 宽限只能递减，保存不改变赛事版本，避免倒计时使人工确认凭证不断过期。
            command.CommandText = "INSERT INTO tournament_disconnect_grace(room_id,p0,p1) VALUES($id,$p0,$p1) ON CONFLICT(room_id) DO UPDATE SET p0=MIN(p0,$p0),p1=MIN(p1,$p1) WHERE p0>$p0 OR p1>$p1";
            command.Parameters.AddWithValue("$id", roomId); command.Parameters.AddWithValue("$p0", Math.Clamp(p0, 0, 120_000)); command.Parameters.AddWithValue("$p1", Math.Clamp(p1, 0, 120_000)); command.ExecuteNonQuery();
        }
    }
    public IReadOnlyList<Tournament> ReadAll()
    {
        lock (_gate) { using var connection = Open(); return ReadAll(connection, null); }
    }
    private static List<Tournament> ReadAll(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT document FROM tournaments ORDER BY rowid DESC";
        using var reader = command.ExecuteReader();
        var result = new List<Tournament>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<Tournament>(reader.GetString(0), JsonOptions)!);
        return result;
    }
    public Tournament Read(string id) => ReadAll().FirstOrDefault(t => t.Id == id) ?? throw new InvalidOperationException("赛事不存在。");
    public static bool IsCapacityValid(int value) => value is >= 8 and <= 256 && (value & (value - 1)) == 0;
    public static bool CanManage(Tournament t, string actor, bool admin) => admin || t.Managers.Contains(actor, StringComparer.OrdinalIgnoreCase);
    public static bool CanHost(Tournament t, string actor, bool admin) => Same(t.Owner, actor) || CanManage(t, actor, admin);
    public static bool CanJudge(Tournament t, string actor, bool admin) => CanManage(t, actor, admin) || t.Judges.Contains(actor, StringComparer.OrdinalIgnoreCase);
    public static bool CanView(Tournament t, string actor, bool admin)
    {
        if (t.Status == "cancelled") return Same(t.Owner, actor) || CanJudge(t, actor, admin);
        return t.Status is not ("pending" or "rejected") || admin || Same(t.Owner, actor) || t.Entrants.Any(e => Same(e.Account, actor));
    }
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Text(string text, int maximum, string field, bool required = false)
    {
        text = text.Trim(); Require(text.Length <= maximum && (!required || text.Length > 0), $"{field}不能为空或超过{maximum}字。"); return text;
    }

    public Tournament Execute(string actor, string displayName, bool admin, TournamentCommand request,
        StoredDeck? deck = null, long? now = null, string rulesetId = "", string rulesetHash = "")
    {
        actor = actor.Trim(); Require(actor.Length > 0, "请先登录。");
        var at = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Require(request.RequestId.Length is > 0 and <= 100, "缺少有效操作编号，请刷新后重试。");
        lock (_gate)
        {
            using var connection = Open(); using var tx = connection.BeginTransaction();
            var all = ReadAll(connection, tx);
            using var duplicate = connection.CreateCommand(); duplicate.Transaction = tx;
            duplicate.CommandText = "SELECT tournament_id FROM tournament_commands WHERE actor=$actor AND request_id=$request";
            duplicate.Parameters.AddWithValue("$actor", actor.ToUpperInvariant()); duplicate.Parameters.AddWithValue("$request", request.RequestId);
            if (duplicate.ExecuteScalar() is string prior)
            {
                var previous = all.First(t => t.Id == prior);
                Require(CanView(previous, actor, admin), "没有查看本场赛事的权限。");
                return previous;
            }
            Tournament t;
            string? penaltyAccount = null;
            if (request.Action == "apply")
            {
                Require(all.Count(t => Same(t.Owner, actor) && t.Status == "pending") < 3, "最多同时保留3份待审核申请。");
                t = new Tournament { Owner = actor, OwnerName = displayName, CreatedAt = at };
                Configure(t, request, at);
            }
            else
            {
                t = all.FirstOrDefault(t => t.Id == request.Id) ?? throw new InvalidOperationException("赛事不存在。");
                Require(CanView(t, actor, admin), "没有查看本场赛事的权限。");
                Require(request.Revision == t.Revision, "赛事状态已更新，请查看最新信息后重试。");
                var round = t.CurrentRound;
                var manager = CanManage(t, actor, admin);
                switch (request.Action)
                {
                    case "edit":
                        Require(Same(t.Owner, actor) && t.Rounds.Count == 0 && t.Status != "cancelled", "只能修改自己尚未开赛的申请。");
                        Configure(t, request, at); t.Status = "pending";
                        break;
                    case "approve":
                    case "reject":
                        Require(admin && t.Status == "pending", "仅平台管理员可审核待审申请。");
                        t.ReviewReason = Text(request.Reason, 500, "审核说明", request.Action == "reject");
                        t.Status = request.Action == "approve" ? "approved" : "rejected";
                        if (t.Status == "approved")
                        {
                            Require(at < t.StartsAt, "开赛时间已过，请申请人修改后重新提交。");
                            t.Managers = Accounts(request.Managers.Count > 0 ? request.Managers : t.Managers);
                            t.Judges = Accounts(request.Judges.Count > 0 ? request.Judges : t.Judges);
                            Require(t.Managers.Count > 0, "至少指定一名赛事管理员。");
                            Require(!t.Entrants.Any(e => e.Status != "removed" && (t.Managers.Contains(e.Account, StringComparer.OrdinalIgnoreCase) || t.Judges.Contains(e.Account, StringComparer.OrdinalIgnoreCase))), "裁判和赛事管理员必须独立于参赛选手。");
                            t.RulesetId = rulesetId; t.RulesetHash = rulesetHash;
                        }
                        break;
                    case "register":
                        Require(t.Status == "approved" && at >= t.RegistrationOpensAt && at < t.RegistrationClosesAt && at < t.StartsAt - 300_000, "当前不在报名或卡组修改时间内。");
                        Require(!CanJudge(t, actor, false), "赛事管理员和裁判不能参加自己管理的赛事，请安排独立人员。");
                        Require(deck is not null, "请选择已保存的云端卡组。");
                        var member = t.Entrants.FirstOrDefault(e => Same(e.Account, actor));
                        Require(member?.Status != "disqualified", "你已被移出本场赛事，不能重新报名。");
                        if (member is null) { member = new() { Account = actor, Name = displayName, Order = t.Entrants.Count, RegisteredAt = at }; t.Entrants.Add(member); }
                        member.Deck = deck!; member.DeckRaw = string.Join('\n', new[] { deck!.Leader }.Concat(deck.Cards));
                        member.Status = "registered";
                        member.LeaveReason = ""; member.LeftAt = null;
                        Allocate(t, false);
                        break;
                    case "checkin":
                        Require(t.Status == "approved" && at >= t.RegistrationOpensAt, "请在开放报名后、赛事实际开赛前签到。");
                        Require(request.ConfirmCheckin, "请先确认：签到后退赛会扣15信誉分。");
                        var checkedMember = t.Entrants.FirstOrDefault(e => Same(e.Account, actor) && e.Status is "registered" or "waitlist");
                        Require(checkedMember is not null, "请先报名；正式名额逾期未签到已被释放。");
                        checkedMember!.CheckedIn = true;
                        Allocate(t, at >= t.StartsAt - 300_000);
                        break;
                    case "withdraw":
                        Require(t.Status is "approved" or "swiss" or "cut", "当前赛事不能退赛。");
                        var withdrawing = t.Entrants.FirstOrDefault(e => Same(e.Account, actor));
                        Require(withdrawing is not null && withdrawing.Status is "registered" or "waitlist" or "active", "你没有有效参赛资格。");
                        if (withdrawing!.CheckedIn) { Require(request.ConfirmWithdrawal, "请二次确认退赛及扣除15信誉分。"); penaltyAccount = actor; }
                        Leave(t, withdrawing, t.Rounds.Count == 0 ? "removed" : "withdrawn", "选手主动退赛", actor, at);
                        break;
                    case "removeEntrant":
                        Require(CanHost(t, actor, admin) && t.Status is "approved" or "swiss" or "cut", "没有管理参赛名单的权限或赛事已结束。");
                        var removed = t.Entrants.FirstOrDefault(e => Same(e.Account, request.TargetAccount));
                        Require(removed is not null && removed.Status is "registered" or "waitlist" or "active", "该选手没有有效参赛资格。");
                        var affected = round?.Tables.FirstOrDefault(m => Same(m.Player0, removed!.Account) || Same(m.Player1, removed!.Account));
                        Require(!Same(actor, removed!.Account) && (affected is null || affected.Confirmed || !Same(affected.Player0, actor) && !Same(affected.Player1, actor)), "请由独立管理员处理自己或自己当前对手的移出与退赛。");
                        var leaveReason = Text(request.Reason, 500, "移出或退赛原因", true);
                        if (request.WithdrawEntrant && removed.CheckedIn)
                        {
                            Require(request.ConfirmWithdrawal, "代办退赛同样扣15信誉分，请二次确认。"); penaltyAccount = removed.Account;
                        }
                        Leave(t, removed, request.WithdrawEntrant ? "withdrawn" : "disqualified", leaveReason, actor, at);
                        break;
                    case "reschedule":
                        Require(CanHost(t, actor, admin) && t.Status == "approved" && round is null, "仅举办者或管理员可调整尚未开赛的已审核赛事。");
                        Text(request.Reason, 500, "调整时间原因", true);
                        Require(request.StartsAt > at && request.StartsAt <= at + 366L * 86400_000
                            && t.RegistrationOpensAt < request.RegistrationClosesAt && request.RegistrationClosesAt <= request.StartsAt - 300_000,
                            "开赛时间须在将来一年内，报名截止须晚于开放报名且早于开赛至少5分钟；立即开赛请使用开启首轮。");
                        t.StartsAt = request.StartsAt; t.RegistrationClosesAt = request.RegistrationClosesAt;
                        break;
                    case "openRound":
                        Require(manager || round is null && CanHost(t, actor, admin), "没有开启轮次的权限。");
                        Require(t.Status is "approved" or "swiss" or "cut", "当前赛事不能开启轮次。");
                        Require(round is null || round.Status == "settled", "本轮尚有未结算的比赛桌。");
                        Require(!t.Appeals.Any(a => a.Status == "open"), "请先处理所有未决申诉。");
                        if (round is null)
                        {
                            Allocate(t, true);
                            var roster = t.Entrants.Where(e => e.Status == "registered" && e.CheckedIn).ToArray();
                            Require(roster.Length >= 2, $"至少需要2名有效签到选手才能开赛，当前{roster.Length}人。");
                            Require(request.ConfirmStart, "请二次确认实际签到人数、提前或未满员开赛及赛程。");
                            Require(!all.Any(other => other.Id != t.Id && other.Status is "swiss" or "cut" && other.Entrants.Any(e => e.Status == "active" && roster.Any(r => Same(r.Account, e.Account)))), "部分选手正在参加其他赛事，暂不能开赛。");
                            foreach (var entrant in roster) entrant.Status = "active";
                            foreach (var entrant in t.Entrants.Where(e => e.Status == "waitlist"))
                            { entrant.Status = "removed"; entrant.CheckedIn = false; entrant.LeaveReason = "开赛时未递补，未纳入参赛名单"; }
                            t.StartedEntrantCount = roster.Length; t.ActualStartedAt = at;
                            t.Status = "swiss";
                        }
                        OpenRound(t, request.AllowRepeat, Text(request.Reason, 500, "配对说明"), at);
                        break;
                    case "seat":
                        Require(round is { Status: "live" } && t.Status is "swiss" or "cut", "本轮当前不能落座。");
                        var seat = round!.Tables.FirstOrDefault(m => m.Id == request.TableId);
                        Require(seat is not null && (Same(seat.Player0, actor) || Same(seat.Player1, actor)), "你不是本桌参赛者。");
                        Require(seat!.Status == "blocked" || seat.Status == "waiting" && at <= round.PublishedAt + 300_000, "本桌已开始、已结束或超过落座时间。");
                        var seatedEntrant = t.Entrants.FirstOrDefault(e => Same(e.Account, actor) && e.Status == "active");
                        Require(seatedEntrant is not null, "你已退出赛事。");
                        seatedEntrant!.MissedRounds = 0;
                        if (Same(seat.Player0, actor)) seat.Seated0 = true; else seat.Seated1 = true;
                        if (seat.Seated0 && seat.Seated1) { seat.Status = "starting"; seat.RoomId ??= Guid.NewGuid().ToString("N")[..12]; seat.StartedAt = at; seat.Reason = ""; }
                        break;
                    case "endRound":
                        Require(manager && round is { Stage: "swiss", Status: "live" }, "只能主动结束进行中的瑞士轮。");
                        Require(request.ConfirmationToken == EndToken(t, actor), "请先二次确认结束当前轮次；确认信息已过期时请重新操作。");
                        round!.EndReason = Text(request.Reason, 500, "结束原因", true); round.Status = "manual";
                        foreach (var table in round.Tables)
                        {
                            table.OriginalOutcome = table.Outcome; table.Confirmed = false; table.ConfirmedBy = null; table.ConfirmedAt = null; table.Status = "manual";
                        }
                        break;
                    case "settle":
                        Require(manager && round is { Status: "manual", Stage: "swiss" }, "当前不是逐桌手动结算阶段。");
                        var settling = round!.Tables.FirstOrDefault(m => m.Id == request.TableId);
                        Require(settling is not null, "比赛桌不存在。");
                        Require(!Same(settling!.Player0, actor) && !Same(settling.Player1, actor), "不能结算自己所在的比赛桌。");
                        Require(!settling.Confirmed, "该桌已确认；如需改判请走改判操作。");
                        ValidateOutcome(settling, request.Outcome, false);
                        settling.ExitAdjusted = false;
                        Finish(settling, request.Outcome, Text(request.Reason, 500, "结算原因", true), actor, at);
                        RefreshRound(t);
                        break;
                    case "correct":
                        Require(t.Status is "swiss" or "cut", "赛事结束后不能改写结果。");
                        Require(CanJudge(t, actor, admin) && round is not null, "没有改判权限。");
                        var correcting = round!.Tables.FirstOrDefault(m => m.Id == request.TableId);
                        Require(correcting is { Confirmed: true } or { Status: "blocked" }, "只能裁定当前轮次开局受阻的桌，或改判已确认结果。");
                        Require(!Same(correcting!.Player0, actor) && !Same(correcting.Player1, actor), "不能改判自己的桌。");
                        ValidateOutcome(correcting, request.Outcome, round.Stage == "cut");
                        correcting.OriginalOutcome ??= correcting.Outcome;
                        correcting.ExitAdjusted = false;
                        Finish(correcting, request.Outcome, Text(request.Reason, 500, "改判原因", true), actor, at);
                        RefreshRound(t);
                        break;
                    case "announce":
                        Require(manager || Same(t.Owner, actor), "没有发布赛事公告的权限。");
                        t.Announcement = Text(request.Announcement, 2000, "赛事公告", true); break;
                    case "appeal":
                        var appealing = round?.Tables.FirstOrDefault(m => m.Id == request.TableId && (Same(m.Player0, actor) || Same(m.Player1, actor)));
                        Require(appealing is not null && t.Status is "swiss" or "cut", "仅能申诉本轮自己的比赛桌。");
                        Require(!t.Appeals.Any(a => a.TableId == request.TableId && Same(a.Account, actor) && a.Status == "open"), "本桌已有你的未决申诉。");
                        t.Appeals.Add(new() { Account = actor, TableId = request.TableId, Description = Text(request.Reason, 1000, "申诉说明", true), At = at }); break;
                    case "resolveAppeal":
                        Require(CanJudge(t, actor, admin), "没有处理申诉的权限。");
                        var appeal = t.Appeals.FirstOrDefault(a => a.Id == request.AppealId && a.Status == "open");
                        Require(appeal is not null && !Same(appeal.Account, actor), "申诉不存在或不能处理自己的申诉。");
                        appeal!.Status = "resolved"; appeal.Resolution = Text(request.Reason, 1000, "裁定说明", true); break;
                    case "cancel":
                        Require((manager || Same(t.Owner, actor)) && t.Status != "completed", "没有取消赛事的权限。");
                        Require(round is null || round.Status == "settled", "请先结束并结算当前轮次。");
                        t.Announcement = Text(request.Reason, 1000, "取消原因", true); t.Status = "cancelled"; break;
                    default: throw new InvalidOperationException("未知赛事操作。");
                }
            }
            t.Revision++;
            t.Audit.Add(new(at, actor, request.Action, request.Action switch {
                "settle" or "correct" => $"{request.TableId} {request.Outcome} {request.Reason}",
                "removeEntrant" => $"{request.TargetAccount} {(request.WithdrawEntrant ? "代办退赛" : "移出赛事")} {request.Reason}",
                "reschedule" => $"开赛{t.StartsAt} 报名截止{t.RegistrationClosesAt} {request.Reason}",
                "openRound" when t.Rounds.Count == 1 => $"实际{t.StartedEntrantCount}人，{t.SwissRounds}轮瑞士轮，{t.CutSize}强",
                _ => request.Reason }));
            if (penaltyAccount is not null) ChangeReputation(connection, tx, penaltyAccount, t.Id, "withdraw", actor.ToUpperInvariant() + ":" + request.RequestId, -15, "签到后退赛", at);
            Save(connection, tx, t);
            using var receipt = connection.CreateCommand(); receipt.Transaction = tx;
            receipt.CommandText = "INSERT INTO tournament_commands(actor,request_id,tournament_id) VALUES($actor,$request,$id)";
            receipt.Parameters.AddWithValue("$actor", actor.ToUpperInvariant()); receipt.Parameters.AddWithValue("$request", request.RequestId); receipt.Parameters.AddWithValue("$id", t.Id); receipt.ExecuteNonQuery();
            tx.Commit(); return t;
        }
    }

    private static List<string> Accounts(IEnumerable<string> values) => values.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
    private static void Configure(Tournament t, TournamentCommand r, long now)
    {
        Require(IsCapacityValid(r.Capacity), "人数必须为8、16、32、64、128或256人。");
        Require(r.Format is "standard" or "wild", "请选择标准或狂野卡池。");
        Require(r.RegistrationOpensAt < r.RegistrationClosesAt && r.RegistrationClosesAt <= r.StartsAt - 300_000 && r.StartsAt > now, "报名截止须早于开赛至少5分钟，开赛时间必须在将来。");
        Require(r.StartsAt <= now + 366L * 86400_000, "赛事最多提前一年申请。");
        t.Name = Text(r.Name, 60, "赛事名称", true); t.Description = Text(r.Description, 4000, "赛事简介");
        t.Capacity = r.Capacity; t.Format = r.Format; t.RegistrationOpensAt = r.RegistrationOpensAt; t.RegistrationClosesAt = r.RegistrationClosesAt; t.StartsAt = r.StartsAt;
        t.Rewards = Text(r.Rewards, 1000, "奖励说明"); t.Managers = Accounts(r.Managers); t.Judges = Accounts(r.Judges);
        Require(t.Managers.Count > 0, "请填写至少一个赛事管理员账号。");
    }
    private static void Allocate(Tournament t, bool checkedOnly)
    {
        var available = t.Entrants.Where(e => e.Status is "registered" or "waitlist").OrderBy(e => e.Order).ToArray();
        if (checkedOnly) foreach (var entrant in available.Where(e => !e.CheckedIn && e.Status == "registered")) entrant.Status = "removed";
        var eligible = available.Where(e => e.Status != "removed" && (!checkedOnly || e.CheckedIn)).ToArray();
        for (var i = 0; i < eligible.Length; i++) eligible[i].Status = i < t.Capacity ? "registered" : "waitlist";
    }
    private static void Leave(Tournament t, TournamentEntrant member, string status, string reason, string actor, long at)
    {
        member.Status = status; member.LeaveReason = reason; member.LeftAt = at;
        if (t.Rounds.Count == 0) { member.CheckedIn = false; Allocate(t, at >= t.StartsAt - 300_000); return; }
        var round = t.CurrentRound!;
        var table = round.Tables.FirstOrDefault(m => Same(m.Player0, member.Account) || Same(m.Player1, member.Account));
        if (table is not null && (!table.Confirmed || table.ExitAdjusted && round.Status != "manual"))
        {
            var active0 = t.Entrants.Any(e => Same(e.Account, table.Player0) && e.Status == "active");
            var active1 = t.Entrants.Any(e => Same(e.Account, table.Player1) && e.Status == "active");
            var outcome = active0 ? "p0" : active1 ? "p1" : round.Stage == "swiss" ? "doubleLoss" : "void";
            table.ExitAdjusted = true;
            if (round.Status == "manual") { table.Outcome = outcome; table.Reason = reason + "；等待逐桌确认"; }
            else Finish(table, outcome, reason, actor, at);
        }
        RefreshRound(t);
    }
    internal static bool ReconcileCheckins(Tournament t, long now)
    {
        if (t.Status != "approved" || now < t.StartsAt - 300_000) return false;
        var before = t.Entrants.Select(e => e.Status).ToArray();
        Allocate(t, true);
        return !before.SequenceEqual(t.Entrants.Select(e => e.Status));
    }
    public static string EndToken(Tournament t, string actor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{t.DrawSeed}:{t.Id}:{t.Revision}:{t.CurrentRound?.Number}:{actor.ToUpperInvariant()}:end")));
    private static void Save(SqliteConnection c, SqliteTransaction tx, Tournament t)
    {
        if (t.Status == "completed")
            foreach (var member in t.Entrants.Where(e => e.Status is "active" or "eliminated" && t.Rounds.Any(r => r.Tables.Any(m => m.Confirmed && !m.ExitAdjusted && m.Outcome is "p0" or "p1" && m.Player1 is not null && (Same(m.Player0, e.Account) || Same(m.Player1, e.Account))))))
                ChangeReputation(c, tx, member.Account, t.Id, "complete", "complete", 5, "正常完成赛事", t.CurrentRound?.Tables.Max(m => m.ConfirmedAt ?? t.CreatedAt) ?? t.CreatedAt);
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "INSERT INTO tournaments(id,revision,document) VALUES($id,$revision,$document) ON CONFLICT(id) DO UPDATE SET revision=excluded.revision,document=excluded.document";
        command.Parameters.AddWithValue("$id", t.Id); command.Parameters.AddWithValue("$revision", t.Revision); command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(t, JsonOptions)); command.ExecuteNonQuery();
    }
    public int ReadReputation(string account)
    {
        lock (_gate) { using var c = Open(); return ReadReputation(c, null, account); }
    }
    private static int ReadReputation(SqliteConnection c, SqliteTransaction? tx, string account)
    {
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT score FROM tournament_reputation WHERE account=$account";
        command.Parameters.AddWithValue("$account", account.Trim().ToUpperInvariant());
        return command.ExecuteScalar() is long score ? (int)score : 100;
    }
    public IReadOnlyList<TournamentReputationEntry> ReadReputationHistory(string account)
    {
        lock (_gate)
        {
            using var c = Open(); using var command = c.CreateCommand();
            command.CommandText = "SELECT tournament_id,delta,score,reason,at FROM tournament_reputation_ledger WHERE account=$account ORDER BY id DESC LIMIT 20";
            command.Parameters.AddWithValue("$account", account.Trim().ToUpperInvariant()); using var reader = command.ExecuteReader();
            var result = new List<TournamentReputationEntry>();
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt64(4)));
            return result;
        }
    }
    private static void ChangeReputation(SqliteConnection c, SqliteTransaction tx, string account, string id, string kind, string operationId, int delta, string reason, long at)
    {
        account = account.Trim().ToUpperInvariant();
        var before = ReadReputation(c, tx, account); var score = Math.Clamp(before + delta, 0, 100);
        using var ledger = c.CreateCommand(); ledger.Transaction = tx;
        ledger.CommandText = "INSERT OR IGNORE INTO tournament_reputation_ledger(account,tournament_id,kind,operation_id,delta,score,reason,at) VALUES($account,$id,$kind,$operation,$delta,$score,$reason,$at)";
        ledger.Parameters.AddWithValue("$account", account); ledger.Parameters.AddWithValue("$id", id); ledger.Parameters.AddWithValue("$kind", kind);
        ledger.Parameters.AddWithValue("$operation", operationId); ledger.Parameters.AddWithValue("$delta", score - before); ledger.Parameters.AddWithValue("$score", score);
        ledger.Parameters.AddWithValue("$reason", reason); ledger.Parameters.AddWithValue("$at", at);
        if (ledger.ExecuteNonQuery() == 0) return;
        using var update = c.CreateCommand(); update.Transaction = tx;
        update.CommandText = "INSERT INTO tournament_reputation(account,score) VALUES($account,$score) ON CONFLICT(account) DO UPDATE SET score=excluded.score";
        update.Parameters.AddWithValue("$account", account); update.Parameters.AddWithValue("$score", score); update.ExecuteNonQuery();
    }
    public bool SystemUpdate(string id, Func<Tournament, bool> update)
    {
        lock (_gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            var t = ReadAll(c, tx).FirstOrDefault(t => t.Id == id);
            if (t is null || !update(t)) return false;
            t.Revision++; Save(c, tx, t); tx.Commit(); return true;
        }
    }
    public static void Finish(TournamentTable table, string outcome, string reason, string actor, long now)
    {
        table.Outcome = outcome; table.Reason = reason; table.Confirmed = true; table.ConfirmedBy = actor; table.ConfirmedAt = now; table.Status = "finished";
    }
    private static void ValidateOutcome(TournamentTable table, string outcome, bool cut)
    {
        Require(table.Player1 is null ? outcome == "bye" : outcome is "p0" or "p1" || !cut && outcome == "doubleLoss", "请选择有效结果；淘汰赛必须确定胜者，只有原轮空桌可确认轮空。");
    }
    public static void RefreshRound(Tournament t)
    {
        var round = t.CurrentRound;
        if (round is null || !round.Tables.All(m => m.Confirmed)) return;
        round.Status = "settled";
        if (round.Stage == "cut")
            foreach (var member in t.Entrants.Where(e => e.Status is "active" or "eliminated" && round.Tables.Any(m => Same(m.Player0, e.Account) || Same(m.Player1, e.Account))))
                member.Status = round.Tables.Any(m => Same(m.Winner, member.Account)) ? "active" : "eliminated";
        if (round.Stage == "cut" && round.Tables.Count == 1) { t.Champion = round.Tables[0].Winner; t.Status = "completed"; }
    }

    public static List<TournamentStanding> Standings(Tournament t)
    {
        var tables = t.Rounds.Where(r => r.Stage == "swiss").SelectMany(r => r.Tables).Where(m => m.Confirmed).ToArray();
        var data = t.Entrants.Where(e => e.Status is "active" or "eliminated" or "withdrawn" or "disqualified" && t.Rounds.Count > 0 || t.Rounds.Count == 0 && e.Status == "registered").Select(e =>
        {
            var games = tables.Where(m => Same(m.Player0, e.Account) || Same(m.Player1, e.Account)).ToArray();
            var wins = games.Count(m => Same(m.Winner, e.Account)); var byes = games.Count(m => m.Outcome == "bye");
            var played = games.Where(m => m.Outcome != "bye").ToArray();
            var opponents = played.Select(m => Same(m.Player0, e.Account) ? m.Player1! : m.Player0!).ToArray();
            return new { Member = e, Wins = wins, Byes = byes, Losses = played.Count(m => !Same(m.Winner, e.Account)), Opponents = opponents, Rate = Math.Max(.33, played.Length == 0 ? .33 : (double)(wins - byes) / played.Length) };
        }).ToArray();
        var rates = data.ToDictionary(d => d.Member.Account, d => d.Rate, StringComparer.OrdinalIgnoreCase);
        var omw = data.ToDictionary(d => d.Member.Account, d => d.Opponents.Length == 0 ? .33 : d.Opponents.Average(o => rates.GetValueOrDefault(o, .33)), StringComparer.OrdinalIgnoreCase);
        var sorted = data.Select(d => new { d.Member, d.Wins, d.Byes, d.Losses, Omw = omw[d.Member.Account], Oomw = d.Opponents.Length == 0 ? .33 : d.Opponents.Average(o => omw.GetValueOrDefault(o, .33)) })
            .OrderByDescending(d => d.Wins).ThenByDescending(d => d.Omw).ThenByDescending(d => d.Oomw).ThenBy(d => DrawOrder(t.DrawSeed, d.Member.Account)).ToArray();
        return sorted.Select((d, i) => new TournamentStanding(d.Member.Account, d.Member.Name, d.Wins, d.Wins, d.Losses, d.Byes, d.Omw, d.Oomw, i + 1, d.Member.Status,
            t.Status == "completed" ? Placement(t, d.Member.Account, i + 1) : null)).ToList();
    }
    private static int Placement(Tournament t, string account, int swissRank)
    {
        if (Same(t.Champion, account)) return 1;
        var lost = t.Rounds.Where(r => r.Stage == "cut").Reverse().FirstOrDefault(r => r.Tables.Any(m => (Same(m.Player0, account) || Same(m.Player1, account)) && !Same(m.Winner, account)));
        return lost is null ? Math.Max(t.CutSize + 1, swissRank) : lost.Tables.Count + 1;
    }
    private static string DrawOrder(string seed, string account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + account.ToUpperInvariant())));

    private static void OpenRound(Tournament t, bool allowRepeat, string reason, long now)
    {
        var swissCount = t.Rounds.Count(r => r.Stage == "swiss");
        var round = new TournamentRound { Number = t.Rounds.Count + 1, PublishedAt = now, Stage = swissCount < t.SwissRounds ? "swiss" : "cut" };
        var active = t.Entrants.Where(e => e.Status == "active").Select(e => e.Account).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(active.Count > 0, "没有继续参赛的选手，请取消赛事。");
        // 仍先经过全桌结算与申诉门禁；最后一人不用连续等待空轮。
        if (active.Count == 1) { t.Champion = active.Single(); t.Status = "completed"; return; }
        var standings = Standings(t);
        if (round.Stage == "swiss")
        {
            var points = standings.ToDictionary(e => e.Account, e => e.Points, StringComparer.OrdinalIgnoreCase);
            var pool = active.OrderByDescending(a => points.GetValueOrDefault(a)).ThenBy(a => DrawOrder(t.DrawSeed + round.Number, a)).ToList();
            var byeCandidates = pool.Count % 2 == 0 ? new string?[] { null } : pool
                .OrderBy(a => standings.FirstOrDefault(s => Same(s.Account, a))?.Byes ?? 0).ThenBy(a => points.GetValueOrDefault(a))
                .ThenBy(a => DrawOrder(t.DrawSeed + round.Number, a)).Cast<string?>().ToArray();
            var played = t.Rounds.SelectMany(r => r.Tables).Where(m => m.Player0 is not null && m.Player1 is not null).Select(m => PairKey(m.Player0!, m.Player1!)).ToHashSet();
            var pairs = new List<(string, string)>(); var attempts = 0;
            bool Match(List<string> remaining)
            {
                if (remaining.Count == 0) return true;
                if (++attempts > 100_000) return false;
                var first = remaining[0];
                foreach (var second in remaining.Skip(1).Where(a => !played.Contains(PairKey(first, a))).OrderBy(a => Math.Abs(points.GetValueOrDefault(a) - points.GetValueOrDefault(first))))
                {
                    pairs.Add((first, second));
                    if (Match(remaining.Where(a => a != first && a != second).ToList())) return true;
                    pairs.RemoveAt(pairs.Count - 1);
                }
                return false;
            }
            string? assignedBye = null; var matched = false;
            foreach (var candidate in byeCandidates)
            {
                pairs.Clear();
                if (attempts > 100_000) break;
                if (!Match(pool.Where(a => !Same(a, candidate)).ToList())) continue;
                assignedBye = candidate; matched = true; break;
            }
            if (!matched)
            {
                Require(allowRepeat && reason.Trim().Length > 0, "当前无法避免重复交手，需要裁判确认并填写说明后再开启本轮。");
                assignedBye = byeCandidates[0]; pool.RemoveAll(a => Same(a, assignedBye));
                pairs.Clear(); for (var i = 0; i < pool.Count; i += 2) pairs.Add((pool[i], pool[i + 1]));
            }
            if (assignedBye is not null) round.Tables.Add(NewTable(assignedBye, null, now));
            round.Tables.AddRange(pairs.Select(pair => NewTable(pair.Item1, pair.Item2, now)));
        }
        else
        {
            t.Status = "cut";
            if (t.CurrentRound?.Stage == "cut")
            {
                var winners = t.CurrentRound.Tables.Select(m => m.Winner).Select(a => a is not null && active.Contains(a) ? a : null).ToArray();
                for (var i = 0; i < winners.Length; i += 2) round.Tables.Add(NewTable(winners[i], winners[i + 1], now));
            }
            else
            {
                var qualifiers = standings.Where(s => active.Contains(s.Account)).Take(t.CutSize).Select(s => s.Account).ToArray();
                foreach (var member in t.Entrants.Where(e => e.Status == "active" && !qualifiers.Contains(e.Account, StringComparer.OrdinalIgnoreCase))) member.Status = "eliminated";
                var seeds = new List<int> { 1, 2 };
                while (seeds.Count < t.CutSize) { var size = seeds.Count * 2; seeds = seeds.SelectMany(seed => new[] { seed, size + 1 - seed }).ToList(); }
                for (var i = 0; i < seeds.Count; i += 2) round.Tables.Add(NewTable(qualifiers.ElementAtOrDefault(seeds[i] - 1), qualifiers.ElementAtOrDefault(seeds[i + 1] - 1), now));
            }
        }
        for (var i = 0; i < round.Tables.Count; i++) round.Tables[i].Number = i + 1;
        t.Rounds.Add(round); RefreshRound(t);
    }
    private static string PairKey(string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0 ? a.ToUpperInvariant() + ":" + b.ToUpperInvariant() : b.ToUpperInvariant() + ":" + a.ToUpperInvariant();
    private static TournamentTable NewTable(string? first, string? second, long now)
    {
        if (first is null && second is not null) (first, second) = (second, first);
        var table = new TournamentTable { Player0 = first, Player1 = second };
        if (second is null) Finish(table, first is null ? "void" : "bye", "轮空", "system", now);
        return table;
    }
}
