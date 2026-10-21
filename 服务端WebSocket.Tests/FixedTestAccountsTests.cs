using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

[Collection("持久化目录隔离")]
public sealed class FixedTestAccountsTests : IDisposable
{
    private readonly string _root;
    private readonly PlayerDataStore _players;
    private readonly SharedAccountDatabase _shared;
    public FixedTestAccountsTests()
    {
        var temp = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT") ?? throw new InvalidOperationException("固定账号测试必须使用项目测试临时目录。");
        _root = Path.Combine(temp, "fixed-test-accounts-" + Guid.NewGuid().ToString("N"));
        _players = new PlayerDataStore(Path.Combine(_root, "players.db")); _players.Initialize();
        _shared = new SharedAccountDatabase(Path.Combine(_root, "shared.db")); _shared.Initialize();
    }
    public void Dispose() { Directory.Delete(_root, true); }
    private FixedTestAccounts Create() => FixedTestAccounts.Create(_players, _shared, "1", "hk-test-01")!;
    private long SharedCount(string table)
    {
        using var c = new SqliteConnection($"Data Source={_shared.DatabasePath};Pooling=False"); c.Open();
        using var command = c.CreateCommand(); command.CommandText = "SELECT count(*) FROM " + table;
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void 十六个固定账号都能密码认证且不向共享库写入凭证或会话()
    {
        var fixedAccounts = Create(); var auth = new AccountAuthenticationStore(_players, _shared, fixedAccounts);
        var access = new QqAccessStore(_shared, fixedTestAccounts: fixedAccounts);
        Assert.Equal(16, _players.GetPlayerDirectoryEntries().Count);
        foreach (var account in FixedTestAccounts.Names)
        {
            var result = auth.Authenticate(account, "123456", null);
            Assert.True(result.Success); Assert.False(string.IsNullOrEmpty(result.AuthToken));
            Assert.False(AdministratorPolicy.IsAuthorized(result.Account));
            Assert.True(access.EvaluateLogin(account, null).Allowed);
            Assert.True(access.CheckNewGameAccess(account).Allowed);
            Assert.True(access.GetAccountBindingStatus(account).CurrentlyWhitelisted);
        }
        foreach (var table in new[] { "shared_accounts", "shared_player_credentials", "shared_player_auth_sessions", "shared_account_qq_bindings" }) Assert.Equal(0, SharedCount(table));
        using var local = new SqliteConnection($"Data Source={fixedAccounts.DatabasePath};Pooling=False"); local.Open();
        using var read = local.CreateCommand(); read.CommandText = "SELECT password_hash FROM shared_player_credentials";
        using var hashes = read.ExecuteReader(); var count = 0;
        while (hashes.Read()) { Assert.NotEqual("123456", hashes.GetString(0)); count++; }
        Assert.Equal(16, count);
    }

    [Fact]
    public void 账号范围严格限定且普通账号仍执行原密码长度和QQ规则()
    {
        var fixedAccounts = Create(); var auth = new AccountAuthenticationStore(_players, _shared, fixedAccounts);
        var access = new QqAccessStore(_shared, fixedTestAccounts: fixedAccounts);
        Assert.True(auth.Authenticate(" TEST01 ", "123456", null).Success);
        Assert.False(auth.Authenticate("test01", "654321", null).Success);
        foreach (var name in new[] { "test00", "test17", "test1", "test001", "test016", "normal" })
        {
            Assert.False(fixedAccounts.Contains(name));
            Assert.False(auth.Authenticate(name, "123456", null).Success);
            Assert.False(access.EvaluateLogin(name, null).Allowed);
        }
        Assert.Equal(0, SharedCount("shared_accounts"));
    }

    [Fact]
    public void 固定白名单不受群名单替换影响且混合准入仍校验普通玩家()
    {
        var fixedAccounts = Create(); var access = new QqAccessStore(_shared, fixedTestAccounts: fixedAccounts);
        access.Import("admin", "[\"100001\"]");
        access.Import("admin", "[\"100002\"]");
        Assert.True(access.EvaluateLogin("test16", null).Allowed);
        Assert.Equal("已登记", access.ExecuteNewGameAdmission(new[] { "test01", "test16" }, () => "已登记"));
        var ran = false;
        Assert.Throws<QqAccessDeniedException>(() => access.ExecuteNewGameAdmission(new[] { "test01", "normal" }, () => ran = true));
        Assert.False(ran); Assert.Equal(0, SharedCount("shared_account_qq_bindings"));
        Assert.Equal(1, SharedCount("shared_qq_whitelist_members"));
    }

    [Fact]
    public void 重建服务保留固定账号会话且不能修改或重置固定密码()
    {
        var auth = new AccountAuthenticationStore(_players, _shared, Create());
        var login = auth.Authenticate("test02", "123456", null);
        Assert.False(auth.ChangePassword("test02", "123456", "abcdefgh").Success);
        Assert.Throws<PlayerDataValidationException>(() => auth.AdminResetPassword("admin", "test02"));
        var restored = new AccountAuthenticationStore(_players, _shared, Create());
        restored.Initialize();
        Assert.True(restored.Authenticate("test02", null, login.AuthToken).Success);
        Assert.Equal(0, SharedCount("shared_accounts"));
    }

    [Fact]
    public void 固定账号昵称同步留在本地且一致性巡检不误报缺失身份()
    {
        var auth = new AccountAuthenticationStore(_players, _shared, Create());
        _players.UpdateProfile("test03", "赛事测试三号", "");
        using var operations = new OperationsCenterStore(Path.Combine(_root, "operations.db")); operations.Initialize();
        var snapshot = new ConsistencyDoctor(_players, auth, operations).RunOnce();
        Assert.Equal(0, snapshot.Retried); Assert.Equal(0, snapshot.OpenFindings);
        Assert.Equal("赛事测试三号", auth.GetDirectorySearchNames()["test03"]);
        Assert.Equal(0, SharedCount("shared_accounts"));
    }

    [Fact]
    public void 默认关闭且禁止错误节点与本地回退账号库启用()
    {
        Assert.Null(FixedTestAccounts.Create(_players, _shared, null, "hk-test-01"));
        Assert.Null(FixedTestAccounts.Create(_players, _shared, "0", "hk-test-01"));
        Assert.False(File.Exists(Path.Combine(_root, "fixed-test-accounts.db")));
        Assert.Throws<InvalidOperationException>(() => FixedTestAccounts.Create(_players, _shared, "1", "其他节点"));
        Assert.Throws<InvalidOperationException>(() => FixedTestAccounts.Create(_players, new SharedAccountDatabase(_players.DatabasePath), "1", "hk-test-01"));
        var normalAuth = new AccountAuthenticationStore(_players, _shared);
        Assert.False(normalAuth.Authenticate("test01", "123456", null).Success);
        Assert.False(new QqAccessStore(_shared).CheckNewGameAccess("test01").Allowed);
    }

    [Fact]
    public void 发现已有同名共享账号时停止且保留原密码()
    {
        var auth = new AccountAuthenticationStore(_players, _shared);
        Assert.True(auth.Authenticate("test01", "original-password", null).Success);
        Assert.Throws<InvalidOperationException>(() => Create());
        Assert.True(auth.Authenticate("test01", "original-password", null).Success);
        Assert.False(auth.Authenticate("test01", "123456", null).Success);
        Assert.False(File.Exists(Path.Combine(_root, "fixed-test-accounts.db")));
    }
}
