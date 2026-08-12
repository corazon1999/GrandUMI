using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class CharacterDepartureDonTests
{
    [Fact]
    public void RemoveCharacter_RestsAllAttachedDon()
    {
        var player = CreatePlayer();
        var character = CreateCharacter("TEST-001");
        player.Characters.Add(character);
        var firstDon = AttachDon(player, character);
        var secondDon = AttachDon(player, character);

        Assert.True(player.Characters.Remove(character));

        Assert.Equal(DonState.Rest, firstDon.State);
        Assert.Null(firstDon.AttachedToCardId);
        Assert.Equal(DonState.Rest, secondDon.State);
        Assert.Null(secondDon.AttachedToCardId);
    }

    [Fact]
    public void RemoveAtCharacter_RestsAttachedDon()
    {
        var player = CreatePlayer();
        var character = CreateCharacter("TEST-001");
        player.Characters.Add(character);
        var don = AttachDon(player, character);

        player.Characters.RemoveAt(0);

        Assert.Equal(DonState.Rest, don.State);
        Assert.Null(don.AttachedToCardId);
    }

    [Fact]
    public void ClearCharacters_RestsAttachedDonForEveryDepartingCharacter()
    {
        var player = CreatePlayer();
        var firstCharacter = CreateCharacter("TEST-001");
        var secondCharacter = CreateCharacter("TEST-002");
        player.Characters.AddRange([firstCharacter, secondCharacter]);
        var firstDon = AttachDon(player, firstCharacter);
        var secondDon = AttachDon(player, secondCharacter);

        player.Characters.Clear();

        Assert.All(new[] { firstDon, secondDon }, don =>
        {
            Assert.Equal(DonState.Rest, don.State);
            Assert.Null(don.AttachedToCardId);
        });
    }

    private static PlayerState CreatePlayer() => new()
    {
        SessionId = "test-session",
        AccountName = "test-account",
        Leader = new CardInstance { Info = CreateCardInfo("TEST-L001", CardKind.Leader) },
    };

    private static CardInstance CreateCharacter(string number) => new()
    {
        Info = CreateCardInfo(number, CardKind.Character),
    };

    private static CardInfo CreateCardInfo(string number, CardKind kind) => new()
    {
        Number = number,
        Name = number,
        Color = "红",
        Kind = kind,
        Property = "斩",
    };

    private static DonCard AttachDon(PlayerState player, CardInstance character)
    {
        var don = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = character.Id,
        };
        player.CostArea.Add(don);
        return don;
    }
}
