using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    public partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        public const float AutopilotMinDistToPathNode = 30.0f;

        private const float AutopilotRayCastInterval = 0.5f;
        private const float RecalculatePathInterval = 5.0f;

        private const float AutoPilotSteeringLerp = 0.1f;

        private const float AutoPilotMaxSpeed = 0.5f;
        private const float AIPilotMaxSpeed = 1.0f;

        /// <summary>
        /// How many units before crush depth the pressure warning is shown
        /// </summary>
        public const float PressureWarningThreshold = 500.0f;

        /// <summary>
        /// How fast the steering vector adjusts when the nav terminal is operated by something else than a character (= signals)
        /// </summary>
        const float DefaultSteeringAdjustSpeed = 0.2f;

        private Vector2 targetVelocity;

        private Vector2 steeringInput;

        private bool autoPilot;

        private Vector2? posToMaintain;
        /// <summary>
        /// Set when <see cref="RefreshPosToMaintain"/> couldn't determine the controlled sub yet (no signal from the transducers yet).
        /// </summary>
        private bool posToMaintainRefreshPending;

        private SteeringPath steeringPath;

        private PathFinder pathFinder;

        private float networkUpdateTimer;
        private bool unsentChanges;

        private float autopilotRayCastTimer;
        private float autopilotRecalculatePathTimer;

        private Vector2 avoidStrength;

        private float neutralBallastLevel;

        private float steeringAdjustSpeed = 1.0f;

        private Character user;

        private Sonar sonar;

        private Submarine controlledSub;
        public Submarine ControlledSub => controlledSub;

        public float AIRamTimer { get; set; }
        private Vector2? aiTacticalTarget;
        bool navigateTactically;

        private bool showIceSpireWarning;

        private List<Submarine> connectedSubs = new List<Submarine>();
        private const float ConnectedSubUpdateInterval = 1.0f;
        float connectedSubUpdateTimer;

        private double lastReceivedSteeringSignalTime;

        [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes, description: "Is autopilot currently on or not?", AlwaysUseInstanceValues = true)]
        public bool AutoPilot
        {
            get { return autoPilot; }
            set
            {
                if (value == autoPilot) { return; }
                autoPilot = value;
#if CLIENT
                UpdateGUIElements();
#endif
                if (autoPilot)
                {
                    MaintainPos = true;
                    if (posToMaintain == null)
                    {
                        RefreshPosToMaintain();
                    }
                }
                else
                {
                    PosToMaintain = null;
                    MaintainPos = false;
                    LevelEndSelected = false;
                    LevelStartSelected = false;
                }
            }
        }

        [Editable(0.0f, 1.0f, decimals: 4),
        Serialize(0.5f, IsPropertySaveable.Yes, description: "How full the ballast tanks should be when the submarine is not being steered upwards/downwards."
            + " Can be used to compensate if the ballast tanks are too large/small relative to the size of the submarine.")]
        public float NeutralBallastLevel
        {
            get { return neutralBallastLevel; }
            set
            {
                neutralBallastLevel = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Serialize(1000.0f, IsPropertySaveable.Yes, description: "How close the docking port has to be to another docking port for the docking mode to become active.")]
        public float DockingAssistThreshold
        {
            get;
            set;
        }

        public Vector2 TargetVelocity
        {
            get { return targetVelocity; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    if (!MathUtils.IsValid(targetVelocity))
                    {
                        targetVelocity = Vector2.Zero;
                    }
                    return;
                }
                targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }

        public float TargetVelocityLengthSquared
        {
            get => TargetVelocity.LengthSquared();
        }

        public Vector2 SteeringInput
        {
            get { return steeringInput; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                steeringInput.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                steeringInput.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }

        public SteeringPath SteeringPath
        {
            get { return steeringPath; }
        }

        public Vector2? PosToMaintain
        {
            get { return posToMaintain; }
            set { posToMaintain = value; }
        }

        struct ObstacleDebugInfo
        {
            public Vector2 Point1;
            public Vector2 Point2;

            public Vector2? Intersection;

            public float Dot;

            public Vector2 AvoidStrength;

            public ObstacleDebugInfo(GraphEdge edge, Vector2? intersection, float dot, Vector2 avoidStrength, Vector2 translation)
            {
                Point1 = edge.Point1 + translation;
                Point2 = edge.Point2 + translation;
                Intersection = intersection;
                Dot = dot;
                AvoidStrength = avoidStrength;
            }
        }

        //edge point 1, edge point 2, avoid strength
        private List<ObstacleDebugInfo> debugDrawObstacles = new List<ObstacleDebugInfo>();

        #region Docking
        public List<DockingPort> DockingSources = new List<DockingPort>();
        private bool searchedConnectedDockingPort;

        private bool dockingModeEnabled;
        public bool DockingModeEnabled
        {
            get { return UseAutoDocking && dockingModeEnabled; }
            set { dockingModeEnabled = value; }
        }

        public bool UseAutoDocking
        {
            get;
            set;
        } = true;

        private void FindConnectedDockingPort()
        {
            searchedConnectedDockingPort = true;
            foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item item)
                {
                    var port = item.GetComponent<DockingPort>();
                    if (port != null)
                    {
                        DockingSources.Add(port);
                    }
                }
            }

            var dockingConnection = item.Connections.FirstOrDefault(c => c.Name == "toggle_docking");
            if (dockingConnection != null)
            {
                var connectedPorts = item.GetConnectedComponentsRecursive<DockingPort>(dockingConnection, allowTraversingBackwards: false);
                DockingSources.AddRange(connectedPorts.Where(p => p.Item.Submarine != null && !p.Item.Submarine.Info.IsOutpost));
            }
        }
        #endregion

        public Steering(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            sonar = item.GetComponent<Sonar>();
        }

        /// <summary>
        /// Sets the position the autopilot tries to maintain to the current position of the sub.
        /// </summary>
        public void RefreshPosToMaintain()
        {
            //controlledSub is normally refreshed in Update, make sure it's up to date
            //(e.g. if we're refreshing the position right after loading, before the item has been updated)
            RefreshControlledSub();
            posToMaintain = controlledSub != null ?
                controlledSub.WorldPosition :
                item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
            //if we're controlling the sub via transducers, but haven't received a signal from them yet (e.g. right after loading),
            //we don't know which sub we're controlling yet: refresh the position again once we do (see Update)
            posToMaintainRefreshPending = controlledSub == null && item.GetComponent<Sonar>() is { UseTransducers: true };
        }

        /// <summary>
        /// Refreshes which submarine this steering component controls: either the sub the item is in, or the sub the connected sonar transducers are in.
        /// </summary>
        private void RefreshControlledSub()
        {
            controlledSub = item.Submarine;
            var sonar = item.GetComponent<Sonar>();
            if (sonar != null && sonar.UseTransducers)
            {
                controlledSub = sonar.ConnectedTransducers.Any() ? sonar.ConnectedTransducers.First().Item.Submarine : null;
            }
        }

        public override void OnMapLoaded()
        {
            if (MaintainPos)
            {
                RefreshPosToMaintain();
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!searchedConnectedDockingPort)
            {
                FindConnectedDockingPort();
            }
            networkUpdateTimer -= deltaTime;
            if (unsentChanges)
            {
                if (networkUpdateTimer <= 0.0f)
                {
#if CLIENT
                    if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }
#endif
#if SERVER
                    item.CreateServerEvent(this);
#endif

                    networkUpdateTimer = 0.1f;
                    unsentChanges = false;
                }
            }

            RefreshControlledSub();
            if (posToMaintainRefreshPending && controlledSub != null)
            {
                posToMaintainRefreshPending = false;
                if (MaintainPos && posToMaintain.HasValue)
                {
                    posToMaintain = controlledSub.WorldPosition;
#if SERVER
                    unsentChanges = true;
#endif
                }
            }

            if (!HasPower) { return; }

            if (user != null && user.Removed)
            {
                user = null;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            float userSkill = 0.0f;
            if (user != null && controlledSub != null &&
                (user.SelectedItem == item || item.linkedTo.Contains(user.SelectedItem)))
            {
                userSkill = user.GetSkillLevel(Tags.HelmSkill) / 100.0f;
            }

            // override autopilot pathing while the AI rams, and go full speed ahead
            if (AIRamTimer > 0f && aiTacticalTarget.HasValue && controlledSub != null)
            {
                AIRamTimer -= deltaTime;
                TargetVelocity = GetSteeringVelocity(aiTacticalTarget.Value, 0f);
            }
            else if (AutoPilot)
            {
                //signals override autopilot for a duration of one second
                if (lastReceivedSteeringSignalTime < Timing.TotalTime - 1)
                {
                    UpdateAutoPilot(deltaTime);
                    float throttle = 1.0f;
                    if (controlledSub != null)
                    {
                        //if the sub is heading in the correct direction, throttle the speed according to the user's skill
                        //if it's e.g. sinking due to extra water, don't throttle, but allow emptying up the ballast completely
                        throttle = MathHelper.Clamp(Vector2.Dot(controlledSub.Velocity, TargetVelocity) / 100.0f, 0.0f, 1.0f);
                    }
                    float maxSpeed = MathHelper.Lerp(AutoPilotMaxSpeed, AIPilotMaxSpeed, userSkill) * 100.0f;
                    TargetVelocity = TargetVelocity.ClampLength(MathHelper.Lerp(100.0f, maxSpeed, throttle));
                }
            }
            else
            {
                showIceSpireWarning = false;
                if (user != null && user.Info != null && 
                    user.SelectedItem == item)
                {
                    IncreaseSkillLevel(user, deltaTime);
                }

                Vector2 velocityDiff = steeringInput - targetVelocity;
                if (velocityDiff != Vector2.Zero)
                {
                    if (steeringAdjustSpeed >= 0.99f)
                    {
                        TargetVelocity = steeringInput;
                    }
                    else
                    {
                        float steeringChange = 1.0f / (1.0f - steeringAdjustSpeed);
                        steeringChange *= steeringChange * 10.0f;

                        TargetVelocity += Vector2.Normalize(velocityDiff) * 
                            Math.Min(steeringChange * deltaTime, velocityDiff.Length());
                    }
                }
            }

            float velX = targetVelocity.X;
            if (controlledSub != null && controlledSub.FlippedX) { velX *= -1; }
            item.SendSignal(new Signal(velX.ToString(CultureInfo.InvariantCulture), sender: user), "velocity_x_out");

            float velY = MathHelper.Lerp((neutralBallastLevel * 100 - 50) * 2, -100 * Math.Sign(targetVelocity.Y), Math.Abs(targetVelocity.Y) / 100.0f);
            item.SendSignal(new Signal(velY.ToString(CultureInfo.InvariantCulture), sender: user), "velocity_y_out");

            // converts the controlled sub's velocity to km/h and sends it. 
            if (controlledSub is { } sub)
            {
                item.SendSignal(new Signal((ConvertUnits.ToDisplayUnits(sub.Velocity.X * Physics.DisplayToRealWorldRatio) * 3.6f).ToString("0.0000", CultureInfo.InvariantCulture), sender: user), "current_velocity_x");
                item.SendSignal(new Signal((ConvertUnits.ToDisplayUnits(sub.Velocity.Y * Physics.DisplayToRealWorldRatio) * -3.6f).ToString("0.0000", CultureInfo.InvariantCulture), sender: user), "current_velocity_y");

                Vector2 pos = new Vector2(sub.WorldPosition.X * Physics.DisplayToRealWorldRatio, sub.RealWorldDepth);
                if (sonar != null && sonar.UseTransducers && sonar.CenterOnTransducers && sonar.ConnectedTransducers.Any())
                {
                    pos = Vector2.Zero;
                    foreach (var connectedTransducer in sonar.ConnectedTransducers)
                    {
                        pos += connectedTransducer.Item.WorldPosition;
                    }
                    pos /= sonar.ConnectedTransducers.Count();
                    pos = new Vector2(
                        pos.X * Physics.DisplayToRealWorldRatio,
                        Level.Loaded?.GetRealWorldDepth(pos.Y) ?? (-pos.Y * Physics.DisplayToRealWorldRatio));
                }

                item.SendSignal(new Signal(pos.X.ToString("0.0000", CultureInfo.InvariantCulture), sender: user), "current_position_x");
                item.SendSignal(new Signal(pos.Y.ToString("0.0000", CultureInfo.InvariantCulture), sender: user), "current_position_y");
            }

            // if our tactical AI pilot has left, revert back to maintaining position
            if (navigateTactically && (user == null || user.SelectedItem != item))
            {
                navigateTactically = false;
                AIRamTimer = 0f;
                SetMaintainPosition();
            }
        }

        private void IncreaseSkillLevel(Character user, float deltaTime)
        {
            if (controlledSub == null) { return; }
            if (controlledSub.Velocity.LengthSquared() < 0.01f) { return; }
            if (user?.Info == null) { return; }
            // Do not increase the helm skill when "steering" the sub while docked into something static (e.g. outpost or wreck)
            if (GameMain.GameSession?.Campaign != null&& controlledSub.DockedTo.Any(d => d.PhysicsBody.BodyType == BodyType.Static)) { return; }

            float speedMultiplier = MathHelper.Clamp(TargetVelocity.Length() / 100.0f, 0.0f, 1.0f);
            user.Info.ApplySkillGain(Tags.HelmSkill,
                SkillSettings.Current.SkillIncreasePerSecondWhenSteering * speedMultiplier * deltaTime);
        }

        public bool CanSeePosition(Vector2 worldPosition)
        {
            Vector2 diff = ConvertUnits.ToSimUnits(worldPosition - controlledSub.WorldPosition);
            //check if the next waypoint is visible from all corners of the sub
            //(i.e. if we can navigate directly towards it or if there's obstacles in the way)
            for (int x = -1; x < 2; x += 2)
            {
                for (int y = -1; y < 2; y += 2)
                {
                    Vector2 cornerOffset =
                        new Vector2(controlledSub.Borders.Width * x, controlledSub.Borders.Height * y) / 2.0f;
                    Vector2 cornerPos = ConvertUnits.ToSimUnits(cornerOffset * 1.1f + controlledSub.WorldPosition);
                    if (Submarine.PickBody(cornerPos, cornerPos + diff, 
                        ignoredBodies: item.Submarine?.PhysicsBody?.FarseerBody.ToEnumerable(),
                        collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) == null)
                    { 
                        continue; 
                    }
                    return false;
                }
            }
            return true;
        }

        private void UpdateAutoPilot(float deltaTime)
        {
            if (controlledSub == null) { return; }
            if (posToMaintain != null)
            {
                Vector2 steeringVel = GetSteeringVelocity((Vector2)posToMaintain, 10.0f);
                TargetVelocity = Vector2.Lerp(TargetVelocity, steeringVel, AutoPilotSteeringLerp);
                showIceSpireWarning = false;
                return;
            }

            autopilotRayCastTimer -= deltaTime;
            autopilotRecalculatePathTimer -= deltaTime;
            if (autopilotRecalculatePathTimer <= 0.0f)
            {
                //periodically recalculate the path in case the sub ends up to a position 
                //where it can't keep traversing the initially calculated path
                UpdatePath();
                autopilotRecalculatePathTimer = RecalculatePathInterval;
            }

            if (steeringPath == null)
            {
                showIceSpireWarning = false;
                return;
            }

            steeringPath.CheckProgress(controlledSub.WorldPosition, minDistance: 1000.0f);

            connectedSubUpdateTimer -= deltaTime;
            if (connectedSubUpdateTimer <= 0.0f)
            {
                connectedSubs.Clear();
                connectedSubs.AddRange(controlledSub.GetConnectedSubs());                
                connectedSubUpdateTimer = ConnectedSubUpdateInterval;
            }

            if (autopilotRayCastTimer <= 0.0f && steeringPath.NextNode != null)
            {
                Vector2 diff = ConvertUnits.ToSimUnits(steeringPath.NextNode.Position - controlledSub.WorldPosition);
                //if the node is close enough, check if it's visible
                float lengthSqr = diff.LengthSquared();
                if (lengthSqr > 0.001f && lengthSqr < AutopilotMinDistToPathNode * AutopilotMinDistToPathNode)
                {
                    if (CanSeePosition(steeringPath.NextNode.Position)) { steeringPath.SkipToNextNode(); }
                }

                autopilotRayCastTimer = AutopilotRayCastInterval;
            }

            Vector2 newVelocity = Vector2.Zero;
            if (steeringPath.CurrentNode != null)
            {
                newVelocity = GetSteeringVelocity(steeringPath.CurrentNode.WorldPosition, 2.0f);
            }

            Vector2 avoidDist = new Vector2(
                Math.Max(1000.0f * Math.Abs(controlledSub.Velocity.X), controlledSub.Borders.Width * 0.75f),
                Math.Max(1000.0f * Math.Abs(controlledSub.Velocity.Y), controlledSub.Borders.Height * 0.75f));

            float avoidRadius = avoidDist.Length();
            float damagingWallAvoidRadius = MathHelper.Clamp(avoidRadius * 1.5f, 5000.0f, 10000.0f);

            Vector2 newAvoidStrength = Vector2.Zero;

            debugDrawObstacles.Clear();

            //steer away from nearby walls
            showIceSpireWarning = false;
            var closeCells = Level.Loaded.GetCells(controlledSub.WorldPosition, 4);
            foreach (VoronoiCell cell in closeCells)
            {
                if (cell.DoesDamage || cell.Body is { BodyType: BodyType.Dynamic })
                {
                    foreach (GraphEdge edge in cell.Edges)
                    {
                        Vector2 closestPoint = MathUtils.GetClosestPointOnLineSegment(edge.Point1 + cell.Translation, edge.Point2 + cell.Translation, controlledSub.WorldPosition);
                        Vector2 diff = closestPoint - controlledSub.WorldPosition;
                        float dist = diff.Length() - Math.Max(controlledSub.Borders.Width, controlledSub.Borders.Height) / 2;
                        if (dist > damagingWallAvoidRadius) { continue; }

                        Vector2 normalizedDiff = Vector2.Normalize(diff);
                        float dot = Vector2.Dot(normalizedDiff, controlledSub.Velocity);

                        float avoidStrength = MathHelper.Clamp(MathHelper.Lerp(1.0f, 0.0f, dist / damagingWallAvoidRadius - dot), 0.0f, 1.0f);
                        Vector2 avoid = -normalizedDiff * avoidStrength;
                        newAvoidStrength += avoid;
                        debugDrawObstacles.Add(new ObstacleDebugInfo(edge, edge.Center, 1.0f, avoid, cell.Translation));

                        if (dot > 0.0f && cell.DoesDamage)
                        {
                            showIceSpireWarning = true;
                        }
                    }
                    continue;
                }

                foreach (GraphEdge edge in cell.Edges)
                {
                    if (MathUtils.GetLineSegmentIntersection(edge.Point1 + cell.Translation, edge.Point2 + cell.Translation, controlledSub.WorldPosition, cell.Center, out Vector2 intersection))
                    {
                        Vector2 diff = controlledSub.WorldPosition - intersection;
                        //far enough -> ignore
                        if (Math.Abs(diff.X) > avoidDist.X && Math.Abs(diff.Y) > avoidDist.Y)
                        {
                            debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, 0.0f, Vector2.Zero, Vector2.Zero));
                            continue;
                        }
                        if (diff.LengthSquared() < 1.0f) { diff = Vector2.UnitY; }

                        Vector2 normalizedDiff = Vector2.Normalize(diff);
                        float dot = controlledSub.Velocity == Vector2.Zero ?
                            0.0f : Vector2.Dot(controlledSub.Velocity, -normalizedDiff);

                        //not heading towards the wall -> ignore
                        if (dot < 1.0)
                        {
                            debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, dot, Vector2.Zero, cell.Translation));
                            continue;
                        }
                        
                        Vector2 change = (normalizedDiff * Math.Max((avoidRadius - diff.Length()), 0.0f)) / avoidRadius;
                        if (change.LengthSquared() < 0.001f) { continue; }
                        newAvoidStrength += change * (dot - 1.0f);
                        debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, dot - 1.0f, change * (dot - 1.0f), cell.Translation));
                    }
                }
            }


            //steer away from other subs
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == controlledSub || connectedSubs.Contains(sub)) { continue; }
                Point sizeSum = controlledSub.Borders.Size + sub.Borders.Size;
                Vector2 minDist = sizeSum.ToVector2() / 2;
                Vector2 diff = controlledSub.WorldPosition - sub.WorldPosition;
                //guard against NaN: if the subs were somehow at almost the same position, consider the sub to be above and try avoiding downwards
                if (diff.NearlyEquals(Vector2.Zero)) { diff = Vector2.One; }
                float xDist = Math.Abs(diff.X);
                float yDist = Math.Abs(diff.Y);
                //20% of margin
                Vector2 maxAvoidDistance = minDist * 1.2f;
                if (xDist > maxAvoidDistance.X || yDist > maxAvoidDistance.Y)
                {
                    //far enough -> ignore
                    continue;
                }
                Vector2 normalizedDiff = Vector2.Normalize(diff);
                float dot = controlledSub.Velocity == Vector2.Zero ? 
                    0.0f : 
                    Vector2.Dot(Vector2.Normalize(controlledSub.Velocity), -normalizedDiff);
                if (dot < 0.0f)
                {
                    //heading away -> ignore
                    continue;
                }
                float distanceFactor = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(maxAvoidDistance.X + maxAvoidDistance.Y, minDist.X + minDist.Y, xDist + yDist));
                float velocityFactor = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, 3, controlledSub.Velocity.Length()));
                newAvoidStrength += 100 * normalizedDiff * distanceFactor * velocityFactor * dot;
            }

            avoidStrength = Vector2.Lerp(avoidStrength, newAvoidStrength, deltaTime * 10.0f);
            TargetVelocity = Vector2.Lerp(TargetVelocity, newVelocity + avoidStrength * 100.0f, AutoPilotSteeringLerp);

            //clamp velocity magnitude to 100.0f (Is this required? The X and Y components are clamped in the property setter)
            float velMagnitude = TargetVelocity.Length();
            if (velMagnitude > 100.0f)
            {
                TargetVelocity *= 100.0f / velMagnitude;
            }

