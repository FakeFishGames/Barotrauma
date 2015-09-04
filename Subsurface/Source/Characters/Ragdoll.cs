using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;

namespace Subsurface
{
    class Ragdoll
    {
        public static List<Ragdoll> list = new List<Ragdoll>();

        protected Hull currentHull;

        public Limb[] limbs;
        private Dictionary<LimbType, Limb> limbDictionary;
        public RevoluteJoint[] limbJoints;

        Character character;

        private Limb lowestLimb;

        protected float strongestImpact;

        float headPosition, headAngle;
        float torsoPosition, torsoAngle;

        protected double onFloorTimer;

        //the movement speed of the ragdoll
        public Vector2 movement;
        //the target speed towards which movement is interpolated
        private Vector2 targetMovement;

        //a movement vector that overrides targetmovement if trying to steer
        //a character to the position sent by server in multiplayer mode
        private Vector2 correctionMovement;
        
        protected float floorY;
        protected float surfaceY;
        
        protected bool inWater, headInWater;
        public bool onGround;
        private bool ignorePlatforms;

        protected Structure stairs;
                
        protected Direction dir;

        //private byte ID;
        
        public Limb LowestLimb
        {
            get { return lowestLimb; }
        }

        public float Mass
        {
            get;
            private set;
        }

        public Vector2 TargetMovement
        {
            get 
            { 
                return (correctionMovement == Vector2.Zero) ? targetMovement : correctionMovement; 
            }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetMovement.X = MathHelper.Clamp(value.X, -3.0f, 3.0f);
                targetMovement.Y = MathHelper.Clamp(value.Y, -3.0f, 3.0f);
            }
        }

        public float HeadPosition
        { 
            get { return headPosition; } 
        }

        public float HeadAngle
        { 
            get { return headAngle; } 
        }
        
        public float TorsoPosition
        { 
            get { return torsoPosition; } 
        }

        public float TorsoAngle
        { 
            get { return torsoAngle; } 
        }

        public float Dir
        {
            get { return ((dir == Direction.Left) ? -1.0f : 1.0f); }
        }

        public bool InWater
        {
            get { return inWater; }
        }

        public bool HeadInWater
        {
            get { return headInWater; }
        }

        public Hull CurrentHull
        {
            get { return currentHull;}
        }

        public bool IgnorePlatforms
        {
            get { return ignorePlatforms; }
            set 
            {
                if (ignorePlatforms == value) return;
                ignorePlatforms = value;

                foreach (Limb l in limbs)
                {
                    if (l.ignoreCollisions) continue;

                    l.body.CollidesWith = (ignorePlatforms) ?
                        Physics.CollisionWall | Physics.CollisionProjectile | Physics.CollisionStairs
                        : Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionMisc;                    
                }

            }
        }

        public float StrongestImpact
        {
            get { return strongestImpact; }
            set { strongestImpact = Math.Max(value, strongestImpact); }
        }

        public Structure Stairs
        {
            get { return stairs; }
        }
        
