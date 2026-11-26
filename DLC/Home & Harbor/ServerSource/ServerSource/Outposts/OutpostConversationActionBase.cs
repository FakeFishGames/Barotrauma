using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal abstract partial class OutpostConversationActionBase
{
    partial void ShowMessageProjSpecific(PersistentOutpostData outpostData, Location location, Character? speaker)
    {
        // In multiplayer, send the message to the client
        if (GameMain.NetworkMember is { IsServer: true })
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.NewMessage($"{GetType().Name} missing TargetTag, the message won't be shown to anyone!");
                return;
            }

            // List all recipients before building the message. BuildMessageAndApply may have side effects so we only
            // want to call it if someone will get the message.
            List<Client> recipients = [];
            foreach (Entity? entity in ParentEvent.GetTargets(TargetTag))
            {
                if (entity is Character { IsRemotePlayer: true } character &&
                    GameMain.Server.ConnectedClients.Find(c => c.Character == character) is Client client)
                {
                    recipients.Add(client);
                }
            }

            if (recipients.Count == 0)
            {
                DebugConsole.NewMessage($"{GetType().Name}: no connected recipients matched TargetTag \"{TargetTag}\", the message won't be shown to anyone! How did this even happen?");
                return;
            }

            ushort speakerId = speaker?.ID ?? Entity.NullEntityID;
            string messageText = BuildMessageAndApply(outpostData, location).Value;

            foreach (Client client in recipients)
            {
                GameNetwork.SendToClient(client, Plugin.NetworkHeaders.ShowOutpostMessage, new ShowOutpostMessageData(messageText ?? "NULL", speakerId));
            }
        }
    }
}
