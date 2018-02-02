using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Networking;
using System.Xml.Linq;
using System.IO;

namespace Barotrauma
{
    //Class for storing, sending and receiving of Event-Specific Chat information to players and management of the Chat XML file
    class NilModEventChatter
    {
        const string ChatSavePath = "Data/NilModEventChatterSettings.xml";

        //Chat Configuration
        public Boolean ChatModServerJoin;
        public Boolean ChatTraitorReminder;
        public Boolean ChatNoneTraitorReminder;
        public Boolean ChatShuttleRespawn;
        public Boolean ChatShuttleLeaving500;
        public Boolean ChatShuttleLeaving400;
        public Boolean ChatShuttleLeaving300;
        public Boolean ChatShuttleLeaving200;
        public Boolean ChatShuttleLeaving130;
        public Boolean ChatShuttleLeaving100;
        public Boolean ChatShuttleLeaving030;
        public Boolean ChatShuttleLeaving015;
        public Boolean ChatShuttleLeavingKill;
        public Boolean ChatSubvsSub;
        public Boolean ChatSalvage;
        public Boolean ChatMonster;
        public Boolean ChatCargo;
        public Boolean ChatSandbox;
        public Boolean ChatVoteEnd;

        public List<String> NilModRules;
        public List<String> NilTraitorReminder;
        public List<String> NilNoneTraitorReminder;
        public List<String> NilShuttleRespawn;
        public List<String> NilShuttleLeaving500;
        public List<String> NilShuttleLeaving400;
        public List<String> NilShuttleLeaving300;
        public List<String> NilShuttleLeaving200;
        public List<String> NilShuttleLeaving130;
        public List<String> NilShuttleLeaving100;
        public List<String> NilShuttleLeaving030;
        public List<String> NilShuttleLeaving015;
        public List<String> NilShuttleLeavingKill;
        public List<String> NilSubvsSubCoalition;
        public List<String> NilSubvsSubRenegade;
        public List<String> NilSalvage;
        public List<String> NilMonster;
        public List<String> NilCargo;
        public List<String> NilSandbox;
        public List<String> NilVoteEnd;

