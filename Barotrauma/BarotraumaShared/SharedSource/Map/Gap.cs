using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Gap : MapEntity
    {
        public static List<Gap> GapList = new List<Gap>();

        const float MaxFlowForce = 500.0f;

        public static bool ShowGaps = true;

        const float OutsideColliderRaycastIntervalLowPrio = 1.5f;
        const float OutsideColliderRaycastIntervalHighPrio = 0.1f;

        public bool IsHorizontal
        {
            get;
            private set;
        }

        //a value between 0.0f-1.0f (0.0 = closed, 1.0f = open)
        private float open;           

        //the force of the water flow which is exerted on physics bodies
        private Vector2 flowForce;
        private Hull flowTargetHull;

        private float openedTimer = 1.0f;
        
        private float higherSurface;
        private float lowerSurface;
        
        private Vector2 lerpedFlowForce;
        
        //if set to true, hull connections of this gap won't be updated when changes are being done to hulls
        public bool DisableHullRechecks;
        
        //can ambient light get through the gap even if it's not open
        public bool PassAmbientLight;
        

        //a collider outside the gap (for example an ice wall next to the sub)
        //used by ragdolls to prevent them from ending up inside colliders when teleporting out of the sub
        private Body outsideCollisionBlocker;
        private float outsideColliderRaycastTimer;

        public float Open
        {
            get { return open; }
            set 
            {
                if (value > open) { openedTimer = 1.0f; }
                open = MathHelper.Clamp(value, 0.0f, 1.0f); 
            }
        }

        public float Size => IsHorizontal ? Rect.Height : Rect.Width;

        private Door connectedDoor;
        public Door ConnectedDoor
        {
            get
            {
                if (connectedDoor != null && connectedDoor.Item.Removed)
                {
                    connectedDoor = null;
                }
                return connectedDoor;
            }
            set { connectedDoor = value; }
        }

        public Structure ConnectedWall;

        public Vector2 LerpedFlowForce
        {
            get { return lerpedFlowForce; }
        }

        public Hull FlowTargetHull
        {
            get { return flowTargetHull; }
        }

        public bool IsRoomToRoom
        {
            get
            {
                return linkedTo.Count == 2;
            }
        }

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;

                FindHulls();
            }
        }

        public override string Name
        {
            get
            {
                return "Gap";
            }
        }

        public Gap(MapEntityPrefab prefab, Rectangle rectangle)
            : this(rectangle, Submarine.MainSub)
        {
#if CLIENT
            if (SubEditorScreen.IsSubEditor())
            {
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { this }, false));
            }
