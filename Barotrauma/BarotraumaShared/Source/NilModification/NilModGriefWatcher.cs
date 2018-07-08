using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Xml.Linq;
using System.IO;

namespace Barotrauma
{
    class NilModGriefWatcher
    {
        public const string GriefWatchSavePath = "Data/NilMod/GriefWatcher.xml";

        //These two are for when placing an explosive in a detonator, wiring any, etc)
        public List<String> GWListDetonators;
        public List<String> GWListExplosives;

        //For thrown objects that are bad to throw
        public List<String> GWListThrown;

        //Diving masks and diving suits, if they contain any of the hazardous or a hazardous is added
        //and is not their own inventory, their probably using it to kill/grief.
        public List<String> GWListMaskItems;
        public List<String> GWListMaskHazardous;

        //critical devices that should not be unwired (Pumps, oxygen generators, reactors, etc).
        public List<String> GWListWireKeyDevices;

        //Junctions, relays, anything "Worth watching"
        public List<String> GWListWireJunctions;

        //Items that count as syringes, and what shouldn't be placed into them normally (Sufforin/morb/etc)
        public List<String> GWListSyringes;
        public List<String> GWListSyringechems;

        //Guns that have been loaded with bad ammo types
        public List<String> GWListRanged;
        public List<String> GWListRangedAmmo;

        //Railgun objects for loading / what ammo is considered significant to load/shoot
        public List<String> GWListRailgunLaunch;
        public List<String> GWListRailgunRacks;
        public List<String> GWListRailgunAmmo;

        //Things that are self used, yet bad. incase you can self-use husk eggs in a mod or such
        public List<String> GWListUse;

        //This is more for items that are used directly, like a bandage. incase needed
        public List<String> GWListMeleeOther;

        //Items that when being created should be considered bad
        public List<String> GWListFabricated;

        //Placing items that are considered cuffs/restrictive on other players could be stated.
        public List<String> GWListHandcuffs;

        public string GriefWatchName = "GW-AI";
        public Boolean ExcludeTraitors;
        public Boolean AdminsOnly = false;
        public Boolean KeepTeamSpecific = true;
        public Boolean ReactorAutoTempOff;
        public Boolean ReactorShutDownTemp;
        public Boolean ReactorStateLastBlamed;
        public Boolean ReactorFissionBeyondAuto;
        public Boolean ReactorLastFuelRemoved;
        public float ReactorLastFuelRemovedTimer;
        public Boolean WallBreachOutside;
        public Boolean WallBreachInside;
        public Boolean DoorBroken;
        public Boolean DoorStuck;
        public Boolean PlayerIncapaciteDamage;
        public Boolean PlayerIncapaciteBleed;
        public Boolean PlayerTakeIDOffLiving;
        public Boolean PumpPositive;
        public Boolean PumpOff;



        public void ReportSettings()
        {

        }

