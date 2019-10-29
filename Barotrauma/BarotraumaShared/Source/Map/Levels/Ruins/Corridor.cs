using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.RuinGeneration
{

    class Corridor : RuinShape
    {
        private readonly bool isHorizontal;
        
        public bool IsHorizontal
        {
            get { return isHorizontal; }
        }

        public BTRoom[] ConnectedRooms
        {
            get;
            private set;
        }
            
        public Corridor(Rectangle rect)
        {
            this.rect = rect;

            isHorizontal = rect.Width > rect.Height;
        }

        public Corridor(BTRoom room, int width, List<Corridor> corridors)
        {
            System.Diagnostics.Debug.Assert(room.Adjacent != null);

            ConnectedRooms = new BTRoom[2];
            ConnectedRooms[0] = room;
            ConnectedRooms[1] = room.Adjacent;

            Rectangle room1, room2;

            room1 = room.Rect;
            room2 = room.Adjacent.Rect;

            isHorizontal = (room1.Right <= room2.X || room2.Right <= room1.X);

            //use the leaves as starting points for the corridor
            if (room.SubRooms != null)
            {
                var leaves1 = room.GetLeaves();
                var leaves2 = room.Adjacent.GetLeaves();

                var suitableLeaves = GetSuitableLeafRooms(leaves1, leaves2, width, isHorizontal);
                if (suitableLeaves == null || suitableLeaves.Length < 2)
                {
                    // No suitable leaves found due to intersections
                    //DebugConsole.ThrowError("Error while generating ruins. Could not find a suitable position for a corridor. The width of the corridors may be too large compared to the sizes of the rooms.");
                    return;
                }
                else
                {
                    ConnectedRooms[0] = suitableLeaves[0];
                    ConnectedRooms[1] = suitableLeaves[1];
                }
            }
            else
            {
                rect = CalculateRectangle(room1, room2, width, isHorizontal);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    DebugConsole.ThrowError("Error while generating ruins. Attempted to create a corridor with a width or height of <= 0");
                    return;
                }
            }

            room.Corridor = this;
            room.Adjacent.Corridor = this;

            for (int i = corridors.Count - 1; i >= 0; i--)
            {
                var corridor = corridors[i];

                if (corridor.rect.Intersects(this.rect))
                {
                    if (isHorizontal && corridor.isHorizontal)
                    {
                        if (this.rect.Width < corridor.rect.Width)
                            return;
                        else
                            corridors.RemoveAt(i);
                    }
                    else if (!isHorizontal && !corridor.isHorizontal)
                    {
                        if (this.rect.Height < corridor.rect.Height)
                            return;
                        else
                            corridors.RemoveAt(i);
                    }
                }
            }

            corridors.Add(this);
        }

        public override void CreateWalls()
        {
            Walls = new List<Line>();
            if (IsHorizontal)
            {
                Walls.Add(new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.Right, Rect.Y)));
                Walls.Add(new Line(new Vector2(Rect.X, Rect.Bottom), new Vector2(Rect.Right, Rect.Bottom)));
            }
            else
            {
                Walls.Add(new Line(new Vector2(Rect.X, Rect.Y), new Vector2(Rect.X, Rect.Bottom)));
                Walls.Add(new Line(new Vector2(Rect.Right, Rect.Y), new Vector2(Rect.Right, Rect.Bottom)));
            }
        }

        /// <summary>
        /// Find two rooms which have two face-two-face walls that we can place a corridor in between
        /// </summary>
        /// <returns></returns>
        private BTRoom[] GetSuitableLeafRooms(List<BTRoom> leaves1, List<BTRoom> leaves2, int width, bool isHorizontal)
        {
            int iOffset = Rand.Int(leaves1.Count, Rand.RandSync.Server);
            int jOffset = Rand.Int(leaves2.Count, Rand.RandSync.Server);

            for (int iCount = 0; iCount < leaves1.Count; iCount++)
            {
                int i = (iCount + iOffset) % leaves1.Count;

                for (int jCount = 0; jCount < leaves2.Count; jCount++)
                {
                    int j = (jCount + jOffset) % leaves2.Count;

                    if (isHorizontal)
                    {
                        if (leaves1[i].Rect.Y > leaves2[j].Rect.Bottom - width) continue;
                        if (leaves1[i].Rect.Bottom < leaves2[j].Rect.Y + width) continue;
                    }
                    else
                    {
                        if (leaves1[i].Rect.X > leaves2[j].Rect.Right - width) continue;
                        if (leaves1[i].Rect.Right < leaves2[j].Rect.X + width) continue;
                    }

                    // Check if the given corridor rect would intersect over a third room
                    if (CheckForIntersection(leaves1[i], leaves2[j], leaves1, leaves2, width, isHorizontal)) continue;

                    return new BTRoom[] { leaves1[i], leaves2[j] };
                }
            }

            return null;
        }

        private bool CheckForIntersection(BTRoom potential1, BTRoom potential2, List<BTRoom> leaves1, List<BTRoom> leaves2, int width, bool isHorizontal)
        {
            Rectangle potentialCorridorRectangle = CalculateRectangle(potential1.Rect, potential2.Rect, width, isHorizontal);

            if (potentialCorridorRectangle.Width <= 0 || potentialCorridorRectangle.Height <= 0) return true; // Invalid rectangle

            for (int i = 0; i < leaves1.Count; i++)
            {
                if (leaves1[i] == potential1) continue;
                if (potentialCorridorRectangle.Intersects(leaves1[i].Rect)) return true;
            }

            for (int i = 0; i < leaves2.Count; i++)
            {
                if (leaves2[i] == potential2) continue;
                if (potentialCorridorRectangle.Intersects(leaves2[i].Rect)) return true;
            }

            rect = potentialCorridorRectangle; // Save the rectangle that passes the test
            return false;
        }

        private Rectangle CalculateRectangle(Rectangle rect1, Rectangle rect2, int width, bool isHorizontal)
        {
            if (isHorizontal)
            {
                int left = Math.Min(rect1.Right, rect2.Right);
                int right = Math.Max(rect1.X, rect2.X);

                int top = Math.Max(rect1.Y, rect2.Y);
                //int bottom = Math.Min(room1.Bottom, room2.Bottom);
                int yPos = top;//Rand.Range(top, bottom - width, Rand.RandSync.Server);

                return new Rectangle(left, yPos, right - left, width);
            }
            else if (rect1.Y > rect2.Bottom || rect2.Y > rect1.Bottom)
            {
                int left = Math.Max(rect1.X, rect2.X);
                int right = Math.Min(rect1.Right, rect2.Right);

                int top = Math.Min(rect1.Bottom, rect2.Bottom);
                int bottom = Math.Max(rect1.Y, rect2.Y);

                int xPos = Rand.Range(left, right - width, Rand.RandSync.Server);

                return new Rectangle(xPos, top, width, bottom - top);
            }
            else
            {
                DebugConsole.ThrowError("wat");
                return new Rectangle();
            }
        }
    }
}
