using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class AccountAuthenticationStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _databasePath;
    private readonly string _sharedDatabasePath;

    public AccountAuthenticationStoreTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "账号认证测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempDir = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_tempDir, "players.db");
        _sharedDatabasePath = Path.Combine(_tempDir, "accounts.db");
    }

    [Fact]
    public void ExistingAccount_MustSetPassword_ThenRequiresItOnLaterLogin()
    {
        var players = CreatePlayers();
        players.Login("Alice");
        var auth = CreateAuth(players);

        var challenge = auth.Authenticate("alice", null, null);
        Assert.False(challenge.Success);
        Assert.True(challenge.NeedsPasswordSetup);
        Assert.True(challenge.IsChallenge);

        var firstLogin = auth.Authenticate("alice", "correct horse", null);
        Assert.True(firstLogin.Success);
        Assert.Equal("Alice", firstLogin.Account);
        Assert.False(string.IsNullOrWhiteSpace(firstLogin.AuthToken));

        var wrongPassword = auth.Authenticate("ALICE", "wrong password", null);
        Assert.False(wrongPassword.Success);
        Assert.False(wrongPassword.NeedsPasswordSetup);

        var laterLogin = auth.Authenticate("Alice", "correct horse", null);
        Assert.True(laterLogin.Success);
        Assert.NotEqual(firstLogin.AuthToken, laterLogin.AuthToken);

        using var connection = new SqliteConnection($"Data Source={_sharedDatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT password_hash FROM shared_player_credentials;";
        var storedHash = Assert.IsType<string>(command.ExecuteScalar());
        Assert.DoesNotContain("correct horse", storedHash, StringComparison.Ordinal);
    }

    [Fact]
    public void NewAccount_IsOnlyCreatedAfterAValidPasswordIsSubmitted()
    {
        var players = CreatePlayers();
        var auth = CreateAuth(players);

        var challenge = auth.Authenticate("新玩家", null, null);
        Assert.True(challenge.NeedsPasswordSetup);
        Assert.Throws<PlayerDataValidationException>(() => players.GetPlayerData("新玩家"));

        var tooShort = auth.Authenticate("新玩家", "1234567", null);
        Assert.False(tooShort.Success);
        Assert.True(tooShort.NeedsPasswordSetup);
        Assert.Throws<PlayerDataValidationException>(() => players.GetPlayerData("新玩家"));

        var created = auth.Authenticate("新玩家", "12345678", null);
        Assert.True(created.Success);
        Assert.Equal("新玩家", players.GetPlayerData("新玩家").Account);
    }

    [Fact]
    public void SessionTokenSupportsReconnect_AndPasswordChangeRevokesOldCredentials()
    {
        var players = CreatePlayers();
        var auth = CreateAuth(players);
        var login = auth.Authenticate("Nami", "old-password", null);
        Assert.True(login.Success);

        var resumed = auth.Authenticate("nami", null, login.AuthToken);
        Assert.True(resumed.Success);
        Assert.Equal(login.AuthToken, resumed.AuthToken);

        var rejectedChange = auth.ChangePassword("Nami", "not-the-password", "new-password");
        Assert.False(rejectedChange.Success);

        var changed = auth.ChangePassword("Nami", "old-password", "new-password");
        Assert.True(changed.Success);
        Assert.False(string.IsNullOrWhiteSpace(changed.AuthToken));

        Assert.False(auth.Authenticate("Nami", null, login.AuthToken).Success);
        Assert.False(auth.Authenticate("Nami", "old-password", null).Success);
        Assert.True(auth.Authenticate("Nami", "new-password", null).Success);
        Assert.True(auth.Authenticate("Nami", null, changed.AuthToken).Success);
    }

    [Fact]
    public void 管理员重置密码会生成临时密码撤销旧凭据并写入审计()
    {
        var players = CreatePlayers();
        var auth = CreateAuth(players);
        var login = auth.Authenticate("Robin", "old-password", null);

        var reset = auth.AdminResetPassword("释迦", "robin");

        Assert.Equal("Robin", reset.Account);
        Assert.Equal(18, reset.TemporaryPassword.Length);
        Assert.False(auth.Authenticate("Robin", "old-password", null).Success);
        Assert.False(auth.Authenticate("Robin", null, login.AuthToken).Success);
        Assert.True(auth.Authenticate("Robin", reset.TemporaryPassword, null).Success);
        using var connection = new SqliteConnection($"Data Source={_sharedDatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT action, detail_json FROM shared_admin_player_audit;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("reset_password", reader.GetString(0));
        Assert.Equal("{}", reader.GetString(1));
        Assert.DoesNotContain(reset.TemporaryPassword, reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public void 管理员不能在当前会话重置自己的密码()
    {
        var players = CreatePlayers();
        var auth = CreateAuth(players);
        auth.Authenticate("释迦", "original-password", null);

        var error = Assert.Throws<PlayerDataValidationException>(
            () => auth.AdminResetPassword("释迦", "释迦"));

        Assert.Contains("不能", error.Message);
        Assert.True(auth.Authenticate("释迦", "original-password", null).Success);
    }

    [Fact]
    public void 测试服与正式服共享密码和会话撤销但玩法资料隔离()
    {
        var formalPlayers = CreatePlayers();
        var testPlayersPath = Path.Combine(_tempDir, "test-players.db");
        var testPlayers = new PlayerDataStore(testPlayersPath);
        testPlayers.Initialize();
        var accounts = new SharedAccountDatabase(_sharedDatabasePath);
        accounts.Initialize();
        var formalAuth = new AccountAuthenticationStore(formalPlayers, accounts);
        var testAuth = new AccountAuthenticationStore(testPlayers, accounts);

        var formalLogin = formalAuth.Authenticate("SharedUser", "formal-password", null);
        Assert.True(formalLogin.Success);
        Assert.Throws<PlayerDataValidationException>(() => testPlayers.GetPlayerData("SharedUser"));

        var testLogin = testAuth.Authenticate("shareduser", "formal-password", null);
        Assert.True(testLogin.Success);
        Assert.Equal("SharedUser", testPlayers.GetPlayerData("SHAREDUSER").Account);

        var changed = testAuth.ChangePassword("SharedUser", "formal-password", "changed-password");
        Assert.True(changed.Success);
        Assert.False(formalAuth.Authenticate("SharedUser", null, formalLogin.AuthToken).Success);
        Assert.False(formalAuth.Authenticate("SharedUser", "formal-password", null).Success);
        Assert.True(formalAuth.Authenticate("SharedUser", "changed-password", null).Success);
    }

    [Fact]
    public void 共享检索昵称与环境玩法昵称冲突时仍可物化且既有昵称不被跨环境覆盖()
    {
        var formalPlayers = CreatePlayers();
        var testPlayersPath = Path.Combine(_tempDir, "test-players.db");
        var testPlayers = new PlayerDataStore(testPlayersPath);
        testPlayers.Initialize();
        testPlayers.Login("LocalOwner");
        testPlayers.UpdateProfile("LocalOwner", "SharedName", "");

        var accounts = new SharedAccountDatabase(_sharedDatabasePath);
        accounts.Initialize();
        var formalAuth = new AccountAuthenticationStore(formalPlayers, accounts);
        Assert.True(formalAuth.Authenticate("RemoteUser", "shared-password", null).Success);
        formalAuth.UpdateDirectorySearchName("RemoteUser", "SharedName");

        var testAuth = new AccountAuthenticationStore(testPlayers, accounts);
        Assert.True(testAuth.Authenticate("RemoteUser", "shared-password", null).Success);
        var materialized = testPlayers.GetPlayerData("RemoteUser");
        Assert.StartsWith("SharedName·", materialized.DisplayName, StringComparison.Ordinal);
        Assert.Equal("SharedName", testPlayers.GetPlayerData("LocalOwner").DisplayName);

        testPlayers.AdminRenamePlayer("测试管理员", "RemoteUser", "TestGameplayName");
        testAuth.UpdateDirectorySearchName("RemoteUser", "TestGameplayName");
        Assert.Equal(
            "RemoteUser",
            Assert.Single(new QqAccessStore(accounts).SearchAccountsForAdmin("TestGameplayName", "player")).Account);
        Assert.True(testAuth.Authenticate("RemoteUser", "shared-password", null).Success);
        Assert.Equal("TestGameplayName", testPlayers.GetPlayerData("RemoteUser").DisplayName);
        Assert.Equal("RemoteUser", formalPlayers.GetPlayerData("RemoteUser").DisplayName);
    }

    private PlayerDataStore CreatePlayers()
    {
        var players = new PlayerDataStore(_databasePath);
        players.Initialize();
        return players;
    }

    private AccountAuthenticationStore CreateAuth(PlayerDataStore players)
    {
        var accounts = new SharedAccountDatabase(_sharedDatabasePath);
        accounts.Initialize(
            [new LegacyAccountSource("local", players.DatabasePath, Authoritative: true)]);
        var auth = new AccountAuthenticationStore(players, accounts);
        auth.Initialize();
        return auth;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
