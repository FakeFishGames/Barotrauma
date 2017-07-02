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

        public int failedAttempts;

        public float AuthTimer;

        public UnauthenticatedClient(NetConnection connection, int nonce)
        {
            Connection = connection;
            Nonce = nonce;

            AuthTimer = 10.0f;

            failedAttempts = 0;
        }
    }
    
    partial class GameServer : NetworkMember, IPropertyObject
    {
        List<UnauthenticatedClient> unauthenticatedClients = new List<UnauthenticatedClient>();

        private void ClientAuthRequest(NetConnection conn)
        {
            //client wants to know if server requires password
            if (ConnectedClients.Find(c => c.Connection == conn) != null)
            {
                //this client has already been authenticated
                return;
            }
            
            UnauthenticatedClient unauthClient = unauthenticatedClients.Find(uc => uc.Connection == conn);
            if (unauthClient == null)
            {
                //new client, generate nonce and add to unauth queue
                if (ConnectedClients.Count >= MaxPlayers)
                {
                    //server is full, can't allow new connection
                    conn.Disconnect("Server full");
                    return;
                }

                int nonce = CryptoRandom.Instance.Next();
                unauthClient = new UnauthenticatedClient(conn, nonce);
                unauthenticatedClients.Add(unauthClient);
            }
            unauthClient.AuthTimer = 10.0f;
            //if the client is already in the queue, getting another unauth request means that our response was lost; resend
            NetOutgoingMessage nonceMsg = server.CreateMessage();
            nonceMsg.Write((byte)ServerPacketHeader.AUTH_RESPONSE);
            if (string.IsNullOrEmpty(password))
            {
                nonceMsg.Write(false); //false = no password
            }
            else
            {
                nonceMsg.Write(true); //true = password
                nonceMsg.Write((Int32)unauthClient.Nonce); //here's nonce, encrypt with this
            }
            server.SendMessage(nonceMsg, conn, NetDeliveryMethod.Unreliable);
        }

        private void ClientInitRequest(NetIncomingMessage inc)
        {
            if (ConnectedClients.Find(c => c.Connection == inc.SenderConnection) != null)
            {
                //this client was already authenticated
                //another init request means they didn't get any update packets yet
                return;
            }

            UnauthenticatedClient unauthClient = unauthenticatedClients.Find(uc => uc.Connection == inc.SenderConnection);
            if (unauthClient == null)
            {
                //client did not ask for nonce first, can't authorize
                inc.SenderConnection.Disconnect("Client did not properly request authentication.");
                return;
            }

            if (!string.IsNullOrEmpty(password))
            {
                //decrypt message and compare password
                string saltedPw = password;
                saltedPw = saltedPw + Convert.ToString(unauthClient.Nonce);
                saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));
                string clPw = inc.ReadString();
                if (clPw != saltedPw)
                {
                    unauthClient.failedAttempts++;
                    if (unauthClient.failedAttempts > 3)
                    {
                        //disconnect and ban after too many failed attempts
                        banList.BanPlayer("Unnamed", unauthClient.Connection.RemoteEndPoint.Address.ToString(), "Too many failed login attempts.");
                        DisconnectUnauthClient(inc, unauthClient, "Too many failed login attempts. You have been automatically banned from the server.");

                        Log(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " has been banned from the server (too many wrong passwords)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " has been banned from the server (too many wrong passwords)", Color.Red);
                        return;
                    }
                    else
                    {
                        //not disconnecting the player here, because they'll still use the same connection and nonce if they try logging in again
                        NetOutgoingMessage reject = server.CreateMessage();
                        reject.Write((byte)ServerPacketHeader.AUTH_FAILURE);
                        reject.Write("Wrong password! You have "+Convert.ToString(4-unauthClient.failedAttempts)+" more attempts before you're banned from the server.");
                        Log(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " failed to join the server (incorrect password)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " failed to join the server (incorrect password)", Color.Red);
                        server.SendMessage(reject, unauthClient.Connection, NetDeliveryMethod.Unreliable);
                        unauthClient.AuthTimer = 10.0f;
                        return;
                    }
                }
            }
            string clVersion = inc.ReadString();
            string clPackageName = inc.ReadString();
            string clPackageHash = inc.ReadString();

            string clName = Client.SanitizeName(inc.ReadString());
            if (string.IsNullOrWhiteSpace(clName))
            {
                DisconnectUnauthClient(inc, unauthClient, "You need a name.");

                Log(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " couldn't join the server (no name given)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " couldn't join the server (no name given)", Color.Red);
                return;
            }

            if (clVersion != GameMain.Version.ToString())
            {
                DisconnectUnauthClient(inc, unauthClient, "Version " + GameMain.Version + " required to connect to the server (Your version: " + clVersion + ")");
                
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong game version)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong game version)", Color.Red);
                return;
            }
            if (clPackageName != GameMain.SelectedPackage.Name)
            {
                DisconnectUnauthClient(inc, unauthClient, "Your content package (" + clPackageName + ") doesn't match the server's version (" + GameMain.SelectedPackage.Name + ")");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong content package name)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong content package name)", Color.Red);
                return;
            }
            if (clPackageHash != GameMain.SelectedPackage.MD5hash.Hash)
            {
                DisconnectUnauthClient(inc, unauthClient, "Your content package (MD5: " + clPackageHash + ") doesn't match the server's version (MD5: " + GameMain.SelectedPackage.MD5hash.Hash + ")");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong content package hash)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (wrong content package hash)", Color.Red);
                return;
            }
            
            if (!whitelist.IsWhiteListed(clName, inc.SenderConnection.RemoteEndPoint.Address.ToString()))
            {
                DisconnectUnauthClient(inc, unauthClient, "You're not in this server's whitelist.");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (not in whitelist)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (not in whitelist)", Color.Red);
                return;
            }
            if (!Client.IsValidName(clName))
            {
                DisconnectUnauthClient(inc, unauthClient, "Your name contains illegal symbols.");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (invalid name)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (invalid name)", Color.Red);
                return;
            }
            if (clName.ToLower() == Name.ToLower())
            {
                DisconnectUnauthClient(inc, unauthClient, "That name is taken.");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name taken by the server)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name taken by the server)", Color.Red);
                return;
            }
            Client nameTaken = ConnectedClients.Find(c => c.name.ToLower() == clName.ToLower());
            if (nameTaken != null)
            {
                if (nameTaken.Connection.RemoteEndPoint.Address.ToString() == inc.SenderEndPoint.Address.ToString())
                {
                    //both name and IP address match, replace this player's connection
                    nameTaken.Connection.Disconnect("Your session was taken by a new connection on the same IP address.");
                    nameTaken.Connection = unauthClient.Connection;
                    nameTaken.InitClientSync(); //reinitialize sync ids because this is a new connection
                    unauthenticatedClients.Remove(unauthClient);
                    unauthClient = null;
                    return;
                }
                else
                {
                    //can't authorize this client
                    DisconnectUnauthClient(inc, unauthClient, "That name is taken.");
                    Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name already taken)", ServerLog.MessageType.Error);
                    DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name already taken)", Color.Red);
                    return;
                }
            }

            //new client
            Client newClient = new Client(clName, GetNewClientID());
            newClient.InitClientSync();
            newClient.Connection = unauthClient.Connection;
            unauthenticatedClients.Remove(unauthClient);
            unauthClient = null;
            ConnectedClients.Add(newClient);

#if CLIENT
            GameMain.NetLobbyScreen.AddPlayer(newClient.name);
#endif
            GameMain.Server.SendChatMessage(clName + " has joined the server.", ChatMessageType.Server, null);

            var savedPermissions = clientPermissions.Find(cp => cp.IP == newClient.Connection.RemoteEndPoint.Address.ToString());
            if (savedPermissions != null)
            {
                newClient.SetPermissions(savedPermissions.Permissions);
            }
            else
            {
                newClient.SetPermissions(ClientPermissions.None);
            }
        }
                
        private void DisconnectUnauthClient(NetIncomingMessage inc, UnauthenticatedClient unauthClient, string reason)
        {
            inc.SenderConnection.Disconnect(reason);

            if (unauthClient != null)
            {
                unauthenticatedClients.Remove(unauthClient);
            }
        }
    }
}
