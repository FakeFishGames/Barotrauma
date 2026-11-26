using Barotrauma;

namespace HomeAndHarbor;

public static class NPCConversationsCustom
{
    public static void AddRequiredConversationFlags(List<Identifier> flags)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || !Level.IsLoadedOutpost) { return; }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (outpostData.IsPlayerOwned)
        {
            //remove the generic "enteroutpost" flag, these lines aren't that suitable in the player outpost where we can assume the dwellers know you
            if (flags.Contains("EnterOutpost".ToIdentifier()))
            {
                flags.Remove("EnterOutpost".ToIdentifier());
                flags.Add("EnterPlayerOutpost".ToIdentifier());
            }
        }
    }

    public static void AddCharacterSpecificConversationFlags(Character character, List<Identifier> flags)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || !Level.IsLoadedOutpost) { return; }
        
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (outpostData.IsPlayerOwned)
        {
            //remove the generic "enteroutpost" flag, these lines aren't that suitable in the player outpost where we can assume the dwellers know you
            if (flags.Contains("EnterOutpost".ToIdentifier()))
            {
                flags.Remove("EnterOutpost".ToIdentifier());
                flags.Add("EnterPlayerOutpost".ToIdentifier());
            }
            flags.Add("PlayerOutpost".ToIdentifier());
            if (!character.IsOnPlayerTeam)
            {
                flags.Add("PlayerOutpostNPC".ToIdentifier());
            }
        }
        if (outpostData.ResourceManager is ResourceManager resourceManager)
        {
            foreach (var resourceStatusIndicator in resourceManager.GetActiveResourceStatusIndicators())
            {
                flags.Add($"OutpostStatus:{resourceStatusIndicator.Identifier}".ToIdentifier());
            }
        }
    }
    
}