        public void Load()
        {
            XDocument doc = null;

            if (File.Exists(GriefWatchSavePath))
            {
                doc = XMLExtensions.TryLoadXml(GriefWatchSavePath);
            }
            else
            {
                DebugConsole.ThrowError("NilModGriefWatcher config file \"" + GriefWatchSavePath + "\" Does not exist, generating default XML");
                //Save();
                doc = XMLExtensions.TryLoadXml(GriefWatchSavePath);
            }

            if (doc == null)
            {
                DebugConsole.ThrowError("NilModGriefWatcher config file \"" + GriefWatchSavePath + "\" failed to load. Disabling Grief Watcher.");
                GameMain.NilMod.EnableGriefWatcher = false;
            }
            else
            {
                XElement NilModGriefWatchSettings = doc.Root.Element("NilModGriefWatchSettings");
                
                GriefWatchName = NilModGriefWatchSettings.GetAttributeString("GriefWatchName", "GW-AI");
                ExcludeTraitors = NilModGriefWatchSettings.GetAttributeBool("ExcludeTraitors", true);
                AdminsOnly = NilModGriefWatchSettings.GetAttributeBool("AdminsOnly", false);
                KeepTeamSpecific = NilModGriefWatchSettings.GetAttributeBool("KeepTeamSpecific", true);
                ReactorAutoTempOff = NilModGriefWatchSettings.GetAttributeBool("ReactorAutoTempOff", true);
                ReactorShutDownTemp = NilModGriefWatchSettings.GetAttributeBool("ReactorShutDownTemp", true);
                ReactorStateLastBlamed = NilModGriefWatchSettings.GetAttributeBool("ReactorStateLastBlamed", true);
                ReactorFissionBeyondAuto = NilModGriefWatchSettings.GetAttributeBool("ReactorFissionBeyondAuto", true);
                ReactorLastFuelRemoved = NilModGriefWatchSettings.GetAttributeBool("ReactorLastFuelRemoved", true);
                ReactorLastFuelRemovedTimer = MathHelper.Clamp(NilModGriefWatchSettings.GetAttributeFloat("ReactorLastFuelRemovedTimer", 6f),0f,15f);
                WallBreachOutside = NilModGriefWatchSettings.GetAttributeBool("WallBreachOutside", true);
                WallBreachInside = NilModGriefWatchSettings.GetAttributeBool("WallBreachInside", true);
                DoorBroken = NilModGriefWatchSettings.GetAttributeBool("DoorBroken", true);
                DoorStuck = NilModGriefWatchSettings.GetAttributeBool("DoorStuck", true);
                PlayerIncapaciteDamage = NilModGriefWatchSettings.GetAttributeBool("PlayerIncapaciteDamage", true);
                PlayerIncapaciteBleed = NilModGriefWatchSettings.GetAttributeBool("PlayerIncapaciteBleed", true);
                PlayerTakeIDOffLiving = NilModGriefWatchSettings.GetAttributeBool("PlayerTakeIDOffLiving", true);
                PumpPositive = NilModGriefWatchSettings.GetAttributeBool("PumpPositive", true);
                PumpOff = NilModGriefWatchSettings.GetAttributeBool("PumpOff", true);

                XElement GWListDetonatorsdoc = doc.Root.Element("GWListDetonators");
                GWListDetonators = new List<string>();

                if (GWListDetonatorsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListDetonatorsdoc.Elements())
                    {
                        GWListDetonators.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListExplosivesdoc = doc.Root.Element("GWListExplosives");
                GWListExplosives = new List<string>();

                if (GWListExplosivesdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListExplosivesdoc.Elements())
                    {
                        GWListExplosives.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListThrowndoc = doc.Root.Element("GWListThrown");
                GWListThrown = new List<string>();

                if (GWListThrowndoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListThrowndoc.Elements())
                    {
                        GWListThrown.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListMaskItemsdoc = doc.Root.Element("GWListMaskItems");
                GWListMaskItems = new List<string>();

                if (GWListMaskItemsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListMaskItemsdoc.Elements())
                    {
                        GWListMaskItems.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListMaskHazardousdoc = doc.Root.Element("GWListMaskHazardous");
                GWListMaskHazardous = new List<string>();

                if (GWListMaskHazardousdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListMaskHazardousdoc.Elements())
                    {
                        GWListMaskHazardous.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListWireKeyDevicesdoc = doc.Root.Element("GWListWireKeyDevices");
                GWListWireKeyDevices = new List<string>();

                if (GWListWireKeyDevicesdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListWireKeyDevicesdoc.Elements())
                    {
                        GWListWireKeyDevices.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListWireJunctionsdoc = doc.Root.Element("GWListWireJunctions");
                GWListWireJunctions = new List<string>();

                if (GWListWireJunctionsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListWireJunctionsdoc.Elements())
                    {
                        GWListWireJunctions.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListSyringesdoc = doc.Root.Element("GWListSyringes");
                GWListSyringes = new List<string>();

                if (GWListSyringesdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListSyringesdoc.Elements())
                    {
                        GWListSyringes.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                //Text for players when the shuttle has 30 seconds remaining

                XElement GWListSyringechemsdoc = doc.Root.Element("GWListSyringechems");
                GWListSyringechems = new List<string>();

                if (GWListSyringechemsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListSyringechemsdoc.Elements())
                    {
                        GWListSyringechems.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                //Text for players when the shuttle has 15 seconds remaining

                XElement GWListRangeddoc = doc.Root.Element("GWListRanged");
                GWListRanged = new List<string>();

                if (GWListRangeddoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListRangeddoc.Elements())
                    {
                        GWListRanged.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                //Text for players when the shuttle is going to leave and kill its occupants

                XElement GWListRangedAmmodoc = doc.Root.Element("GWListRangedAmmo");
                GWListRangedAmmo = new List<string>();

                if (GWListRangedAmmodoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListRangedAmmodoc.Elements())
                    {
                        GWListRangedAmmo.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                //Text for sub vs sub - Coalition team spawns

                XElement GWListRailgunLaunchdoc = doc.Root.Element("GWListRailgunLaunch");
                GWListRailgunLaunch = new List<string>();

                if (GWListRailgunLaunchdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListRailgunLaunchdoc.Elements())
                    {
                        GWListRailgunLaunch.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                //Text for sub vs sub - Renegade team spawns

                XElement GWListRailgunRacksdoc = doc.Root.Element("GWListRailgunRacks");
                GWListRailgunRacks = new List<string>();

                if (GWListRailgunRacksdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListRailgunRacksdoc.Elements())
                    {
                        GWListRailgunRacks.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListRailgunAmmodoc = doc.Root.Element("GWListRailgunAmmo");
                GWListRailgunAmmo = new List<string>();

                if (GWListRailgunAmmodoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListRailgunAmmodoc.Elements())
                    {
                        GWListRailgunAmmo.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListSelfUsedoc = doc.Root.Element("GWListUse");
                GWListUse = new List<string>();

                if (GWListSelfUsedoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListSelfUsedoc.Elements())
                    {
                        GWListUse.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListMeleeOtherdoc = doc.Root.Element("GWListMeleeOther");
                GWListMeleeOther = new List<string>();

                if (GWListMeleeOtherdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListMeleeOtherdoc.Elements())
                    {
                        GWListMeleeOther.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListFabricateddoc = doc.Root.Element("GWListFabricated");
                GWListFabricated = new List<string>();

                if (GWListFabricateddoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListFabricateddoc.Elements())
                    {
                        GWListFabricated.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                XElement GWListHandcuffsdoc = doc.Root.Element("GWListHandcuffs");
                GWListHandcuffs = new List<string>();

                if (GWListHandcuffsdoc?.Elements().Count() > 0)
                {
                    foreach (XElement subElement in GWListHandcuffsdoc.Elements())
                    {
                        GWListHandcuffs.Add(subElement.GetAttributeString("name", ""));
                    }
                }

                if(GameMain.Server != null)
                {
                    //Recheck the prefabs
                    GameInitialize();
                }
            }
        }


        public void Save()
        {

        }

        public void SendWarning(string WarningMessage, Client Offender)
        {
            //If server is not running this should never trigger
            if (GameMain.Server == null || Offender == null) return;

            //Do not send messages if their traitors and set to be ignored as such.
            if(GameMain.Server.TraitorManager != null && GameMain.Server.TraitorManager.IsTraitor(Offender.Character) && NilMod.NilModGriefWatcher.ExcludeTraitors) return;

            //Loop through the clients whom qualify for receiving the warning.
            for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
            {
                if((GameMain.Server.ConnectedClients[i].TeamID == Offender.TeamID
                    || !NilMod.NilModGriefWatcher.KeepTeamSpecific
                    || GameMain.Server.ConnectedClients[i].SpectateOnly
                    || GameMain.Server.ConnectedClients[i].Character == null
                    || (GameMain.Server.ConnectedClients[i].Character != null && GameMain.Server.ConnectedClients[i].Character.IsDead))
                    && !AdminsOnly)
                {
                    SendMessage("[Team " + Offender.TeamID + "] " + WarningMessage, GameMain.Server.ConnectedClients[i]);
                }
                else if(AdminsOnly &&
                    (GameMain.Server.ConnectedClients[i].AdministratorSlot
                    || GameMain.Server.ConnectedClients[i].OwnerSlot
                    || GameMain.Server.ConnectedClients[i].HasPermission(ClientPermissions.Ban)
                    || GameMain.Server.ConnectedClients[i].HasPermission(ClientPermissions.Kick)))
                {
                    SendMessage("[Team " + Offender.TeamID + "] " + WarningMessage, GameMain.Server.ConnectedClients[i]);
                }
            }
            //Send to the Server/Host itself.
            SendMessage(WarningMessage, null);
        }

        public void SendMessage(string messagetext,Client clientreceiver)
        {
            if (clientreceiver != null)
            {
                var chatMsg = ChatMessage.Create(
                GriefWatchName,
                messagetext,
                (ChatMessageType)ChatMessageType.Server,
                null);

                GameMain.Server.SendChatMessage(chatMsg, clientreceiver);
            }
            else
            {
                //Local Host Chat code here
                //if (Character.Controlled != null)
                //{
                    GameMain.NetworkMember.AddChatMessage(GriefWatchName + ":" + messagetext, ChatMessageType.Server);
                //}
            }
        }


        public void GameInitialize()
        {
            for (int i = GWListDetonators.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListDetonators[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListDetonators[i] + ").", Color.Red);
                    GWListDetonators.RemoveAt(i);
                }
                else
                {
                    GWListDetonators[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListExplosives.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListExplosives[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListExplosives[i] + ").", Color.Red);
                    GWListExplosives.RemoveAt(i);
                }
                else
                {
                    GWListExplosives[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListThrown.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListThrown[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListThrown[i] + ").", Color.Red);
                    GWListThrown.RemoveAt(i);
                }
                else
                {
                    GWListThrown[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListMaskItems.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListMaskItems[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListMaskItems[i] + ").", Color.Red);
                    GWListMaskItems.RemoveAt(i);
                }
                else
                {
                    GWListMaskItems[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListMaskHazardous.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListMaskHazardous[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListMaskHazardous[i] + ").", Color.Red);
                    GWListMaskHazardous.RemoveAt(i);
                }
                else
                {
                    GWListMaskHazardous[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListWireKeyDevices.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListWireKeyDevices[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListWireKeyDevices[i] + ").", Color.Red);
                    GWListWireKeyDevices.RemoveAt(i);
                }
                else
                {
                    GWListWireKeyDevices[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListWireJunctions.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListWireJunctions[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListWireJunctions[i] + ").", Color.Red);
                    GWListWireJunctions.RemoveAt(i);
                }
                else
                {
                    GWListWireJunctions[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListSyringes.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListSyringes[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListSyringes[i] + ").", Color.Red);
                    GWListSyringes.RemoveAt(i);
                }
                else
                {
                    GWListSyringes[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListSyringechems.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListSyringechems[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListSyringechems[i] + ").", Color.Red);
                    GWListSyringechems.RemoveAt(i);
                }
                else
                {
                    GWListSyringechems[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListRanged.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListRanged[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListRanged[i] + ").", Color.Red);
                    GWListRanged.RemoveAt(i);
                }
                else
                {
                    GWListRanged[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListRangedAmmo.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListRangedAmmo[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListRangedAmmo[i] + ").", Color.Red);
                    GWListRangedAmmo.RemoveAt(i);
                }
                else
                {
                    GWListRangedAmmo[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListRailgunLaunch.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListRailgunLaunch[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListRailgunLaunch[i] + ").", Color.Red);
                    GWListRailgunLaunch.RemoveAt(i);
                }
                else
                {
                    GWListRailgunLaunch[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListRailgunRacks.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListRailgunRacks[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListRailgunRacks[i] + ").", Color.Red);
                    GWListRailgunRacks.RemoveAt(i);
                }
                else
                {
                    GWListRailgunRacks[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListRailgunAmmo.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListRailgunAmmo[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListRailgunAmmo[i] + ").", Color.Red);
                    GWListRailgunAmmo.RemoveAt(i);
                }
                else
                {
                    GWListRailgunAmmo[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListUse.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListUse[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListUse[i] + ").", Color.Red);
                    GWListUse.RemoveAt(i);
                }
                else
                {
                    GWListUse[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListFabricated.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListFabricated[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListFabricated[i] + ").", Color.Red);
                    GWListFabricated.RemoveAt(i);
                }
                else
                {
                    GWListFabricated[i] = PrefabCheck.Name;
                }
            }

            for (int i = GWListHandcuffs.Count - 1; i >= 0; i--)
            {
                MapEntityPrefab PrefabCheck = ItemPrefab.Find(GWListHandcuffs[i]);
                if (PrefabCheck == null)
                {
                    DebugConsole.NewMessage("NilModGriefWatcher Error - Prefab does not exist! ("
                        + GWListHandcuffs[i] + ").", Color.Red);
                    GWListHandcuffs.RemoveAt(i);
                }
                else
                {
                    GWListHandcuffs[i] = PrefabCheck.Name;
                }
            }
        }
    }
}