#endif
        }

        public Gap(Rectangle rect, Submarine submarine)
            : this(rect, rect.Width < rect.Height, submarine)
        { }

        public Gap(Rectangle rect, bool isHorizontal, Submarine submarine, ushort id = Entity.NullEntityID)
            : base(MapEntityPrefab.Find(null, "gap"), submarine, id)
        {
            this.rect = rect;
            flowForce = Vector2.Zero;
            IsHorizontal = isHorizontal;
            open = 1.0f;

            FindHulls();
            GapList.Add(this);
            InsertToList();

            float blockerSize = ConvertUnits.ToSimUnits(Math.Max(rect.Width, rect.Height)) / 2;
            outsideCollisionBlocker = GameMain.World.CreateEdge(-Vector2.UnitX * blockerSize, Vector2.UnitX * blockerSize);
            outsideCollisionBlocker.UserData = $"CollisionBlocker (Gap {ID})";
            outsideCollisionBlocker.BodyType = BodyType.Static;
            outsideCollisionBlocker.CollisionCategories = Physics.CollisionWall;
            outsideCollisionBlocker.CollidesWith = Physics.CollisionCharacter;
            outsideCollisionBlocker.Enabled = false;
#if CLIENT
            Resized += newRect => IsHorizontal = newRect.Width < newRect.Height;
#endif
            DebugConsole.Log("Created gap (" + ID + ")");
        }

        public override MapEntity Clone()
        {
            return new Gap(rect, IsHorizontal, Submarine);
        }

        public override void Move(Vector2 amount)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move a gap by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            base.Move(amount);

            if (!DisableHullRechecks) { FindHulls(); }
        }

        public static void UpdateHulls()
        {
            foreach (Gap g in GapList)
            {
                for (int i = g.linkedTo.Count - 1; i >= 0; i--)
                {
                    if (g.linkedTo[i].Removed)
                    {
                        g.linkedTo.RemoveAt(i);
                    }
                }

                if (g.DisableHullRechecks) continue;
                g.FindHulls();
            }
        }

        public override bool IsMouseOn(Vector2 position)
        {
            return ShowGaps && Submarine.RectContains(WorldRect, position) &&
                !Submarine.RectContains(MathUtils.ExpandRect(WorldRect, -5), position);
        }

        public void AutoOrient()
        {
            Vector2 searchPosLeft = new Vector2(rect.X, rect.Y - rect.Height / 2);
            Hull hullLeft = Hull.FindHullOld(searchPosLeft, null, false);
            Vector2 searchPosRight = new Vector2(rect.Right, rect.Y - rect.Height / 2);
            Hull hullRight = Hull.FindHullOld(searchPosRight, null, false);

            if (hullLeft != null && hullRight != null && hullLeft != hullRight)
            {
                IsHorizontal = true;
                return;
            }

            Vector2 searchPosTop = new Vector2(rect.Center.X, rect.Y);
            Hull hullTop = Hull.FindHullOld(searchPosTop, null, false);
            Vector2 searchPosBottom = new Vector2(rect.Center.X, rect.Y - rect.Height);
            Hull hullBottom = Hull.FindHullOld(searchPosBottom, null, false);

            if (hullTop != null && hullBottom != null && hullTop != hullBottom)
            {
                IsHorizontal = false;
                return;
            }

            if ((hullLeft == null) != (hullRight == null))
            {
                IsHorizontal = true;
            }
            else if ((hullTop == null) != (hullBottom == null))
            {
                IsHorizontal = false;
            }
        }

        private void FindHulls()
        {
            Hull[] hulls = new Hull[2];

            foreach (var linked in linkedTo)
            {
                if (linked is Hull hull)
                {
                    hull.ConnectedGaps.Remove(this);
                }
            }
            linkedTo.Clear();

            Vector2[] searchPos = new Vector2[2];
            if (IsHorizontal)
            {
                searchPos[0] = new Vector2(rect.X, rect.Y - rect.Height / 2);
                searchPos[1] = new Vector2(rect.Right, rect.Y - rect.Height / 2);
            }
            else
            {
                searchPos[0] = new Vector2(rect.Center.X, rect.Y);
                searchPos[1] = new Vector2(rect.Center.X, rect.Y - rect.Height);
            }

            for (int i = 0; i < 2; i++)
            {
                hulls[i] = Hull.FindHullOld(searchPos[i], null, false);
                if (hulls[i] == null) hulls[i] = Hull.FindHullOld(searchPos[i], null, false, true);
            }

            if (hulls[0] == null && hulls[1] == null) { return; }

            if (hulls[0] == null && hulls[1] != null)
            {
                Hull temp = hulls[0];
                hulls[0] = hulls[1];
                hulls[1] = temp;
            }

            flowTargetHull = hulls[0];

            for (int i = 0; i < 2; i++)
            {
                if (hulls[i] == null) { continue; }
                linkedTo.Add(hulls[i]);
                if (!hulls[i].ConnectedGaps.Contains(this)) hulls[i].ConnectedGaps.Add(this);
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            flowForce = Vector2.Zero;

            outsideColliderRaycastTimer -= deltaTime;

            if (open == 0.0f || linkedTo.Count == 0)
            {
                lerpedFlowForce = Vector2.Zero;
                return;
            }

            Hull hull1 = (Hull)linkedTo[0];
            Hull hull2 = linkedTo.Count < 2 ? null : (Hull)linkedTo[1];
            if (hull1 == hull2) { return; }

            UpdateOxygen(hull1, hull2);

            if (linkedTo.Count == 1)
            {
                //gap leading from a room to outside
                UpdateRoomToOut(deltaTime, hull1);
            }
            else if (linkedTo.Count == 2)
            {
                //gap leading from a room to another
                UpdateRoomToRoom(deltaTime, hull1, hull2);
            }

            flowForce.X = MathHelper.Clamp(flowForce.X, -MaxFlowForce, MaxFlowForce);
            flowForce.Y = MathHelper.Clamp(flowForce.Y, -MaxFlowForce, MaxFlowForce);
            if (openedTimer > 0.0f && flowForce.Length() > lerpedFlowForce.Length())
            {
                //if the gap has just been opened/created, allow it to exert a large force instantly without any smoothing
                lerpedFlowForce = flowForce;                
            }
            else
            {
                lerpedFlowForce = Vector2.Lerp(lerpedFlowForce, flowForce, deltaTime * 5.0f);
            }
            openedTimer -= deltaTime;

            EmitParticles(deltaTime);
        }

        partial void EmitParticles(float deltaTime);

        void UpdateRoomToRoom(float deltaTime, Hull hull1, Hull hull2)
        {
            Vector2 subOffset = Vector2.Zero;
            if (hull1.Submarine != Submarine)
            {
                subOffset = Submarine.Position - hull1.Submarine.Position;
            }
            else if (hull2.Submarine != Submarine)
            {
                subOffset = hull2.Submarine.Position - Submarine.Position;
            }

            if (hull1.WaterVolume <= 0.0 && hull2.WaterVolume <= 0.0) return;

            float size = IsHorizontal ? rect.Height : rect.Width;

            //a variable affecting the water flow through the gap
            //the larger the gap is, the faster the water flows
            float sizeModifier = size / 100.0f * open;

            //horizontal gap (such as a regular door)
            if (IsHorizontal)
            {
                higherSurface = Math.Max(hull1.Surface, hull2.Surface + subOffset.Y);
                float delta = 0.0f;

                //water level is above the lower boundary of the gap
                if (Math.Max(hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1], hull2.Surface + subOffset.Y + hull2.WaveY[0]) > rect.Y - size)
                {
                    int dir = (hull1.Pressure > hull2.Pressure + subOffset.Y) ? 1 : -1;

                    //water flowing from the righthand room to the lefthand room
                    if (dir == -1)
                    {
                        if (!(hull2.WaterVolume > 0.0f)) return;
                        lowerSurface = hull1.Surface - hull1.WaveY[hull1.WaveY.Length - 1];
                        //delta = Math.Min((room2.water.pressure - room1.water.pressure) * sizeModifier, Math.Min(room2.water.Volume, room2.Volume));
                        //delta = Math.Min(delta, room1.Volume - room1.water.Volume + Water.MaxCompress);

                        flowTargetHull = hull1;

                        //make sure not to move more than what the room contains
                        delta = Math.Min(((hull2.Pressure + subOffset.Y) - hull1.Pressure) * 5.0f * sizeModifier, Math.Min(hull2.WaterVolume, hull2.Volume));
                        
                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull1.Volume * Hull.MaxCompress - (hull1.WaterVolume));
                        hull1.WaterVolume += delta;
                        hull2.WaterVolume -= delta;
                        if (hull1.WaterVolume > hull1.Volume)
                        {
                            hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + hull2.Pressure+subOffset.Y) / 2);
                        }

                        flowForce = new Vector2(-delta, 0.0f);
                    }
                    else if (dir == 1)
                    {
                        if (!(hull1.WaterVolume > 0.0f)) return;
                        lowerSurface = hull2.Surface - hull2.WaveY[hull2.WaveY.Length - 1];

                        flowTargetHull = hull2;

                        //make sure not to move more than what the room contains
                        delta = Math.Min((hull1.Pressure - (hull2.Pressure + subOffset.Y)) * 5.0f * sizeModifier, Math.Min(hull1.WaterVolume, hull1.Volume));

                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull2.Volume * Hull.MaxCompress - (hull2.WaterVolume));
                        hull1.WaterVolume -= delta;
                        hull2.WaterVolume += delta;
                        if (hull2.WaterVolume > hull2.Volume)
                        {
                            hull2.Pressure = Math.Max(hull2.Pressure, ((hull1.Pressure-subOffset.Y) + hull2.Pressure) / 2);
                        }
                        
                        flowForce = new Vector2(delta, 0.0f);
                    }

                    if (delta > 100.0f && subOffset == Vector2.Zero)
                    {
                        float avg = (hull1.Surface + hull2.Surface) / 2.0f;

                        if (hull1.WaterVolume < hull1.Volume / Hull.MaxCompress &&
                            hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1] < rect.Y)
                        {
                            hull1.WaveVel[hull1.WaveY.Length - 1] = (avg - (hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1])) * 0.1f;
                            hull1.WaveVel[hull1.WaveY.Length - 2] = hull1.WaveVel[hull1.WaveY.Length - 1];
                        }

                        if (hull2.WaterVolume < hull2.Volume / Hull.MaxCompress &&
                            hull2.Surface + hull2.WaveY[0] < rect.Y)
                        {
                            hull2.WaveVel[0] = (avg - (hull2.Surface + hull2.WaveY[0])) * 0.1f;
                            hull2.WaveVel[1] = hull2.WaveVel[0];
                        }
                    }
                }

            }
            else
            {
                //lower room is full of water
                if (hull2.Pressure + subOffset.Y > hull1.Pressure && hull2.WaterVolume > 0.0f)
                {
                    float delta = Math.Min(hull2.WaterVolume - hull2.Volume + (hull2.Volume * Hull.MaxCompress), deltaTime * 8000.0f * sizeModifier);

                    //make sure not to place more water to the target room than it can hold
                    if (hull1.WaterVolume + delta > hull1.Volume * Hull.MaxCompress)
                    {
                        delta -= (hull1.WaterVolume + delta) - (hull1.Volume * Hull.MaxCompress);
                    }

                    delta = Math.Max(delta, 0.0f);
                    hull1.WaterVolume += delta;
                    hull2.WaterVolume -= delta;

                    flowForce = new Vector2(
                        0.0f, 
                        Math.Min(Math.Min((hull2.Pressure + subOffset.Y) - hull1.Pressure, 200.0f), delta));

                    flowTargetHull = hull1;

                    if (hull1.WaterVolume > hull1.Volume)
                    {
                        hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + (hull2.Pressure + subOffset.Y)) / 2);
                    }                   

                }
                //there's water in the upper room, drop to lower
                else if (hull1.WaterVolume > 0)
                {
                    flowTargetHull = hull2;

                    //make sure the amount of water moved isn't more than what the room contains
                    float delta = Math.Min(hull1.WaterVolume, deltaTime * 25000f * sizeModifier);

                    //make sure not to place more water to the target room than it can hold
                    if (hull2.WaterVolume + delta > hull2.Volume * Hull.MaxCompress)
                    {
                        delta -= (hull2.WaterVolume + delta) - (hull2.Volume * Hull.MaxCompress);
                    }
                    hull1.WaterVolume -= delta;
                    hull2.WaterVolume += delta;

                    flowForce = new Vector2(
                        hull1.WaveY[hull1.GetWaveIndex(rect.X)] - hull1.WaveY[hull1.GetWaveIndex(rect.Right)],
                        MathHelper.Clamp(-delta, -200.0f, 0.0f));

                    if (hull2.WaterVolume > hull2.Volume)
                    {
                        hull2.Pressure = Math.Max(hull2.Pressure, ((hull1.Pressure - subOffset.Y) + hull2.Pressure) / 2);
                    }
                }
            }

            if (open > 0.0f)
            {
                if (hull1.WaterVolume > hull1.Volume / Hull.MaxCompress && hull2.WaterVolume > hull2.Volume / Hull.MaxCompress)
                {
                    float avgLethality = (hull1.LethalPressure + hull2.LethalPressure) / 2.0f;
                    hull1.LethalPressure = avgLethality;
                    hull2.LethalPressure = avgLethality;
                }
                else 
                {
                    hull1.LethalPressure = 0.0f;
                    hull2.LethalPressure = 0.0f;
                }
            }
        }

        void UpdateRoomToOut(float deltaTime, Hull hull1)
        {
            float size = IsHorizontal ? rect.Height : rect.Width;

            //a variable affecting the water flow through the gap
            //the larger the gap is, the faster the water flows
            float sizeModifier = size * open * open;

            float delta = 500.0f * sizeModifier * deltaTime;
            
            //make sure not to place more water to the target room than it can hold
            delta = Math.Min(delta, hull1.Volume * Hull.MaxCompress - hull1.WaterVolume);
            hull1.WaterVolume += delta;

            if (hull1.WaterVolume > hull1.Volume) hull1.Pressure += 0.5f;

            flowTargetHull = hull1;

            if (IsHorizontal)
            {
                //water flowing from right to left
                if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                {
                    flowForce = new Vector2(-delta, 0.0f);
                    
                }
                else
                {
                    flowForce = new Vector2(delta, 0.0f);
                }

                higherSurface = hull1.Surface;
                lowerSurface = rect.Y;

                if (hull1.WaterVolume < hull1.Volume / Hull.MaxCompress &&
                    hull1.Surface < rect.Y)
                {
                    if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                    {
                        float vel = ((rect.Y - rect.Height / 2) - (hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1])) * 0.1f;
                        vel *= Math.Min(Math.Abs(flowForce.X) / 200.0f, 1.0f);

                        hull1.WaveVel[hull1.WaveY.Length - 1] += vel;
                        hull1.WaveVel[hull1.WaveY.Length - 2] += vel;
                    }
                    else
                    {
                        float vel = ((rect.Y - rect.Height / 2) - (hull1.Surface + hull1.WaveY[0])) * 0.1f;
                        vel *= Math.Min(Math.Abs(flowForce.X) / 200.0f, 1.0f);

                        hull1.WaveVel[0] += vel;
                        hull1.WaveVel[1] += vel;
                    } 
                }
                else
                {
                    hull1.LethalPressure += (Submarine != null && Submarine.AtDamageDepth) ? 100.0f * deltaTime : 10.0f * deltaTime;
                }
            }
            else
            {
                if (rect.Y > hull1.Rect.Y - hull1.Rect.Height / 2.0f)
                {
                    flowForce = new Vector2(0.0f, -delta);
                }
                else
                {
                    flowForce = new Vector2(0.0f, delta);
                }
                if (hull1.WaterVolume >= hull1.Volume / Hull.MaxCompress)
                {
                    hull1.LethalPressure += (Submarine != null && Submarine.AtDamageDepth) ? 100.0f * deltaTime : 10.0f * deltaTime;
                }
            }
        }

        public bool RefreshOutsideCollider()
        {
            if (IsRoomToRoom || Submarine == null || open <= 0.0f || linkedTo.Count == 0 || !(linkedTo[0] is Hull)) return false;

            if (outsideColliderRaycastTimer <= 0.0f)
            {
                UpdateOutsideColliderPos((Hull)linkedTo[0]);
                outsideColliderRaycastTimer = outsideCollisionBlocker.Enabled ?
                    OutsideColliderRaycastIntervalHighPrio :
                    OutsideColliderRaycastIntervalLowPrio;
            }

            return outsideCollisionBlocker.Enabled;
        }

        private void UpdateOutsideColliderPos(Hull hull)
        {
            if (Submarine == null || IsRoomToRoom || Level.Loaded == null) { return; }

            Vector2 rayDir;
            if (IsHorizontal)
            {
                rayDir = new Vector2(Math.Sign(rect.Center.X - hull.Rect.Center.X), 0);
            }
            else
            {
                rayDir = new Vector2(0, Math.Sign((rect.Y - rect.Height / 2) - (hull.Rect.Y - hull.Rect.Height / 2)));
            }

            Vector2 rayStart = ConvertUnits.ToSimUnits(WorldPosition);
            Vector2 rayEnd = rayStart + rayDir * 5.0f;

            var levelCells = Level.Loaded.GetCells(WorldPosition, searchDepth: 1);
            foreach (var cell in levelCells)
            {
                if (cell.IsPointInside(WorldPosition))
                {
                    outsideCollisionBlocker.Enabled = true;
                    Vector2 colliderPos = rayStart - Submarine.SimPosition;
                    float colliderRotation = MathUtils.VectorToAngle(rayDir) - MathHelper.PiOver2;
                    outsideCollisionBlocker.SetTransformIgnoreContacts(ref colliderPos, colliderRotation);
                    return;
                }
            }

            var blockingBody = Submarine.CheckVisibility(rayStart, rayEnd);
            if (blockingBody != null)
            {
                //if the ray hit the body of the submarine itself (for example, if there's 2 layers of walls) we can ignore it
                if (blockingBody.UserData == Submarine) { return; }
                outsideCollisionBlocker.Enabled = true;
                Vector2 colliderPos = Submarine.LastPickedPosition - Submarine.SimPosition;
                float colliderRotation = MathUtils.VectorToAngle(rayDir) - MathHelper.PiOver2;
                outsideCollisionBlocker.SetTransformIgnoreContacts(ref colliderPos, colliderRotation);
            }
            else
            {
                outsideCollisionBlocker.Enabled = false;
            }
        }

        private void UpdateOxygen(Hull hull1, Hull hull2)
        {
            if (hull1 == null || hull2 == null) { return; }

            if (IsHorizontal)
            {
                //if the water level is above the gap, oxygen doesn't circulate
                if (Math.Max(hull1.WorldSurface + hull1.WaveY[hull1.WaveY.Length - 1], hull2.WorldSurface + hull2.WaveY[0]) > WorldRect.Y) { return; }
            }

            float totalOxygen = hull1.Oxygen + hull2.Oxygen;
            float totalVolume = (hull1.Volume + hull2.Volume);
            
            float deltaOxygen = (totalOxygen * hull1.Volume / totalVolume) - hull1.Oxygen;
            deltaOxygen = MathHelper.Clamp(deltaOxygen, -Hull.OxygenDistributionSpeed, Hull.OxygenDistributionSpeed);

            hull1.Oxygen += deltaOxygen;
            hull2.Oxygen -= deltaOxygen;            
        }

        public static Gap FindAdjacent(IEnumerable<Gap> gaps, Vector2 worldPos, float allowedOrthogonalDist)
        {
            foreach (Gap gap in gaps)
            {
                if (gap.Open == 0.0f || gap.IsRoomToRoom) continue;

                if (gap.ConnectedWall != null)
                {
                    int sectionIndex = gap.ConnectedWall.FindSectionIndex(gap.Position);
                    if (sectionIndex > -1 && !gap.ConnectedWall.SectionBodyDisabled(sectionIndex)) continue;
                }

                if (gap.IsHorizontal)
                {
                    if (worldPos.Y < gap.WorldRect.Y && worldPos.Y > gap.WorldRect.Y - gap.WorldRect.Height &&
                        Math.Abs(gap.WorldRect.Center.X - worldPos.X) < allowedOrthogonalDist)
                    {
                        return gap;
                    }
                }
                else
                {
                    if (worldPos.X > gap.WorldRect.X && worldPos.X < gap.WorldRect.Right &&
                        Math.Abs(gap.WorldRect.Y - gap.WorldRect.Height / 2 - worldPos.Y) < allowedOrthogonalDist)
                    {
                        return gap;
                    }
                }
            }

            return null;
        }

        public override void ShallowRemove()
        {
            base.ShallowRemove();
            GapList.Remove(this);

            foreach (Hull hull in Hull.hullList)
            {
                hull.ConnectedGaps.Remove(this);
            }
        }

        public override void Remove()
        {
            base.Remove();
            GapList.Remove(this);

            foreach (Hull hull in Hull.hullList)
            {
                hull.ConnectedGaps.Remove(this);
            }

            if (outsideCollisionBlocker != null)
            {
                GameMain.World.Remove(outsideCollisionBlocker);
                outsideCollisionBlocker = null;
            }
        }

        public override void OnMapLoaded()
        {
            if (!DisableHullRechecks) FindHulls();
        }
        
        public static Gap Load(XElement element, Submarine submarine, IdRemap idRemap)
        {
            Rectangle rect = Rectangle.Empty;

            if (element.Attribute("rect") != null)
            {
                rect = element.GetAttributeRect("rect", Rectangle.Empty);
            }
            else
            {
                //backwards compatibility
                rect = new Rectangle(
                    int.Parse(element.Attribute("x").Value),
                    int.Parse(element.Attribute("y").Value),
                    int.Parse(element.Attribute("width").Value),
                    int.Parse(element.Attribute("height").Value));
            }

            bool isHorizontal = rect.Height > rect.Width;

            var horizontalAttribute = element.Attribute("horizontal");
            if (horizontalAttribute != null)
            {
                isHorizontal = horizontalAttribute.Value.ToString() == "true";
            }

            Gap g = new Gap(rect, isHorizontal, submarine, idRemap.GetOffsetId(element))
            {
                linkedToID = new List<ushort>(),
            };
            return g;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Gap");

            element.Add(
                new XAttribute("ID", ID),
                new XAttribute("horizontal", IsHorizontal ? "true" : "false"));

            element.Add(new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

            parentElement.Add(element);

            return element;
        }
    }
}
