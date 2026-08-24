extern alias Server;

using FluentAssertions;
using Xunit;
using GameServer = Server::Barotrauma.Networking.GameServer;

namespace TestProject;

public class CharacterControlRequestTests
{
    [Theory]
    [InlineData(false, true, true, true, false, true, false, 1)]
    [InlineData(true, false, true, true, false, true, false, 2)]
    [InlineData(true, true, false, true, false, true, false, 3)]
    [InlineData(true, true, true, false, false, true, false, 4)]
    [InlineData(true, true, true, true, true, true, false, 5)]
    [InlineData(true, true, true, true, false, false, false, 6)]
    [InlineData(true, true, true, true, false, true, true, 7)]
    public void InvalidCharacterControlRequestsAreRejected(
        bool featureEnabled,
        bool gameStarted,
        bool isGameModeAllowed,
        bool senderInGame,
        bool senderIsSpectating,
        bool targetIsValid,
        bool targetIsControlled,
        int expectedResult)
    {
        GameServer.ValidateCharacterControlRequest(
                featureEnabled,
                gameStarted,
                isGameModeAllowed,
                senderInGame,
                senderIsSpectating,
                targetIsValid,
                targetIsControlled)
            .Should().Be((GameServer.CharacterControlRequestResult)expectedResult);
    }

    [Fact]
    public void ValidCharacterControlRequestIsAccepted()
    {
        GameServer.ValidateCharacterControlRequest(
                featureEnabled: true,
                gameStarted: true,
                isGameModeAllowed: true,
                senderInGame: true,
                senderIsSpectating: false,
                targetIsValid: true,
                targetIsControlled: false)
            .Should().Be(GameServer.CharacterControlRequestResult.Accepted);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CharacterControlTargetMustBeBotOrReservedForSender(bool targetIsBot, bool targetIsReservedForSender)
    {
        GameServer.IsCharacterControlTargetValid(targetIsBot, targetIsReservedForSender).Should().BeTrue();
    }

    [Fact]
    public void CharacterControlTargetCannotBeAnotherPlayersFormerCharacter()
    {
        GameServer.IsCharacterControlTargetValid(targetIsBot: false, targetIsReservedForSender: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, null, 12, true)]
    [InlineData(false, 12, 12, true)]
    [InlineData(false, 7, 12, false)]
    [InlineData(false, null, 12, false)]
    public void ControlledCharacterRestorationUsesIdentityOrStableIdentifier(
        bool sameCharacterInfo,
        int? characterIdentifier,
        int controlledCharacterIdentifier,
        bool expectedMatch)
    {
        GameServer.IsControlledCharacterMatch(sameCharacterInfo, characterIdentifier, controlledCharacterIdentifier)
            .Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, false)]
    public void CrewControlStateMustIgnoreDeadOrDiscardedCharacters(
        bool mainAlive,
        bool mainDiscarded,
        bool controlledDead,
        bool expectedActive)
    {
        var main = mainAlive ? new Server::Barotrauma.CharacterInfo(Server::Barotrauma.CharacterPrefab.HumanSpeciesName) : null;
        if (main != null)
        {
            main.Discarded = mainDiscarded;
            main.PermanentlyDead = !mainAlive;
        }

        var controlled = new Server::Barotrauma.CharacterInfo(Server::Barotrauma.CharacterPrefab.HumanSpeciesName);
        controlled.PermanentlyDead = controlledDead;

        GameServer.IsCrewControlStateActive(main, controlled).Should().Be(expectedActive);
    }
}