        public Ragdoll(Character character, XElement element)
        {
            list.Add(this);

            this.character = character;

            dir = Direction.Right;
            
            //int limbAmount = ;
            limbs = new Limb[element.Elements("limb").Count()];
            limbJoints = new RevoluteJoint[element.Elements("joint").Count()];
            limbDictionary = new Dictionary<LimbType, Limb>();

            headPosition = ToolBox.GetAttributeFloat(element, "headposition", 50.0f);
            headPosition = ConvertUnits.ToSimUnits(headPosition);
            headAngle = MathHelper.ToRadians(ToolBox.GetAttributeFloat(element, "headangle", 0.0f));

            torsoPosition = ToolBox.GetAttributeFloat(element, "torsoposition", 50.0f);
            torsoPosition = ConvertUnits.ToSimUnits(torsoPosition);
            torsoAngle = MathHelper.ToRadians(ToolBox.GetAttributeFloat(element, "torsoangle", 0.0f));
                       
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "limb":
                        byte ID = Convert.ToByte(subElement.Attribute("id").Value);

                        Limb limb = new Limb(character, subElement);

                        limb.body.FarseerBody.OnCollision += OnLimbCollision;
                        
                        limbs[ID] = limb;
                        Mass += limb.Mass;
                        if (!limbDictionary.ContainsKey(limb.type)) limbDictionary.Add(limb.type, limb);
                        break;
                    case "joint":
                        Byte limb1ID = Convert.ToByte(subElement.Attribute("limb1").Value);
                        Byte limb2ID = Convert.ToByte(subElement.Attribute("limb2").Value);

                        Vector2 limb1Pos = ToolBox.GetAttributeVector2(subElement, "limb1anchor", Vector2.Zero);
                        limb1Pos = ConvertUnits.ToSimUnits(limb1Pos);

                        Vector2 limb2Pos = ToolBox.GetAttributeVector2(subElement, "limb2anchor", Vector2.Zero);
                        limb2Pos = ConvertUnits.ToSimUnits(limb2Pos);

                        RevoluteJoint joint = new RevoluteJoint(limbs[limb1ID].body.FarseerBody, limbs[limb2ID].body.FarseerBody, limb1Pos, limb2Pos);

                        joint.CollideConnected = false;

                        if (subElement.Attribute("lowerlimit")!=null)
                        {
                            joint.LimitEnabled = true;
                            joint.LowerLimit = float.Parse(subElement.Attribute("lowerlimit").Value) * ((float)Math.PI / 180.0f);
                            joint.UpperLimit = float.Parse(subElement.Attribute("upperlimit").Value) * ((float)Math.PI / 180.0f);
                        }

                        joint.MotorEnabled = true;
                        joint.MaxMotorTorque = 0.25f;

                        Game1.World.AddJoint(joint);

                        for (int i = 0; i < limbJoints.Length; i++ )
                        {
                            if (limbJoints[i] != null) continue;
                            
                            limbJoints[i] = joint;
                            break;                            
                        }

                        break;
                }

            }

            foreach (var joint in limbJoints)
            {

                joint.BodyB.SetTransform(
                    joint.BodyA.Position+joint.LocalAnchorA-joint.LocalAnchorB,
                    (joint.LowerLimit+joint.UpperLimit)/2.0f);
            }

            float startDepth = 0.1f;
            float increment = 0.001f;

            foreach (Character otherCharacter in Character.CharacterList)
            {
                if (otherCharacter==character) continue;
                startDepth+=increment;
            }

            foreach (Limb limb in limbs)
            {
                limb.sprite.Depth = startDepth + limb.sprite.Depth * 0.0001f;
            }
        }
          
        public bool OnLimbCollision(Fixture f1, Fixture f2, Contact contact)
        {
            Structure structure = f2.Body.UserData as Structure;
            
            //always collides with bodies other than structures
            if (structure == null)
            {
                CalculateImpact(f1, f2, contact);
                return true;
            }
            
            if (structure.IsPlatform)
            {
                if (ignorePlatforms) return false;

                //the collision is ignored if the lowest limb is under the platform
                if (lowestLimb==null || lowestLimb.Position.Y < structure.Rect.Y) return false; 
            }
            else if (structure.StairDirection!=Direction.None)
            {
                if (inWater || !(targetMovement.Y>Math.Abs(targetMovement.X/2.0f)) && lowestLimb.Position.Y < structure.Rect.Y - structure.Rect.Height + 50.0f)
                {
                    stairs = null;
                    return false;
                }

                if (targetMovement.Y >= 0.0f && lowestLimb.SimPosition.Y > ConvertUnits.ToSimUnits(structure.Rect.Y - Submarine.GridSize.Y * 8.0f))
                {
                    stairs = null;
                    return false;
                }
                
                Limb limb = f1.Body.UserData as Limb;
                if (limb != null && (limb.type == LimbType.LeftFoot || limb.type == LimbType.RightFoot))
                {
                    if (contact.Manifold.LocalNormal.Y >= 0.0f)
                    {
                        stairs = structure;
                        return true;
                    }
                    else
                    {
                        stairs = null;
                        return false;
                    }                    
                }
                else
                {
                    return false;
                }
            }
                

            CalculateImpact(f1, f2, contact);
            return true;
        }

        private void CalculateImpact(Fixture f1, Fixture f2, Contact contact)
        {
            Vector2 normal = contact.Manifold.LocalNormal;

            Vector2 avgVelocity = Vector2.Zero;
            foreach (Limb limb in limbs)
            {
                avgVelocity += limb.LinearVelocity;
            }

            avgVelocity = avgVelocity / limbs.Count();
            
            float impact = Vector2.Dot((f1.Body.LinearVelocity + avgVelocity) / 2.0f, -normal);

            if (Game1.Server != null) impact = impact / 2.0f;

            Limb l = (Limb)f1.Body.UserData;

            if (impact > 1.0f && l.HitSound != null && l.soundTimer <= 0.0f) l.HitSound.Play(Math.Min(impact / 5.0f, 1.0f), impact * 100.0f, l.body.FarseerBody);

            if (impact > l.impactTolerance)
            {
                character.Health -= (impact - l.impactTolerance * 0.1f);
                strongestImpact = Math.Max(strongestImpact, impact - l.impactTolerance);
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch)
        {            
            foreach (Limb limb in limbs)
            {
                limb.Draw(spriteBatch);
            }
            
            if (!Game1.DebugDraw) return;

            foreach (Limb limb in limbs)
            {

                if (limb.pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.pullJoint.WorldAnchorA);
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Red, true, 0.01f);

                    if (limb.AnimTargetPos == Vector2.Zero) continue;

                    Vector2 pos2 = ConvertUnits.ToDisplayUnits(limb.AnimTargetPos);
                    pos2.Y = -pos2.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos2.X, (int)pos2.Y, 5, 5), Color.Blue, true, 0.01f);

                    GUI.DrawLine(spriteBatch, pos, pos2, Color.Green);
                }
            }

            foreach (RevoluteJoint joint in limbJoints)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorA);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);

                pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);
            }            

        }

        public virtual void Flip()
        {
            dir = (dir == Direction.Left) ? Direction.Right : Direction.Left;

            for (int i = 0; i < limbJoints.Count(); i++)
            {
                float lowerLimit = -limbJoints[i].UpperLimit;
                float upperLimit = -limbJoints[i].LowerLimit;

                limbJoints[i].LowerLimit = lowerLimit;
                limbJoints[i].UpperLimit = upperLimit;

                limbJoints[i].LocalAnchorA = new Vector2(-limbJoints[i].LocalAnchorA.X, limbJoints[i].LocalAnchorA.Y);
                limbJoints[i].LocalAnchorB = new Vector2(-limbJoints[i].LocalAnchorB.X, limbJoints[i].LocalAnchorB.Y);
            }


            for (int i = 0; i < limbs.Count(); i++)
            {
                if (limbs[i] == null) continue;

                Vector2 spriteOrigin = limbs[i].sprite.Origin;
                spriteOrigin.X = limbs[i].sprite.SourceRect.Width - spriteOrigin.X;
                limbs[i].sprite.Origin = spriteOrigin;

                limbs[i].Dir = Dir;

                if (limbs[i].pullJoint == null) continue;

                limbs[i].pullJoint.LocalAnchorA = 
                    new Vector2(
                        -limbs[i].pullJoint.LocalAnchorA.X, 
                        limbs[i].pullJoint.LocalAnchorA.Y);
            }            
        }

        public Vector2 GetCenterOfMass()
        {
            Vector2 centerOfMass = Vector2.Zero;
            foreach (Limb limb in limbs)
            {
                centerOfMass += limb.Mass * limb.SimPosition;
            }

            centerOfMass /= Mass;

            return centerOfMass;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="pullFromCenter">if false, force is applied to the position of pullJoint</param>
        protected void MoveLimb(Limb limb, Vector2 pos, float amount, bool pullFromCenter = false)
        {
            limb.Move(pos, amount, pullFromCenter);
        }
                
        public void ResetPullJoints()
        {
            for (int i = 0; i < limbs.Count(); i++)
            {
                if (limbs[i] == null) continue;
                if (limbs[i].pullJoint == null) continue;
                limbs[i].pullJoint.Enabled = false;
            }
        }

        public static void UpdateAll(float deltaTime)
        {
            foreach (Ragdoll r in list)
            {
                r.Update(deltaTime);
            }
        }

        public void FindHull()
        {
            Limb torso = GetLimb(LimbType.Torso);
            if (torso==null) torso = GetLimb(LimbType.Head);

            currentHull = Hull.FindHull(
                ConvertUnits.ToDisplayUnits(torso.SimPosition), 
                currentHull);
        }

        public void Update(float deltaTime)
        {
            UpdateNetplayerPosition();

            Vector2 flowForce = Vector2.Zero;

            FindLowestLimb();

            FindHull();
            
            //ragdoll isn't in any room -> it's in the water
            if (currentHull == null)
            {
                inWater = true;
                headInWater = true;
            }
            else
            {
                flowForce = GetFlowForce();

                inWater = false;
                headInWater = false;

                if (currentHull.Volume > currentHull.FullVolume * 0.95f || ConvertUnits.ToSimUnits(currentHull.Surface) - floorY > HeadPosition * 0.95f)
                    inWater = true;
                
            }
                       
            foreach (Limb limb in limbs)
            {
                Vector2 limbPosition = ConvertUnits.ToDisplayUnits(limb.SimPosition);

                //find the room which the limb is in
                //the room where the ragdoll is in is used as the "guess", meaning that it's checked first
                Hull limbHull = Hull.FindHull(limbPosition, currentHull);
                
                bool prevInWater = limb.inWater;
                limb.inWater = false;
                            
                if (limbHull==null)
                {                  
                    //limb isn't in any room -> it's in the water
                    limb.inWater = true;
                }
                else if (limbHull.Volume>0.0f && Submarine.RectContains(limbHull.Rect, limbPosition))
                {
                    
                    if (limbPosition.Y < limbHull.Surface)                        
                    {
                        limb.inWater = true;

                        if (flowForce.Length() > 0.01f)
                        {
                            limb.body.ApplyForce(flowForce);
                            if (flowForce.Length() > 15.0f) surfaceY = limbHull.Surface;
                        }

                        surfaceY = limbHull.Surface;

                        if (limb.type == LimbType.Head)
                        {
                            headInWater = true;
                            surfaceY = limbHull.Surface;
                        }
                    }
                        //the limb has gone through the surface of the water
                    if (Math.Abs(limb.LinearVelocity.Y) > 3.0 && inWater != prevInWater)
                    {

                        //create a splash particle
                        Subsurface.Particles.Particle splash = Game1.ParticleManager.CreateParticle("watersplash",
                            new Vector2(limb.SimPosition.X, ConvertUnits.ToSimUnits(limbHull.Surface)),
                            new Vector2(0.0f, Math.Abs(-limb.LinearVelocity.Y * 0.1f)),
                            0.0f);

                        if (splash != null) splash.yLimits = ConvertUnits.ToSimUnits(
                            new Vector2(
                                limbHull.Rect.Y,
                                limbHull.Rect.Y - limbHull.Rect.Height));

                        Game1.ParticleManager.CreateParticle("bubbles",
                            new Vector2(limb.SimPosition.X, ConvertUnits.ToSimUnits(limbHull.Surface)),                            
                            limb.LinearVelocity*0.001f,
                            0.0f);



                        //if the character dropped into water, create a wave
                        if (limb.LinearVelocity.Y<0.0f)
                        {
                            //1.0 when the limb is parallel to the surface of the water
                            // = big splash and a large impact
                            float parallel = (float)Math.Abs(Math.Sin(limb.Rotation));
                            Vector2 impulse = Vector2.Multiply(limb.LinearVelocity, -parallel * limb.Mass);
                            //limb.body.ApplyLinearImpulse(impulse);
                            int n = (int)((limbPosition.X - limbHull.Rect.X) / Hull.WaveWidth);
                            limbHull.WaveVel[n] = Math.Min(impulse.Y * 1.0f, 5.0f);
                            StrongestImpact = ((impulse.Length() * 0.5f) - limb.impactTolerance);
                        }
                    }

                    
                }

                limb.Update(deltaTime);

            }
            
        }

        private void UpdateNetplayerPosition()
        {
            Limb refLimb = GetLimb(LimbType.Torso);
            if (refLimb == null) refLimb = GetLimb(LimbType.Head);

            if (refLimb.body.TargetPosition == Vector2.Zero) return;

            //if the limb is further away than resetdistance, all limbs are immediately snapped to their targetpositions
            float resetDistance = NetConfig.ResetRagdollDistance;

            //if the limb is closer than alloweddistance, just ignore the difference
            float allowedDistance = NetConfig.AllowedRagdollDistance;

            float dist = Vector2.Distance(limbs[0].body.Position, refLimb.body.TargetPosition);
            bool resetAll = (dist > resetDistance && character.LargeUpdateTimer == 1);

            Vector2 newMovement = (refLimb.body.TargetPosition - refLimb.body.Position);

            if (newMovement == Vector2.Zero || newMovement.Length() < allowedDistance)
            {
                refLimb.body.TargetPosition = Vector2.Zero;
                correctionMovement = Vector2.Zero;
                return;
            }
            else
            {
                if (inWater)
                {
                    foreach (Limb limb in limbs)
                    {
                        if (limb.body.TargetPosition == Vector2.Zero) continue;

                        limb.body.SetTransform(limb.SimPosition + newMovement * 0.1f, limb.Rotation);
                    }
                }
                else
                {
                    correctionMovement = Vector2.Normalize(newMovement) * Math.Min(1.0f + dist, 3.0f);
                    if (Math.Abs(correctionMovement.Y) < 0.1f) correctionMovement.Y = 0.0f;
                }
            }

            if (resetAll)
            {
                System.Diagnostics.Debug.WriteLine("reset ragdoll limb positions");

                foreach (Limb limb in limbs)
                {
                    if (limb.body.TargetPosition == Vector2.Zero) continue;

                    limb.body.LinearVelocity = limb.body.TargetVelocity;
                    limb.body.AngularVelocity = limb.body.TargetAngularVelocity;

                    limb.body.SetTransform(limb.body.TargetPosition, limb.body.TargetRotation);
                    limb.body.TargetPosition = Vector2.Zero;
                }
            } 
        }


        private Vector2 GetFlowForce()
        {
            Vector2 limbPos = ConvertUnits.ToDisplayUnits(limbs[0].SimPosition);

            Vector2 force = Vector2.Zero;
            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                Gap gap = e as Gap;
                if (gap == null || gap.FlowTargetHull != currentHull || gap.FlowForce == Vector2.Zero) continue;

                Vector2 gapPos = gap.SimPosition;

                float dist = Vector2.Distance(limbPos, gapPos);

                force += Vector2.Normalize(gap.FlowForce) * (Math.Max(gap.FlowForce.Length() - dist, 0.0f) / 500.0f);
            }

            if (force.Length() > 20.0f) return force;
            return force;
        }

        public Limb GetLimb(LimbType limbType)
        {
            Limb limb = null;
            limbDictionary.TryGetValue(limbType, out limb);
            return limb;
        }
        
        public void FindLowestLimb()
        {
            //find the lowest limb
            lowestLimb = null;
            foreach (Limb limb in limbs)
            {
                if (lowestLimb == null)
                    lowestLimb = limb;
                else if (limb.SimPosition.Y < lowestLimb.SimPosition.Y)
                    lowestLimb = limb;
            }
        }

        public void Remove()
        {
            foreach (Limb l in limbs) l.Remove();
            foreach (RevoluteJoint joint in limbJoints)
            {
                Game1.World.RemoveJoint(joint);
            }
        }

    }
}
