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

        public void Split(float minDivRatio, float verticalProbability = 0.5f, int minWidth = 200, int minHeight = 200)
        {
            bool verticalSplit = Rand.Range(0.0f, rect.Height / (float)rect.Width, Rand.RandSync.Server) < verticalProbability;
            if (rect.Width * minDivRatio < minWidth && rect.Height * minDivRatio < minHeight)
            {
                minDivRatio = 0.5f;
            }
            else if (rect.Width * minDivRatio < minWidth)
            {
                verticalSplit = false;
            }
            else if (rect.Height * minDivRatio < minHeight)
            {
                verticalSplit = true;
            }

            subRooms = new BTRoom[2];
            if (verticalSplit)
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
            Walls = new List<Line>
            {
                new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.Right, Rect.Y)),
                new Line(new Vector2(Rect.X, Rect.Bottom), new Vector2(Rect.Right, Rect.Bottom)),
                new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.X, Rect.Bottom)),
                new Line(new Vector2(Rect.Right, Rect.Y), new Vector2(Rect.Right, Rect.Bottom))
            };
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

        public static void CalculateDistancesFromEntrance(BTRoom entrance, List<BTRoom> rooms, List<Corridor> corridors)
        {
            entrance.CalculateDistanceFromEntrance(0, rooms, new List<Corridor>(corridors));
        }

        private void CalculateDistanceFromEntrance(int currentDist, List<BTRoom> rooms, List<Corridor> corridors)
        {
            DistanceFromEntrance = DistanceFromEntrance == 0 ? currentDist : Math.Min(currentDist, DistanceFromEntrance);

            currentDist++;

            var roomRect = Rect;
            roomRect.Inflate(5, 5);
            foreach (var corridor in corridors)
            {
                var corridorRect = corridor.Rect;
                corridorRect.Inflate(5, 5);
                if (!corridorRect.Intersects(roomRect)) continue;

                corridor.DistanceFromEntrance = corridor.DistanceFromEntrance == 0 ?
                    DistanceFromEntrance + 1 :
                    Math.Min(corridor.DistanceFromEntrance, DistanceFromEntrance + 1);

                
                List<BTRoom> connectedRooms = new List<BTRoom>();
                foreach (var otherRoom in rooms)
                {
                    if (otherRoom == this) continue;
                    if (otherRoom.DistanceFromEntrance > 0 && otherRoom.DistanceFromEntrance < currentDist) continue;

                    var otherRoomRect = otherRoom.Rect;
                    otherRoomRect.Inflate(5, 5);
                    if (corridorRect.Intersects(otherRoomRect)) { connectedRooms.Add(otherRoom); }
                }

                connectedRooms.Sort((r1, r2) =>
                {
                    return
                        (Math.Abs(r1.Rect.Center.X - Rect.Center.X) + Math.Abs(r1.Rect.Center.Y - Rect.Center.Y)) -
                        (Math.Abs(r2.Rect.Center.X - Rect.Center.X) + Math.Abs(r2.Rect.Center.Y - Rect.Center.Y));
                });

                for (int i = 0; i < connectedRooms.Count; i++)
                {
                    connectedRooms[i].CalculateDistanceFromEntrance(currentDist + 1 + i, rooms, corridors);
                }
            }
        }
    }
}
