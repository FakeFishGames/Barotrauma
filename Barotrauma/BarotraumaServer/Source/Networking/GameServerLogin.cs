#if FALSE
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class UnauthenticatedClient
    {
        public readonly NetworkConnection Connection;
        public readonly ulong SteamID;
        public Facepunch.Steamworks.ServerAuth.Status? SteamAuthStatus = null;
        public readonly int Nonce;
        
        public int FailedAttempts;

        public float AuthTimer;
        
        public UnauthenticatedClient(NetworkConnection connection, int nonce, ulong steamID = 0)
        {
            Connection = connection;
            SteamID = steamID;
            Nonce = nonce;
            AuthTimer = 10.0f;
            FailedAttempts = 0;
        }
    }
    
    partial class GameServer : NetworkMember
    {
        private Int32 ownerKey = 0;

        List<UnauthenticatedClient> unauthenticatedClients = new List<UnauthenticatedClient>();

        private void ReadClientSteamAuthRequest(IReadMessage inc, NetworkConnection senderConnection, out ulong clientSteamID)
        {
            clientSteamID = 0;
            if (!Steam.SteamManager.USE_STEAM)
            {
                DebugConsole.Log("Received a Steam auth request from " + senderConnection.RemoteEndPoint + ". Steam authentication not required, handling auth normally.");
                //not using steam, handle auth normally
                HandleClientAuthRequest(senderConnection, 0);
                return;
            }
            
            if (senderConnection == OwnerConnection)
            {
                //the client is the owner of the server, no need for authentication
                //(it would fail with a "duplicate request" error anyway)
                HandleClientAuthRequest(senderConnection, 0);
                return;
            }

            clientSteamID = inc.ReadUInt64();
            int authTicketLength = inc.ReadInt32();
            byte[] authTicketData = new byte[authTicketLength];
            inc.ReadBytes(authTicketData, 0, authTicketLength);

            DebugConsole.Log("Received a Steam auth request");
            DebugConsole.Log("  Steam ID: "+ clientSteamID);
            DebugConsole.Log("  Auth ticket length: " + authTicketLength);
            DebugConsole.Log("  Auth ticket data: " + 
                ((authTicketData == null) ? "null" : ToolBox.LimitString(string.Concat(authTicketData.Select(b => b.ToString("X2"))), 16)));

            if (senderConnection != OwnerConnection && 
                serverSettings.BanList.IsBanned(senderConnection.RemoteEndPoint.Address, clientSteamID))
            {
                return;
            }
            ulong steamID = clientSteamID;
            if (unauthenticatedClients.Any(uc => uc.Connection == inc.SenderConnection))
            {
                var steamAuthedClient = unauthenticatedClients.Find(uc =>
                    uc.Connection == inc.SenderConnection &&
                    uc.SteamID == steamID &&
                    uc.SteamAuthStatus == Facepunch.Steamworks.ServerAuth.Status.OK);
                if (steamAuthedClient != null)
                {
                    DebugConsole.Log("Client already authenticated, sending AUTH_RESPONSE again...");
                    HandleClientAuthRequest(inc.SenderConnection, steamID);
                }
                DebugConsole.Log("Steam authentication already pending...");
                return;
            }

            if (authTicketData == null)
            {
                DebugConsole.Log("Invalid request");
                return;
            }

            unauthenticatedClients.RemoveAll(uc => uc.Connection == senderConnection);
            int nonce = CryptoRandom.Instance.Next();
            var unauthClient = new UnauthenticatedClient(senderConnection, nonce, clientSteamID)
            {
                AuthTimer = 20
            };
            unauthenticatedClients.Add(unauthClient);

            if (!Steam.SteamManager.StartAuthSession(authTicketData, clientSteamID))
            {
                unauthenticatedClients.Remove(unauthClient);
                if (GameMain.Config.RequireSteamAuthentication)
                {
                    unauthClient.Connection.Disconnect(DisconnectReason.SteamAuthenticationFailed.ToString());
                    Log("Disconnected unauthenticated client (Steam ID: " + steamID + "). Steam authentication failed.", ServerLog.MessageType.ServerMessage);
                }
                else
                {
                    DebugConsole.Log("Steam authentication failed, skipping to basic auth...");
                    HandleClientAuthRequest(senderConnection);
                    return;
                }
            }

            return;
        }

        public void OnAuthChange(ulong steamID, ulong ownerID, Facepunch.Steamworks.ServerAuth.Status status)
        {
            DebugConsole.Log("************ OnAuthChange");
            DebugConsole.Log("  Steam ID: " + steamID);
            DebugConsole.Log("  Owner ID: " + ownerID);
            DebugConsole.Log("  Status: " + status);
            
            UnauthenticatedClient unauthClient = unauthenticatedClients.Find(uc => uc.SteamID == ownerID);
            if (unauthClient != null)
            {
                unauthClient.SteamAuthStatus = status;
                switch (status)
                {
                    case Facepunch.Steamworks.ServerAuth.Status.OK:
                        ////steam authentication done, check password next
                        Log("Successfully authenticated client via Steam (Steam ID: " + steamID + ").", ServerLog.MessageType.ServerMessage);
                        HandleClientAuthRequest(unauthClient.Connection, unauthClient.SteamID);
                        break;
                    default:
                        unauthenticatedClients.Remove(unauthClient);
                        if (GameMain.Config.RequireSteamAuthentication)
                        {
                            Log("Disconnected unauthenticated client (Steam ID: " + steamID + "). Steam authentication failed, (" + status + ").", ServerLog.MessageType.ServerMessage);
                            unauthClient.Connection.Disconnect(DisconnectReason.SteamAuthenticationFailed.ToString() + "/ (" + status.ToString() + ")");
                        }
                        else
                        {
                            DebugConsole.Log("Steam authentication failed (" + status.ToString() + "), skipping to basic auth...");
                            HandleClientAuthRequest(unauthClient.Connection);
                            return;
                        }
                        break;
                }
                return;
            }
            else
            {
                DebugConsole.Log("    No unauthenticated clients found with the Steam ID " + steamID);
            }

            //kick connected client if status becomes invalid (e.g. VAC banned, not connected to steam)
            /*if (status != Facepunch.Steamworks.ServerAuth.Status.OK && GameMain.Config.RequireSteamAuthentication)
            {
                var connectedClient = connectedClients.Find(c => c.SteamID == ownerID);
                if (connectedClient != null)
                {
                    Log("Disconnecting client " + connectedClient.Name + " (Steam ID: " + steamID + "). Steam authentication no longer valid (" + status + ").", ServerLog.MessageType.ServerMessage);                    
                    KickClient(connectedClient, $"DisconnectMessage.SteamAuthNoLongerValid~[status]={status.ToString()}");
                }
            }*/
        }

        private bool IsServerOwner(IReadMessage inc, NetworkConnection senderConnection)
        {
            string address = senderConnection.RemoteEndPoint.Address.MapToIPv4().ToString();
            int incKey = inc.ReadInt32();

            if (ownerKey == 0)
            {
                return false; //ownership key has been destroyed or has never existed
            }
            if (address.ToString() != "127.0.0.1")
            {
                return false; //not localhost
            }

            if (incKey != ownerKey)
            {
                return false; //incorrect owner key, how did this even happen
            }
            return true;
        }
        
        private void HandleOwnership(IReadMessage inc, NetworkConnection senderConnection)
        {
            DebugConsole.Log("HandleOwnership (" + senderConnection.RemoteEndPoint.Address + ")");
            if (IsServerOwner(inc, senderConnection))
            {
                ownerKey = 0; //destroy owner key so nobody else can take ownership of the server
                OwnerConnection = senderConnection;
                DebugConsole.NewMessage("Successfully set up server owner", Color.Lime);
            }
        }

        private void HandleClientAuthRequest(NetworkConnection connection, ulong steamID = 0)
        {
            DebugConsole.Log("HandleClientAuthRequest (steamID " + steamID + ")");

            if (GameMain.Config.RequireSteamAuthentication && connection != OwnerConnection && steamID == 0)
            {
                DebugConsole.Log("Disconnecting " + connection.RemoteEndPoint + ", Steam authentication required.");
                connection.Disconnect(DisconnectReason.SteamAuthenticationRequired.ToString());
                return;
            }

            //client wants to know if server requires password
            if (ConnectedClients.Find(c => c.Connection == connection) != null)
            {
                //this client has already been authenticated
                return;
            }
            
            UnauthenticatedClient unauthClient = unauthenticatedClients.Find(uc => uc.Connection == connection);
            if (unauthClient == null)
            {
                DebugConsole.Log("Unauthed client, generating a nonce...");
                //new client, generate nonce and add to unauth queue
                if (ConnectedClients.Count >= serverSettings.MaxPlayers)
                {
                    //server is full, can't allow new connection
                    connection.Disconnect(DisconnectReason.ServerFull.ToString());
                    if (steamID > 0) { Steam.SteamManager.StopAuthSession(steamID); }
                    return;
                }

                int nonce = CryptoRandom.Instance.Next();
                unauthClient = new UnauthenticatedClient(connection, nonce, steamID);
                unauthenticatedClients.Add(unauthClient);
            }
            unauthClient.AuthTimer = 10.0f;
            //if the client is already in the queue, getting another unauth request means that our response was lost; resend
            IWriteMessage nonceMsg = new WriteOnlyMessage();
            nonceMsg.Write((byte)ServerPacketHeader.AUTH_RESPONSE);
            if (serverSettings.HasPassword && connection != OwnerConnection)
            {
                nonceMsg.Write(true); //true = password
                nonceMsg.Write((Int32)unauthClient.Nonce); //here's nonce, encrypt with this
            }
            else
            {
                nonceMsg.Write(false); //false = no password
            }
            CompressOutgoingMessage(nonceMsg);
            DebugConsole.Log("Sending auth response...");
            serverPeer.Send(nonceMsg, connection, DeliveryMethod.Unreliable);
        }

        private void ClientInitRequest(IReadMessage inc)
        {
            DebugConsole.Log("Received client init request");
            if (ConnectedClients.Find(c => c.Connection == inc.Sender) != null)
            {
                //this client was already authenticated
                //another init request means they didn't get any update packets yet
                DebugConsole.Log("Client already connected, ignoring...");
                return;
            }

            UnauthenticatedClient unauthClient = unauthenticatedClients.Find(uc => uc.Connection == inc.Sender);
            if (unauthClient == null)
            {
                //client did not ask for nonce first, can't authorize
                inc.Sender.Disconnect(DisconnectReason.AuthenticationRequired.ToString());
                if (unauthClient.SteamID > 0) { Steam.SteamManager.StopAuthSession(unauthClient.SteamID); }
                return;
            }

            if (serverSettings.HasPassword && inc.Sender != OwnerConnection)
            {
                //decrypt message and compare password
                string clPw = inc.ReadString();
                if (!serverSettings.IsPasswordCorrect(clPw, unauthClient.Nonce))
                {
                    unauthClient.FailedAttempts++;
                    if (unauthClient.FailedAttempts > 3)
                    {
                        //disconnect and ban after too many failed attempts
                        serverSettings.BanList.BanPlayer("Unnamed", unauthClient.Connection.RemoteEndPoint.Address, "DisconnectMessage.TooManyFailedLogins", duration: null);
                        DisconnectUnauthClient(inc, unauthClient, DisconnectReason.TooManyFailedLogins, "");

                        Log(inc.Sender.RemoteEndPoint.Address.ToString() + " has been banned from the server (too many wrong passwords)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(inc.Sender.RemoteEndPoint.Address.ToString() + " has been banned from the server (too many wrong passwords)", Color.Red);
                        return;
                    }
                    else
                    {
                        //not disconnecting the player here, because they'll still use the same connection and nonce if they try logging in again
                        IWriteMessage reject = new WriteOnlyMessage();
                        reject.Write((byte)ServerPacketHeader.AUTH_FAILURE);
                        reject.Write("Wrong password! You have " + Convert.ToString(4 - unauthClient.FailedAttempts) + " more attempts before you're banned from the server.");
                        Log(inc.Sender.RemoteEndPoint.Address.ToString() + " failed to join the server (incorrect password)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(inc.Sender.RemoteEndPoint.Address.ToString() + " failed to join the server (incorrect password)", Color.Red);
                        CompressOutgoingMessage(reject);
                        serverPeer.Send(reject, unauthClient.Connection, DeliveryMethod.Unreliable);
                        unauthClient.AuthTimer = 10.0f;
                        return;
                    }
                }
            }
            string clVersion = inc.ReadString();

            UInt16 contentPackageCount = inc.ReadUInt16();
            List<string> contentPackageNames = new List<string>();
            List<string> contentPackageHashes = new List<string>();
            for (int i = 0; i < contentPackageCount; i++)
            {
                string packageName = inc.ReadString();
                string packageHash = inc.ReadString();
                contentPackageNames.Add(packageName);
                contentPackageHashes.Add(packageHash);
                if (contentPackageCount == 0)
                {
                    DebugConsole.Log("Client is using content package " +
                        (packageName ?? "null") + " (" + (packageHash ?? "null" + ")"));
                }
            }

            if (contentPackageCount == 0)
            {
                DebugConsole.Log("Client did not list any content packages.");
            }

            string clName = Client.SanitizeName(inc.ReadString());
            if (string.IsNullOrWhiteSpace(clName))
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.NoName, "");

                Log(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " couldn't join the server (no name given)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(inc.SenderConnection.RemoteEndPoint.Address.ToString() + " couldn't join the server (no name given)", Color.Red);
                return;
            }

            bool? isCompatibleVersion = IsCompatible(clVersion, GameMain.Version.ToString());
            if (isCompatibleVersion.HasValue && !isCompatibleVersion.Value)
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.InvalidVersion,
                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version.ToString()}~[clientversion]={clVersion}");

                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", Color.Red);
                return;
            }
            
            //check if the client is missing any of the content packages the server requires
            List<ContentPackage> missingPackages = new List<ContentPackage>();
            foreach (ContentPackage contentPackage in GameMain.SelectedPackages)
            {
                if (!contentPackage.HasMultiplayerIncompatibleContent) continue;
                bool packageFound = false;
                for (int i = 0; i < contentPackageCount; i++)
                {
                    if (contentPackageNames[i] == contentPackage.Name && contentPackageHashes[i] == contentPackage.MD5hash.Hash)
                    {
                        packageFound = true;
                        break;
                    }
                }
                if (!packageFound) missingPackages.Add(contentPackage);
            }

            if (missingPackages.Count == 1)
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.MissingContentPackage, $"DisconnectMessage.MissingContentPackage~[missingcontentpackage]={GetPackageStr(missingPackages[0])}");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (missing content package " + GetPackageStr(missingPackages[0]) + ")", ServerLog.MessageType.Error);
                return;
            }
            else if (missingPackages.Count > 1)
            {
                List<string> packageStrs = new List<string>();
                missingPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.MissingContentPackage, $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (missing content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                return;
            }

            string GetPackageStr(ContentPackage contentPackage)
            {
                return "\"" + contentPackage.Name + "\" (hash " + contentPackage.MD5hash.ShortHash + ")";
            }

            //check if the client is using any contentpackages that are not compatible with the server
            List<Pair<string, string>> incompatiblePackages = new List<Pair<string, string>>();
            for (int i = 0; i < contentPackageNames.Count; i++)
            {
                if (!GameMain.Config.SelectedContentPackages.Any(cp => cp.Name == contentPackageNames[i] && cp.MD5hash.Hash == contentPackageHashes[i]))
                {
                    incompatiblePackages.Add(new Pair<string, string>(contentPackageNames[i], contentPackageHashes[i]));
                }
            }

            if (incompatiblePackages.Count == 1)
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.IncompatibleContentPackage, 
                    $"DisconnectMessage.IncompatibleContentPackage~[incompatiblecontentpackage]={GetPackageStr2(incompatiblePackages[0])}");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible content package " + GetPackageStr2(incompatiblePackages[0]) + ")", ServerLog.MessageType.Error);
                return;
            }
            else if (incompatiblePackages.Count > 1)
            {
                List<string> packageStrs = new List<string>();
                incompatiblePackages.ForEach(cp => packageStrs.Add(GetPackageStr2(cp)));
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.IncompatibleContentPackage, 
                    $"DisconnectMessage.IncompatibleContentPackages~[incompatiblecontentpackages]={string.Join(", ", packageStrs)}");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                return;
            }
            
            string GetPackageStr2(Pair<string, string> nameAndHash)
            {
                return "\"" + nameAndHash.First + "\" (hash " + Md5Hash.GetShortHash(nameAndHash.Second) + ")";
            }

            if (inc.SenderConnection != OwnerConnection && !serverSettings.Whitelist.IsWhiteListed(clName, inc.SenderConnection.RemoteEndPoint.Address))
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.NotOnWhitelist, "");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (not in whitelist)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (not in whitelist)", Color.Red);
                return;
            }
            if (!Client.IsValidName(clName, this))
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.InvalidName, "");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (invalid name)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (invalid name)", Color.Red);
                return;
            }
            if (inc.SenderConnection != OwnerConnection && Homoglyphs.Compare(clName.ToLower(), Name.ToLower()))
            {
                DisconnectUnauthClient(inc, unauthClient, DisconnectReason.NameTaken, "");
                Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name taken by the server)", ServerLog.MessageType.Error);
                DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name taken by the server)", Color.Red);
                return;
            }
            Client nameTaken = ConnectedClients.Find(c => Homoglyphs.Compare(c.Name.ToLower(), clName.ToLower()));
            if (nameTaken != null)
            {
                if (nameTaken.Connection.RemoteEndPoint.Address.ToString() == inc.SenderEndPoint.Address.ToString())
                {
                    //both name and IP address match, replace this player's connection
                    nameTaken.Connection.Disconnect(DisconnectReason.SessionTaken.ToString());
                    nameTaken.Connection = unauthClient.Connection;
                    nameTaken.InitClientSync(); //reinitialize sync ids because this is a new connection
                    unauthenticatedClients.Remove(unauthClient);
                    unauthClient = null;
                    return;
                }
                else
                {
                    //can't authorize this client
                    DisconnectUnauthClient(inc, unauthClient, DisconnectReason.NameTaken, "");
                    Log(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name already taken)", ServerLog.MessageType.Error);
                    DebugConsole.NewMessage(clName + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (name already taken)", Color.Red);
                    return;
                }
            }

            //new client
            Client newClient = new Client(clName, GetNewClientID());
            newClient.InitClientSync();
            newClient.Connection = unauthClient.Connection;
            newClient.SteamID = unauthClient.SteamID;
            unauthenticatedClients.Remove(unauthClient);
            unauthClient = null;
            ConnectedClients.Add(newClient);

            var previousPlayer = previousPlayers.Find(p => p.MatchesClient(newClient));
            if (previousPlayer != null)
            {
                foreach (Client c in previousPlayer.KickVoters)
                {
                    if (!connectedClients.Contains(c)) { continue; }
                    newClient.AddKickVote(c);
                }
            }

            LastClientListUpdateID++;

            if (newClient.Connection == OwnerConnection)
            {
                newClient.GivePermission(ClientPermissions.All);
                newClient.PermittedConsoleCommands.AddRange(DebugConsole.Commands);

                GameMain.Server.UpdateClientPermissions(newClient);
                GameMain.Server.SendConsoleMessage("Granted all permissions to " + newClient.Name + ".", newClient);
            }
            
            GameMain.Server.SendChatMessage($"ServerMessage.JoinedServer~[client]={clName}", ChatMessageType.Server, null);
            serverSettings.ServerDetailsChanged = true;

            if (previousPlayer != null && previousPlayer.Name != newClient.Name)
            {
                GameMain.Server.SendChatMessage($"ServerMessage.PreviousClientName~[client]={clName}~[previousname]={previousPlayer.Name}", ChatMessageType.Server, null);
                previousPlayer.Name = newClient.Name;
            }

            var savedPermissions = serverSettings.ClientPermissions.Find(cp => 
                cp.SteamID > 0 ? 
                cp.SteamID == newClient.SteamID :            
                newClient.IPMatches(cp.IP));

            if (savedPermissions != null)
            {
                newClient.SetPermissions(savedPermissions.Permissions, savedPermissions.PermittedCommands);
            }
            else
            {
                var defaultPerms = PermissionPreset.List.Find(p => p.Name == "None");
                if (defaultPerms != null)
                {
                    newClient.SetPermissions(defaultPerms.Permissions, defaultPerms.PermittedCommands);
                }
                else
                {
                    newClient.SetPermissions(ClientPermissions.None, new List<DebugConsole.Command>());
                }
            }
        }
                
        private void DisconnectUnauthClient(IReadMessage inc, UnauthenticatedClient unauthClient, DisconnectReason reason, string message)
        {
            inc.SenderConnection.Disconnect(reason.ToString() + "/ " + TextManager.GetServerMessage(message));
            if (unauthClient.SteamID > 0) { Steam.SteamManager.StopAuthSession(unauthClient.SteamID); }
            if (unauthClient != null)
            {
                unauthenticatedClients.Remove(unauthClient);
            }
        }
    }
}
#endif

//TODO: delete
