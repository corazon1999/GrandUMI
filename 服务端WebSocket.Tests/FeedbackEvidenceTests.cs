using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

public class FeedbackEvidenceTests
{
    [Fact]
    public void StructuredClientEvidence_IsWhitelistedBoundedAndNonAuthoritative()
    {
        var submitted = JsonSerializer.SerializeToElement(new
        {
            schema = "grandumi.feedback.client.v1",
            capturedAtUtc = "2026-08-31T01:02:03Z",
            account = "secret-account",
            authToken = "secret-token",
            url = "https://example.test/?token=secret-query",
            client = new { version = "0.999", commit = new string('a', 40), context = "game", password = "secret-password" },
            connection = new
            {
                state = "connected",
                endpointHost = "test.grand-umi.com",
                connectionGeneration = 7,
                reconnectCount = 3,
                endpointFailureCount = 2,
                handshakeMs = 123,
                rttMs = 45,
                rttP95Ms = 80,
                actionRoundTripMs = 90,
                actionRoundTripP95Ms = 120,
                lastDisconnectReason = new string('x', 500),
                stateDeltaEnabled = true,
                stateDeltaCount = 12,
                fullStateCount = 4,
                maxMessageQueueDepth = 6,
            },
            viewport = new { width = 390, height = 844, orientation = "portrait", devicePixelRatio = 3, standalone = false, online = true },
            gameStore = new { hand = new[] { "OP01-001" }, chat = "private-chat" },
        });

        var normalized = FeedbackEvidenceSanitizer.Sanitize(submitted, null);
        var json = normalized.ToJsonString();

        Assert.Contains("client_non_authoritative", json, StringComparison.Ordinal);
        Assert.Contains("test.grand-umi.com", json, StringComparison.Ordinal);
        Assert.Contains("\"connectionGeneration\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"disconnectCategory\":\"other\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-account", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-query", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OP01-001", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-chat", json, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 100), json, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= FeedbackEvidenceSanitizer.MaxPersistedBytes);
    }

    [Fact]
    public void LegacyClientInfo_OnlyKeepsConnectionDiagnosticsAndDropsPrivateState()
    {
        var legacy = JsonSerializer.Serialize(new
        {
            meta = new
            {
                context = "game",
                account = "legacy-secret-account",
                playerName = "legacy-secret-name",
                url = "https://example.test/secret",
                userAgent = "fingerprint",
                connectionState = "reconnecting",
                networkDiagnostics = new
                {
                    endpointHost = "direct.grand-umi.com",
                    reconnectCount = 9,
                    lastDisconnectReason = "network changed",
                },
            },
            gameStore = new { myHand = new[] { "OP02-072" }, opponentHand = new[] { "hidden" } },
        });

        var normalized = FeedbackEvidenceSanitizer.Sanitize(null, legacy);
        var json = normalized.ToJsonString();

        Assert.Contains("\"source\":\"legacy\"", json, StringComparison.Ordinal);
        Assert.Contains("direct.grand-umi.com", json, StringComparison.Ordinal);
        Assert.Contains("reconnecting", json, StringComparison.Ordinal);
        Assert.Contains("\"disconnectCategory\":\"network\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-secret-account", json, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-secret-name", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OP02-072", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredClientEvidence_RejectsSecretsInjectedIntoAllowedStringFields()
    {
        const string password = "password-secret-value";
        const string token = "token-secret-value";
        const string cardNumber = "OP15-001";
        var guid = Guid.NewGuid().ToString();
        var submitted = JsonSerializer.SerializeToElement(new
        {
            schema = $"grandumi.feedback.client.v1-{token}",
            capturedAtUtc = $"2026-08-31T01:02:03Z?{token}",
            client = new { version = password, commit = guid, context = "game" },
            connection = new
            {
                state = "connected",
                endpointHost = $"{token}.example.test/{cardNumber}",
                lastDisconnectReason = $"{password} {token} {cardNumber} {guid}",
            },
            viewport = new { width = 390, height = 844, orientation = "portrait" },
        });

        var normalized = FeedbackEvidenceSanitizer.Sanitize(submitted, null);
        using var document = JsonDocument.Parse(normalized.ToJsonString());
        var root = document.RootElement;
        var json = root.GetRawText();

        Assert.Equal("unknown", root.GetProperty("submittedSchema").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("capturedAtUtc").ValueKind);
        Assert.Equal("unknown", root.GetProperty("client").GetProperty("version").GetString());
        Assert.Equal("unknown", root.GetProperty("client").GetProperty("commit").GetString());
        Assert.Equal("unknown", root.GetProperty("connection").GetProperty("endpointHost").GetString());
        Assert.Equal("other", root.GetProperty("connection").GetProperty("disconnectCategory").GetString());
        Assert.DoesNotContain(password, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cardNumber, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(guid, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedClientEvidence_IsRejectedWithoutPersistingRawPayload()
    {
        var oversized = JsonSerializer.SerializeToElement(new
        {
            schema = "grandumi.feedback.client.v1",
            unknown = new string('z', FeedbackEvidenceSanitizer.MaxSubmittedBytes + 1),
        });

        var normalized = FeedbackEvidenceSanitizer.Sanitize(oversized, null);
        var json = normalized.ToJsonString();

        Assert.Contains("structured_rejected_too_large", json, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('z', 100), json, StringComparison.Ordinal);
    }

    [Fact]
    public void BugReportPath_IsStableDeidentifiedAndContainedByRoot()
    {
        var root = Path.Combine(TestTempRoot(), "feedback-path-test");
        var first = BugReportStore.BuildReportPath(root, "feedback-abc123");
        var second = BugReportStore.BuildReportPath(root, "feedback-abc123");
        var malicious = BugReportStore.BuildReportPath(root, "../../account/token");

        Assert.Equal(first, second);
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, first, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, malicious, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first, malicious);
        Assert.DoesNotContain("feedback-abc123", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", malicious, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", malicious, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(first));
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(malicious));
    }

    [Fact]
    public void FeedbackRequestIdentity_IsScopedByAuthenticatedSubmitterWithoutPersistingIdentity()
    {
        var first = FeedbackRequestIdentityFactory.Create("Alice", "session-a", "same-request-id");
        var reconnect = FeedbackRequestIdentityFactory.Create(" alice ", "session-b", "same-request-id");
        var otherAccount = FeedbackRequestIdentityFactory.Create("Bob", "session-a", "same-request-id");
        var anonymous = FeedbackRequestIdentityFactory.Create(null, "session-a", "same-request-id");
        var otherAnonymous = FeedbackRequestIdentityFactory.Create(null, "session-b", "same-request-id");

        Assert.Equal(first, reconnect);
        Assert.NotEqual(first, otherAccount);
        Assert.NotEqual(first, anonymous);
        Assert.NotEqual(anonymous, otherAnonymous);
        Assert.DoesNotContain("alice", first.SourceRequestId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("same-request-id", first.SourceRequestId, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", first.FeedbackId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("same-request-id", first.FeedbackId, StringComparison.Ordinal);
        Assert.Matches("^bug-report-[0-9a-f]{40}$", first.SourceRequestId);
        Assert.Matches("^feedback-[0-9a-f]{40}$", first.FeedbackId);
    }

    [Fact]
    public void DifferentSubmittersUsingSameClientRequest_CreateIndependentCasesAndFiles()
    {
        var root = Path.Combine(TestTempRoot(), $"feedback-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var operations = new OperationsCenterStore(Path.Combine(root, "operations.db"));
            operations.Initialize();
            var alice = FeedbackRequestIdentityFactory.Create("Alice", "session-a", "same-request-id");
            var bob = FeedbackRequestIdentityFactory.Create("Bob", "session-b", "same-request-id");

            OperationsCaseCreate Build(FeedbackRequestIdentity identity) => new(
                OperationsCaseSources.BugReport,
                "bug",
                "玩家 Bug 反馈",
                "相同文本不应导致跨提交者碰撞。",
                null,
                null,
                null,
                null,
                null,
                identity.FeedbackId,
                identity.SourceRequestId,
                [new OperationsCaseEvidenceInput("bug_report_evidence_v1", "{}")],
                "high");

            var aliceCase = operations.CreateCase(Build(alice));
            var bobCase = operations.CreateCase(Build(bob));

            Assert.NotEqual(aliceCase, bobCase);
            Assert.Equal(aliceCase, operations.CreateCase(Build(alice)));
            Assert.NotEqual(
                BugReportStore.BuildReportPath(root, alice.FeedbackId),
                BugReportStore.BuildReportPath(root, bob.FeedbackId));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(" replay_12345678 ", "replay_12345678")]
    [InlineData("12345678-1234-1234-1234-123456789abc", "12345678-1234-1234-1234-123456789abc")]
    public void ReplayId_IsNormalizedBeforePersistence(string submitted, string expected)
    {
        Assert.True(CloudReplayStore.TryNormalizeReplayId(submitted, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("../account/token")]
    [InlineData("replay id with spaces")]
    [InlineData("replay-id?token=secret")]
    public void UnsafeReplayId_IsRejectedBeforePersistence(string submitted)
        => Assert.False(CloudReplayStore.TryNormalizeReplayId(submitted, out _));

    [Fact]
    public async Task BugReportStore_IsAtomicIdempotentAndSizeBounded()
    {
        var root = Path.Combine(TestTempRoot(), $"feedback-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var report = new { schema = "grandumi.feedback.report.v1", description = "可安全持久化" };
            var writes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
                BugReportStore.SaveAtRoot(report, root, "feedback-idempotent-1", "bug"))));

            Assert.Single(writes.Distinct(StringComparer.OrdinalIgnoreCase));
            var file = Assert.Single(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories));
            Assert.Equal(writes[0], file);
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*", SearchOption.AllDirectories));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            Assert.Equal("grandumi.feedback.report.v1", document.RootElement.GetProperty("schema").GetString());
            Assert.Equal(file, BugReportStore.SaveAtRoot(report, root, "feedback-idempotent-1", "bug"));
            Assert.Equal(file, BugReportStore.SaveAtRoot(report, root, "feedback-idempotent-1", "suggestion"));

            Assert.Throws<InvalidDataException>(() => BugReportStore.SaveAtRoot(
                new { evidence = new string('x', BugReportStore.MaxReportBytes) },
                root,
                "feedback-too-large",
                "bug"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("紫/黑")]
    [InlineData("紫／黑")]
    [InlineData("紫色, 黑色")]
    [InlineData("紫/紫/黑/伪造色")]
    public void ServerColorParser_AcceptsAllDualColorDelimiters(string color)
    {
        var card = BuildCard("TEST-001", color);
        Assert.Equal(new[] { "紫", "黑" }, card.ColorList);
        Assert.True(card.SharesColorWith(BuildCard("TEST-002", "黑")));
        Assert.False(card.SharesColorWith(BuildCard("TEST-003", "红")));
    }

    [Fact]
    public async Task AuthorityEvidence_IsQueueOrderedRedactedAndIncludesReconnectSummary()
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var session0 = $"feedback-s0-{suffix}";
        var session1 = $"feedback-s1-{suffix}";
        var account0 = $"feedback-private-account-a-{suffix}";
        var account1 = $"feedback-private-account-b-{suffix}";
        var room = GameRoomManager.CreateRoom(
            session0, account0, BuildLegalDeck("OP15-001"),
            session1, account1, BuildLegalDeck("OP15-001"),
            p0First: true,
            broadcastInitialState: false,
            p0DisplayName: $"private-name-a-{suffix}",
            p1DisplayName: $"private-name-b-{suffix}");

        try
        {
            var empty = JsonSerializer.SerializeToElement(new { });
            var sensitiveCardId = Guid.NewGuid().ToString();
            GameRoomManager.HandleAction(session0, $"Unknown-OP15-001-{sensitiveCardId}", empty, "request-rejected-1");
            GameRoomManager.HandleAction(session0, "DebugRefreshDon", empty, "request-accepted-1");
            GameRoomManager.OnPlayerDisconnect(session0);
            var reboundSession = $"feedback-s0-rebound-{suffix}";
            Assert.True(GameRoomManager.TryReclaim(reboundSession, account0));

            var authority = await GameRoomManager.CaptureFeedbackEvidenceAsync(reboundSession);
            using var document = JsonDocument.Parse(authority.ToJsonString());
            var root = document.RootElement;
            var json = root.GetRawText();

            Assert.Equal("grandumi.feedback.authority.v1", root.GetProperty("schema").GetString());
            Assert.Equal("server_authoritative", root.GetProperty("trust").GetString());
            Assert.Equal("captured", root.GetProperty("captureStatus").GetString());
            Assert.Equal(room.RoomId, root.GetProperty("room").GetProperty("roomId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("room").GetProperty("rulesetId").GetString()));
            Assert.True(root.GetProperty("state").TryGetProperty("journalSequence", out _));
            Assert.True(root.TryGetProperty("clock", out _));

            var actions = root.GetProperty("recentActions").EnumerateArray().ToArray();
            Assert.Contains(actions, action => action.GetProperty("outcome").GetString() == "rejected"
                && action.GetProperty("requestId").GetString() == "request-rejected-1"
                && !string.IsNullOrWhiteSpace(action.GetProperty("reason").GetString()));
            Assert.Contains(actions, action => action.GetProperty("outcome").GetString() == "accepted"
                && action.GetProperty("requestId").GetString() == "request-accepted-1");

            var selfConnection = root.GetProperty("connection").GetProperty("self");
            Assert.Equal(2, selfConnection.GetProperty("generation").GetInt32());
            Assert.Equal(1, selfConnection.GetProperty("reconnectCount").GetInt32());
            Assert.Equal(1, selfConnection.GetProperty("disconnectCount").GetInt32());
            Assert.False(selfConnection.GetProperty("disconnected").GetBoolean());

            Assert.DoesNotContain(account0, json, StringComparison.Ordinal);
            Assert.DoesNotContain(account1, json, StringComparison.Ordinal);
            Assert.DoesNotContain(session0, json, StringComparison.Ordinal);
            Assert.DoesNotContain(session1, json, StringComparison.Ordinal);
            Assert.DoesNotContain("private-name", json, StringComparison.Ordinal);
            Assert.DoesNotContain("handCard", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OP15-", json, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveCardId, json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
        }
    }

    private static CardInfo BuildCard(string number, string color) => new()
    {
        Number = number,
        Name = number,
        Color = color,
        Kind = CardKind.Character,
        Property = "打",
    };

    private static string TestTempRoot()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "反馈证据测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        return Path.GetFullPath(configured);
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }
}
