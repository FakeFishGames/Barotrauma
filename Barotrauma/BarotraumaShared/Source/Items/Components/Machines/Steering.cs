using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private const float AutopilotRayCastInterval = 0.5f;

        private Vector2 currVelocity;
        private Vector2 targetVelocity;

        private bool autoPilot;

        private Vector2? posToMaintain;

        private SteeringPath steeringPath;

        private PathFinder pathFinder;

        private float networkUpdateTimer;
        private bool unsentChanges;

        private float autopilotRayCastTimer;

        private Vector2 avoidStrength;

        private float neutralBallastLevel;
                
        public bool AutoPilot
        {
            get { return autoPilot; }
            set
            {
                if (value == autoPilot) return;

                autoPilot = value;
#if CLIENT
                autopilotTickBox.Selected = value;

                maintainPosTickBox.Enabled = autoPilot;
                levelEndTickBox.Enabled = autoPilot;
                levelStartTickBox.Enabled = autoPilot;
#endif
                if (autoPilot)
                {
                    if (pathFinder == null) pathFinder = new PathFinder(WayPoint.WayPointList, false);
#if CLIENT
                    ToggleMaintainPosition(maintainPosTickBox);
#endif
                }
#if CLIENT
                else
                {
                    maintainPosTickBox.Selected = false;
                    levelEndTickBox.Selected    = false;
                    levelStartTickBox.Selected  = false;

                    posToMaintain = null;
                }
#endif
            }
        }
        
        [Editable, Serialize(0.5f, true)]
        public float NeutralBallastLevel
        {
            get { return neutralBallastLevel; }
            set
            {
                neutralBallastLevel = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        public Vector2 TargetVelocity
        {
            get { return targetVelocity;}
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }
        
        public SteeringPath SteeringPath
        {
            get { return steeringPath; }
        }

        public Steering(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override void Update(float deltaTime, Camera cam)
        {
            if (unsentChanges)
            {
                networkUpdateTimer -= deltaTime;
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
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                    }

                    networkUpdateTimer = 0.5f;
                    unsentChanges = false;
                }
            }
     
            if (voltage < minVoltage && powerConsumption > 0.0f) return;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (autoPilot)
            {
                UpdateAutoPilot(deltaTime);
            }

            item.SendSignal(0, targetVelocity.X.ToString(CultureInfo.InvariantCulture), "velocity_x_out", null);

            float targetLevel = -targetVelocity.Y;
            targetLevel += (neutralBallastLevel - 0.5f) * 100.0f;

            item.SendSignal(0, targetLevel.ToString(CultureInfo.InvariantCulture), "velocity_y_out", null);

            voltage -= deltaTime;
        }

        private void UpdateAutoPilot(float deltaTime)
        {
            if (posToMaintain != null)
            {
                SteerTowardsPosition((Vector2)posToMaintain);
                return;
            }

            autopilotRayCastTimer -= deltaTime;

            steeringPath.CheckProgress(ConvertUnits.ToSimUnits(item.Submarine.WorldPosition), 10.0f);

            if (autopilotRayCastTimer <= 0.0f && steeringPath.NextNode != null)
            {
                Vector2 diff = Vector2.Normalize(ConvertUnits.ToSimUnits(steeringPath.NextNode.Position - item.Submarine.WorldPosition));

                bool nextVisible = true;
                for (int x = -1; x < 2; x += 2)
                {
                    for (int y = -1; y < 2; y += 2)
                    {
                        Vector2 cornerPos =
                            new Vector2(item.Submarine.Borders.Width * x, item.Submarine.Borders.Height * y) / 2.0f;

                        cornerPos = ConvertUnits.ToSimUnits(cornerPos * 1.2f + item.Submarine.WorldPosition);

                        float dist = Vector2.Distance(cornerPos, steeringPath.NextNode.SimPosition);

                        if (Submarine.PickBody(cornerPos, cornerPos + diff * dist, null, Physics.CollisionLevel) == null) continue;

                        nextVisible = false;
                        x = 2;
                        y = 2;
                    }
                }

                if (nextVisible) steeringPath.SkipToNextNode();

                autopilotRayCastTimer = AutopilotRayCastInterval;
            }

            if (steeringPath.CurrentNode != null)
            {
                SteerTowardsPosition(steeringPath.CurrentNode.WorldPosition);
            }

            float avoidRadius = Math.Max(item.Submarine.Borders.Width, item.Submarine.Borders.Height) * 2.0f;
            avoidRadius = Math.Max(avoidRadius, 2000.0f);

            Vector2 newAvoidStrength = Vector2.Zero;

            //steer away from nearby walls
            var closeCells = Level.Loaded.GetCells(item.Submarine.WorldPosition, 4);
            foreach (VoronoiCell cell in closeCells)
            {
                foreach (GraphEdge edge in cell.edges)
                {
                    var intersection = MathUtils.GetLineIntersection(edge.point1, edge.point2, item.Submarine.WorldPosition, cell.Center);
                    if (intersection != null)
                    {
                        Vector2 diff = item.Submarine.WorldPosition - (Vector2)intersection;

                        //far enough -> ignore
                        if (diff.Length() > avoidRadius) continue;

                        float dot = item.Submarine.Velocity == Vector2.Zero ?
                            0.0f : Vector2.Dot(item.Submarine.Velocity, -Vector2.Normalize(diff));

                        //not heading towards the wall -> ignore
                        if (dot < 0.5) continue;

                        Vector2 change = (Vector2.Normalize(diff) * Math.Max((avoidRadius - diff.Length()), 0.0f)) / avoidRadius;

                        newAvoidStrength += change * dot;
                    }
                }
            }

            avoidStrength = Vector2.Lerp(avoidStrength, newAvoidStrength, deltaTime * 10.0f);

            targetVelocity += avoidStrength * 100.0f;

            //steer away from other subs
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == item.Submarine) continue;
                if (item.Submarine.DockedTo.Contains(sub)) continue;

                float thisSize = Math.Max(item.Submarine.Borders.Width, item.Submarine.Borders.Height);
                float otherSize = Math.Max(sub.Borders.Width, sub.Borders.Height);

                Vector2 diff = item.Submarine.WorldPosition - sub.WorldPosition;

                float dist = diff == Vector2.Zero ? 0.0f : diff.Length();

                //far enough -> ignore
                if (dist > thisSize + otherSize) continue;

                diff = Vector2.Normalize(diff);

                float dot = item.Submarine.Velocity == Vector2.Zero ?
                    0.0f : Vector2.Dot(Vector2.Normalize(item.Submarine.Velocity), -Vector2.Normalize(diff));

                //heading away -> ignore
                if (dot < 0.0f) continue;

                targetVelocity += diff * 200.0f;
            }

            //clamp velocity magnitude to 100.0f
            float velMagnitude = targetVelocity.Length();
            if (velMagnitude > 100.0f)
            {
                targetVelocity *= 100.0f / velMagnitude;
            }

        }

        private void UpdatePath()
        {
            if (pathFinder == null) pathFinder = new PathFinder(WayPoint.WayPointList, false);

            Vector2 target;
            if (LevelEndSelected)
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.EndPosition);
            }
            else
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.StartPosition);
            }
            

            steeringPath = pathFinder.FindPath(ConvertUnits.ToSimUnits(item.WorldPosition), target);
        }

        public void SetDestinationLevelStart()
        {
            AutoPilot = true;

            MaintainPos = false;
            posToMaintain = null;

            LevelEndSelected = false;

            if (!LevelStartSelected)
            {
                LevelStartSelected = true;
                UpdatePath();
            }
        }

        public void SetDestinationLevelEnd()
        {
            AutoPilot = false;

            MaintainPos = false;
            posToMaintain = null;

            LevelStartSelected = false;

            if (!LevelEndSelected)
            {
                LevelEndSelected = true;
                UpdatePath();
            }
        }
        private void SteerTowardsPosition(Vector2 worldPosition)
        {
            float prediction = 10.0f;

            Vector2 futurePosition = ConvertUnits.ToDisplayUnits(item.Submarine.Velocity) * prediction;
            Vector2 targetSpeed = ((worldPosition - item.Submarine.WorldPosition) - futurePosition);

            if (targetSpeed.Length()>500.0f)
            {
                targetSpeed = Vector2.Normalize(targetSpeed);
                TargetVelocity = targetSpeed * 100.0f;
            }
            else
            {
                TargetVelocity = targetSpeed / 5.0f;
            }
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            if (connection.Name == "velocity_in")
            {
                currVelocity = XMLExtensions.ParseVector2(signal, false);
            }
            else
            {
                base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);
            }
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c)
        {
            bool autoPilot              = msg.ReadBoolean();
            Vector2 newTargetVelocity   = targetVelocity;
            bool maintainPos            = false;
            Vector2? newPosToMaintain   = null;
            bool headingToStart         = false;

            if (autoPilot)
            {
                maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadFloat(), 
                        msg.ReadFloat());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newTargetVelocity = new Vector2(msg.ReadFloat(), msg.ReadFloat());
            }

            if (!item.CanClientAccess(c)) return; 

            AutoPilot = autoPilot;

            if (!AutoPilot)
            {
                targetVelocity = newTargetVelocity;
            }
            else
            {

                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }

            //notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            msg.Write(autoPilot);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(targetVelocity.X);
                msg.Write(targetVelocity.Y);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
    }
}
