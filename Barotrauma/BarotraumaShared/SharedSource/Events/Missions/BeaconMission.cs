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
        private readonly string monsterSpeciesName;
        private Point monsterCountRange;
        private Level level;
        private readonly string sonarLabel;

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

                var availablePositions = Level.Loaded.PositionsOfInterest.FindAll(p => 
                    p.PositionType == Level.PositionType.MainPath || 
                    p.PositionType == Level.PositionType.SidePath);
                availablePositions.RemoveAll(p => Level.Loaded.ExtraWalls.Any(w => w.IsPointInside(p.Position.ToVector2())));
                availablePositions.RemoveAll(p => Submarine.FindContaining(p.Position.ToVector2()) != null);

                if (availablePositions.Any())
                {
                    Level.InterestingPosition? closestPos = null;
                    float closestDist = float.PositiveInfinity;
                    foreach (var pos in availablePositions)
                    {
                        float dist = Vector2.DistanceSquared(pos.Position.ToVector2(), level.BeaconStation.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestPos = pos;
                        }
                    }
                    if (closestPos.HasValue)
                    {
                        spawnPos = closestPos.Value.Position.ToVector2();
                    }
                }

                int amount = Rand.Range(monsterCountRange.X, monsterCountRange.Y + 1);
                for (int i = 0; i < amount; i++)
                {
                    CoroutineManager.InvokeAfter(() =>
                    {
                        //round ended before the coroutine finished
                        if (GameMain.GameSession == null || Level.Loaded == null) { return; }
                        Entity.Spawner.AddToSpawnQueue(monsterSpeciesName, spawnPos);
                    }, Rand.Range(0f, amount));
                }
                swarmSpawned = true;
            }
        }

        public override void End()
        {
            completed = level.CheckBeaconActive();
            if (completed)
            {
                ChangeLocationType("None", "Explored");
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