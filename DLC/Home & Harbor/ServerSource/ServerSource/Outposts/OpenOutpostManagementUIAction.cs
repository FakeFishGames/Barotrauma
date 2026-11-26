using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class OpenOutpostManagementUIAction
{
    partial void OpenUIProjSpecific()
    {
        // In multiplayer, tell the client to open the UI
        if (GameMain.NetworkMember is { IsServer: true })
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.NewMessage("OpenOutpostManagementUIAction missing TargetTag, the management UI won't open for anyone!");
            }

            foreach (Entity? entity in ParentEvent.GetTargets(TargetTag))
            {
                if (entity is Character { IsRemotePlayer: true } character)
                {
                    if (GameMain.Server.ConnectedClients.Find(c => c.Character == character) is Client client)
                    {
                        GameNetwork.SendToClient(client, Plugin.NetworkHeaders.OpenOutpostManagementUI, new OpenOutpostManagementUIData());
                    }
                }
            }
        }
    }
}
