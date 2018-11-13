using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.RuinGeneration
{
    /// <summary>
    /// nodes of a binary tree used for generating underwater "dungeons"
    /// </summary>
    class BTRoom : RuinShape
    {
        private BTRoom[] subRooms;

        public BTRoom Parent
        {
            get;
            private set;
        }

        public Corridor Corridor
        {
            get;
            set;
        }

        public BTRoom[] SubRooms
        {
            get { return subRooms; }
        }

        public BTRoom Adjacent
        {
            get;
            private set;
        }

        public BTRoom(Rectangle rect)
        {
            this.rect = rect;
        }

        public void Split(float minDivRatio, float verticalProbability = 0.5f, int minWidth = 200)
        {
            subRooms = new BTRoom[2];

            if (Rand.Range(0.0f, rect.Height / (float)rect.Width, Rand.RandSync.Server) < verticalProbability && 
                rect.Width * minDivRatio >= minWidth)
            {
                SplitVertical(minDivRatio);
            }
            else
            {
                SplitHorizontal(minDivRatio);
            }

            subRooms[0].Parent = this;
            subRooms[1].Parent = this;

            subRooms[0].Adjacent = subRooms[1];
            subRooms[1].Adjacent = subRooms[0];
        }

        private void SplitHorizontal(float minDivRatio)
        {
            float div = Rand.Range(minDivRatio, 1.0f - minDivRatio, Rand.RandSync.Server);
            subRooms[0] = new BTRoom(new Rectangle(rect.X, rect.Y, rect.Width, (int)(rect.Height * div)));
            subRooms[1] = new BTRoom(new Rectangle(rect.X, rect.Y + subRooms[0].rect.Height, rect.Width, rect.Height - subRooms[0].rect.Height));

        }

        private void SplitVertical(float minDivRatio)
        {
            float div = Rand.Range(minDivRatio, 1.0f - minDivRatio, Rand.RandSync.Server);
            subRooms[0] = new BTRoom(new Rectangle(rect.X, rect.Y, (int)(rect.Width * div), rect.Height));
            subRooms[1] = new BTRoom(new Rectangle(rect.X + subRooms[0].rect.Width, rect.Y, rect.Width - subRooms[0].rect.Width, rect.Height));
        }

        public override void CreateWalls()
        {
            Walls = new List<Line>();

            Walls.Add(new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.Right, Rect.Y), RuinStructureType.Wall));
            Walls.Add(new Line(new Vector2(Rect.X, Rect.Bottom), new Vector2(Rect.Right, Rect.Bottom), RuinStructureType.Wall));

            Walls.Add(new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.X, Rect.Bottom), RuinStructureType.Wall));
            Walls.Add(new Line(new Vector2(Rect.Right, Rect.Y), new Vector2(Rect.Right, Rect.Bottom), RuinStructureType.Wall));
        }

        public void Scale(Vector2 scale)
        {
            rect.Inflate((scale.X - 1.0f) * 0.5f * rect.Width, (scale.Y - 1.0f) * 0.5f * rect.Height);
        }

        public List<BTRoom> GetLeaves()
        {
            return GetLeaves(new List<BTRoom>());
        }

        private List<BTRoom> GetLeaves(List<BTRoom> leaves)
        {
            if (subRooms == null)
            {
                leaves.Add(this);
            }
            else
            {
                subRooms[0].GetLeaves(leaves);
                subRooms[1].GetLeaves(leaves);
            }

            return leaves;
        }

        public void GenerateCorridors(int minWidth, int maxWidth, List<Corridor> corridors)
        {
            if (Adjacent != null && Corridor == null)
            {
                Corridor = new Corridor(this, Rand.Range(minWidth, maxWidth, Rand.RandSync.Server), corridors);
            }

            if (subRooms != null)
            {
                subRooms[0].GenerateCorridors(minWidth, maxWidth, corridors);
                subRooms[1].GenerateCorridors(minWidth, maxWidth, corridors);
            }
        }

        public static void CalculateDistancesFromEntrance(BTRoom entrance, List<Corridor> corridors)
        {
            entrance.CalculateDistanceFromEntrance(1, new List<Corridor>(corridors));
        }

        private void CalculateDistanceFromEntrance(int currentDist, List<Corridor> corridors)
        {
            if (DistanceFromEntrance == 0)
            {
                DistanceFromEntrance = currentDist;
            }
            else
            {
                DistanceFromEntrance = Math.Min(currentDist, DistanceFromEntrance);
            }

            currentDist++;

            for (int i = corridors.Count - 1; i >= 0; i = Math.Min(i - 1, corridors.Count - 1))
            {
                var corridor = corridors[i];

                if (!corridor.ConnectedRooms.Contains(this)) continue;

                corridors.RemoveAt(i);

                corridor.ConnectedRooms[corridor.ConnectedRooms[0] == this ? 1 : 0].CalculateDistanceFromEntrance(currentDist, corridors);
            }
        }
    }
}
