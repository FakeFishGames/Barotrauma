using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    class LoggedPlayer
    {
        public string IP;
        public List<string> UniqueNames;

        public LoggedPlayer(string ip, List<string> names)
        {
            this.IP = ip;
            UniqueNames = names;
        }

        public override string ToString()
        {
            return IP.ToString();
        }
    }

    class PlayerLog
    {
        const string SavePath = "Data/loggedplayers.txt";

        public Boolean PlayerLogStateNames;
        public Boolean PlayerLogStateFirstJoinedNames;
        public Boolean PlayerLogStateLastJoinedNames;

        private List<LoggedPlayer> loggedPlayers;

        public void LogPlayer(string ip, string name)
        {
            Load();

            System.Diagnostics.Debug.Assert(!name.Contains(','));

            List<string> uniquenames;

            LoggedPlayer match = loggedPlayers.Find(bp => bp.IP == ip);

            if(match != null)
            {
                if (match.UniqueNames.Any(bp => bp == name.ToLowerInvariant())) return;

                uniquenames = match.UniqueNames;
                uniquenames.Add(name.ToLowerInvariant());
                //uniquenames.Sort();
                loggedPlayers.Remove(match);
            }
            else
            {
                uniquenames = new List<string>();
                uniquenames.Add(name.ToLowerInvariant());
            }
            loggedPlayers.Add(new LoggedPlayer(ip, uniquenames));
            //loggedPlayers.Sort();
            loggedPlayers = loggedPlayers.OrderBy(x => x.ToString()).ToList();
            Save();
        }

        public void Save()
        {
            GameServer.Log("Saving playerlog", ServerLog.MessageType.ServerMessage);

            List<string> lines = new List<string>();
            for (int i = 0; i < loggedPlayers.Count;i++)
            {
                string line = loggedPlayers[i].IP;

                for(int y = 0; y < loggedPlayers[i].UniqueNames.Count();y++)
                {
                    line += "," + loggedPlayers[i].UniqueNames[y];
                }

                lines.Add(line);
            }

            try
            {
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the list of logged players to " + SavePath + " failed", e);
            }
        }

        public void Load()
        {
            loggedPlayers = new List<LoggedPlayer>();

            if (File.Exists(SavePath))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to open the list of logged players in " + SavePath, e);
                    return;
                }

                foreach (string line in lines)
                {
                    string[] separatedLine = line.Split(',');
                    if (separatedLine.Length < 2) continue;

                    string ip = separatedLine[0];
                    List<string> names = new List<string>();

                    for (int i = 1; i < separatedLine.Count(); i++)
                    {
                        names.Add(separatedLine[i]);
                    }

                    loggedPlayers.Add(new LoggedPlayer(ip, names));
                }
            }
        }

        public void ReportSettings()
        {
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateNames = " + (PlayerLogStateNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateFirstJoinedNames = " + (PlayerLogStateFirstJoinedNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateLastJoinedNames = " + (PlayerLogStateLastJoinedNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
        }

        public string ListPrevious(string ip, string joinname, Boolean includeip, Boolean statenames, Boolean IsJoinMessage)
        {
            string returnmessage = "";

            LoggedPlayer match = loggedPlayers.Find(bp => bp.IP == ip);

            DisconnectedCharacter ReconnectedClient = null;

            KickedClient kickedclient = null;

            if (GameMain.NilMod.DisconnectedCharacters.Count > 0)
            {
                ReconnectedClient = GameMain.NilMod.DisconnectedCharacters.Find(dc => dc.IPAddress == ip && dc.clientname == joinname);
            }

            if (GameMain.NilMod.KickedClients.Count > 0)
            {
                kickedclient = GameMain.NilMod.KickedClients.Find(dc => dc.IPAddress == ip && dc.clientname == joinname);
            }

            if (kickedclient != null)
            {
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

            if (match == null && ReconnectedClient == null && kickedclient == null)
            {
                if (includeip)
                {
                    returnmessage = "New Player " + joinname + " (" + ip + ") has joined.";
                }
                else
                {
                    returnmessage = "New Player " + joinname + " has joined.";
                }
            }
            else
            {
                if (IsJoinMessage)
                {
                    if (includeip)
                    {
                        if (kickedclient != null)
                        {
                            returnmessage = "Kicked Player " + joinname + " (" + kickedclient.clientname + ") (" + ip + ") has rejoined";
                        }
                        else if (ReconnectedClient != null)
                        {
                            returnmessage = joinname + " (" + ip + ") has reconnected";
                        }
                        else
                        {
                            returnmessage = joinname + " (" + ip + ") has joined";
                        }
                    }
                    else
                    {
                        if (kickedclient != null)
                        {
                            returnmessage = "Kicked Player " + joinname + " (" + kickedclient.clientname + ") has rejoined";
                        }
                        else if (ReconnectedClient != null)
                        {
                            returnmessage = joinname + " has reconnected";
                        }
                        else
                        {
                            returnmessage = joinname + " has joined";
                        }
                    }
                }

                if (PlayerLogStateNames | statenames)
                {
                    if (!PlayerLogStateFirstJoinedNames && !PlayerLogStateLastJoinedNames) PlayerLogStateLastJoinedNames = true;
                    if (match.UniqueNames.Count() > 1)
                    {
                        int ListedNames = 0;
                        returnmessage += " - Previously (";
                        if (PlayerLogStateFirstJoinedNames)
                        {
                            //Loop from the start of the names, for the first recorded.
                            for (int i = 0; i < match.UniqueNames.Count() && ListedNames < (PlayerLogStateLastJoinedNames ? 2 : 4); i++)
                            {
                                if (match.UniqueNames[i] != joinname.ToLowerInvariant())
                                {
                                    if (ListedNames != 0) returnmessage += ", ";
                                    returnmessage += match.UniqueNames[i];
                                    ListedNames += 1;
                                }
                            }
                        }
                        if (PlayerLogStateLastJoinedNames)
                        {
                            //Loop from the end of the names, do not include the already declared by previous one.
                            for (int i = match.UniqueNames.Count() - 1; i > ListedNames && ListedNames <= 4; i--)
                            {
                                if (match.UniqueNames[i] != joinname.ToLowerInvariant())
                                {
                                    if (ListedNames != 0) returnmessage += ", ";
                                    returnmessage += match.UniqueNames[i];
                                    ListedNames += 1;
                                }
                            }
                        }
                        returnmessage += ")";
                    }
                }
                returnmessage += ".";
            }

            return returnmessage;
        }
    }
}
