using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Net;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        private static UInt32 LastIdentifier = 0;

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
        const string SavePath = "Data/bannedplayers.txt";

        partial void InitProjectSpecific()
        {
            if (!File.Exists(SavePath)) { return; }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(SavePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to open the list of banned players in " + SavePath, e);
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
        }

        public void RemoveExpired()
        {
            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);
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
            var bannedPlayer = bannedPlayers.Find(bp => bp.AddressOrAccountId.TryGet(out AccountId id) && accountId.Equals(id));
            reason = bannedPlayer?.Reason ?? string.Empty;
            return bannedPlayer != null;
        }

        public void BanPlayer(string name, Endpoint endpoint, string reason, TimeSpan? duration)
            => BanPlayer(name, endpoint.Address, reason, duration);
        
        public void BanPlayer(string name, Either<Address, AccountId> addressOrAccountId, string reason, TimeSpan? duration)
        {
            var existingBan = bannedPlayers.Find(bp => bp.AddressOrAccountId == addressOrAccountId);
            if (existingBan != null)
            {
                if (!duration.HasValue) { return; }

                DebugConsole.Log("Set \"" + name + "\"'s ban duration to " + duration.Value);
                existingBan.ExpirationTime = DateTime.Now + duration.Value;
                Save();
                return;
            }

            System.Diagnostics.Debug.Assert(!name.Contains(','));

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

            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);

            List<string> lines = new List<string>();
            foreach (BannedPlayer banned in bannedPlayers)
            {
                string line = banned.Name;
                line += "," + (banned.AddressOrAccountId.ToString());
                line += "," + (banned.ExpirationTime.HasValue ? banned.ExpirationTime.Value.ToString() : "");
                if (!string.IsNullOrWhiteSpace(banned.Reason)) { line += "," + banned.Reason; }

                lines.Add(line);
            }

            try
            {
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the list of banned players to " + SavePath + " failed", e);
            }
        }

        public void ServerAdminWrite(IWriteMessage outMsg, Client c)
        {
            try
            {
                if (outMsg == null) { throw new ArgumentException("OutMsg was null"); }
                if (GameMain.Server == null) { throw new Exception("GameMain.Server was null"); }

                if (!c.HasPermission(ClientPermissions.Ban))
                {
                    outMsg.Write(false); outMsg.WritePadBits();
                    return;
                }

                outMsg.Write(true);
                outMsg.Write(c.Connection == GameMain.Server.OwnerConnection);

                outMsg.WritePadBits();
                outMsg.WriteVariableUInt32((UInt32)bannedPlayers.Count);
                for (int i = 0; i < bannedPlayers.Count; i++)
                {
                    BannedPlayer bannedPlayer = bannedPlayers[i];

                    outMsg.Write(bannedPlayer.Name);
                    outMsg.Write(bannedPlayer.UniqueIdentifier);
                    outMsg.Write(bannedPlayer.ExpirationTime != null);
                    outMsg.WritePadBits();
                    if (bannedPlayer.ExpirationTime != null)
                    {
                        double hoursFromNow = (bannedPlayer.ExpirationTime.Value - DateTime.Now).TotalHours;
                        outMsg.Write(hoursFromNow);
                    }

                    outMsg.Write(bannedPlayer.Reason ?? "");

                    if (c.Connection == GameMain.Server.OwnerConnection)
                    {
                        if (bannedPlayer.AddressOrAccountId.TryGet(out Address endpoint))
                        {
                            outMsg.Write(true); outMsg.WritePadBits();
                            outMsg.Write(endpoint.StringRepresentation);
                        }
                        else
                        {
                            outMsg.Write(false); outMsg.WritePadBits();
                            outMsg.Write(((SteamId)bannedPlayer.AddressOrAccountId).StringRepresentation);
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
                    BannedPlayer bannedPlayer = bannedPlayers.Find(p => p.UniqueIdentifier == id);
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
