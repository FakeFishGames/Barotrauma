using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class BeaconMission : Mission
    {
        private bool swarmSpawned;
        private string monsterSpeciesName;
        private Point monsterCountRange;
        private Level level;
        private Location[] locations;
        private string sonarLabel;

        public BeaconMission(MissionPrefab prefab, Location[] locations) : base(prefab, locations)
        {
            swarmSpawned = false;

            XElement monsterElement = prefab.ConfigElement.Element("monster");

            monsterSpeciesName = monsterElement.GetAttributeString("character", string.Empty);
            int defaultCount = monsterElement.GetAttributeInt("count", -1);
            if (defaultCount < 0)
            {
                defaultCount = monsterElement.GetAttributeInt("amount", 1);
            }
            int min = Math.Min(monsterElement.GetAttributeInt("min", defaultCount), 255);
            int max = Math.Min(Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount)), 255);

            monsterCountRange = new Point(min, max);

            this.locations = locations;

            sonarLabel = TextManager.Get("beaconstationsonarlabel");
        }

        public override string SonarLabel
        {
            get
            {
                return string.IsNullOrEmpty(base.SonarLabel) ? sonarLabel : base.SonarLabel;
            }
        }

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                yield return level.BeaconStation.WorldPosition;
            }
        }

        public override void Start(Level level)
        {
            this.level = level;
        }

        public override void Update(float deltaTime)
        {
            if (IsClient) { return; }
            if (!swarmSpawned && level.CheckBeaconActive())
            {
                State = 1;
                Vector2 spawnPos = level.BeaconStation.WorldPosition;
                spawnPos.Y += level.BeaconStation.GetDockedBorders().Height * 1.5f;
                int amount = Rand.Range(monsterCountRange.X, monsterCountRange.Y + 1);
                for (int i = 0; i < amount; i++)
                {
                    Entity.Spawner.AddToSpawnQueue(monsterSpeciesName, spawnPos);
                }
                swarmSpawned = true;
            }
        }

        public override void End()
        {
            completed = level.CheckBeaconActive();
            if (completed)
            {
                if (GameMain.GameSession.GameMode is CampaignMode)
                {
                    int naturalFormationIndex = locations[0].Type.Identifier.Equals("None", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    var upgradeLocation = locations[naturalFormationIndex];
                    upgradeLocation.ChangeType(LocationType.List.Find(lt => lt.Identifier.Equals("Explored", StringComparison.OrdinalIgnoreCase)));
                }
                GiveReward();
            }
        }

        public override void AdjustLevelData(LevelData levelData)
        {
            levelData.HasBeaconStation = true;
            levelData.IsBeaconActive = false;
        }
    }
}