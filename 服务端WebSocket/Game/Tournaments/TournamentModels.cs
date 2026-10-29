using GrandUMI.Persistence;

namespace GrandUMI.Game.Tournaments;

public sealed class Tournament
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Revision { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public int Capacity { get; set; } = 8;
    public string Format { get; set; } = "standard";
    public long RegistrationOpensAt { get; set; }
    public long RegistrationClosesAt { get; set; }
    public long StartsAt { get; set; }
    public long CreatedAt { get; set; }
    public string Rewards { get; set; } = "";
    public string Announcement { get; set; } = "";
    public string ReviewReason { get; set; } = "";
    public string RulesetId { get; set; } = "";
    public string RulesetHash { get; set; } = "";
    public List<string> Managers { get; set; } = [];
    public List<string> Judges { get; set; } = [];
    public List<TournamentEntrant> Entrants { get; set; } = [];
    public List<TournamentRound> Rounds { get; set; } = [];
    public List<TournamentAudit> Audit { get; set; } = [];
    public List<TournamentAppeal> Appeals { get; set; } = [];
    public string DrawSeed { get; set; } = Guid.NewGuid().ToString("N");
    public string? Champion { get; set; }
    public int StartedEntrantCount { get; set; }
    public long? ActualStartedAt { get; set; }
    public int? ConfiguredSwissRounds { get; set; }
    public int? ConfiguredCutSize { get; set; }
    // 已配置的轮次保持不变；缺少配置的旧赛事沿用原来的实际人数规则。
    public int SwissRounds => ConfiguredSwissRounds ?? (int)Math.Ceiling(Math.Log2(StartedEntrantCount > 0 ? StartedEntrantCount : Capacity));
    public int CutSize => Math.Min(ConfiguredCutSize ?? RecommendedCutSize(Capacity),
        1 << System.Numerics.BitOperations.Log2((uint)(StartedEntrantCount > 0 ? StartedEntrantCount : Capacity)));
    public static int RecommendedCutSize(int capacity) => capacity <= 16 ? 4 : capacity <= 128 ? 8 : 16;
    public TournamentRound? CurrentRound => Rounds.LastOrDefault();
}

public sealed class TournamentEntrant
{
    public string Account { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "registered";
    public long RegisteredAt { get; set; }
    public int Order { get; set; }
    public bool CheckedIn { get; set; }
    public int MissedRounds { get; set; }
    public string LeaveReason { get; set; } = "";
    public long? LeftAt { get; set; }
    public StoredDeck Deck { get; set; } = new();
    public string DeckRaw { get; set; } = "";
}

public sealed class TournamentRound
{
    public int Number { get; set; }
    public string Stage { get; set; } = "swiss";
    public string Status { get; set; } = "live";
    public long PublishedAt { get; set; }
    public string EndReason { get; set; } = "";
    public List<TournamentTable> Tables { get; set; } = [];
}

public sealed class TournamentTable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; }
    public string? Player0 { get; set; }
    public string? Player1 { get; set; }
    public bool Seated0 { get; set; }
    public bool Seated1 { get; set; }
    public string Status { get; set; } = "waiting";
    public string? RoomId { get; set; }
    public long? StartedAt { get; set; }
    // 未设置时使用轮次默认值；0表示不限时，单桌调整后的截止时间单独持久化。
    public long? TimeLimitMs { get; set; }
    public long? DeadlineAt { get; set; }
    public long? TimeoutNotifiedAt { get; set; }
    public int? ExtraTurnLimit { get; set; }
    public string? Outcome { get; set; }
    public string? OriginalOutcome { get; set; }
    public bool ExitAdjusted { get; set; }
    public string Reason { get; set; } = "";
    public bool Confirmed { get; set; }
    public string? ConfirmedBy { get; set; }
    public long? ConfirmedAt { get; set; }
    public string? Winner => Outcome is "p0" or "bye" ? Player0 : Outcome == "p1" ? Player1 : null;
}

public sealed record TournamentAudit(long At, string Actor, string Action, string Detail);
public sealed class TournamentAppeal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TableId { get; set; } = "";
    public string Account { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "open";
    public string Resolution { get; set; } = "";
    public long At { get; set; }
}

public sealed class TournamentCommand
{
    public string Action { get; set; } = "list";
    public string Id { get; set; } = "";
    public string RequestId { get; set; } = "";
    public long? Revision { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Capacity { get; set; }
    public int? SwissRounds { get; set; }
    public int? CutSize { get; set; }
    public string Format { get; set; } = "standard";
    public long RegistrationOpensAt { get; set; }
    public long RegistrationClosesAt { get; set; }
    public long StartsAt { get; set; }
    public string Rewards { get; set; } = "";
    public string Announcement { get; set; } = "";
    public string Reason { get; set; } = "";
    public string DeckName { get; set; } = "";
    public List<string> Managers { get; set; } = [];
    public List<string> Judges { get; set; } = [];
    public string TableId { get; set; } = "";
    public int? RemainingSeconds { get; set; }
    public string Outcome { get; set; } = "";
    public string ConfirmationToken { get; set; } = "";
    public string AppealId { get; set; } = "";
    public bool AllowRepeat { get; set; }
    public bool ConfirmCheckin { get; set; }
    public bool ConfirmStart { get; set; }
    public bool ConfirmWithdrawal { get; set; }
    public string TargetAccount { get; set; } = "";
    public bool WithdrawEntrant { get; set; }
}

public sealed record TournamentContact(string Account, string Name, string? Qq);
public sealed record TournamentReputationEntry(string TournamentId, int Delta, int Score, string Reason, long At);

public sealed record TournamentStanding(string Account, string Name, int Points, int Wins, int Losses,
    int Byes, double Omw, double Oomw, int Rank, string Status, int? Placement);
