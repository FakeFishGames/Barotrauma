using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public bool MirrorLevel
        {
            get;
            protected set;
        }

        private static bool IsOwner(Client client) => client != null && client.Connection == GameMain.Server.OwnerConnection;

        /// <summary>
        /// There is a client-side implementation of the method in <see cref="CampaignMode"/>
        /// </summary>
        public static bool AllowedToManageCampaign(Client client, ClientPermissions permissions)
        {
            //allow managing the campaign if the client has permissions, is the owner, or the only client in the server,
            //or if no-one has management permissions
            return
                client.HasPermission(permissions) ||
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                //allow managing if no-one with permissions is alive
                GameMain.Server.ConnectedClients.None(c => c.InGame && c.Character is { IsIncapacitated: false, IsDead: false } &&  (IsOwner(c) || c.HasPermission(permissions)));
        }

        public bool AllowedToManageWallets(Client client)
        {
            return
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                client.HasPermission(ClientPermissions.ManageMoney) ||
                IsOwner(client);
        }

        public override void ShowStartMessage()
        {
            foreach (Mission mission in Missions)
            {
                GameServer.Log($"{TextManager.Get("Mission")}: {mission.Name}", ServerLog.MessageType.ServerMessage);
                GameServer.Log(mission.Description.Value, ServerLog.MessageType.ServerMessage);
            }
        }
    }
}