        public void ReportSettings()
        {
            //Informational Chat Message Related Settings
            GameMain.Server.ServerLog.WriteLine("ChatModRules = " + (ChatModServerJoin ? "Enabled" : "Disabled") + "With " + NilModRules.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatTraitorReminder = " + (ChatTraitorReminder ? "Enabled" : "Disabled") + "With " + NilTraitorReminder.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatNoneTraitorReminder = " + (ChatNoneTraitorReminder ? "Enabled" : "Disabled") + "With " + NilNoneTraitorReminder.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleRespawn = " + (ChatShuttleRespawn ? "Enabled" : "Disabled") + "With " + NilShuttleRespawn.Count() + " Lines", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving500 = " + (ChatShuttleLeaving500 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving500.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving400 = " + (ChatShuttleLeaving400 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving400.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving300 = " + (ChatShuttleLeaving300 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving300.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving200 = " + (ChatShuttleLeaving200 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving200.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving130 = " + (ChatShuttleLeaving130 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving130.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving100 = " + (ChatShuttleLeaving100 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving100.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving030 = " + (ChatShuttleLeaving030 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving030.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeaving015 = " + (ChatShuttleLeaving015 ? "Enabled" : "Disabled") + "With " + NilShuttleLeaving015.Count() + " Lines", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("ChatShuttleLeavingKill = " + (ChatShuttleLeavingKill ? "Enabled" : "Disabled") + "With " + NilShuttleLeavingKill.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatSubvsSub = " + (ChatSubvsSub ? "Enabled" : "Disabled") + "With " + NilSubvsSubCoalition.Count() + " Coalition Lines + " + NilSubvsSubRenegade.Count() + " Renegade Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatSalvage = " + (ChatSalvage ? "Enabled" : "Disabled") + "With " + NilSalvage.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatMonster = " + (ChatMonster ? "Enabled" : "Disabled") + "With " + NilMonster.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatCargo = " + (ChatCargo ? "Enabled" : "Disabled") + "With " + NilCargo.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatSandbox = " + (ChatSandbox ? "Enabled" : "Disabled") + "With " + NilSandbox.Count() + " Lines", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatVoteEnd = " + (ChatVoteEnd ? "Enabled" : "Disabled") + "With " + NilVoteEnd.Count() + " Lines", ServerLog.MessageType.NilMod);
        }

        public void Load()
        {
            XDocument doc = null;

            if (File.Exists(ChatSavePath))
            {
                doc = XMLExtensions.TryLoadXml(ChatSavePath);
            }
            else
            {
                DebugConsole.ThrowError("NilModChatter config file \"" + ChatSavePath + "\" Does not exist, generating new XML");
                Save();
                doc = XMLExtensions.TryLoadXml(ChatSavePath);
            }
            if (doc == null)
            {
                DebugConsole.ThrowError("NilModChatter config file \"" + ChatSavePath + "\" failed to load.");
            }
            else
            {


                //Chatter Settings
                XElement NilModEventChatterSettings = doc.Root.Element("NilModEventChatterSettings");

                ChatModServerJoin = NilModEventChatterSettings.GetAttributeBool("ChatModServerJoin", false); //Implemented
                ChatTraitorReminder = NilModEventChatterSettings.GetAttributeBool("ChatTraitorReminder", false); //Implemented
                ChatNoneTraitorReminder = NilModEventChatterSettings.GetAttributeBool("ChatNoneTraitorReminder", false); //Implemented
                ChatShuttleRespawn = NilModEventChatterSettings.GetAttributeBool("ChatShuttleRespawn", false); //Implemented
                ChatShuttleLeaving500 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving500", false);
                ChatShuttleLeaving400 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving400", false);
                ChatShuttleLeaving300 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving300", false);
                ChatShuttleLeaving200 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving200", false);
                ChatShuttleLeaving130 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving130", false);
                ChatShuttleLeaving100 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving100", false);
                ChatShuttleLeaving030 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving030", false);
                ChatShuttleLeaving015 = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeaving015", false);
                ChatShuttleLeavingKill = NilModEventChatterSettings.GetAttributeBool("ChatShuttleLeavingKill", false);
                ChatSubvsSub = NilModEventChatterSettings.GetAttributeBool("ChatSubvsSub", false); //Implemented
                ChatSalvage = NilModEventChatterSettings.GetAttributeBool("ChatSalvage", false); //Implemented
                ChatMonster = NilModEventChatterSettings.GetAttributeBool("ChatMonster", false); //Implemented
                ChatCargo = NilModEventChatterSettings.GetAttributeBool("ChatCargo", false); //Implemented
                ChatSandbox = NilModEventChatterSettings.GetAttributeBool("ChatSandbox", false); //Implemented
                ChatVoteEnd = NilModEventChatterSettings.GetAttributeBool("ChatVoteEnd", false); //Implemented

                //Rules + Greeting Text On Lobby Join

                XElement NilModRulesdoc = doc.Root.Element("NilModServerJoin");
                NilModRules = new List<string>();

                if (NilModRulesdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilModRulesdoc.Elements())
                    {

                        NilModRules.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Traitor reminder on spawn

                XElement NilTraitorReminderdoc = doc.Root.Element("NilTraitorReminder");
                NilTraitorReminder = new List<string>();

                if (NilTraitorReminderdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilTraitorReminderdoc.Elements())
                    {

                        NilTraitorReminder.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Non-Traitor reminder on spawn

                XElement NilNoneTraitorReminderdoc = doc.Root.Element("NilNoneTraitorReminder");
                NilNoneTraitorReminder = new List<string>();

                if (NilNoneTraitorReminderdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilNoneTraitorReminderdoc.Elements())
                    {

                        NilNoneTraitorReminder.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for respawning players

                XElement NilShuttleRespawndoc = doc.Root.Element("NilShuttleRespawn");
                NilShuttleRespawn = new List<string>();

                if (NilShuttleRespawndoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleRespawndoc.Elements())
                    {

                        NilShuttleRespawn.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 5 minutes remaining

                XElement NilShuttleLeaving500doc = doc.Root.Element("NilShuttleLeaving500");
                NilShuttleLeaving500 = new List<string>();

                if (NilShuttleLeaving500doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving500doc.Elements())
                    {

                        NilShuttleLeaving500.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 4 minutes remaining

                XElement NilShuttleLeaving400doc = doc.Root.Element("NilShuttleLeaving400");
                NilShuttleLeaving400 = new List<string>();

                if (NilShuttleLeaving400doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving400doc.Elements())
                    {

                        NilShuttleLeaving400.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 3 minutes remaining

                XElement NilShuttleLeaving300doc = doc.Root.Element("NilShuttleLeaving300");
                NilShuttleLeaving300 = new List<string>();

                if (NilShuttleLeaving300doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving300doc.Elements())
                    {

                        NilShuttleLeaving300.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 2 minutes remaining

                XElement NilShuttleLeaving200doc = doc.Root.Element("NilShuttleLeaving200");
                NilShuttleLeaving200 = new List<string>();

                if (NilShuttleLeaving200doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving200doc.Elements())
                    {

                        NilShuttleLeaving200.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 1:30 minutes remaining

                XElement NilShuttleLeaving130doc = doc.Root.Element("NilShuttleLeaving130");
                NilShuttleLeaving130 = new List<string>();

                if (NilShuttleLeaving130doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving130doc.Elements())
                    {

                        NilShuttleLeaving130.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 1 minutes remaining

                XElement NilShuttleLeaving100doc = doc.Root.Element("NilShuttleLeaving100");
                NilShuttleLeaving100 = new List<string>();

                if (NilShuttleLeaving100doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving100doc.Elements())
                    {

                        NilShuttleLeaving100.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 30 seconds remaining

                XElement NilShuttleLeaving030doc = doc.Root.Element("NilShuttleLeaving030");
                NilShuttleLeaving030 = new List<string>();

                if (NilShuttleLeaving030doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving030doc.Elements())
                    {

                        NilShuttleLeaving030.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle has 15 seconds remaining

                XElement NilShuttleLeaving015doc = doc.Root.Element("NilShuttleLeaving015");
                NilShuttleLeaving015 = new List<string>();

                if (NilShuttleLeaving015doc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeaving015doc.Elements())
                    {

                        NilShuttleLeaving015.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for players when the shuttle is going to leave and kill its occupants

                XElement NilShuttleLeavingKilldoc = doc.Root.Element("NilShuttleLeavingKill");
                NilShuttleLeavingKill = new List<string>();

                if (NilShuttleLeavingKilldoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilShuttleLeavingKilldoc.Elements())
                    {

                        NilShuttleLeavingKill.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for sub vs sub - Coalition team spawns

                XElement NilSubvsSubCoalitiondoc = doc.Root.Element("NilSubvsSubCoalition");
                NilSubvsSubCoalition = new List<string>();

                if (NilSubvsSubCoalitiondoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilSubvsSubCoalitiondoc.Elements())
                    {

                        NilSubvsSubCoalition.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                //Text for sub vs sub - Renegade team spawns

                XElement NilSubvsSubRenegadedoc = doc.Root.Element("NilSubvsSubRenegade");
                NilSubvsSubRenegade = new List<string>();

                if (NilSubvsSubRenegadedoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilSubvsSubRenegadedoc.Elements())
                    {

                        NilSubvsSubRenegade.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                XElement NilSalvagedoc = doc.Root.Element("NilSalvage");
                NilSalvage = new List<string>();

                if (NilSalvagedoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilSalvagedoc.Elements())
                    {

                        NilSalvage.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                XElement NilMonsterdoc = doc.Root.Element("NilMonster");
                NilMonster = new List<string>();

                if (NilMonsterdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilMonsterdoc.Elements())
                    {

                        NilMonster.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                XElement NilCargodoc = doc.Root.Element("NilCargo");
                NilCargo = new List<string>();

                if (NilCargodoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilCargodoc.Elements())
                    {

                        NilCargo.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                XElement NilSandboxdoc = doc.Root.Element("NilSandbox");
                NilSandbox = new List<string>();

                if (NilSandboxdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilSandboxdoc.Elements())
                    {

                        NilSandbox.Add(subElement.GetAttributeString("Text", ""));
                    }
                }

                XElement NilVoteEnddoc = doc.Root.Element("NilVoteEnd");
                NilVoteEnd = new List<string>();

                if (NilVoteEnddoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in NilVoteEnddoc.Elements())
                    {

                        NilVoteEnd.Add(subElement.GetAttributeString("Text", ""));
                    }
                }
            }
        }

        public void Save()
        {
            List<string> lines = new List<string>
            {
                @"<?xml version=""1.0"" encoding=""utf-8"" ?>",

                "",

                "  <!--The chat information below can currently use the following TAGS:-->",
                "  <!--#SERVERNAME #CLIENTNAME #TRAITORTARGET #TRAITORNAME #MISSIONNAME #MISSIONDESC #REWARD #RADARLABEL #STARTLOCATION #ENDLOCATION-->",
                "  <!--Remember that these messages have a maximum size and you should write considering the per-line looks, as well as test for issues-->",
                "  <!--They are sent to specific clients and others do not get sent these messages but if there is enough messages to a single client their spam filter may or may not block them-->",

                "",

                "  <!--ChatModRules = Setting to enable per-client-sending of messages on server-join (configured at bottom of the xml), Default=false-->",
                "  <!--ChatTraitorReminder = Setting to per-client-sending of messages to specifically the traitor on initial spawn (configured at bottom of the xml), Default=false-->",
                "  <!--ChatNoneTraitorReminder = Setting to enable per-client-sending of messages to none-traitors on initial spawn (configured at bottom of the xml), Default=false-->",
                "  <!--ChatShuttleRespawn = Setting to enable per-client-sending of messages on shuttle respawn (configured at bottom of the xml), Default=false-->",
                "  <!--ChatShuttleLeavingKill = Setting to enable per-client-sending of messages if shuttle kills the player by leaving (configured at bottom of the xml), Default=false-->",
                "  <!--ChatSubvsSub = Setting to enable per-client-sending of the Coalition/Renegade text below (configured at bottom of the xml), Default=false-->",
                "  <!--ChatSalvage = Setting to enable per-client-sending of the text below (configured at bottom of the xml), Default=false-->",
                "  <!--ChatMonster = Setting to enable per-client-sending of the text below (configured at bottom of the xml), Default=false-->",
                "  <!--ChatCargo = Setting to enable per-client-sending of the text below (configured at bottom of the xml), Default=false-->",
                "  <!--ChatSandbox = Setting to enable per-client-sending per-player dialogue (configured at bottom of the xml), Default=false-->",
                "  <!--ChatVoteEnd = Setting to enable per-client-sending per-player dialogue (configured at bottom of the xml), Default=false-->",

                "",

                "<NilModEvents>",
                "  <NilModEventChatterSettings",
                @"    ChatModServerJoin=""false""",
                @"    ChatTraitorReminder=""false""",
                @"    ChatNoneTraitorReminder=""false""",
                @"    ChatShuttleRespawn=""false""",
                @"    ChatShuttleLeavingKill=""false""",
                @"    ChatSubvsSub=""false""",
                @"    ChatSalvage=""false""",
                @"    ChatMonster=""false""",
                @"    ChatCargo=""false""",
                @"    ChatSandbox=""false""",
                @"    ChatVoteEnd=""false""",
                "  />",

                "",

                "  <!--This is for the initial On server join messages to inform players of rules, welcome text or otherwise for your server!-->",
                "  <NilModServerJoin>",
                @"    <Line Text=""Welcome to #SERVERNAME! Feel free to visit our website for communication! - Please read the following:""/>",
                @"    <Line Text=""1.) Do your job - Security / Captains can stun/ cuff for unauthorised access or anyone may perform actions towards the mission goal.""/>",
                @"    <Line Text=""2.) No Random Murders - if you can cuff use it, if you can stun and disarm do it.""/>",
                @"    <Line Text=""3.) No Griefing -annoying everybody for your fun only is not really fun.""/>",
                @"    <Line Text=""4.) Server is heavilly modified to be very difficult!expect slight oddities as your client attempts to cope.""/>",
                "  </NilModServerJoin>",
                "  <!--This is the custom text a TRAITOR will see on spawn, it replaces the none-traitor round text.-->",
                "",
                @"  <NilTraitorReminder>",
                @"    <Line Text=""You have been handed a secret mission by your fellow Renegade forces!""/>",
                @"    <Line Text=""Your task is to Assassinate #TRAITORTARGET! Though take care in this important endeavour""/>",
                @"    <Line Text=""Take as few Coalition out as possible and make it back in one piece #TRAITORNAME, They must not find out your involvement.""/>",
                "  </NilTraitorReminder>",
                "  <!--This is the custom text a NONE TRAITOR will see on spawn, if it is set to MAYBE or YES (Regardless of traitors)-->",
                "  <NilNoneTraitorReminder>",
                @"    <Line Text=""The coalition have potential reports of renegade spies targeting key personnel!""/>",
                @"    <Line Text=""Although it is unknown if they have made it onboard or what their target may be...""/>",
                @"    <Line Text=""The coalition finds it unacceptable to let these scum have their way!""/>",
                @"    <Line Text=""Ensure the submarine reaches its objective and the traitor either hangs or fails.""/>",
                "  </NilNoneTraitorReminder>",
                "  <!--This is the text a player will see when respawning Via Shuttle-->",
                "  <NilShuttleRespawn>",
                @"    <Line Text=""The coalition have sent you useless meatbags as additional backup.""/>",
                @"    <Line Text=""Locate the submarine and use your provided supplies to aid its mission.""/>",
                @"    <Line Text=""You only have limited time to disembark the shuttle, we will be disappointed if you should fail us.""/>",
                "  </NilShuttleRespawn>",
                "  <!--This is the text a player will see if they have 5 minutes remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn500>",
                @"    <Line Text=""You have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn500>",
                "  <!--This is the text a player will see if they have 4 minutes remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn400>",
                @"    <Line Text=""You have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn400>",
                "  <!--This is the text a player will see if they have 3 minutes remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn300>",
                @"    <Line Text=""You have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn300>",
                "  <!--This is the text a player will see if they have 2 minutes remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn200>",
                @"    <Line Text=""You have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn200>",
                "  <!--This is the text a player will see if they have 1:30 minutes remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn130>",
                @"    <Line Text=""You have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn130>",
                "  <!--This is the text a player will see if they have 1 minute remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn100>",
                @"    <Line Text=""You only have #SHUTTLELEAVETIME to reach the main submarine and disembark!""/>",
                "  </NilShuttleLeavingWarn100>",
                "  <!--This is the text a player will see if they have 30 seconds remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn030>",
                @"    <Line Text=""You only have #SHUTTLELEAVETIME to reach the main submarine and disembark.""/>",
                "  </NilShuttleLeavingWarn030>",
                "  <!--This is the text a player will see if they have 15 seconds remaining inside the shuttle-->",
                "  <NilShuttleLeavingWarn015>",
                @"    <Line Text=""You only have #SHUTTLELEAVETIME to reach the main submarine and disembark!""/>",
                @"    <Line Text=""You must leave before the shuttle returns or we will throw you in the drink for insubordination!""/>",
                "  </NilShuttleLeavingWarn015>",
                "  <!--This is the text a player will see if they are killed by staying on a shuttle as it leaves-->",
                "  <NilShuttleLeavingKill>",
                @"    <Line Text=""Cowardess is not tolerated by the coalition #CLIENTNAME.""/>",
                @"    <Line Text=""You will be sent back into the drink, Fish food or otherwise...""/>",
                @"    <Line Text=""(Next time examine a shuttle for invisible suits, supplies and disembark before the timer ends!) ""/>",
                "  </NilShuttleLeavingKill>",
                "  <!--This is the text a player will see if its sub vs sub and they are on the Coalition team-->",
                "  <NilSubvsSubCoalition>",
                @"    <Line Text=""A renegade vessel has been located in the nearby area, Remove the subversive elements.""/>",
                @"    <Line Text=""Gear up and use sonar to find the Renegade sub, then shoot, board and do anything it takes.""/>",
                @"    <Line Text=""Failiure is not an option.""/>",
                "  </NilSubvsSubCoalition>",
                "  <!--This is the text a player will see if its sub vs sub and they are on the Renegade team-->",
                "  <NilSubvsSubRenegade>",
                @"    <Line Text=""A Nearby coalition sub has likely identified we are not with the coalition, dispose of them!""/>",
                @"    <Line Text=""Gear up and use sonar to find the Coalition sub, then shoot, board and do anything it takes.""/>",
                @"    <Line Text=""Failiure is not an option.""/>",
                "  </NilSubvsSubRenegade>",
                "  <!--This is the text a player will see on spawn if the mission is Salvage-->",
                "  <NilSalvage>",
                @"    <Line Text=""#CLIENTNAME! You have been employed by the coalition to embark from #STARTLOCATION and collect an artifact!""/>",
                @"    <Line Text=""Gear up into your diving suits and use a portable Sonar to locate the #RADARLABEL""/>",
                @"    <Line Text=""You will be compensated with #REWARD Credits to divy up amongst your fellow crewmates.""/>",
                @"    <Line Text=""Provided you successfully get our artifact to #ENDLOCATION without losing it.""/>",
                @"    <Line Text=""Some artifacts are very dangerous, Great care is to be taken depending on its type.""/>",
                "  </NilSalvage>",
                "  <!--This is the text a player will see on spawn if the mission is Monster-->",
                "  <NilMonster>",
                @"    <Line Text=""#CLIENTNAME! You have been employed by the coalition to embark from #STARTLOCATION for monster patrol!""/>",
                @"    <Line Text=""Prepare your submarine for combat and reach the designated target: #RADARLABEL""/>",
                @"    <Line Text=""You will be compensated with #REWARD Credits to divy up amongst your fellow crewmates.""/>",
                @"    <Line Text=""Provided you successfully survive the ordeal and actually reach #ENDLOCATION with the submarine intact""/>",
                @"    <Line Text=""The coalition is not in the business of losing submarines, It is unacceptable to return without it.""/>",
                "  </NilMonster>",
                "  <!--This is the text a player will see on spawn if the mission is Cargo-->",
                "  <NilCargo>",
                @"    <Line Text=""#CLIENTNAME! You have been employed by the coalition to embark from #STARTLOCATION for a Cargo run""/>",
                @"    <Line Text=""Simply reach #ENDLOCATION without losing the cargo.""/>",
                @"    <Line Text=""You will be compensated with #REWARD Credits to divy up amongst your fellow crewmates.""/>",
                @"    <Line Text=""Consider it an almost free meal and paycheck for this simple work.""/>",
                "  </NilCargo>",
                "  <!--This is the text a player will see on spawn if the Gamemode is Sandbox-->",
                "  <NilSandbox>",
                @"    <Line Text=""#CLIENTNAME! Welcome to sandbox mode.""/>",
                @"    <Line Text=""No Goals, No paychecks, no respawning fishies im afraid(They spawn once per level generation)""/>",
                @"    <Line Text=""When your bored of this feel free to hit the vote end at the top right""/>",
                @"    <Line Text=""Simply reach #ENDLOCATION alive.""/>",
                "  </NilSandbox>",
                "  <!--Text for players voting end round-->",
                "  <NilVoteEnd>",
                @"    <Line Text=""#CLIENTNAME you and your crew are dishonerable cowards! x:""/>",
                "  </NilVoteEnd>",
                "</NilModEvents>"
            };
            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(ChatSavePath, false, Encoding.UTF8))
            {
                foreach (string line in lines)
                {
                    file.WriteLine(line);
                }
            }
        }

        //Code related stuff for sending messages
        public void SendServerMessage(string MessageToSend, Client clientreceiver)
        {
            string RefinedMessage = MessageToSend.Trim();

            if (RefinedMessage.Contains("#"))
            {
                if (RefinedMessage.Contains("#SERVERNAME"))
                {
                    RefinedMessage = RefinedMessage.Replace("#SERVERNAME", GameMain.Server.Name);
                }
                if (RefinedMessage.Contains("#CLIENTNAME"))
                {
                    if (clientreceiver != null)
                    {
                        RefinedMessage = RefinedMessage.Replace("#CLIENTNAME", clientreceiver.Name);
                    }
                    else
                    {
                        if (GameMain.Server.CharacterInfo != null)
                        {
                            RefinedMessage = RefinedMessage.Replace("#CLIENTNAME", GameMain.Server.CharacterInfo.Name);
                        }
                        else
                        {
                            RefinedMessage = RefinedMessage.Replace("#CLIENTNAME", "NA");
                        }
                    }
                }
                if (RefinedMessage.Contains("#TRAITORTARGET"))
                {
                    if (GameMain.NilMod.TraitorTarget == "")
                    {
                        if (GameMain.Server.ConnectedClients.Count() > 0)
                        {
                            RefinedMessage = RefinedMessage.Replace("#TRAITORTARGET", GameMain.Server.ConnectedClients[Rand.Int(GameMain.Server.ConnectedClients.Count() - 1)].Name);
                        }
                        else
                        {
                            RefinedMessage = RefinedMessage.Replace("#TRAITORTARGET", "Nobody");
                        }
                    }
                    else
                    {
                        RefinedMessage = RefinedMessage.Replace("#TRAITORTARGET", GameMain.NilMod.TraitorTarget);
                    }

                }
                if (RefinedMessage.Contains("#TRAITORNAME"))
                {
                    RefinedMessage = RefinedMessage.Replace("#TRAITORNAME", GameMain.NilMod.Traitor);
                }
                if (RefinedMessage.Contains("#SHUTTLELEAVETIME"))
                {
                    RefinedMessage = RefinedMessage.Replace("#SHUTTLELEAVETIME", ToolBox.SecondsToReadableTime(GameMain.Server.respawnManager.TransportTimer));
                }
                if (RefinedMessage.Contains("#MISSIONNAME"))
                {
                    RefinedMessage = RefinedMessage.Replace("#MISSIONNAME", GameMain.GameSession.Mission.Name);
                }
                if (RefinedMessage.Contains("#MISSIONDESC"))
                {
                    RefinedMessage = RefinedMessage.Replace("#MISSIONDESC", GameMain.GameSession.Mission.Description);
                }
                if (RefinedMessage.Contains("#REWARD"))
                {
                    RefinedMessage = RefinedMessage.Replace("#REWARD", GameMain.GameSession.Mission.Reward.ToString());
                }
                if (RefinedMessage.Contains("#RADARLABEL"))
                {
                    RefinedMessage = RefinedMessage.Replace("#RADARLABEL", GameMain.GameSession.Mission.RadarLabel);
                }
                if (RefinedMessage.Contains("#STARTLOCATION"))
                {
                    RefinedMessage = RefinedMessage.Replace("#STARTLOCATION", GameMain.GameSession.StartLocation.Name);
                }
                if (RefinedMessage.Contains("#ENDLOCATION"))
                {
                    RefinedMessage = RefinedMessage.Replace("#ENDLOCATION", GameMain.GameSession.EndLocation.Name);
                }
            }



            if (clientreceiver != null)
            {
                var chatMsg = ChatMessage.Create(
                null,
                RefinedMessage,
                (ChatMessageType)ChatMessageType.Server,
                null);

                GameMain.Server.SendChatMessage(chatMsg, clientreceiver);
            }
            else
            {
                //Local Host Chat code here
                if (Character.Controlled != null)
                {
                    GameMain.NetworkMember.AddChatMessage(RefinedMessage, ChatMessageType.Server);
                }
            }
        }

        public void RoundStartClientMessages(Client receivingclient)
        {
            //Barotrauma.MonsterMission
            //Barotrauma.CombatMission
            //Barotrauma.CargoMission
            //Barotrauma.SalvageMission

            //An Actual Mission
            if (GameMain.GameSession.Mission != null)
            {
                //GameMain.NilMod.SendServerMessage("This Missions name is: " + GameMain.GameSession.Mission.ToString(), receivingclient);

                //Combat Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.CombatMission")
                {
                    if (receivingclient.TeamID == 1)
                    {
                        if (NilMod.NilModEventChatter.NilSubvsSubCoalition.Count() > 0 && NilMod.NilModEventChatter.ChatSubvsSub == true)
                        {
                            foreach (string message in NilMod.NilModEventChatter.NilSubvsSubCoalition)
                            {
                                NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                            }
                        }
                    }
                    if (receivingclient.TeamID == 2)
                    {
                        if (NilMod.NilModEventChatter.NilSubvsSubRenegade.Count() > 0 && NilMod.NilModEventChatter.ChatSubvsSub == true)
                        {
                            foreach (string message in NilMod.NilModEventChatter.NilSubvsSubRenegade)
                            {
                                NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                            }
                        }
                    }
                }
                //Monster Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.MonsterMission")
                {
                    if (NilMod.NilModEventChatter.NilMonster.Count() > 0 && NilMod.NilModEventChatter.ChatMonster == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilMonster)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                        }
                    }
                }
                //Salvage Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.SalvageMission")
                {
                    if (NilMod.NilModEventChatter.NilSalvage.Count() > 0 && NilMod.NilModEventChatter.ChatSalvage == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilSalvage)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                        }
                    }
                }
                //Cargo Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.CargoMission")
                {
                    if (NilMod.NilModEventChatter.NilCargo.Count() > 0 && NilMod.NilModEventChatter.ChatCargo == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilCargo)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                        }
                    }
                }

            }
            //Sandbox Mode
            else
            {
                if (NilMod.NilModEventChatter.NilSandbox.Count() > 0 && NilMod.NilModEventChatter.ChatSandbox == true)
                {
                    foreach (string message in NilMod.NilModEventChatter.NilSandbox)
                    {
                        NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                    }
                }
            }


            //Traitor Reminder Code

            if (GameMain.Server.TraitorsEnabled == YesNoMaybe.Yes | GameMain.Server.TraitorsEnabled == YesNoMaybe.Maybe)
            {
                if (receivingclient.Name == GameMain.NilMod.Traitor)
                {
                    if (NilMod.NilModEventChatter.NilTraitorReminder.Count() > 0 && NilMod.NilModEventChatter.ChatTraitorReminder == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilTraitorReminder)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                        }
                    }
                }
                else
                {
                    if (NilMod.NilModEventChatter.NilNoneTraitorReminder.Count() > 0 && NilMod.NilModEventChatter.ChatNoneTraitorReminder == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilNoneTraitorReminder)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, receivingclient);
                        }
                    }
                }
            }
        }

        public void SendRespawnLeavingWarning(float timeremaining)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character != null)
                {
                    if (client.Character.Submarine == GameMain.Server.respawnManager.respawnShuttle && client.Character.Enabled)
                    {
                        switch (timeremaining)
                        {
                            case 15f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving015.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving015)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 30f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving030.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving030)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 60f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving100.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving100)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 90f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving130.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving130)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 120f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving200.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving200)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 180f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving300.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving300)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 240f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving400.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving400)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                            case 300f:
                                if (NilMod.NilModEventChatter.NilShuttleLeaving500.Count() > 0)
                                {
                                    foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving500)
                                    {
                                        SendServerMessage(message, client);
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            //Hosts character code
            if (Character.Controlled != null)
            {
                if (Character.Controlled.Submarine == GameMain.Server.respawnManager.respawnShuttle && Character.Controlled.Enabled)
                {
                    switch (timeremaining)
                    {
                        case 15f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving015.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving015)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 30f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving030.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving030)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 60f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving100.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving100)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 90f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving130.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving130)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 120f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving200.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving200)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 180f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving300.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving300)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 240f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving400.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving400)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                        case 300f:
                            if (NilMod.NilModEventChatter.NilShuttleLeaving500.Count() > 0)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilShuttleLeaving500)
                                {
                                    SendServerMessage(message, null);
                                }
                            }
                            break;
                    }
                }
            }
        }

        public void SendHostMessages()
        {

            //Barotrauma.MonsterMission
            //Barotrauma.CombatMission
            //Barotrauma.CargoMission
            //Barotrauma.SalvageMission

            //An Actual Mission
            if (GameMain.GameSession.Mission != null)
            {
                //GameMain.NilMod.SendServerMessage("This Missions name is: " + GameMain.GameSession.Mission.ToString(), receivingclient);

                //Combat Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.CombatMission")
                {
                    if (GameMain.Server.CharacterInfo != null)
                    {
                        if (GameMain.Server.CharacterInfo.Character.TeamID == 1)
                        {
                            if (NilMod.NilModEventChatter.NilSubvsSubCoalition.Count() > 0 && NilMod.NilModEventChatter.ChatSubvsSub == true)
                            {
                                foreach (string message in NilMod.NilModEventChatter.NilSubvsSubCoalition)
                                {
                                    NilMod.NilModEventChatter.SendServerMessage(message, null);
                                }
                            }
                        }
                    }
                }
                //Monster Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.MonsterMission")
                {
                    if (NilMod.NilModEventChatter.NilMonster.Count() > 0 && NilMod.NilModEventChatter.ChatMonster == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilMonster)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, null);
                        }
                    }
                }
                //Salvage Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.SalvageMission")
                {
                    if (NilMod.NilModEventChatter.NilSalvage.Count() > 0 && NilMod.NilModEventChatter.ChatSalvage == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilSalvage)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, null);
                        }
                    }
                }
                //Cargo Mission code
                if (GameMain.GameSession.Mission.ToString() == "Barotrauma.CargoMission")
                {
                    if (NilMod.NilModEventChatter.NilCargo.Count() > 0 && NilMod.NilModEventChatter.ChatCargo == true)
                    {
                        foreach (string message in NilMod.NilModEventChatter.NilCargo)
                        {
                            NilMod.NilModEventChatter.SendServerMessage(message, null);
                        }
                    }
                }

            }
            //Sandbox Mode
            else
            {
                if (NilMod.NilModEventChatter.NilSandbox.Count() > 0 && NilMod.NilModEventChatter.ChatSandbox == true)
                {
                    foreach (string message in NilMod.NilModEventChatter.NilSandbox)
                    {
                        NilMod.NilModEventChatter.SendServerMessage(message, null);
                    }
                }
            }


            //Traitor Reminder Code

            if (Character.Controlled != null)
            {
                if (GameMain.Server.TraitorsEnabled == YesNoMaybe.Yes | GameMain.Server.TraitorsEnabled == YesNoMaybe.Maybe)
                {
                    if (Character.Controlled.Name == GameMain.NilMod.Traitor)
                    {
                        if (NilMod.NilModEventChatter.NilTraitorReminder.Count() > 0 && NilMod.NilModEventChatter.ChatTraitorReminder == true)
                        {
                            foreach (string message in NilMod.NilModEventChatter.NilTraitorReminder)
                            {
                                SendServerMessage(message, null);
                            }
                        }
                    }
                    else
                    {
                        if (NilMod.NilModEventChatter.NilNoneTraitorReminder.Count() > 0 && NilMod.NilModEventChatter.ChatNoneTraitorReminder == true)
                        {
                            foreach (string message in NilMod.NilModEventChatter.NilNoneTraitorReminder)
                            {
                                NilMod.NilModEventChatter.SendServerMessage(message, null);
                            }
                        }
                    }
                }
            }

        }
    }
}
