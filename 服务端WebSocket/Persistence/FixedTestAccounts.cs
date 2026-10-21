using System.Text;
using Microsoft.AspNetCore.Identity;

namespace GrandUMI.Persistence;

/// <summary>仅测试节点启用的固定账号，凭证、会话及昵称目录独立存放，不写入共享账号库。</summary>
public sealed class FixedTestAccounts
{
    public const string Password = "123456";
    public static IReadOnlyList<string> Names { get; } = Enumerable.Range(1, 16).Select(i => $"test{i:00}").ToArray();
    private static readonly HashSet<string> Keys = Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    internal AccountAuthenticationStore Authentication { get; }
    public string DatabasePath { get; }

    private FixedTestAccounts(PlayerDataStore players)
    {
        DatabasePath = Path.Combine(Path.GetDirectoryName(players.DatabasePath)!, "fixed-test-accounts.db");
        var local = new SharedAccountDatabase(DatabasePath);
        local.Initialize();
        var hasher = new PasswordHasher<string>();
        using (var connection = local.OpenConnection())
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            foreach (var name in Names)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                using var create = connection.CreateCommand(); create.Transaction = transaction;
                create.CommandText = "INSERT INTO shared_accounts(account_key,account,display_name,created_at,updated_at,last_login_at) VALUES($key,$name,$name,$now,$now,$now) ON CONFLICT(account_key) DO NOTHING";
                create.Parameters.AddWithValue("$key", name.ToUpperInvariant()); create.Parameters.AddWithValue("$name", name); create.Parameters.AddWithValue("$now", now); create.ExecuteNonQuery();
                using var read = connection.CreateCommand(); read.Transaction = transaction;
                read.CommandText = "SELECT password_hash FROM shared_player_credentials WHERE account_key=$key";
                read.Parameters.AddWithValue("$key", name.ToUpperInvariant());
                var storedHash = read.ExecuteScalar() as string;
                if (storedHash is not null && hasher.VerifyHashedPassword(name, storedHash, Password) == PasswordVerificationResult.Success) continue;
                using var credential = connection.CreateCommand(); credential.Transaction = transaction;
                credential.CommandText = "INSERT INTO shared_player_credentials(account_key,password_hash,created_at,updated_at) VALUES($key,$hash,$now,$now) ON CONFLICT(account_key) DO UPDATE SET password_hash=excluded.password_hash,updated_at=excluded.updated_at; DELETE FROM shared_player_auth_sessions WHERE account_key=$key";
                credential.Parameters.AddWithValue("$key", name.ToUpperInvariant()); credential.Parameters.AddWithValue("$hash", hasher.HashPassword(name, Password)); credential.Parameters.AddWithValue("$now", now); credential.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        Authentication = new AccountAuthenticationStore(players, local);
        foreach (var name in Names) players.LoginSharedAccount(name, name);
    }

    public static FixedTestAccounts? FromEnvironment(PlayerDataStore players, SharedAccountDatabase shared)
        => Create(players, shared, Environment.GetEnvironmentVariable("GRANDUMI_FIXED_TEST_ACCOUNTS"), Environment.GetEnvironmentVariable("GRANDUMI_NODE_ID"));

    internal static FixedTestAccounts? Create(PlayerDataStore players, SharedAccountDatabase shared, string? enabled, string? nodeId)
    {
        if (enabled != "1") return null;
        if (nodeId != "hk-test-01") throw new InvalidOperationException("固定测试账号只能在指定测试节点启用。");
        var localPath = Path.Combine(Path.GetDirectoryName(players.DatabasePath)!, "fixed-test-accounts.db");
        if (string.Equals(shared.DatabasePath, players.DatabasePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shared.DatabasePath, localPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("固定测试账号必须与共享账号库分离，不能在本地回退账号库启用。");
        using (var connection = shared.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT account FROM shared_accounts";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Keys.Contains(reader.GetString(0).Trim().Normalize(NormalizationForm.FormKC))) throw new InvalidOperationException("固定测试账号名称与已有共享账号冲突，未覆盖原账号。");
        }
        if (Names.Any(AdministratorPolicy.IsAuthorized)) throw new InvalidOperationException("固定测试账号不能配置为平台管理员。");
        return new FixedTestAccounts(players);
    }

    public bool Contains(string? account) => Keys.Contains((account ?? "").Trim().Normalize(NormalizationForm.FormKC));
    internal static QqLoginAccessResult Allowed() => new(QqLoginAccessKind.Allowed, "固定测试白名单验证通过。", MaskedQq: "固定测试账号");
}
