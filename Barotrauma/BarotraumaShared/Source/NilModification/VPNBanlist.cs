using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace Barotrauma
{
    class VPNBanEntry
    {
        public Byte[] LowRange;
        public Byte[] HighRange;
    }

    class VPNBanlist
    {
        const string LoadPath = "Data/VPNBlacklist.txt";

        private List<VPNBanEntry> Banlist;
        public Boolean CheckIP(Lidgren.Network.NetConnection address)
        {
            System.Net.IPAddress IpToCheck = IPAddress.Parse(address.RemoteEndPoint.Address.ToString());

            Byte[] IPBytes = IpToCheck.GetAddressBytes();
            try
            {
                if (Banlist.Count > 0)
                {
                    for (int i = 0; i < Banlist.Count; i++)
                    {
                        //Could be inside range
                        if (Banlist[i].LowRange[0] <= IPBytes[0] && Banlist[i].HighRange[0] >= IPBytes[0])
                        {
                            //Its inside of the range, thus this is a banned IP.
                            if (Banlist[i].LowRange[0] < IPBytes[0] && Banlist[i].HighRange[0] > IPBytes[0])
                            {
                                return true;
                            }
                        }
                        else
                        {
                            //Is outside range
                            continue;
                        }

                        //Could be inside range
                        if (Banlist[i].LowRange[1] <= IPBytes[1] && Banlist[i].HighRange[1] >= IPBytes[1])
                        {
                            //Its inside of the range, thus this is a banned IP.
                            if (Banlist[i].LowRange[1] < IPBytes[1] && Banlist[i].HighRange[1] > IPBytes[1])
                            {
                                return true;
                            }
                        }
                        else
                        {
                            //Is outside range
                            continue;
                        }

                        //Could be inside range
                        if (Banlist[i].LowRange[2] <= IPBytes[2] && Banlist[i].HighRange[2] >= IPBytes[2])
                        {
                            //Its inside of the range, thus this is a banned IP.
                            if (Banlist[i].LowRange[2] < IPBytes[2] && Banlist[i].HighRange[2] > IPBytes[2])
                            {
                                return true;
                            }
                        }
                        else
                        {
                            //Is outside range
                            continue;
                        }

                        //Could be inside range - if true Its inside of the range, thus this is a banned IP.
                        if (Banlist[i].LowRange[3] <= IPBytes[3] && Banlist[i].HighRange[3] >= IPBytes[3])
                        {
                            return true;
                        }
                        else
                        {
                            //Is outside range
                            continue;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                DebugConsole.NewMessage("Error occured in VPN Banlist: " + e.Message.ToString(),Microsoft.Xna.Framework.Color.Red);
                return false;
            }
            return false;
        }

        public IEnumerable<object> CheckVPNBan(Lidgren.Network.NetConnection address, string clname)
        {
            Boolean IsVPNBanned = false;
            try
            {
                IsVPNBanned = CheckIP(address);
            }
            catch
            {

            }

            if(IsVPNBanned && GameMain.NilMod.VPNBanKicksPlayer)
            {
                if (GameMain.NilMod.EnablePlayerLogSystem)
                {
                    NilMod.NilModPlayerLog.LogPlayer(address.RemoteEndPoint.Address.ToString(), "VPNBLACKLISTED: " + clname);
                }
                GameMain.Server.KickVPNClient(address, "The IP: " + address.RemoteEndPoint.Address.ToString() + " has been VPN Blacklisted.",clname);
            }
            else
            {
                if (GameMain.NilMod.EnablePlayerLogSystem)
                {
                    GameMain.Server.SendChatMessage(NilMod.NilModPlayerLog.ListPrevious(address.RemoteEndPoint.Address.ToString(), clname, false, false, true), Barotrauma.Networking.ChatMessageType.Server, null);
                    if(IsVPNBanned)
                    {
                        NilMod.NilModPlayerLog.LogPlayer(address.RemoteEndPoint.Address.ToString(), clname);
                        DebugConsole.NewMessage("VPN USER: " + NilMod.NilModPlayerLog.ListPrevious(address.RemoteEndPoint.Address.ToString(), clname, true, true, true) + " - Player not blocked.", Microsoft.Xna.Framework.Color.White);
                        Barotrauma.Networking.GameServer.Log("VPN USER: " + NilMod.NilModPlayerLog.ListPrevious(address.RemoteEndPoint.Address.ToString(), clname, true, true, true) + " - Player not blocked.", Barotrauma.Networking.ServerLog.MessageType.Connection);
                    }
                    else
                    {
                        DebugConsole.NewMessage(NilMod.NilModPlayerLog.ListPrevious(address.RemoteEndPoint.Address.ToString(), clname, true, true, true), Microsoft.Xna.Framework.Color.White);
                        Barotrauma.Networking.GameServer.Log(NilMod.NilModPlayerLog.ListPrevious(address.RemoteEndPoint.Address.ToString(), clname, true, true, true), Barotrauma.Networking.ServerLog.MessageType.Connection);
                        NilMod.NilModPlayerLog.LogPlayer(address.RemoteEndPoint.Address.ToString(), clname);
                    }
                }
                else
                {
                    DisconnectedCharacter ReconnectedClient = null;
                    KickedClient kickedclient = null;

                    if (GameMain.NilMod.DisconnectedCharacters.Count > 0)
                    {
                        ReconnectedClient = GameMain.NilMod.DisconnectedCharacters.Find(dc => dc.IPAddress == address.RemoteEndPoint.Address.ToString() && dc.clientname == clname);
                    }
                    if (GameMain.NilMod.KickedClients.Count > 0)
                    {
                        kickedclient = GameMain.NilMod.KickedClients.Find(dc => dc.IPAddress == address.RemoteEndPoint.Address.ToString() && dc.clientname == clname);
                    }

                    if (kickedclient != null)
                    {
                        GameMain.Server.SendChatMessage("Recently Kicked Player " + clname + " (" + kickedclient.clientname + ") has rejoined the server.", Barotrauma.Networking.ChatMessageType.Server, null);
                        DebugConsole.NewMessage("Recently Kicked Player " + clname + " (" + kickedclient.clientname + ") (" + address.RemoteEndPoint.Address.ToString() + ") has rejoined the server.", Microsoft.Xna.Framework.Color.White);
                        Barotrauma.Networking.GameServer.Log("Recently Kicked Player " + clname + " (" + kickedclient.clientname + ") (" + address.RemoteEndPoint.Address.ToString() + ") has rejoined the server.", Barotrauma.Networking.ServerLog.MessageType.Connection);

                        if (GameMain.NilMod.ClearKickStateNameOnRejoin)
                        {
                            GameMain.NilMod.KickedClients.Remove(kickedclient);
                        }
                        else
                        {
                            kickedclient.ExpireTimer += GameMain.NilMod.KickStateNameTimerIncreaseOnRejoin;
                            if (kickedclient.ExpireTimer > GameMain.NilMod.KickMaxStateNameTimer) kickedclient.ExpireTimer = GameMain.NilMod.KickMaxStateNameTimer;
                        }
                    }
                    else if (ReconnectedClient == null)
                    {
                        GameMain.Server.SendChatMessage(clname + " has joined the server.", Barotrauma.Networking.ChatMessageType.Server, null);
                        DebugConsole.NewMessage(clname + " (" + address.RemoteEndPoint.Address.ToString() + ") has joined the server.", Microsoft.Xna.Framework.Color.White);
                        Barotrauma.Networking.GameServer.Log(clname + " (" + address.RemoteEndPoint.Address.ToString() + ") has joined the server.", Barotrauma.Networking.ServerLog.MessageType.Connection);
                    }
                    else
                    {
                        GameMain.Server.SendChatMessage(clname + " has reconnected to the server.", Barotrauma.Networking.ChatMessageType.Server, null);
                        DebugConsole.NewMessage(clname + " (" + address.RemoteEndPoint.Address.ToString() + ") has reconnected to the server.", Microsoft.Xna.Framework.Color.White);
                        Barotrauma.Networking.GameServer.Log(clname + " (" + address.RemoteEndPoint.Address.ToString() + ") has reconnected to the server.", Barotrauma.Networking.ServerLog.MessageType.Connection);
                    }
                }
            }

            yield return Barotrauma.CoroutineStatus.Success;
        }

        public void LoadVPNBans()
        {
            Banlist = new List<VPNBanEntry>();

            if (File.Exists(LoadPath))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(LoadPath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to open the list of VPN Bans players in " + LoadPath, e);
                    return;
                }

                foreach (string line in lines)
                {
                    string[] separatedLine = line.Split(',');
                    if (separatedLine.Length < 3 || separatedLine.Length > 3) continue;

                    string IPLowRange = separatedLine[1].Trim();
                    string IPHighRange = separatedLine[2].Trim();

                    VPNBanEntry vpnban = new VPNBanEntry();
                    vpnban.LowRange = new Byte[4];
                    vpnban.HighRange = new Byte[4];

                    //Only load IP Addresses
                    try
                    {
                        vpnban.LowRange = IPAddress.Parse(IPLowRange).GetAddressBytes();
                        vpnban.HighRange = IPAddress.Parse(IPHighRange).GetAddressBytes();
                    }
                    catch
                    {
                        continue;
                    }
                    Banlist.Add(vpnban);
                }
            }
            else
            {
                if (GameMain.NilMod.EnableVPNBanlist)
                {
                    DebugConsole.NewMessage("Could not enable VPN Banlist - File: " + "LoadPath" + " does not exist.", Microsoft.Xna.Framework.Color.Red);
                    DebugConsole.NewMessage("Please create the file and fill it with: nametorecognizeit,LowerboundIP,UpperBoundIP", Microsoft.Xna.Framework.Color.Red);
                    DebugConsole.NewMessage("Server must be restarted to reload the VPNBanlist.", Microsoft.Xna.Framework.Color.Red);
                }
            }
        }
    }
}
