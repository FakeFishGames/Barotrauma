using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Networking;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.IO;

namespace Barotrauma
{
    class PermissionGroup
    {
        public string Name;
        public Boolean CountConnectionPlayerSlot;
        public Boolean CountConnectionBonusSlot;
        public Boolean CountconnectionAdminSlot;
        public Boolean AnnounceJoin;
        public Boolean AllowReconnect;
        public Boolean KickImmunity;
        public Boolean BanImmunity;
        public Boolean UseServerBroadcast;
        public Boolean AccessDeathChatAlive;
        public Boolean AdminChannelReceive;
        public Boolean AdminChannelSend;
        public Boolean GlobalChat;

        public List<string> Commands;
    }

    class ClientPermission
    {
        public Client client;
        public string Name;
        public string IPAddress;
        public string Group;

        public Boolean AdminMode = false;

        public Dictionary<string,string> Overrides;
        //Client specific settings can go here
    }

    class NilModPermissions
    {
        const string PermissionsSavePath = "Data/NilModPermissions.xml";
        public Boolean HostAdminMode;

        public List<PermissionGroup> Groups;
        public PermissionGroup NoGroup;

        public List<ClientPermission> ConnectedPermissions;
        public Dictionary<String, String> ExistingCommands;

        public int PlayerSlots;
        public int AdminSlots;
        public int BonusSlots;
        public int GeneralPlayers;
        public int AdminPlayers;
        public int BonusPlayers;
        public Boolean MasterserverShowAllSlots;
        public Boolean MasterserverCountAllSlots;

        public void Load()
        {
            //Setup default commands
            ExistingCommands = new Dictionary<String, String>();
            //ExistingCommands.Add("ToggleAdmin", "ToggleAdmin cancels respawning and reconnecting code.");
            //ExistingCommands.Add("Listids", "Listids # sends a list of player targets as a numeric for cid; - # is page of players.");
            //ExistingCommands.Add("Summon", "Summon teleports a player to your character.");
            //ExistingCommands.Add("Teleportto", "Teleportto teleports your character to a player");


            XDocument doc = null;

            if (File.Exists(PermissionsSavePath))
            {
                doc = XMLExtensions.TryLoadXml(PermissionsSavePath);
            }
            //We have not actually started once yet, lets reset to current versions default instead without errors.
            else
            {
                //Save();
                doc = XMLExtensions.TryLoadXml(PermissionsSavePath);
            }

            if (doc == null)
            {
                DebugConsole.ThrowError("NilMod permissions file 'Data/NilModPermissions.xml' failed to load - Permissions system disabled until resolved.");
                //DebugConsole.ThrowError("If you cannot correct the issue above, deleting or renaming the XML and restarting or reloading in-server will generate a new one.");
                //GameMain.NilMod.EnableAdminSystem = false;
            }
            //Load the real data
            else
            {
                //Base Settings
                XElement PermissionSettings = doc.Root.Element("Settings");
                BonusSlots = PermissionSettings.GetAttributeInt("BonusSlots", 0);
                AdminSlots = PermissionSettings.GetAttributeInt("AdminSlots", 0);
                MasterserverShowAllSlots = PermissionSettings.GetAttributeBool("MasterserverShowAllSlots", false);
                MasterserverCountAllSlots = PermissionSettings.GetAttributeBool("MasterserverCountAllSlots", false);

                //NoGroup Permission loading
                XElement NoGroup = doc.Root.Element("NoGroup");
                if(NoGroup != null)
                {

                }

                XElement Groups = doc.Root.Element("Groups");

                XElement Users = doc.Root.Element("Users");
            }
        }

        public Boolean CheckPermission(Client Usertofind, string PermissionType)
        {
            ClientPermission User = ConnectedPermissions.Find(cp => cp.client == Usertofind);
            switch (PermissionType)
            {
                //Permission does not exist error - shouldnt ever happen tbh x D
                default:
                    DebugConsole.ThrowError("NILMOD ERROR: Permission " + PermissionType + " does not exist!");
                    break;
                case "CountConnectionBonusSlot":
                    break;
                case "CountConnectionAdminSlot":
                    break;
                case "CountConnectionPlayerSlot":
                    break;
                case "AnnounceJoin":
                    break;
                case "AllowReconnect":
                    break;
                case "KickImmunity":
                    break;
                case "BanImmunity":
                    break;
                case "UseServerBroadcast":
                    break;
                case "AccessDeathChatAlive":
                    break;
                case "AdminChannelReceive":
                    break;
                case "AdminChannelSend":
                    break;
                case "GlobalChat":
                    break;
            }

            return false;
        }

        public void ExecuteCommand()
        {

        }

        public ClientPermission SetPerms(string ipaddress = "", string name = "")
        {
            //Set connected client group permissions for everybody
            if(ipaddress == "")
            {
                //Skip if there isnt even a server running yet, nobody would be connected.
                if(GameMain.Server != null)
                {
                    //Go through all existing clients if there are any connected.
                    for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
                    {
                        ClientPermission newclientpermission = new ClientPermission();
                        newclientpermission.client = GameMain.Server.ConnectedClients[i];
                        newclientpermission.IPAddress = GameMain.Server.ConnectedClients[i].Connection.RemoteEndPoint.Address.ToString();
                    }
                }
                return null;
            }
            //Set client permissions for just the one user (Someone is joining, time to create one, but not yet add.
            else
            {
                ClientPermission newclientpermission = new ClientPermission();
                newclientpermission.Name = name;
                newclientpermission.IPAddress = ipaddress;

                return newclientpermission;
            }
        }

        public void CountPlayers()
        {

        }
    }
}
