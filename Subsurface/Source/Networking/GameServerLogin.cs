using Barotrauma.Networking.ReliableMessages;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class UnauthenticatedClient
    {
        public NetConnection Connection;
        public int Nonce;

        public float AuthTimer;

        public UnauthenticatedClient(NetConnection connection, int nonce)
        {
            Connection = connection;
            Nonce = nonce;

            AuthTimer = 5.0f;
        }
    }

    partial class GameServer : NetworkMember, IPropertyObject
    {
        List<UnauthenticatedClient> unauthenticatedClients = new List<UnauthenticatedClient>();

        private void HandleConnectionApproval(NetIncomingMessage inc)
        {
            if ((PacketTypes)inc.ReadByte() != PacketTypes.Login) return;

            if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
            {
                inc.SenderConnection.Deny("You have been banned from the server");
                DebugConsole.NewMessage("Banned player tried to join the server", Color.Red);
                return;
            }

            if (connectedClients.Any(c => c.Connection == inc.SenderConnection))
            {
                inc.SenderConnection.Deny("Connection error - already joined");
                return;
            }

            int nonce = CryptoRandom.Instance.Next();
            var msg = server.CreateMessage();
            msg.Write(nonce);

            unauthenticatedClients.Add(new UnauthenticatedClient(inc.SenderConnection, nonce));

            inc.SenderConnection.Approve(msg);
        }

        private void CheckAuthentication(NetIncomingMessage inc)
        {
            var unauthenticatedClient = unauthenticatedClients.Find(uc => uc.Connection == inc.SenderConnection);
            if (unauthenticatedClient != null)
            {
                unauthenticatedClients.Remove(unauthenticatedClient);

                string saltedPw = password;
                saltedPw = saltedPw + Convert.ToString(unauthenticatedClient.Nonce);
                saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));
                NetEncryption algo = new NetXtea(server, saltedPw);
                inc.Decrypt(algo);

                string rdPw = inc.ReadString();
                if (rdPw != saltedPw)
                {
                    inc.SenderConnection.Disconnect("Wrong password!");
                    return;
                }
            }
            else
            {
                inc.SenderConnection.Disconnect("Authentication failed");
                return;
            }

            if (ConnectedClients.Count>=config.MaximumConnections)
            {
                inc.SenderConnection.Disconnect("Server full");
                return;
            }
            
            byte userID;
            string version = "", packageName = "", packageHash = "", name = "";
            try
            {
                userID = inc.ReadByte();
                version = inc.ReadString();
                packageName = inc.ReadString();
                packageHash = inc.ReadString();
                name = Client.SanitizeName(inc.ReadString());
            }
            catch
            {
                inc.SenderConnection.Disconnect("Connection error - server failed to read your ConnectionApproval message");
                DebugConsole.NewMessage("Connection error - server failed to read the ConnectionApproval message", Color.Red);
                return;
            }

            DebugConsole.NewMessage("New player has joined the server (" + name + ", " + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ")", Color.White);

#if !DEBUG
            if (string.IsNullOrWhiteSpace(name))
            {
                inc.SenderConnection.Disconnect("Invalid username");
                DebugConsole.NewMessage(name + " couldn't join the server (name empty)", Color.Red);
                return;
            }
            else if (!Client.IsValidName(name))
            {
                inc.SenderConnection.Disconnect("Username contains illegal symbols");
                DebugConsole.NewMessage(name + " couldn't join the server (username contains illegal symbols)", Color.Red);
                return;

            }
            else if (version != GameMain.Version.ToString())
            {
                inc.SenderConnection.Disconnect("Version " + GameMain.Version + " required to connect to the server (Your version: " + version + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong game version)", Color.Red);
                return;
            }
            else if (packageName != GameMain.SelectedPackage.Name)
            {
                inc.SenderConnection.Disconnect("Your content package (" + packageName + ") doesn't match the server's version (" + GameMain.SelectedPackage.Name + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong content package name)", Color.Red);
                return;
            }
            else if (packageHash != GameMain.SelectedPackage.MD5hash.Hash)
            {
                inc.SenderConnection.Disconnect("Your content package (MD5: " + packageHash + ") doesn't match the server's version (MD5: " + GameMain.SelectedPackage.MD5hash.Hash + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong content package hash)", Color.Red);
                return;
            }
            else if (connectedClients.Any(c => Homoglyphs.Compare(c.name.ToLower(),name.ToLower()) && c.Connection != inc.SenderConnection))
            {
                inc.SenderConnection.Disconnect("The name \"" + name + "\" is already in use. Please choose another name.");
                DebugConsole.NewMessage(name + " couldn't join the server (name already in use)", Color.Red);
                return;
            }

