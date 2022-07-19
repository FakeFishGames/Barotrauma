using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma.RuinGeneration
{
    partial class Ruin
    {
        private readonly RuinGenerationParams generationParams;

        public List<VoronoiCell> PathCells = new List<VoronoiCell>();

        public Rectangle Area
        {
            get;
            private set;
        }

        public Submarine Submarine
        {
            get;
            private set;
        }

        public Ruin(Level level, RuinGenerationParams generationParams, Location location, Point position, bool mirror = false)
            : this(level, generationParams, location.Type, position, mirror)
        {
        }

        public Ruin(Level level, RuinGenerationParams generationParams, LocationType locationType, Point position, bool mirror = false)
        {
            this.generationParams = generationParams;
            Generate(level, locationType, position, mirror);
        }

        public void Generate(Level level, LocationType locationType, Point position, bool mirror = false)
        {
            Submarine = OutpostGenerator.Generate(generationParams, locationType, onlyEntrance: false);
            Submarine.Info.Name = $"Ruin ({level.Seed})";
            Submarine.Info.Type = SubmarineType.Ruin;
            Submarine.TeamID = CharacterTeamType.None;

            //prevent the ruin from extending above the level "ceiling"
            position.Y = Math.Min(level.Size.Y - (Submarine.Borders.Height / 2) - 100, position.Y);
            Submarine.SetPosition(position.ToVector2());

            if (mirror)
            {
                Submarine.FlipX();
            }

            Rectangle worldBorders = Submarine.Borders;
            worldBorders.Location += Submarine.WorldPosition.ToPoint();
            Area = new Rectangle(worldBorders.X, worldBorders.Y - worldBorders.Height, worldBorders.Width, worldBorders.Height);

            var waypoints = WayPoint.WayPointList.FindAll(wp => wp.Ruin == this || wp.Submarine == Submarine);
            int interestingPosCount = 0;
            foreach (WayPoint wp in waypoints)
            {
                if (wp.SpawnType != SpawnType.Enemy) { continue; }
                level.PositionsOfInterest.Add(new Level.InterestingPosition(wp.WorldPosition.ToPoint(), Level.PositionType.Ruin, this));
                interestingPosCount++;
            }

            if (interestingPosCount == 0)
            {
                //make sure there's at least one PositionsOfInterest in the ruins
                level.PositionsOfInterest.Add(new Level.InterestingPosition(waypoints.GetRandom(Rand.RandSync.ServerAndClient).WorldPosition.ToPoint(), Level.PositionType.Ruin, this));
            }
        }
    }
}
