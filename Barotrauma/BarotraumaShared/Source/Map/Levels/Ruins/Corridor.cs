using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.RuinGeneration
{

    class Corridor : RuinShape
    {
        private bool isHorizontal;
        
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
                    DebugConsole.ThrowError("Error while generating ruins. Could not find a suitable position for a corridor. The width of the corridors may be too large compared to the sizes of the rooms.");
                    return;
                }
                else
                {
                    room1 = suitableLeaves[0].Rect;
                    room2 = suitableLeaves[1].Rect;
                    ConnectedRooms[0] = suitableLeaves[0];
                    ConnectedRooms[1] = suitableLeaves[1];
                }
            }

            if (isHorizontal)
            {
                int left = Math.Min(room1.Right, room2.Right);
                int right = Math.Max(room1.X, room2.X);

                int top = Math.Max(room1.Y, room2.Y);
                //int bottom = Math.Min(room1.Bottom, room2.Bottom);
                int yPos = top;//Rand.Range(top, bottom - width, Rand.RandSync.Server);

                rect = new Rectangle(left, yPos, right - left, width);
            }
            else if (room1.Y > room2.Bottom || room2.Y > room1.Bottom)
            {
                int left = Math.Max(room1.X, room2.X);
                int right = Math.Min(room1.Right, room2.Right);

                int top = Math.Min(room1.Bottom, room2.Bottom);
                int bottom = Math.Max(room1.Y, room2.Y);

                int xPos = Rand.Range(left, right - width, Rand.RandSync.Server);

                rect = new Rectangle(xPos, top, width, bottom - top);
            }
            else
            {
                DebugConsole.ThrowError("wat");
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

                    if (CheckForIntersection(leaves1[i], leaves2[j], leaves1, leaves2, width, isHorizontal)) continue;

                    return new BTRoom[] { leaves1[i], leaves2[j] };
                }
            }

            return null;
        }

        private bool CheckForIntersection(BTRoom potential1, BTRoom potential2, List<BTRoom> leaves1, List<BTRoom> leaves2, int width, bool isHorizontal)
        {
            Rectangle potential1Rect = potential1.Rect;
            Rectangle potential2Rect = potential2.Rect;

            Rectangle corridorRectangle = new Rectangle();

            if (isHorizontal)
            {
                int left = Math.Min(potential1Rect.Right, potential2Rect.Right);
                int right = Math.Max(potential1Rect.X, potential2Rect.X);

                int top = Math.Max(potential1Rect.Y, potential2Rect.Y);
                //int bottom = Math.Min(room1.Bottom, room2.Bottom);
                int yPos = top;//Rand.Range(top, bottom - width, Rand.RandSync.Server);

                corridorRectangle = new Rectangle(left, yPos, right - left, width);
            }
            else if (potential1Rect.Y > potential2Rect.Bottom || potential2Rect.Y > potential1Rect.Bottom)
            {
                int left = Math.Max(potential1Rect.X, potential2Rect.X);
                int right = Math.Min(potential1Rect.Right, potential2Rect.Right);

                int top = Math.Min(potential1Rect.Bottom, potential2Rect.Bottom);
                int bottom = Math.Max(potential1Rect.Y, potential2Rect.Y);

                int xPos = Rand.Range(left, right - width, Rand.RandSync.Server);

                corridorRectangle = new Rectangle(xPos, top, width, bottom - top);
            }
            else
            {
                DebugConsole.ThrowError("wat");
                return false;
            }

            for (int i = 0; i < leaves1.Count; i++)
            {
                if (leaves1[i] == potential1) continue;
                if (corridorRectangle.Intersects(leaves1[i].Rect)) return true;
            }

            for (int i = 0; i < leaves2.Count; i++)
            {
                if (leaves2[i] == potential2) continue;
                if (corridorRectangle.Intersects(leaves2[i].Rect)) return true;
            }

            return false;
        }
    }
}
