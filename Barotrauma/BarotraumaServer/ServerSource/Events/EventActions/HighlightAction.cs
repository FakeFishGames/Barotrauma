#nullable enable
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class HighlightAction : EventAction
{
    partial void SetHighlightProjSpecific(Entity entity, IEnumerable<Character>? targetCharacters)
    {
        if (entity is Item item && GameMain.Server != null)
        {
            IEnumerable<Client>? targetClients = null;
            if (targetCharacters != null)
            {
                targetClients = targetCharacters
                    .Select(c => GameMain.Server.ConnectedClients.FirstOrDefault(client => client.Character == c))
                    .Where(c => c != null)!;
            }
            //don't send an update if TargetCharacter is set and no target clients were found
            if (TargetCharacter.IsEmpty || (targetClients != null && targetClients.Any()))
            {
                GameMain.Server?.CreateEntityEvent(item, new Item.SetHighlightEventData(State, highlightColor, targetClients));
            }

        }
    }
}