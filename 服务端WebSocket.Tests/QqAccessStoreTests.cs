using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class QqAccessStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _databasePath;

    public QqAccessStoreTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "QQ 准入测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempDirectory = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_tempDirectory, "players.db");
    }

    [Fact]
    public void Initialize_迁移幂等且不会改动既有账号密码()
    {
        var players = CreatePlayers();
        players.Login("Alice");
        var auth = new AccountAuthenticationStore(players);
        auth.Initialize();
        var authenticated = auth.Authenticate("Alice", "correct horse", null);
        Assert.True(authenticated.Success);

        var first = new QqAccessStore(players);
        first.Initialize();
        first.Initialize();
        var restarted = new QqAccessStore(players);
        restarted.Initialize();

        Assert.False(restarted.GetStatus().Initialized);
        Assert.True(auth.Authenticate("Alice", "correct horse", null).Success);

        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        foreach (var table in new[]
                 {
                     "qq_whitelist_state", "qq_whitelist_members",
                     "player_qq_bindings", "qq_whitelist_import_audit",
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
            command.Parameters.AddWithValue("$name", table);
            Assert.NotNull(command.ExecuteScalar());
        }
    }

    [Fact]
    public void Bootstrap_只固化首次迁移时已经存在的授权管理员()
    {
        var players = CreatePlayers();
        players.Login("ExistingAdmin");
        var first = new QqAccessStore(players, new[] { "ExistingAdmin", "LateAdmin" });
        first.Initialize();

        Assert.True(first.IsBootstrapAdministrator("existingadmin"));
        Assert.False(first.IsBootstrapAdministrator("LateAdmin"));

        // 首次迁移后才注册同名账号，重启也不能把它提升为初始化管理员。
        players.Login("LateAdmin");
        var restarted = new QqAccessStore(players, new[] { "ExistingAdmin", "LateAdmin" });
        restarted.Initialize();
        Assert.True(restarted.IsBootstrapAdministrator("ExistingAdmin"));
        Assert.False(restarted.IsBootstrapAdministrator("LateAdmin"));
    }

    [Theory]
    [InlineData("[\"12345\",23456,{\"qq\":\"34567\"},{\"uin\":45678},{\"user_id\":\"56789\"},\"12345\"]")]
    [InlineData("{\"members\":[\"12345\",\"23456\",\"34567\",\"45678\",\"56789\",\"12345\"]}")]
    [InlineData("{\"data\":[{\"QQ\":\"12345\"},{\"UIN\":\"23456\"},{\"USER_ID\":\"34567\"},45678,56789,12345]}")]
    [InlineData("{\"list\":[12345,23456,34567,45678,56789,12345]}")]
    public void Import_兼容常见结构并规范化去重(string json)
    {
        var store = CreateStore(out _);

        var preview = QqAccessStore.PreviewImport(json);
        var result = store.Import("释迦", json);
        var status = store.GetStatus();

        Assert.Equal(6, preview.TotalCount);
        Assert.Equal(5, preview.UniqueCount);
        Assert.Equal(1, preview.DuplicateCount);
        Assert.Equal(1, result.Version);
        Assert.Equal(5, result.MemberCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(5, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.True(status.Initialized);
        Assert.Equal("释迦", status.ImportedBy);
    }

    [Fact]
    public void Import_任一非法项与空名单都原子拒绝且保留旧版本()
    {
        var store = CreateStore(out _);
        store.Import("释迦", "[\"12345\",\"23456\"]");

        Assert.Throws<QqAccessValidationException>(() =>
            store.Import("释迦", "[\"34567\",{\"qq\":\"bad-value\"}]") );
        Assert.Throws<QqAccessValidationException>(() => store.Import("释迦", "[]"));
        Assert.Throws<QqAccessValidationException>(() => store.Import("释迦", "{\"members\":{}}"));

        var status = store.GetStatus();
        Assert.Equal(1, status.Version);
        Assert.Equal(2, status.MemberCount);
        Assert.Equal(QqLoginAccessKind.NeedsBinding, store.EvaluateLogin("Alice", null).Kind);
        Assert.Equal(QqLoginAccessKind.NotWhitelisted, store.EvaluateLogin("Alice", "34567").Kind);
    }

    [Fact]
    public void Import_严格限制体积和成员数量()
    {
        var oversizedJson = "[\"" + new string('1', QqAccessStore.MaxImportBytes) + "\"]";
        Assert.Throws<QqAccessValidationException>(() => QqAccessStore.PreviewImport(oversizedJson));

        var tooMany = "[" + string.Join(',', Enumerable.Repeat("\"12345\"", QqAccessStore.MaxImportMembers + 1)) + "]";
        Assert.Throws<QqAccessValidationException>(() => QqAccessStore.PreviewImport(tooMany));
    }

    [Fact]
    public void ExistingAndNewAccounts_首次绑定后可登录且玩家不能改绑()
    {
        var store = CreateStore(out var players);
        players.Login("Existing");
        players.Login("NewAccount");
        store.Import("释迦", "[\"12345\",\"23456\",\"34567\"]");

        Assert.Equal(QqLoginAccessKind.NeedsBinding, store.EvaluateLogin("Existing", null).Kind);
        var existing = store.EvaluateLogin("Existing", "１２３４５");
        Assert.True(existing.Allowed);
        Assert.Equal("1***5", existing.MaskedQq);

        Assert.True(store.EvaluateLogin("NewAccount", "23456").Allowed);
        var change = store.EvaluateLogin("Existing", "34567");
        Assert.Equal(QqLoginAccessKind.QqAlreadyBound, change.Kind);
        Assert.True(store.EvaluateLogin("Existing", null).Allowed);
    }

    [Fact]
    public async Task ConcurrentBinding_同一Qq只能有一个账号成功()
    {
        var store = CreateStore(out var players);
        players.Login("Alice");
        players.Login("Bob");
        store.Import("释迦", "[\"12345\"]");

        var start = new ManualResetEventSlim(false);
        var attempts = new[] { "Alice", "Bob" }.Select(account => Task.Run(() =>
        {
            start.Wait();
            return (Account: account, Result: store.EvaluateLogin(account, "12345"));
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, item => item.Result.Allowed);
        Assert.Single(results, item => item.Result.Kind == QqLoginAccessKind.QqAlreadyBound);
        Assert.NotEqual(
            store.GetAccountBindingStatus("Alice").Bound,
            store.GetAccountBindingStatus("Bob").Bound);
    }

    [Fact]
    public void Replacement_返回增删重复和被移出绑定人数并立即撤销资格()
    {
        var store = CreateStore(out var players);
        players.Login("Alice");
        players.Login("Bob");
        store.Import("释迦", "[\"12345\",\"23456\"]");
        Assert.True(store.EvaluateLogin("Alice", "12345").Allowed);
        Assert.True(store.EvaluateLogin("Bob", "23456").Allowed);

        var replaced = store.Import("栗子", "[\"23456\",\"34567\",\"34567\"]");

        Assert.Equal(2, replaced.Version);
        Assert.Equal(2, replaced.MemberCount);
        Assert.Equal(1, replaced.DuplicateCount);
        Assert.Equal(1, replaced.AddedCount);
        Assert.Equal(1, replaced.RemovedCount);
        Assert.Equal(1, replaced.RemovedBoundCount);
        Assert.Equal(QqLoginAccessKind.NotWhitelisted, store.EvaluateLogin("Alice", null).Kind);
        Assert.True(store.EvaluateLogin("Bob", null).Allowed);
        Assert.False(store.GetAccountBindingStatus("Alice").CurrentlyWhitelisted);
    }

    [Fact]
    public async Task ImportAndGameStart_按同一门锁线性化且移出后禁止下一局()
    {
        var store = CreateStore(out var players);
        players.Login("Alice");
        store.Import("释迦", "[\"12345\"]");
        Assert.True(store.EvaluateLogin("Alice", "12345").Allowed);

        using var gameRegistered = new ManualResetEventSlim(false);
        using var allowRegistrationToReturn = new ManualResetEventSlim(false);
        var admission = Task.Run(() => store.ExecuteNewGameAdmission(new[] { "Alice" }, () =>
        {
            gameRegistered.Set();
            allowRegistrationToReturn.Wait();
            return "room-created";
        }));
        Assert.True(gameRegistered.Wait(TimeSpan.FromSeconds(5)));

        var import = Task.Run(() => store.Import("释迦", "[\"23456\"]"));
        await Task.Delay(100);
        Assert.False(import.IsCompleted);
        allowRegistrationToReturn.Set();

        Assert.Equal("room-created", await admission);
        await import;
        Assert.Throws<QqAccessDeniedException>(() =>
            store.ExecuteNewGameAdmission(new[] { "Alice" }, () => "next-room"));
    }

    [Fact]
    public void Restart_白名单绑定和审计摘要持续存在但不保存原始Json()
    {
        var store = CreateStore(out var players);
        players.Login("Alice");
        var originalJson = "[\"12345\",\"23456\",\"12345\"]";
        store.Import("释迦", originalJson);
        store.EvaluateLogin("Alice", "12345");

        var restarted = new QqAccessStore(players);
        restarted.Initialize();
        Assert.True(restarted.EvaluateLogin("Alice", null).Allowed);
        Assert.Equal(2, restarted.GetStatus().MemberCount);

        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT imported_by, member_count, duplicate_count FROM qq_whitelist_import_audit;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("释迦", reader.GetString(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public async Task BootstrapImport_并发请求也只能有一个初始化成功()
    {
        var store = CreateStore(out _);
        using var start = new ManualResetEventSlim(false);
        var attempts = new[] { "[\"12345\"]", "[\"23456\"]" }
            .Select(json => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    store.Import("释迦", json, initializationOnly: true);
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
        var status = store.GetStatus();
        Assert.True(status.Initialized);
        Assert.Equal(1, status.Version);
        Assert.Equal(1, status.MemberCount);
    }

    [Fact]
    public void SessionRecovery_旧令牌仍有效也不能绕过最新白名单资格()
    {
        var players = CreatePlayers();
        var authentication = new AccountAuthenticationStore(players);
        authentication.Initialize();
        var initialLogin = authentication.Authenticate("Alice", "correct horse", null);
        Assert.True(initialLogin.Success);
        Assert.NotNull(initialLogin.AuthToken);

        var store = new QqAccessStore(players);
        store.Initialize();
        store.Import("释迦", "[\"12345\"]");
        Assert.True(store.EvaluateLogin(initialLogin.Account, "12345").Allowed);
        Assert.True(authentication.Authenticate("Alice", null, initialLogin.AuthToken).Success);

        store.Import("释迦", "[\"23456\"]");

        // 凭据层的 30 天令牌仍然有效，但业务会话必须再次经过 QQ 权威层。
        var recoveredCredential = authentication.Authenticate("Alice", null, initialLogin.AuthToken);
        Assert.True(recoveredCredential.Success);
        Assert.Equal(QqLoginAccessKind.NotWhitelisted,
            store.EvaluateLogin(recoveredCredential.Account, null).Kind);
        Assert.Throws<QqAccessDeniedException>(() =>
            store.ExecuteNewGameAdmission(new[] { recoveredCredential.Account }, () => "room"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private QqAccessStore CreateStore(out PlayerDataStore players)
    {
        players = CreatePlayers();
        players.Login("Alice");
        var store = new QqAccessStore(players);
        store.Initialize();
        return store;
    }

    private PlayerDataStore CreatePlayers()
    {
        var players = new PlayerDataStore(_databasePath);
        players.Initialize();
        return players;
    }
}