#if CLIENT
            HintManager.OnAutoPilotPathUpdated(this);
#endif
        }

        private float? GetNodePenalty(PathNode node, PathNode nextNode)
        {
            if (node.Waypoint?.Tunnel == null || controlledSub == null || node.Waypoint.Tunnel.Type == Level.TunnelType.MainPath) { return 0.0f; }
            //never navigate from the main path to another type of path
            if (node.Waypoint.Tunnel.Type == Level.TunnelType.MainPath && nextNode.Waypoint?.Tunnel?.Type != Level.TunnelType.MainPath) { return null; }
            //higher cost for side paths (= autopilot prefers the main path, but can still navigate side paths if it ends up on one)
            return 1000.0f;
        }

        private void UpdatePath()
        {
            if (Level.Loaded == null) { return; }

            if (pathFinder == null)
            {
                pathFinder = new PathFinder(WayPoint.WayPointList, false)
                {
                    GetNodePenalty = GetNodePenalty
                };
            }

            Vector2 target;

            if (navigateTactically && aiTacticalTarget.HasValue)
            {
                target = ConvertUnits.ToSimUnits(aiTacticalTarget.Value);
            }
            else if (LevelEndSelected)
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.EndExitPosition);
            }
            else
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.StartExitPosition);
            }
            steeringPath = pathFinder.FindPath(ConvertUnits.ToSimUnits(controlledSub == null ? item.WorldPosition : controlledSub.WorldPosition), target, errorMsgStr: "(Autopilot, target: " + target + ")");
        }

        public void SetDestinationLevelStart()
        {
            AutoPilot = true;
            MaintainPos = false;
            posToMaintain = null;
            LevelEndSelected = false;
            navigateTactically = false;
            if (!LevelStartSelected)
            {
                LevelStartSelected = true;
                UpdatePath();
            }
        }

        public void SetDestinationLevelEnd()
        {
            AutoPilot = true;
            MaintainPos = false;
            posToMaintain = null;
            LevelStartSelected = false;
            navigateTactically = false;
            if (!LevelEndSelected)
            {
                LevelEndSelected = true;
                UpdatePath();
            }
        }

        /// <summary>
        /// Makes the AI use "tactical navigation", navigating manually towards a specific position instead of using the level start or end as the target.
        /// If the targetPosition is omitted, the steering will keep the previously set <see cref="aiTacticalTarget"/>.
        /// </summary>
        /// <param name="targetPosition"></param>
        public void SetDestinationTactical(Vector2? targetPosition)
        {
            if (targetPosition != null)
            {
                aiTacticalTarget = targetPosition.Value;
            }
            AutoPilot = true;
            MaintainPos = false;
            posToMaintain = null;
            LevelStartSelected = false;
            LevelEndSelected = false;
            if (!navigateTactically)
            {
                navigateTactically = true;
                UpdatePath();
            }
        }

        public void SetMaintainPosition()
        {
            if (!MaintainPos)
            {
                unsentChanges = true;
                MaintainPos = true;
            }
            if (!posToMaintain.HasValue)
            {
                unsentChanges = true;
                RefreshControlledSub();
                posToMaintain = controlledSub != null ?
                    controlledSub.WorldPosition :
                    item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
            }
            navigateTactically = false;
        }

        /// <summary>
        /// Get optimal velocity for moving towards a position
        /// </summary>
        /// <param name="worldPosition">Position to steer towards to</param>
        /// <param name="slowdownAmount">How heavily the sub slows down when approaching the target</param>
        /// <returns></returns>
        private Vector2 GetSteeringVelocity(Vector2 worldPosition, float slowdownAmount)
        {
            Vector2 futurePosition = ConvertUnits.ToDisplayUnits(controlledSub.Velocity) * slowdownAmount;
            Vector2 targetSpeed = ((worldPosition - controlledSub.WorldPosition) - futurePosition);

            if (targetSpeed.LengthSquared() > 500.0f * 500.0f)
            {                
                return Vector2.Normalize(targetSpeed) * 100.0f;
            }
            else
            {
                return targetSpeed / 5.0f;
            }
        }

        public override bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            character.AIController.SteeringManager.Reset();
            if (objective.Override)
            {
                if (user != character && user != null && user.SelectedItem == item && character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.Get("DialogSteeringTaken").Value, null, 0.0f, "steeringtaken".ToIdentifier(), 10.0f);
                }
            }
            user = character;

            if (Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(Item, character, allowInAnySub: true))
            {
                if (Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveRepairItem(character, Item, objective.objectiveManager, isPriority: true));
                    return false;
                }
                else
                {
                    character.Speak(TextManager.Get("DialogNavTerminalIsBroken").Value, identifier: "navterminalisbroken".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                }
            }

            if (!AutoPilot)
            {
                unsentChanges = true;
                AutoPilot = true;
            }
            IncreaseSkillLevel(user, deltaTime);
            if (objective.Option == "maintainposition")
            {
                if (objective.Override)
                {
                    SetMaintainPosition();
                }
            }
            else if (!Level.IsLoadedOutpost)
            {
                if (objective.Option == "navigateback")
                {
                    if (DockingSources.Any(d => d.Docked))
                    {
                        item.SendSignal("1", "toggle_docking");
                    }

                    if (objective.Override)
                    {
                        if (MaintainPos || LevelEndSelected || !LevelStartSelected || navigateTactically)
                        {
                            unsentChanges = true;
                        }

                        SetDestinationLevelStart();
                    }
                }
                else if (objective.Option == "navigatetodestination")
                {
                    if (DockingSources.Any(d => d.Docked))
                    {
                        item.SendSignal("1", "toggle_docking");
                    }

                    if (objective.Override)
                    {
                        if (MaintainPos || !LevelEndSelected || LevelStartSelected || navigateTactically)
                        {
                            unsentChanges = true;
                        }

                        SetDestinationLevelEnd();
                    }
                }
                else if (objective.Option == "navigatetactical")
                {
                    if (DockingSources.Any(d => d.Docked))
                    {
                        item.SendSignal("1", "toggle_docking");
                    }

                    if (objective.Override)
                    {
                        if (MaintainPos || LevelEndSelected || LevelStartSelected || !navigateTactically)
                        {
                            unsentChanges = true;
                        }
                        SetDestinationTactical(targetPosition: null);
                    }
                }
            }

            sonar?.CrewAIOperate(deltaTime, character, objective);
            if (!MaintainPos && showIceSpireWarning && character.IsOnPlayerTeam)
            {
                character.Speak(TextManager.Get("dialogicespirespottedsonar").Value, null, 0.0f, "icespirespottedsonar".ToIdentifier(), 60.0f);
            }
            return false;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (connection.Name == "velocity_in")
            {
                steeringAdjustSpeed = DefaultSteeringAdjustSpeed;
                steeringInput = XMLExtensions.ParseVector2(signal.value, errorMessages: false);
                steeringInput.X = MathHelper.Clamp(steeringInput.X, -100.0f, 100.0f);
                steeringInput.Y = MathHelper.Clamp(-steeringInput.Y, -100.0f, 100.0f);
                TargetVelocity = steeringInput;
                lastReceivedSteeringSignalTime = Timing.TotalTime;
            }
            else
            {
                base.ReceiveSignal(signal, connection);
            }
        }
    }
}
