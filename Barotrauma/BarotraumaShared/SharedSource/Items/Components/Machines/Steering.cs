using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        public const float AutopilotMinDistToPathNode = 30.0f;

        private const float AutopilotRayCastInterval = 0.5f;
        private const float RecalculatePathInterval = 5.0f;

        private const float AutoPilotSteeringLerp = 0.1f;

        private const float AutoPilotMaxSpeed = 0.5f;
        private const float AIPilotMaxSpeed = 1.0f;

        /// <summary>
        /// How fast the steering vector adjusts when the nav terminal is operated by something else than a character (= signals)
        /// </summary>
        const float DefaultSteeringAdjustSpeed = 0.2f;

        private Vector2 targetVelocity;

        private Vector2 steeringInput;

        private bool autoPilot;

        private Vector2? posToMaintain;

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

        // AI interfacing
        public Vector2 AITacticalTarget { get; set; }
        public float AIRamTimer { get; set; }
        bool navigateTactically; // this will be removed after rewriting steering to use an enum

        private bool showIceSpireWarning;

        private List<Submarine> connectedSubs = new List<Submarine>();
        private const float ConnectedSubUpdateInterval = 1.0f;
        float connectedSubUpdateTimer;

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
                    if (pathFinder == null)
                    {
                        pathFinder = new PathFinder(WayPoint.WayPointList, false)
                        {
                            GetNodePenalty = GetNodePenalty
                        };
                    }
                    MaintainPos = true;
                    if (posToMaintain == null)
                    {
                        posToMaintain = controlledSub != null ?
                            controlledSub.WorldPosition :
                            item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
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
        Serialize(0.5f, true, description: "How full the ballast tanks should be when the submarine is not being steered upwards/downwards."
            + " Can be used to compensate if the ballast tanks are too large/small relative to the size of the submarine.")]
        public float NeutralBallastLevel
        {
            get { return neutralBallastLevel; }
            set
            {
                neutralBallastLevel = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Serialize(1000.0f, true, description: "How close the docking port has to be to another docking port for the docking mode to become active.")]
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

        public override bool RecreateGUIOnResolutionChange => true;

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
                var connectedPorts = item.GetConnectedComponentsRecursive<DockingPort>(dockingConnection);
                DockingSources.AddRange(connectedPorts.Where(p => p.Item.Submarine != null && !p.Item.Submarine.Info.IsOutpost));
            }
        }
        #endregion

        public Steering(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            sonar = item.GetComponent<Sonar>();
        }

        public override bool Select(Character character)
        {
            if (!CanBeSelected) return false;

            user = character;
            return true;
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
                    else
#endif
#if SERVER
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                    }
#endif

                    networkUpdateTimer = 0.1f;
                    unsentChanges = false;
                }
            }

            controlledSub = item.Submarine;
            var sonar = item.GetComponent<Sonar>();
            if (sonar != null && sonar.UseTransducers)
            {
                controlledSub = sonar.ConnectedTransducers.Any() ? sonar.ConnectedTransducers.First().Item.Submarine : null;
            }

            currPowerConsumption = powerConsumption;

            if (Voltage < MinVoltage) { return; }

            if (user != null && user.Removed)
            {
                user = null;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            float userSkill = 0.0f;
            if (user != null && controlledSub != null &&
                (user.SelectedConstruction == item || item.linkedTo.Contains(user.SelectedConstruction)))
            {
                userSkill = user.GetSkillLevel("helm") / 100.0f;
            }

            // override autopilot pathing while the AI rams, and go full speed ahead
            if (AIRamTimer > 0f && controlledSub != null)
            {
                AIRamTimer -= deltaTime;
                TargetVelocity = GetSteeringVelocity(AITacticalTarget, 0f);
            }
            else if (AutoPilot)
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
            else
            {
                showIceSpireWarning = false;
                if (user != null && user.Info != null && 
                    user.SelectedConstruction == item && 
                    controlledSub != null && controlledSub.Velocity.LengthSquared() > 0.01f)
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
            if (navigateTactically && (user == null || user.SelectedConstruction != item))
            {
                navigateTactically = false;
                AIRamTimer = 0f;
                SetMaintainPosition();
            }
        }

        private void IncreaseSkillLevel(Character user, float deltaTime)
        {
            if (user?.Info == null) { return; }
            // Do not increase the helm skill when "steering" the sub in an outpost level
            if (GameMain.GameSession?.Campaign != null && Level.IsLoadedOutpost) { return; }

            float userSkill = Math.Max(user.GetSkillLevel("helm"), 1.0f) / 100.0f;
            user.Info.IncreaseSkillLevel(
                "helm",
                SkillSettings.Current.SkillIncreasePerSecondWhenSteering / userSkill * deltaTime);
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
            steeringPath.CheckProgress(ConvertUnits.ToSimUnits(controlledSub.WorldPosition), 10.0f);

            connectedSubUpdateTimer -= deltaTime;
            if (connectedSubUpdateTimer <= 0.0f)
            {
                connectedSubs.Clear();
                connectedSubs = controlledSub?.GetConnectedSubs();                
                connectedSubUpdateTimer = ConnectedSubUpdateInterval;
            }

            if (autopilotRayCastTimer <= 0.0f && steeringPath.NextNode != null)
            {
                Vector2 diff = ConvertUnits.ToSimUnits(steeringPath.NextNode.Position - controlledSub.WorldPosition);

                //if the node is close enough, check if it's visible
                float lengthSqr = diff.LengthSquared();
                if (lengthSqr > 0.001f && lengthSqr < AutopilotMinDistToPathNode * AutopilotMinDistToPathNode)
                {
                    diff = Vector2.Normalize(diff);

                    //check if the next waypoint is visible from all corners of the sub
                    //(i.e. if we can navigate directly towards it or if there's obstacles in the way)
                    bool nextVisible = true;
                    for (int x = -1; x < 2; x += 2)
                    {
                        for (int y = -1; y < 2; y += 2)
                        {
                            Vector2 cornerPos =
                                new Vector2(controlledSub.Borders.Width * x, controlledSub.Borders.Height * y) / 2.0f;

                            cornerPos = ConvertUnits.ToSimUnits(cornerPos * 1.1f + controlledSub.WorldPosition);

                            float dist = Vector2.Distance(cornerPos, steeringPath.NextNode.SimPosition);

                            if (Submarine.PickBody(cornerPos, cornerPos + diff * dist, null, Physics.CollisionLevel) == null) { continue; }

                            nextVisible = false;
                            x = 2;
                            y = 2;
                        }
                    }

                    if (nextVisible) steeringPath.SkipToNextNode();
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
                if (cell.DoesDamage)
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

                        if (dot > 0.0f)
                        {
                            showIceSpireWarning = true;
                        }
                    }
                    continue;
                }

                foreach (GraphEdge edge in cell.Edges)
                {
                    if (MathUtils.GetLineIntersection(edge.Point1 + cell.Translation, edge.Point2 + cell.Translation, controlledSub.WorldPosition, cell.Center, out Vector2 intersection))
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

            avoidStrength = Vector2.Lerp(avoidStrength, newAvoidStrength, deltaTime * 10.0f);
            TargetVelocity = Vector2.Lerp(TargetVelocity, newVelocity + avoidStrength * 100.0f, AutoPilotSteeringLerp);

            //steer away from other subs
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == controlledSub || connectedSubs.Contains(sub)) { continue; }
                Point sizeSum = controlledSub.Borders.Size + sub.Borders.Size;
                Vector2 minDist = sizeSum.ToVector2() / 2;
                Vector2 diff = controlledSub.WorldPosition - sub.WorldPosition;
                float xDist = Math.Abs(diff.X);
                float yDist = Math.Abs(diff.Y);
                Vector2 maxAvoidDistance = minDist * 2;
                if (xDist > maxAvoidDistance.X || yDist > maxAvoidDistance.Y)
                {
                    //far enough -> ignore
                    continue;
                }
                float dot = controlledSub.Velocity == Vector2.Zero ? 0.0f : Vector2.Dot(Vector2.Normalize(controlledSub.Velocity), -diff);
                if (dot < 0.0f)
                {
                    //heading away -> ignore
                    continue;
                }
                float distanceFactor = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(maxAvoidDistance.X + maxAvoidDistance.Y, minDist.X + minDist.Y, xDist + yDist));
                float velocityFactor = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, 3, controlledSub.Velocity.Length()));
                TargetVelocity += 100 * Vector2.Normalize(diff) * distanceFactor * velocityFactor;
            }

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
                pathFinder = new PathFinder(WayPoint.WayPointList, false);
            }

            Vector2 target;

            if (navigateTactically)
            {
                target = ConvertUnits.ToSimUnits(AITacticalTarget);
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

        private void SetDestinationTactical()
        {
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

        private void SetMaintainPosition()
        {
            if (!MaintainPos)
            {
                unsentChanges = true;
                MaintainPos = true;
            }
            if (!posToMaintain.HasValue)
            {
                unsentChanges = true;
                posToMaintain = controlledSub != null ?
                    controlledSub.WorldPosition :
                    item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
            }
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

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            character.AIController.SteeringManager.Reset();
            if (objective.Override)
            {
                if (user != character && user != null && user.SelectedConstruction == item && character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.Get("DialogSteeringTaken"), null, 0.0f, "steeringtaken", 10.0f);
                }
            }
            user = character;

            if (Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(Item, character))
            {
                if (Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveRepairItem(character, Item, objective.objectiveManager, isPriority: true));
                    return false;
                }
                else
                {
                    character.Speak(TextManager.Get("DialogNavTerminalIsBroken"), identifier: "navterminalisbroken", minDurationBetweenSimilar: 30.0f);
                }
            }

            if (!AutoPilot)
            {
                unsentChanges = true;
                AutoPilot = true;
            }
            IncreaseSkillLevel(user, deltaTime);
            switch (objective.Option.ToLowerInvariant())
            {
                case "maintainposition":
                    if (objective.Override)
                    {
                        SetMaintainPosition();
                    }
                    break;
                case "navigateback":
                    if (Level.IsLoadedOutpost) { break; }
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
                    break;
                case "navigatetodestination":
                    if (Level.IsLoadedOutpost) { break; }
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
                    break;
                case "navigatetactical":
                    if (Level.IsLoadedOutpost) { break; }
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
                        SetDestinationTactical();
                    }
                    break;
            }
            sonar?.AIOperate(deltaTime, character, objective);
            if (!MaintainPos && showIceSpireWarning && character.IsOnPlayerTeam)
            {
                character.Speak(TextManager.Get("dialogicespirespottedsonar"), null, 0.0f, "icespirespottedsonar", 60.0f);
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
            }
            else
            {
                base.ReceiveSignal(signal, connection);
            }
        }
    }
}
