using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class CampaignMode : GameMode
    {
        public readonly CargoManager CargoManager;

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
            map = new Map(seed, 1000);
        }

        protected List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(s =>
                s != leavingSub &&
                !leavingSub.DockedTo.Contains(s) &&
                (s.AtEndPosition != leavingSub.AtEndPosition || s.AtStartPosition != leavingSub.AtStartPosition));
        }

        public override void End(string endMessage = "")
        {
            base.End(endMessage);
        }

        public abstract void Save(XElement element);
    }
}
