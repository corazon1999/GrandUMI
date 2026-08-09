using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class AccountAuthenticationStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "grandumi-auth-tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public AccountAuthenticationStoreTests()
    {
        _databasePath = Path.Combine(_tempDir, "players.db");
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

        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT password_hash FROM player_credentials;";
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

    private PlayerDataStore CreatePlayers()
    {
        var players = new PlayerDataStore(_databasePath);
        players.Initialize();
        return players;
    }

    private static AccountAuthenticationStore CreateAuth(PlayerDataStore players)
    {
        var auth = new AccountAuthenticationStore(players);
        auth.Initialize();
        return auth;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
