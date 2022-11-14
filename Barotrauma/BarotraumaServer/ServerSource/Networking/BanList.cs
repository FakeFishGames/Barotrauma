#nullable enable
using System;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        private static UInt32 LastIdentifier = 0;

        public bool Expired => ExpirationTime is { } expirationTime && DateTime.Now > expirationTime;
        
        public BannedPlayer(
            string name, Either<Address, AccountId> addressOrAccountId, string reason, DateTime? expirationTime)
        {
            this.Name = name;
            this.AddressOrAccountId = addressOrAccountId;
            this.Reason = reason;
            this.ExpirationTime = expirationTime;
            this.UniqueIdentifier = LastIdentifier; LastIdentifier++;
        }
    }

    partial class BanList
    {
        private const string SavePath = "Data/bannedplayers.xml";
        private const string LegacySavePath = "Data/bannedplayers.txt";

        partial void InitProjectSpecific()
        {
            if (!File.Exists(SavePath))
            {
                LoadLegacyBanList();
            }
            else
            {
                LoadBanList();
            }
        }

        private void LoadLegacyBanList()
        {
            if (!File.Exists(LegacySavePath)) { return; }
            
            string[] lines;
            try
            {
                lines = File.ReadAllLines(LegacySavePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to open the list of banned players in {LegacySavePath}", e);
                return;
            }

            foreach (string line in lines)
            {
                string[] separatedLine = line.Split(',');
                if (separatedLine.Length < 2) { continue; }

                string name = separatedLine[0];
                string endpointStr = separatedLine[1];

                DateTime? expirationTime = null;
                if (separatedLine.Length > 2 && !string.IsNullOrEmpty(separatedLine[2]))
                {
                    if (DateTime.TryParse(separatedLine[2], out DateTime parsedTime))
                    {
                        expirationTime = parsedTime;
                    }
                    else
                    {
                        string error = $"Failed to parse the ban duration of \"{name}\" ({separatedLine[2]}) from the legacy ban list file (text file which has now been changed to XML). Considering the ban permanent.";
                        DebugConsole.ThrowError(error);
                        GameServer.AddPendingMessageToOwner(error, ChatMessageType.Error);
                    }
                }
                string reason = separatedLine.Length > 3 ? string.Join(",", separatedLine.Skip(3)) : "";

                if (expirationTime.HasValue && DateTime.Now > expirationTime.Value) { continue; }

                if (AccountId.Parse(endpointStr).TryUnwrap(out var accountId))
                {
                    bannedPlayers.Add(new BannedPlayer(name, accountId, reason, expirationTime));
                }
                else if (Address.Parse(endpointStr).TryUnwrap(out var address))
                {
                    bannedPlayers.Add(new BannedPlayer(name, address, reason, expirationTime));
                }
            }
            
            Save();
            File.Delete(LegacySavePath);
        }

        private void LoadBanList()
        {
            XDocument? doc = XMLExtensions.TryLoadXml(SavePath);
            
            if (doc?.Root is null) { return; }

            static Option<BannedPlayer> loadFromElement(XElement element)
            {
                var accountId = AccountId.Parse(element.GetAttributeString("accountid", ""));
                var address = Address.Parse(element.GetAttributeString("address", ""));

                var name = element.GetAttributeString("name", "")!;
                var reason = element.GetAttributeString("reason", "")!;
                DateTime? expirationTime = DateTime.FromBinary(unchecked((long)element.GetAttributeUInt64("expirationtime", 0)));
                
                if (expirationTime < DateTime.Now) { expirationTime = null; }
                
                if (accountId.IsNone() && address.IsNone()) { return Option<BannedPlayer>.None(); }

                Either<Address, AccountId> addressOrAccountId = accountId.TryUnwrap(out var accId)
                    ? (Either<Address, AccountId>)accId
                    : address.TryUnwrap(out var addr)
                        ? addr
                        : throw new InvalidCastException();
                
                return Option<BannedPlayer>.Some(new BannedPlayer(name, addressOrAccountId, reason, expirationTime));
            }
            
            bannedPlayers.AddRange(doc.Root.Elements().Select(loadFromElement)
                .OfType<Some<BannedPlayer>>().Select(o => o.Value));
        }
        
        private void RemoveExpired()
        {
            bannedPlayers.RemoveAll(bp => bp.Expired);
        }
        
        public bool IsBanned(Endpoint endpoint, out string reason)
            => IsBanned(endpoint.Address, out reason);
        
        public bool IsBanned(Address address, out string reason)
        {
            RemoveExpired();
            if (address.IsLocalHost) 
            {
                reason = string.Empty;
                return false; 
            }
            var bannedPlayer = bannedPlayers.Find(bp => bp.AddressOrAccountId.TryGet(out Address adr) && address.Equals(adr));
            reason = bannedPlayer?.Reason ?? string.Empty;
            return bannedPlayer != null;
        }

        public bool IsBanned(AccountId accountId, out string reason)
        {
            RemoveExpired();
            var bannedPlayer =
                bannedPlayers.Find(bp => bp.AddressOrAccountId.TryGet(out AccountId id) && accountId.Equals(id)) ??
                bannedPlayers.Find(bp => bp.AddressOrAccountId.TryGet(out Address adr) && adr is SteamP2PAddress steamAdr && steamAdr.SteamId.Equals(accountId));
            reason = bannedPlayer?.Reason ?? string.Empty;
            return bannedPlayer != null;
        }

        public void BanPlayer(string name, Endpoint endpoint, string reason, TimeSpan? duration)
            => BanPlayer(name, endpoint.Address, reason, duration);
        
        public void BanPlayer(string name, Either<Address, AccountId> addressOrAccountId, string reason, TimeSpan? duration)
        {
            if (addressOrAccountId.TryGet(out Address address) && address.IsLocalHost) { return; }
            
            var existingBan = bannedPlayers.Find(bp => bp.AddressOrAccountId == addressOrAccountId);
            if (existingBan != null) { bannedPlayers.Remove(existingBan); }

            string logMsg = "Banned " + name;
            if (!string.IsNullOrEmpty(reason)) { logMsg += ", reason: " + reason; }
            if (duration.HasValue) { logMsg += ", duration: " + duration.Value.ToString(); }

            DebugConsole.Log(logMsg);

            DateTime? expirationTime = null;
            if (duration.HasValue)
            {
                expirationTime = DateTime.Now + duration.Value;
            }

            bannedPlayers.Add(new BannedPlayer(name, addressOrAccountId, reason, expirationTime));
            Save();
        }

        public void UnbanPlayer(Endpoint endpoint)
            => UnbanPlayer(endpoint.Address);
        
        public void UnbanPlayer(Either<Address, AccountId> addressOrAccountId)
        {
            var player = bannedPlayers.Find(bp => bp.AddressOrAccountId == addressOrAccountId);
            if (player == null)
            {
                DebugConsole.Log("Could not unban endpoint \"" + addressOrAccountId + "\". Matching player not found.");
            }
            else
            {
                RemoveBan(player);
            }
        }

        private void RemoveBan(BannedPlayer banned)
        {
            DebugConsole.Log("Removing ban from " + banned.Name);
            GameServer.Log("Removing ban from " + banned.Name, ServerLog.MessageType.ServerMessage);

            bannedPlayers.Remove(banned);

            Save();
        }

        public void Save()
        {
            GameServer.Log("Saving banlist", ServerLog.MessageType.ServerMessage);
            
            GameMain.Server?.ServerSettings?.UpdateFlag(ServerSettings.NetFlags.Properties);

            RemoveExpired();

            static XElement saveToElement(BannedPlayer bannedPlayer)
            {
                XElement retVal = new XElement("ban");
                retVal.SetAttributeValue("name", bannedPlayer.Name);
                retVal.SetAttributeValue("reason", bannedPlayer.Reason);
                if (bannedPlayer.AddressOrAccountId.TryGet(out AccountId accountId))
                {
                    retVal.SetAttributeValue("accountid", accountId.StringRepresentation);
                }
                else if (bannedPlayer.AddressOrAccountId.TryGet(out Address address))
                {
                    retVal.SetAttributeValue("address", address.StringRepresentation);
                }
                if (bannedPlayer.ExpirationTime is { } expirationTime)
                {
                    retVal.SetAttributeValue("expirationtime", unchecked((ulong)expirationTime.ToBinary()));
                }

                return retVal;
            }

            XDocument doc = new XDocument(new XElement("bannedplayers"));
            bannedPlayers.Select(saveToElement).ForEach(doc.Root!.Add);
            doc.SaveSafe(SavePath);
        }

        public void ServerAdminWrite(IWriteMessage outMsg, Client c)
        {
            try
            {
                if (outMsg == null) { throw new ArgumentException("OutMsg was null"); }
                if (GameMain.Server == null) { throw new Exception("GameMain.Server was null"); }

                if (!c.HasPermission(ClientPermissions.Ban))
                {
                    outMsg.WriteBoolean(false); outMsg.WritePadBits();
                    return;
                }

                outMsg.WriteBoolean(true);
                outMsg.WriteBoolean(c.Connection == GameMain.Server.OwnerConnection);

                outMsg.WritePadBits();
                outMsg.WriteVariableUInt32((UInt32)bannedPlayers.Count);
                for (int i = 0; i < bannedPlayers.Count; i++)
                {
                    BannedPlayer bannedPlayer = bannedPlayers[i];

                    outMsg.WriteString(bannedPlayer.Name);
                    outMsg.WriteUInt32(bannedPlayer.UniqueIdentifier);
                    outMsg.WriteBoolean(bannedPlayer.ExpirationTime != null);
                    outMsg.WritePadBits();
                    if (bannedPlayer.ExpirationTime != null)
                    {
                        double hoursFromNow = (bannedPlayer.ExpirationTime.Value - DateTime.Now).TotalHours;
                        outMsg.WriteDouble(hoursFromNow);
                    }

                    outMsg.WriteString(bannedPlayer.Reason ?? "");

                    if (c.Connection == GameMain.Server.OwnerConnection)
                    {
                        if (bannedPlayer.AddressOrAccountId.TryGet(out Address endpoint))
                        {
                            outMsg.WriteBoolean(true); outMsg.WritePadBits();
                            outMsg.WriteString(endpoint.StringRepresentation);
                        }
                        else
                        {
                            outMsg.WriteBoolean(false); outMsg.WritePadBits();
                            outMsg.WriteString(((SteamId)bannedPlayer.AddressOrAccountId).StringRepresentation);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                string errorMsg = "Error while writing banlist. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Banlist.ServerAdminWrite", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                throw;
            }
        }

        public bool ServerAdminRead(IReadMessage incMsg, Client c)
        {
            if (!c.HasPermission(ClientPermissions.Ban))
            {
                UInt32 removeCount = incMsg.ReadVariableUInt32();
                incMsg.BitPosition += (int)removeCount * 4 * 8;
                return false;
            }
            else
            {
                UInt32 removeCount = incMsg.ReadVariableUInt32();
                for (int i = 0; i < removeCount; i++)
                {
                    UInt32 id = incMsg.ReadUInt32();
                    BannedPlayer? bannedPlayer = bannedPlayers.Find(p => p.UniqueIdentifier == id);
                    if (bannedPlayer != null)
                    {
                        GameServer.Log(GameServer.ClientLogName(c) + " unbanned " + bannedPlayer.Name + " (" + bannedPlayer.AddressOrAccountId + ")", ServerLog.MessageType.ConsoleUsage);
                        RemoveBan(bannedPlayer);
                    }
                }

                return removeCount > 0;
            }
        }
    }
}