#endif
            if (!whitelist.IsWhiteListed(name, inc.SenderConnection.RemoteEndPoint.Address.ToString()))
            {
                inc.SenderConnection.Disconnect("You're not in this server's whitelist.");
                DebugConsole.NewMessage(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (not in whitelist)", Color.Red);
                return;
            }

            //existing user re-joining
            if (userID > 0)
            {
                Client existingClient = connectedClients.Find(c => 
                    c.ID == userID &&
                    c.Connection == inc.SenderConnection);

                if (existingClient == null)
                {
                    existingClient = disconnectedClients.Find(c =>
                        c.ID == userID &&
                        c.Connection == inc.SenderConnection);

                    if (existingClient != null)
                    {
                        disconnectedClients.Remove(existingClient);
                        connectedClients.Add(existingClient);

                        UpdateCrewFrame();
                    }
                }
                if (existingClient != null)
                {
                    existingClient.Connection = inc.SenderConnection;
                    existingClient.ReliableChannel = new ReliableChannel(server);
                    LogClientIn(inc);
                    return;
                }
            }

            userID = 1;
            while (connectedClients.Any(c => c.ID == userID))
            {
                userID++;
            }

            Client newClient = new Client(server, name, userID);
            newClient.Connection = inc.SenderConnection;
            newClient.version = version;

            var savedPermissions = clientPermissions.Find(cp => cp.IP == newClient.Connection.RemoteEndPoint.Address.ToString());
            if (savedPermissions != null)
            {
                newClient.SetPermissions(savedPermissions.Permissions);
            }
            else
            {
                newClient.SetPermissions(ClientPermissions.None);
            }

            connectedClients.Add(newClient);

            UpdateCrewFrame();

            LogClientIn(inc);

            refreshMasterTimer = DateTime.Now;
        }

        private void LogClientIn(NetIncomingMessage inc)
        {
            Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);

            if (sender == null) return;

            if (sender.version != GameMain.Version.ToString())
            {
                DisconnectClient(sender, sender.name + " was unable to connect to the server (nonmatching game version)",
                    "Version " + GameMain.Version + " required to connect to the server (Your version: " + sender.version + ")");
            }
            else if (connectedClients.Find(x => x.name == sender.name && x != sender) != null)
            {
                DisconnectClient(sender, sender.name + " was unable to connect to the server (name already in use)",
                    "The name \"" + sender.name + "\" is already in use. Please choose another name.");
            }
            else
            {
                //AssignJobs();

                GameMain.NetLobbyScreen.RemovePlayer(sender.name);
                GameMain.NetLobbyScreen.AddPlayer(sender.name);

                // Notify the client that they have logged in
                var outmsg = server.CreateMessage();

                outmsg.Write((byte)PacketTypes.LoggedIn);
                outmsg.Write(sender.ID);
                outmsg.Write((int)sender.Permissions);
                outmsg.Write(gameStarted);
                outmsg.Write(gameStarted && sender.Character != null && !sender.Character.IsDead);
                outmsg.Write(AllowSpectating);

                //notify the client about other clients already logged in
                outmsg.Write((byte)((characterInfo == null) ? connectedClients.Count - 1 : connectedClients.Count));
                foreach (Client c in connectedClients)
                {
                    if (c.Connection == inc.SenderConnection) continue;
                    outmsg.Write(c.name);
                    outmsg.Write(c.ID);
                }

                if (characterInfo != null)
                {
                    outmsg.Write(characterInfo.Name);
                    outmsg.Write((byte)0);
                }

                var subs = GameMain.NetLobbyScreen.GetSubList();
                outmsg.Write((byte)subs.Count);
                foreach (Submarine sub in subs)
                {
                    outmsg.Write(sub.Name);
                    outmsg.Write(sub.MD5Hash.Hash);
                }

                server.SendMessage(outmsg, inc.SenderConnection, NetDeliveryMethod.ReliableUnordered, 0);

                //notify other clients about the new client
                outmsg = server.CreateMessage();
                outmsg.Write((byte)PacketTypes.PlayerJoined);
                outmsg.Write(sender.name);
                outmsg.Write(sender.ID);

                //send the message to everyone except the client who just logged in
                SendMessage(outmsg, NetDeliveryMethod.ReliableUnordered, inc.SenderConnection);

                AddChatMessage(sender.name + " has joined the server", ChatMessageType.Server);
            }
        }
     

    }
}
