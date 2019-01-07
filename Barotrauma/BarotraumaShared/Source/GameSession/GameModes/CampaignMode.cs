using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class CampaignMode : GameMode
    {
        public readonly CargoManager CargoManager;

        public bool CheatsEnabled;

        const int InitialMoney = 10000;

        protected Map map;
        public Map Map
        {
            get { return map; }
        }

        public override Mission Mission
        {
            get
            {
                return Map.SelectedConnection.Mission;
            }
        }

        private int money;
        public int Money
        {
            get { return money; }
            set { money = Math.Max(value, 0); }
        }

        public CampaignMode(GameModePreset preset, object param)
            : base(preset, param)
        {
            Money = InitialMoney;
            CargoManager = new CargoManager(this);            
        }

        public void GenerateMap(string seed)
        {
            map = new Map(seed);
        }

        protected List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(s =>
                s != leavingSub &&
                !leavingSub.DockedTo.Contains(s) &&
                s != Level.Loaded.StartOutpost && s != Level.Loaded.EndOutpost &&
                (s.AtEndPosition != leavingSub.AtEndPosition || s.AtStartPosition != leavingSub.AtStartPosition));
        }
        
        public abstract void Save(XElement element);
        
        public void LogState()
        {
            DebugConsole.NewMessage("********* CAMPAIGN STATUS *********", Color.White);
            DebugConsole.NewMessage("   Money: " + Money, Color.White);
            DebugConsole.NewMessage("   Current location: " + map.CurrentLocation.Name, Color.White);

            DebugConsole.NewMessage("   Available destinations: ", Color.White);
            for (int i = 0; i < map.CurrentLocation.Connections.Count; i++)
            {
                Location destination = map.CurrentLocation.Connections[i].OtherLocation(map.CurrentLocation);
                if (destination == map.SelectedLocation)
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name + " [SELECTED]", Color.White);
                }
                else
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name, Color.White);
                }
            }
            
            if (map.SelectedConnection?.Mission != null)
            {
                DebugConsole.NewMessage("   Selected mission: " + map.SelectedConnection.Mission.Name, Color.White);
                DebugConsole.NewMessage("\n" + map.SelectedConnection.Mission.Description, Color.White);
            }
        }
    }
}
