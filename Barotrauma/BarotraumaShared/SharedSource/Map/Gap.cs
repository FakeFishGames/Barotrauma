using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Gap : MapEntity, ISerializableEntity
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

        /// <summary>
        /// "Diagonal" gaps are used on sloped walls to allow characters to pass through them either horizontally or vertically. 
        /// Water still flows through them only horizontally or vertically
        /// </summary>
        public bool IsDiagonal { get; }

        public readonly float GlowEffectT;

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
                if (float.IsNaN(value)) { return; }
                if (value > open)
                {
                    openedTimer = 1.0f;
                }
                if (connectedDoor == null && !IsHorizontal && linkedTo.Any(e => e is Hull))
                {
                    if (value > open && value >= 1.0f)
                    {
                        InformWaypointsAboutGapState(this, open: true);
                    }
                    else if (value < open && open >= 1.0f)
                    {
                        InformWaypointsAboutGapState(this, open: false);
                    }
                }
                open = MathHelper.Clamp(value, 0.0f, 1.0f);

                static void InformWaypointsAboutGapState(Gap gap, bool open)
                {
                    foreach (var wp in WayPoint.WayPointList)
                    {
                        if (IsWaypointRightAboveGap(gap, wp))
                        {
                            wp.OnGapStateChanged(open, gap);
                        }
                    }
                }

                static bool IsWaypointRightAboveGap(Gap gap, WayPoint wp)
                {
                    if (wp.SpawnType != SpawnType.Path) { return false; }
                    if (!gap.linkedTo.Contains(wp.CurrentHull)) { return false; }
                    if (wp.Position.Y < gap.Rect.Top) { return false; }
                    if (wp.Position.X > gap.Rect.Right) { return false; }
                    if (wp.Position.X < gap.Rect.Left) { return false; }
                    return true;
                }
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

        public override string Name => "Gap";

        public readonly Dictionary<Identifier, SerializableProperty> properties;
        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get { return properties; }
        }

        public Gap(Rectangle rectangle)
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

        public Gap(Rectangle rect, bool isHorizontal, Submarine submarine, bool isDiagonal = false, ushort id = Entity.NullEntityID)
            : base(CoreEntityPrefab.GapPrefab, submarine, id)
        {
            this.rect = rect;
            flowForce = Vector2.Zero;
            IsHorizontal = isHorizontal;
            IsDiagonal = isDiagonal;
            open = 1.0f;

            properties = SerializableProperty.GetProperties(this);

            FindHulls();
            GapList.Add(this);
            InsertToList();

            GlowEffectT = Rand.Range(0.0f, 1.0f);

            float blockerSize = ConvertUnits.ToSimUnits(Math.Max(rect.Width, rect.Height)) / 2;
            outsideCollisionBlocker = GameMain.World.CreateEdge(-Vector2.UnitX * blockerSize, Vector2.UnitX * blockerSize, 
                BodyType.Static, 
                Physics.CollisionWall, 
                Physics.CollisionCharacter,
                findNewContacts: false);
            outsideCollisionBlocker.UserData = $"CollisionBlocker (Gap {ID})";
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

        public override void Move(Vector2 amount, bool ignoreContacts = false)
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
            Hull hullLeft = Hull.FindHullUnoptimized(searchPosLeft, null, false);
            Vector2 searchPosRight = new Vector2(rect.Right, rect.Y - rect.Height / 2);
            Hull hullRight = Hull.FindHullUnoptimized(searchPosRight, null, false);

            if (hullLeft != null && hullRight != null && hullLeft != hullRight)
            {
                IsHorizontal = true;
                return;
            }

            Vector2 searchPosTop = new Vector2(rect.Center.X, rect.Y);
            Hull hullTop = Hull.FindHullUnoptimized(searchPosTop, null, false);
            Vector2 searchPosBottom = new Vector2(rect.Center.X, rect.Y - rect.Height);
            Hull hullBottom = Hull.FindHullUnoptimized(searchPosBottom, null, false);

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

            int tolerance = 1;
            Vector2[] searchPos = new Vector2[2];
            if (IsHorizontal)
            {
                searchPos[0] = new Vector2(rect.X - tolerance, rect.Y - rect.Height / 2);
                searchPos[1] = new Vector2(rect.Right + tolerance, rect.Y - rect.Height / 2);
            }
            else
            {
                searchPos[0] = new Vector2(rect.Center.X, rect.Y + tolerance);
                searchPos[1] = new Vector2(rect.Center.X, rect.Y - rect.Height - tolerance);
            }

            for (int i = 0; i < 2; i++)
            {
                hulls[i] = Hull.FindHullUnoptimized(searchPos[i], null, false);
                if (hulls[i] == null) { hulls[i] = Hull.FindHullUnoptimized(searchPos[i], null, false, true); }
            }

            if (hulls[0] != null || hulls[1] != null) 
            { 
                if (hulls[0] == null && hulls[1] != null)
                {
                    (hulls[1], hulls[0]) = (hulls[0], hulls[1]);
                }

                flowTargetHull = hulls[0];

                for (int i = 0; i < 2; i++)
                {
                    if (hulls[i] == null) { continue; }
                    linkedTo.Add(hulls[i]);
                    if (!hulls[i].ConnectedGaps.Contains(this)) { hulls[i].ConnectedGaps.Add(this); }
                }
            }

            RefreshOutsideCollider();
        }

        private int updateCount;

        public override void Update(float deltaTime, Camera cam)
        {
            int updateInterval = 4;
            float flowMagnitude = flowForce.LengthSquared();
            if (flowMagnitude < 1.0f)
            {
                //very sparse updates if there's practically no water moving
                updateInterval = 8;
            }
            else if (linkedTo.Count == 2 && flowMagnitude > 10.0f)
            {
                //frequent updates if water is moving between hulls
                updateInterval = 1;
            }

            updateCount++;
            if (updateCount < updateInterval) { return; }
            deltaTime *= updateCount;
            updateCount = 0;

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

            UpdateOxygen(hull1, hull2, deltaTime);

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
            if (openedTimer > 0.0f && flowForce.LengthSquared() > lerpedFlowForce.LengthSquared())
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

            if (hull1.WaterVolume <= 0.0 && hull2.WaterVolume <= 0.0) { return; }

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
                        if (!(hull2.WaterVolume > 0.0f)) { return; }
                        lowerSurface = hull1.Surface - hull1.WaveY[hull1.WaveY.Length - 1];
                        //delta = Math.Min((room2.water.pressure - room1.water.pressure) * sizeModifier, Math.Min(room2.water.Volume, room2.Volume));
                        //delta = Math.Min(delta, room1.Volume - room1.water.Volume + Water.MaxCompress);

                        flowTargetHull = hull1;

                        //make sure not to move more than what the room contains
                        delta = Math.Min(((hull2.Pressure + subOffset.Y) - hull1.Pressure) * 300.0f * sizeModifier * deltaTime, Math.Min(hull2.WaterVolume, hull2.Volume));

                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull1.Volume * Hull.MaxCompress - hull1.WaterVolume);
                        hull1.WaterVolume += delta;
                        hull2.WaterVolume -= delta;
                        if (hull1.WaterVolume > hull1.Volume)
                        {
                            hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + hull2.Pressure+subOffset.Y) / 2);
                        }

                        flowForce = new Vector2(-delta * (float)(Timing.Step / deltaTime), 0.0f);
                    }
                    else if (dir == 1)
                    {
                        if (!(hull1.WaterVolume > 0.0f)) { return; }
                        lowerSurface = hull2.Surface - hull2.WaveY[hull2.WaveY.Length - 1];

                        flowTargetHull = hull2;

                        //make sure not to move more than what the room contains
                        delta = Math.Min((hull1.Pressure - (hull2.Pressure + subOffset.Y)) * 300.0f * sizeModifier * deltaTime, Math.Min(hull1.WaterVolume, hull1.Volume));

                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull2.Volume * Hull.MaxCompress - hull2.WaterVolume);
                        hull1.WaterVolume -= delta;
                        hull2.WaterVolume += delta;
                        if (hull2.WaterVolume > hull2.Volume)
                        {
                            hull2.Pressure = Math.Max(hull2.Pressure, ((hull1.Pressure-subOffset.Y) + hull2.Pressure) / 2);
                        }

                        flowForce = new Vector2(delta * (float)(Timing.Step / deltaTime), 0.0f);
                    }

                    if (delta > 1.5f && subOffset == Vector2.Zero)
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
                        Math.Min(Math.Min((hull2.Pressure + subOffset.Y) - hull1.Pressure, 200.0f), delta * (float)(Timing.Step / deltaTime)));

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
                        MathHelper.Clamp(-delta * (float)(Timing.Step / deltaTime), -200.0f, 0.0f));

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

            if (hull1.WaterVolume > hull1.Volume) { hull1.Pressure += 30.0f * deltaTime; }

            flowTargetHull = hull1;

            if (IsHorizontal)
            {
                //water flowing from right to left
                if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                {
                    flowForce = new Vector2(-delta * (float)(Timing.Step / deltaTime), 0.0f);

                }
                else
                {
                    flowForce = new Vector2(delta * (float)(Timing.Step / deltaTime), 0.0f);
                }

                higherSurface = hull1.Surface;
                lowerSurface = rect.Y;

                if (hull1.WaterVolume < hull1.Volume / Hull.MaxCompress &&
                    hull1.Surface < rect.Y)
                {
                    //create a wave from the side of the hull the water is leaking from
                    if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                    {
                        CreateWave(rect, hull1, hull1.WaveY.Length - 1, hull1.WaveY.Length - 2, flowForce, deltaTime);
                    }
                    else
                    {
                        CreateWave(rect, hull1, 0, 1, flowForce, deltaTime);
                    }
                    static void CreateWave(Rectangle rect, Hull hull1, int index1, int index2, Vector2 flowForce, float deltaTime)
                    {
                        float vel = (rect.Y - rect.Height / 2) - (hull1.Surface + hull1.WaveY[index1]);
                        vel *= Math.Min(Math.Abs(flowForce.X) / 200.0f, 1.0f);
                        if (vel > 0.0f)
                        {
                            hull1.WaveVel[index1] += vel * deltaTime;
                            hull1.WaveVel[index2] += vel * deltaTime;
                        }
                    }
                }
                else
                {
                    hull1.LethalPressure += ((Submarine != null && Submarine.AtDamageDepth) ? 100.0f : 10.0f) * deltaTime;
                }
            }
            else
            {
                if (rect.Y > hull1.Rect.Y - hull1.Rect.Height / 2.0f)
                {
                    flowForce = new Vector2(0.0f, -delta * (float)(Timing.Step / deltaTime));
                }
                else
                {
                    flowForce = new Vector2(0.0f, delta * (float)(Timing.Step / deltaTime));
                }
                if (hull1.WaterVolume >= hull1.Volume / Hull.MaxCompress)
                {
                    hull1.LethalPressure += ((Submarine != null && Submarine.AtDamageDepth) ? 100.0f : 10.0f) * deltaTime;
                }
            }
        }

        public bool RefreshOutsideCollider()
        {
            if (outsideCollisionBlocker == null) { return false; }
            if (IsRoomToRoom || Submarine == null || open <= 0.0f || linkedTo.Count == 0 || linkedTo[0] is not Hull) 
            {
                outsideCollisionBlocker.Enabled = false;
                return false; 
            }

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

        private void UpdateOxygen(Hull hull1, Hull hull2, float deltaTime)
        {
            if (hull1 == null || hull2 == null) { return; }

            if (IsHorizontal)
            {
                //if the water level is above the gap, oxygen doesn't circulate
                if (Math.Max(hull1.WorldSurface + hull1.WaveY[hull1.WaveY.Length - 1], hull2.WorldSurface + hull2.WaveY[0]) > WorldRect.Y) { return; }
            }

            float totalOxygen = hull1.Oxygen + hull2.Oxygen;
            float totalVolume = hull1.Volume + hull2.Volume;

            float deltaOxygen = (totalOxygen * hull1.Volume / totalVolume) - hull1.Oxygen;
            deltaOxygen = MathHelper.Clamp(deltaOxygen, -Hull.OxygenDistributionSpeed * deltaTime, Hull.OxygenDistributionSpeed * deltaTime);

            hull1.Oxygen += deltaOxygen;
            hull2.Oxygen -= deltaOxygen;
        }

        public static Gap FindAdjacent(IEnumerable<Gap> gaps, Vector2 worldPos, float allowedOrthogonalDist)
        {
            foreach (Gap gap in gaps)
            {
                if (gap.Open == 0.0f || gap.IsRoomToRoom) { continue; }

                if (gap.ConnectedWall != null)
                {
                    int sectionIndex = gap.ConnectedWall.FindSectionIndex(gap.Position);
                    if (sectionIndex > -1 && !gap.ConnectedWall.SectionBodyDisabled(sectionIndex)) { continue; }
                }

                if (gap.IsHorizontal || gap.IsDiagonal)
                {
                    if (worldPos.Y < gap.WorldRect.Y && worldPos.Y > gap.WorldRect.Y - gap.WorldRect.Height &&
                        Math.Abs(gap.WorldRect.Center.X - worldPos.X) < allowedOrthogonalDist)
                    {
                        return gap;
                    }
                }
                if (!gap.IsHorizontal || gap.IsDiagonal)
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

            foreach (Hull hull in Hull.HullList)
            {
                hull.ConnectedGaps.Remove(this);
            }
        }

        public override void Remove()
        {
            base.Remove();
            GapList.Remove(this);

            foreach (Hull hull in Hull.HullList)
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

        public static Gap Load(ContentXElement element, Submarine submarine, IdRemap idRemap)
        {
            Rectangle rect;
            if (element.GetAttribute("rect") != null)
            {
                rect = element.GetAttributeRect("rect", Rectangle.Empty);
            }
            else
            {
                //backwards compatibility
                rect = new Rectangle(
                    int.Parse(element.GetAttribute("x").Value),
                    int.Parse(element.GetAttribute("y").Value),
                    int.Parse(element.GetAttribute("width").Value),
                    int.Parse(element.GetAttribute("height").Value));
            }

            bool isHorizontal = rect.Height > rect.Width;

            var horizontalAttribute = element.GetAttribute("horizontal");
            if (horizontalAttribute != null)
            {
                isHorizontal = horizontalAttribute.Value.ToString() == "true";
            }

            Gap g = new Gap(rect, isHorizontal, submarine, id: idRemap.GetOffsetId(element))
            {
                linkedToID = new List<ushort>(),
            };

            g.HiddenInGame = element.GetAttributeBool(nameof(HiddenInGame).ToLower(), g.HiddenInGame);
            return g;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Gap");

            element.Add(
                new XAttribute("ID", ID),
                new XAttribute("horizontal", IsHorizontal ? "true" : "false"),
                new XAttribute(nameof(HiddenInGame).ToLower(), HiddenInGame));

            element.Add(new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

            parentElement.Add(element);

            return element;
        }
    }
}
