using System.Security.Cryptography;
using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class SharedAccountDatabaseTests : IDisposable
{
    private readonly string _tempDirectory;

    public SharedAccountDatabaseTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "共享账号测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempDirectory = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void 首次迁移以正式服为权威源合并测试服且不改动源库()
    {
        var formalPath = Path.Combine(_tempDirectory, "formal-players.db");
        var testPath = Path.Combine(_tempDirectory, "test-players.db");
        var targetPath = Path.Combine(_tempDirectory, "accounts.db");

        var formal = CreatePlayers(formalPath, "Duplicate", "FormalOnly");
        formal.UpdateProfile("Duplicate", "FormalName", "");
        formal.UpdateProfile("FormalOnly", "SharedNick", "");
        SeedLegacySecurity(
            formalPath,
            version: 3,
            whitelist: ["11111", "22222"],
            ("Duplicate", "FORMAL_HASH", "FORMAL_TOKEN", "11111"),
            ("FormalOnly", "FORMAL_ONLY_HASH", "FORMAL_ONLY_TOKEN", "22222"));

        var test = CreatePlayers(testPath, "Duplicate", "TestOnly", "TestConflict");
        test.UpdateProfile("Duplicate", "TestName", "");
        test.UpdateProfile("TestOnly", "SharedNick", "");
        SeedLegacySecurity(
            testPath,
            version: 9,
            whitelist: ["22222", "33333", "44444"],
            ("Duplicate", "TEST_HASH", "TEST_TOKEN", "33333"),
            ("TestOnly", "TEST_ONLY_HASH", "TEST_ONLY_TOKEN", "44444"),
            ("TestConflict", "TEST_CONFLICT_HASH", "TEST_CONFLICT_TOKEN", "22222"));

        SqliteConnection.ClearAllPools();
        var formalHash = FileHash(formalPath);
        var testHash = FileHash(testPath);
        var database = new SharedAccountDatabase(targetPath);
        var sources = new[]
        {
            new LegacyAccountSource("production", formalPath, Authoritative: true),
            new LegacyAccountSource("test", testPath, Authoritative: false),
        };

        var summary = database.Initialize(sources, ["FormalOnly"]);

        Assert.Equal(2, summary.SourceCount);
        Assert.Equal(4, summary.AccountCount);
        Assert.Equal(4, summary.CredentialCount);
        Assert.Equal(4, summary.SessionCount);
        Assert.Equal(3, summary.BindingCount);
        Assert.Equal(2, summary.BindingConflictCount);
        Assert.True(summary.WhitelistInitialized);
        Assert.Equal(3, summary.WhitelistVersion);
        Assert.Equal("FormalName", ScalarText(targetPath,
            "SELECT display_name FROM shared_accounts WHERE account_key='DUPLICATE';"));
        Assert.Equal("FORMAL_HASH", ScalarText(targetPath,
            "SELECT password_hash FROM shared_player_credentials WHERE account_key='DUPLICATE';"));
        Assert.Equal("TEST_ONLY_HASH", ScalarText(targetPath,
            "SELECT password_hash FROM shared_player_credentials WHERE account_key='TESTONLY';"));
        Assert.Equal("44444", ScalarText(targetPath,
            "SELECT qq FROM shared_account_qq_bindings WHERE account_key='TESTONLY';"));
        Assert.Null(ScalarText(targetPath,
            "SELECT qq FROM shared_account_qq_bindings WHERE account_key='TESTCONFLICT';"));
        Assert.Equal(1L, ScalarLong(targetPath,
            "SELECT COUNT(*) FROM shared_qq_whitelist_import_audit;"));
        Assert.Equal("ok", ScalarText(targetPath, "PRAGMA integrity_check;"));

        var replay = database.Initialize(sources, ["FormalOnly"]);
        Assert.Equal(summary.AccountCount, replay.AccountCount);
        Assert.Equal(summary.BindingCount, replay.BindingCount);
        Assert.Equal(2, replay.BindingConflictCount);
        Assert.Equal(formalHash, FileHash(formalPath));
        Assert.Equal(testHash, FileHash(testPath));

        var qq = new QqAccessStore(database);
        var duplicateNicknames = qq.SearchAccountsForAdmin("SharedNick", "player");
        Assert.Equal(2, duplicateNicknames.Count);
        Assert.All(duplicateNicknames, player => Assert.Equal("nickname_exact", player.MatchKind));
        var reverse = Assert.Single(qq.SearchAccountsForAdmin("44444", "qq"));
        Assert.Equal("TestOnly", reverse.Account);
        Assert.Equal("44444", reverse.Qq);
        Assert.Equal("qq_exact", reverse.MatchKind);
    }

    [Fact]
    public void 预激活测试服已物化的共享密码会话白名单和绑定会进入正式迁移()
    {
        var formalPath = Path.Combine(_tempDirectory, "formal-preactivation.db");
        var testPath = Path.Combine(_tempDirectory, "test-preactivation.db");
        var targetPath = Path.Combine(_tempDirectory, "accounts-preactivation.db");
        var formal = CreatePlayers(formalPath, "FormalOnly");
        var test = CreatePlayers(testPath, "LegacyTest");

        var localAccounts = new SharedAccountDatabase(testPath);
        localAccounts.Initialize(
            [new LegacyAccountSource("test-local-fallback", testPath, Authoritative: true)]);
        var localAuth = new AccountAuthenticationStore(test, localAccounts);
        var previewLogin = localAuth.Authenticate("PreviewOnly", "preview-password", null);
        Assert.True(previewLogin.Success);
        var localQq = new QqAccessStore(localAccounts);
        localQq.Import("测试管理员", "[\"55555\",\"66666\"]");
        Assert.True(localQq.EvaluateLogin("PreviewOnly", "55555").Allowed);

        var central = new SharedAccountDatabase(targetPath);
        var summary = central.Initialize(
        [
            new LegacyAccountSource("production", formalPath, Authoritative: true),
            new LegacyAccountSource("test", testPath, Authoritative: false),
        ]);

        Assert.Equal(2, summary.SourceCount);
        Assert.Equal(3, summary.AccountCount);
        Assert.True(summary.WhitelistInitialized);
        Assert.Equal(1, summary.BindingCount + summary.BindingConflictCount);
        Assert.Equal("55555", ScalarText(targetPath,
            "SELECT qq FROM shared_account_qq_bindings WHERE account_key='PREVIEWONLY';"));
        Assert.Equal(2L, ScalarLong(targetPath,
            "SELECT COUNT(*) FROM shared_qq_whitelist_members WHERE qq IN ('55555','66666');"));

        var verificationPlayers = CreatePlayers(
            Path.Combine(_tempDirectory, "verification-players.db"));
        var centralAuth = new AccountAuthenticationStore(verificationPlayers, central);
        Assert.True(centralAuth.Authenticate("PreviewOnly", "preview-password", null).Success);
        Assert.True(centralAuth.Authenticate("PreviewOnly", null, previewLogin.AuthToken).Success);
        Assert.True(new QqAccessStore(central).EvaluateLogin("PreviewOnly", null).Allowed);
    }

    [Fact]
    public async Task 管理员改绑与解绑按修订号线性化并支持幂等重放()
    {
        var playersPath = Path.Combine(_tempDirectory, "players.db");
        var accountsPath = Path.Combine(_tempDirectory, "accounts.db");
        var players = CreatePlayers(playersPath, "Alice", "Bob");
        var accounts = new SharedAccountDatabase(accountsPath);
        accounts.Initialize([new LegacyAccountSource("local", playersPath, Authoritative: true)]);
        var auth = new AccountAuthenticationStore(players, accounts);
        var aliceLogin = auth.Authenticate("Alice", "alice-password", null);
        Assert.True(aliceLogin.Success);
        Assert.True(auth.Authenticate("Bob", "bob-password", null).Success);
        var qq = new QqAccessStore(accounts);
        qq.Import("释迦", "[\"12345\",\"23456\",\"34567\",\"45678\"]");
        Assert.True(qq.EvaluateLogin("Alice", "12345").Allowed);
        Assert.True(qq.EvaluateLogin("Bob", "23456").Allowed);

        var unbindRequest = Guid.NewGuid().ToString("D");
        var unbound = qq.AdminUpdateBinding("释迦", "Alice", "unbind", null, 1, unbindRequest);
        Assert.Null(unbound.Qq);
        Assert.Equal(2, unbound.Revision);
        Assert.False(auth.Authenticate("Alice", null, aliceLogin.AuthToken).Success);

        var unbindReplay = qq.AdminUpdateBinding("释迦", "Alice", "unbind", null, 1, unbindRequest);
        Assert.True(unbindReplay.Replayed);
        Assert.Null(unbindReplay.Qq);
        Assert.Equal(2, unbindReplay.Revision);

        var setRequest = Guid.NewGuid().ToString("D");
        var rebound = qq.AdminUpdateBinding("释迦", "Alice", "set", "34567", 2, setRequest);
        Assert.Equal("34567", rebound.Qq);
        Assert.Equal(3, rebound.Revision);

        var oldReplayAfterNewMutation = qq.AdminUpdateBinding(
            "释迦", "Alice", "unbind", null, 1, unbindRequest);
        Assert.True(oldReplayAfterNewMutation.Replayed);
        Assert.Null(oldReplayAfterNewMutation.Qq);
        Assert.Equal(2, oldReplayAfterNewMutation.Revision);
        Assert.Throws<QqAccessValidationException>(() => qq.AdminUpdateBinding(
            "释迦", "Alice", "set", "45678", 2, setRequest));
        Assert.Throws<QqAccessValidationException>(() => qq.AdminUpdateBinding(
            "释迦", "Alice", "set", "45678", 2, Guid.NewGuid().ToString("D")));
        Assert.Throws<QqAccessValidationException>(() => qq.AdminUpdateBinding(
            "释迦", "Alice", "set", "23456", 3, Guid.NewGuid().ToString("D")));
        Assert.Equal("34567", qq.SearchAccountsForAdmin("Alice", "player").Single().Qq);

        var competingStore = new QqAccessStore(new SharedAccountDatabase(accountsPath));
        var status = qq.GetAccountBindingStatus("Alice");
        var start = new ManualResetEventSlim(false);
        var attempts = new[] { (Store: qq, Qq: "45678"), (Store: competingStore, Qq: "12345") }
            .Select(item => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    item.Store.AdminUpdateBinding(
                        "管理员", "Alice", "set", item.Qq, status.Revision,
                        Guid.NewGuid().ToString("D"));
                    return true;
                }
                catch (QqAccessValidationException)
                {
                    return false;
                }
            }))
            .ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, success => success);
        Assert.Single(results, success => !success);

        using var connection = new SqliteConnection($"Data Source={accountsPath};Pooling=False");
        connection.Open();
        using var audit = connection.CreateCommand();
        audit.CommandText = "SELECT detail_json FROM shared_admin_player_audit WHERE action IN ('set_qq_binding','unbind_qq');";
        using var reader = audit.ExecuteReader();
        var details = new List<string>();
        while (reader.Read()) details.Add(reader.GetString(0));
        Assert.Equal(3, details.Count);
        Assert.All(details, detail =>
        {
            Assert.DoesNotContain("12345", detail, StringComparison.Ordinal);
            Assert.DoesNotContain("23456", detail, StringComparison.Ordinal);
            Assert.DoesNotContain("34567", detail, StringComparison.Ordinal);
            Assert.DoesNotContain("45678", detail, StringComparison.Ordinal);
        });
        Assert.True(ScalarLong(accountsPath,
            "SELECT COUNT(*) FROM shared_account_security_events WHERE event_type='qq_binding_changed' AND target_account='Alice';") >= 3);
    }

    [Fact]
    public async Task 两个环境并发创建同名账号时只有一个密码成为权威结果()
    {
        var formalPlayers = CreatePlayers(Path.Combine(_tempDirectory, "formal.db"));
        var testPlayers = CreatePlayers(Path.Combine(_tempDirectory, "test.db"));
        var accountsPath = Path.Combine(_tempDirectory, "accounts.db");
        var accounts = new SharedAccountDatabase(accountsPath);
        accounts.Initialize();
        var formalAuth = new AccountAuthenticationStore(formalPlayers, accounts);
        var testAuth = new AccountAuthenticationStore(testPlayers, accounts);
        using var start = new ManualResetEventSlim(false);
        var attempts = new[]
        {
            (Store: formalAuth, Password: "formal-password"),
            (Store: testAuth, Password: "testing-password"),
        }.Select(item => Task.Run(() =>
        {
            start.Wait();
            return (item.Password, Result: item.Store.Authenticate("RaceUser", item.Password, null));
        })).ToArray();

        start.Set();
        var results = await Task.WhenAll(attempts);
        var winner = Assert.Single(results, item => item.Result.Success);
        Assert.Single(results, item => !item.Result.Success);
        Assert.True(formalAuth.Authenticate("RaceUser", winner.Password, null).Success);
        Assert.True(testAuth.Authenticate("RaceUser", winner.Password, null).Success);
        Assert.Equal(1, ScalarLong(accountsPath, "SELECT COUNT(*) FROM shared_accounts;"));
        Assert.Equal(1, ScalarLong(accountsPath, "SELECT COUNT(*) FROM shared_player_credentials;"));
    }

    private static PlayerDataStore CreatePlayers(string path, params string[] accounts)
    {
        var players = new PlayerDataStore(path);
        players.Initialize();
        foreach (var account in accounts) players.Login(account);
        return players;
    }

    private static void SeedLegacySecurity(
        string databasePath,
        long version,
        string[] whitelist,
        params (string Account, string PasswordHash, string TokenHash, string Qq)[] accounts)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE player_credentials (
                    player_id INTEGER PRIMARY KEY,
                    password_hash TEXT NOT NULL,
                    created_at INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL);
                CREATE TABLE player_auth_sessions (
                    token_hash TEXT PRIMARY KEY,
                    player_id INTEGER NOT NULL,
                    created_at INTEGER NOT NULL,
                    expires_at INTEGER NOT NULL);
                CREATE TABLE qq_whitelist_state (
                    singleton_id INTEGER PRIMARY KEY,
                    version INTEGER NOT NULL,
                    imported_at INTEGER NOT NULL,
                    imported_by TEXT NOT NULL,
                    member_count INTEGER NOT NULL,
                    duplicate_count INTEGER NOT NULL,
                    added_count INTEGER NOT NULL,
                    removed_count INTEGER NOT NULL,
                    removed_bound_count INTEGER NOT NULL);
                CREATE TABLE qq_whitelist_members (qq TEXT PRIMARY KEY, version INTEGER NOT NULL);
                CREATE TABLE player_qq_bindings (
                    player_id INTEGER PRIMARY KEY,
                    qq TEXT NOT NULL UNIQUE,
                    bound_at INTEGER NOT NULL,
                    whitelist_version INTEGER NOT NULL);
                CREATE TABLE qq_whitelist_import_audit (
                    version INTEGER PRIMARY KEY,
                    imported_at INTEGER NOT NULL,
                    imported_by TEXT NOT NULL,
                    member_count INTEGER NOT NULL,
                    duplicate_count INTEGER NOT NULL,
                    added_count INTEGER NOT NULL,
                    removed_count INTEGER NOT NULL,
                    removed_bound_count INTEGER NOT NULL);
                """;
            schema.ExecuteNonQuery();
        }

        using (var state = connection.CreateCommand())
        {
            state.CommandText = """
                INSERT INTO qq_whitelist_state VALUES(1,$version,$now,'legacy-admin',$count,0,$count,0,0);
                INSERT INTO qq_whitelist_import_audit VALUES($version,$now,'legacy-admin',$count,0,$count,0,0);
                """;
            state.Parameters.AddWithValue("$version", version);
            state.Parameters.AddWithValue("$now", now);
            state.Parameters.AddWithValue("$count", whitelist.Length);
            state.ExecuteNonQuery();
        }

        foreach (var qq in whitelist)
        {
            using var member = connection.CreateCommand();
            member.CommandText = "INSERT INTO qq_whitelist_members(qq,version) VALUES($qq,$version);";
            member.Parameters.AddWithValue("$qq", qq);
            member.Parameters.AddWithValue("$version", version);
            member.ExecuteNonQuery();
        }

        foreach (var account in accounts)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO player_credentials(player_id,password_hash,created_at,updated_at)
                SELECT id,$passwordHash,$now,$now FROM players WHERE account_key=$accountKey;
                INSERT INTO player_auth_sessions(token_hash,player_id,created_at,expires_at)
                SELECT $tokenHash,id,$now,$expiresAt FROM players WHERE account_key=$accountKey;
                INSERT INTO player_qq_bindings(player_id,qq,bound_at,whitelist_version)
                SELECT id,$qq,$now,$version FROM players WHERE account_key=$accountKey;
                """;
            insert.Parameters.AddWithValue("$passwordHash", account.PasswordHash);
            insert.Parameters.AddWithValue("$tokenHash", account.TokenHash);
            insert.Parameters.AddWithValue("$qq", account.Qq);
            insert.Parameters.AddWithValue("$accountKey", account.Account.ToUpperInvariant());
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$expiresAt", now + (long)TimeSpan.FromDays(1).TotalMilliseconds);
            insert.Parameters.AddWithValue("$version", version);
            insert.ExecuteNonQuery();
        }
    }

    private static string FileHash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string? ScalarText(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static long ScalarLong(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}

[CollectionDefinition("共享账号路径门禁隔离", DisableParallelization = true)]
public sealed class SharedAccountPathGateCollectionDefinition;

[Collection("共享账号路径门禁隔离")]
public sealed class SharedAccountPathGateTests : IDisposable
{
    private readonly string? _oldDatabase = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB");
    private readonly string? _oldActivation = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER");
    private readonly string? _oldPrepared = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_PREPARED_MARKER");
    private readonly string? _oldFallback = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ALLOW_LOCAL_FALLBACK");
    private readonly string _tempDirectory;

    public SharedAccountPathGateTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "共享账号路径门禁测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempDirectory = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void 测试服激活标记不存在时继续使用本环境账号库且不创建共享库()
    {
        var localPath = Path.Combine(_tempDirectory, "local-players.db");
        var sharedPath = Path.Combine(_tempDirectory, "accounts.db");
        Configure(
            sharedPath,
            Path.Combine(_tempDirectory, "inactive"),
            preparedMarker: null,
            allowLocalFallback: true);

        Assert.Equal(Path.GetFullPath(localPath), SharedAccountDatabase.ResolveDefaultPath(localPath));
        Assert.False(File.Exists(sharedPath));
    }

    [Fact]
    public void 正式服缺少准备标记或共享库时拒绝启动且绝不自动创建()
    {
        var localPath = Path.Combine(_tempDirectory, "formal-players.db");
        var sharedPath = Path.Combine(_tempDirectory, "accounts.db");
        var preparedMarker = Path.Combine(_tempDirectory, "prepared");

        Configure(
            sharedPath,
            Path.Combine(_tempDirectory, "inactive"),
            preparedMarker: null,
            allowLocalFallback: false);
        Assert.Throws<InvalidOperationException>(() => SharedAccountDatabase.ResolveDefaultPath(localPath));
        Assert.False(File.Exists(sharedPath));

        Configure(sharedPath, activationMarker: null, preparedMarker: null);
        Assert.Throws<InvalidOperationException>(() => SharedAccountDatabase.ResolveDefaultPath(localPath));
        Assert.False(File.Exists(sharedPath));

        File.WriteAllText(preparedMarker, "schema=1\n");
        Configure(sharedPath, activationMarker: null, preparedMarker);
        Assert.Throws<InvalidOperationException>(() => SharedAccountDatabase.ResolveDefaultPath(localPath));
        Assert.False(File.Exists(sharedPath));
    }

    [Fact]
    public void 独立共享库必须带有源数据迁移审计服务启动不得自行导入正式库()
    {
        var formalPath = Path.Combine(_tempDirectory, "formal-players.db");
        var sharedPath = Path.Combine(_tempDirectory, "accounts.db");
        var preparedMarker = Path.Combine(_tempDirectory, "prepared");
        var formal = new PlayerDataStore(formalPath);
        formal.Initialize();
        formal.Login("FormalOnly");

        var unprepared = new SharedAccountDatabase(sharedPath);
        unprepared.Initialize();
        File.WriteAllText(preparedMarker, "schema=1\n");
        Configure(sharedPath, activationMarker: null, preparedMarker);

        Assert.Equal(Path.GetFullPath(sharedPath), SharedAccountDatabase.ResolveDefaultPath(formalPath));
        Assert.Throws<InvalidOperationException>(() => unprepared.Initialize(
            [new LegacyAccountSource("current-environment", formalPath, Authoritative: true)],
            requirePreparedMigration: true));
        Assert.Equal(0, ScalarLong(sharedPath, "SELECT COUNT(*) FROM shared_accounts;"));

        var migratedPath = Path.Combine(_tempDirectory, "migrated-accounts.db");
        var migrated = new SharedAccountDatabase(migratedPath);
        migrated.Initialize([new LegacyAccountSource("production", formalPath, Authoritative: true)]);
        var ready = migrated.Initialize(requirePreparedMigration: true);
        Assert.Equal(1, ready.AccountCount);
        Assert.Equal(1, ScalarLong(migratedPath, "SELECT COUNT(*) FROM shared_account_migration_audit;"));
    }

    private static long ScalarLong(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Configure(
        string databasePath,
        string? activationMarker,
        string? preparedMarker,
        bool allowLocalFallback = false)
    {
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB", databasePath);
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER", activationMarker);
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_PREPARED_MARKER", preparedMarker);
        Environment.SetEnvironmentVariable(
            "GRANDUMI_ACCOUNT_DB_ALLOW_LOCAL_FALLBACK",
            allowLocalFallback ? "1" : null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB", _oldDatabase);
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER", _oldActivation);
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_PREPARED_MARKER", _oldPrepared);
        Environment.SetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ALLOW_LOCAL_FALLBACK", _oldFallback);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
