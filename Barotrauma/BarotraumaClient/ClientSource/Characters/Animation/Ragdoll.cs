using Barotrauma.Items.Components;
using Barotrauma.SpriteDeformations;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using Barotrauma.Particles;

namespace Barotrauma
{
    abstract partial class Ragdoll
    {
        public HashSet<SpriteDeformation> SpriteDeformations { get; protected set; } = new HashSet<SpriteDeformation>();

        /// <summary>
        /// Inversed draw order, which is used for drawing the limbs in 3d (deformable sprites).
        /// </summary>
        protected Limb[] inversedLimbDrawOrder;

        partial void UpdateNetPlayerPositionProjSpecific(float deltaTime, float lowestSubPos)
        {
            if (character != GameMain.Client.Character || !character.CanMove)
            {
                //remove states without a timestamp (there may still be ID-based states 
                //in the list when the controlled character switches to timestamp-based interpolation)
                character.MemState.RemoveAll(m => m.Timestamp == 0.0f);

                //use simple interpolation for other players' characters and characters that can't move
                if (character.MemState.Count > 0)
                {
                    CharacterStateInfo serverPos = character.MemState.Last();
                    if (!character.isSynced)
                    {
                        SetPosition(serverPos.Position, false);
                        Collider.LinearVelocity = Vector2.Zero;
                        character.MemLocalState.Clear();
                        character.LastNetworkUpdateID = serverPos.ID;
                        character.isSynced = true;
                        return;
                    }

                    if (character.MemState[0].SelectedCharacter == null || character.MemState[0].SelectedCharacter.Removed)
                    {
                        character.DeselectCharacter();
                    }
                    else if (character.MemState[0].SelectedCharacter != null)
                    {
                        character.SelectCharacter(character.MemState[0].SelectedCharacter);
                    }

                    if (character.MemState[0].SelectedItem == null || character.MemState[0].SelectedItem.Removed)
                    {
                        character.SelectedConstruction = null;
                    }
                    else
                    {
                        if (character.SelectedConstruction != character.MemState[0].SelectedItem)
                        {
                            foreach (var ic in character.MemState[0].SelectedItem.Components)
                            {
                                if (ic.CanBeSelected) ic.Select(character);
                            }
                        }
                        character.SelectedConstruction = character.MemState[0].SelectedItem;
                    }

                    if (character.MemState[0].Animation == AnimController.Animation.CPR)
                    {
                        character.AnimController.Anim = AnimController.Animation.CPR;
                    }
                    else if (character.AnimController.Anim == AnimController.Animation.CPR)
                    {
                        character.AnimController.Anim = AnimController.Animation.None;
                    }

                    Vector2 newVelocity = Collider.LinearVelocity;
                    Vector2 newPosition = Collider.SimPosition;
                    float newRotation = Collider.Rotation;
                    float newAngularVelocity = Collider.AngularVelocity;
                    Collider.CorrectPosition(character.MemState, out newPosition, out newVelocity, out newRotation, out newAngularVelocity);

                    newVelocity = newVelocity.ClampLength(100.0f);
                    if (!MathUtils.IsValid(newVelocity)) { newVelocity = Vector2.Zero; }
                    overrideTargetMovement = newVelocity.LengthSquared() > 0.01f ? newVelocity : Vector2.Zero;

                    Collider.LinearVelocity = newVelocity;
                    Collider.AngularVelocity = newAngularVelocity;

                    float distSqrd = Vector2.DistanceSquared(newPosition, Collider.SimPosition);
                    float errorTolerance = character.CanMove ? 0.01f : 0.2f;
                    if (distSqrd > errorTolerance)
                    {
                        if (distSqrd > 10.0f || !character.CanMove)
                        {
                            Collider.TargetRotation = newRotation;
                            SetPosition(newPosition, lerp: distSqrd < 5.0f, ignorePlatforms: false);
                        }
                        else
                        {
                            Collider.TargetRotation = newRotation;
                            Collider.TargetPosition = newPosition;
                            Collider.MoveToTargetPosition(true);
                        }
                    }
                    
                    //immobilized characters can't correct their position using AnimController movement
                    // -> we need to correct it manually
                    if (!character.CanMove)
                    {
                        float mainLimbDistSqrd = Vector2.DistanceSquared(MainLimb.PullJointWorldAnchorA, Collider.SimPosition);
                        float mainLimbErrorTolerance = 0.1f;
                        //if the main limb is roughly at the correct position and the collider isn't moving (much at least),
                        //don't attempt to correct the position.
                        if (mainLimbDistSqrd > mainLimbErrorTolerance || Collider.LinearVelocity.LengthSquared() > 0.05f)
                        {
                            MainLimb.PullJointWorldAnchorB = Collider.SimPosition;
                            MainLimb.PullJointEnabled = true;
                        }
                    }
                }
                character.MemLocalState.Clear();
            }
            else
            {
                //remove states with a timestamp (there may still timestamp-based states 
                //in the list if the controlled character switches from timestamp-based interpolation to ID-based)
                character.MemState.RemoveAll(m => m.Timestamp > 0.0f);
                
                for (int i = 0; i < character.MemLocalState.Count; i++)
                {
                    if (character.Submarine == null)
                    {
                        //transform in-sub coordinates to outside coordinates
                        if (character.MemLocalState[i].Position.Y > lowestSubPos)
                        {                            
                            character.MemLocalState[i].TransformInToOutside();
                        }
                    }
                    else if (currentHull?.Submarine != null)
                    {
                        //transform outside coordinates to in-sub coordinates
                        if (character.MemLocalState[i].Position.Y < lowestSubPos)
                        {
                            character.MemLocalState[i].TransformOutToInside(currentHull.Submarine);
                        }
                    }
                }

                if (character.MemState.Count < 1) return;

                overrideTargetMovement = Vector2.Zero;

                CharacterStateInfo serverPos = character.MemState.Last();

                if (!character.isSynced)
                {
                    SetPosition(serverPos.Position, false);
                    Collider.LinearVelocity = Vector2.Zero;
                    character.MemLocalState.Clear();
                    character.LastNetworkUpdateID = serverPos.ID;
                    character.isSynced = true;
                    return;
                }

                int localPosIndex = character.MemLocalState.FindIndex(m => m.ID == serverPos.ID);
                if (localPosIndex > -1)
                {
                    CharacterStateInfo localPos = character.MemLocalState[localPosIndex];
                    
                    //the entity we're interacting with doesn't match the server's
                    if (localPos.SelectedCharacter != serverPos.SelectedCharacter)
                    {
                        if (serverPos.SelectedCharacter == null || serverPos.SelectedCharacter.Removed)
                        {
                            character.DeselectCharacter();
                        }
                        else if (serverPos.SelectedCharacter != null)
                        {
                            character.SelectCharacter(serverPos.SelectedCharacter);
                        }
                    }
                    if (localPos.SelectedItem != serverPos.SelectedItem)
                    {
                        if (serverPos.SelectedItem == null || serverPos.SelectedItem.Removed)
                        {
                            character.SelectedConstruction = null;
                        }
                        else if (serverPos.SelectedItem != null)
                        {
                            if (character.SelectedConstruction != serverPos.SelectedItem)
                            {
                                serverPos.SelectedItem.TryInteract(character, true, true);
                            }
                            character.SelectedConstruction = serverPos.SelectedItem;
                        }
                    }

                    if (localPos.Animation != serverPos.Animation)
                    {
                        if (serverPos.Animation == AnimController.Animation.CPR)
                        {
                            character.AnimController.Anim = AnimController.Animation.CPR;
                        }
                        else if (character.AnimController.Anim == AnimController.Animation.CPR) 
                        {
                            character.AnimController.Anim = AnimController.Animation.None;
                        }
                    }

                    Hull serverHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(serverPos.Position), character.CurrentHull, serverPos.Position.Y < lowestSubPos);
                    Hull clientHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(localPos.Position), serverHull, localPos.Position.Y < lowestSubPos);
                    
                    if (serverHull != null && clientHull != null && serverHull.Submarine != clientHull.Submarine)
                    {
                        //hull subs don't match => teleport the camera to the other sub
                        character.Submarine = serverHull.Submarine;
                        character.CurrentHull = CurrentHull = serverHull;
                        SetPosition(serverPos.Position);
                        character.MemLocalState.Clear();
                    }
                    else
                    {
                        Vector2 positionError = serverPos.Position - localPos.Position;
                        float rotationError = serverPos.Rotation.HasValue && localPos.Rotation.HasValue ?
                            serverPos.Rotation.Value - localPos.Rotation.Value :
                            0.0f;

                        for (int i = localPosIndex; i < character.MemLocalState.Count; i++)
                        {
                            Hull pointHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(character.MemLocalState[i].Position), clientHull, character.MemLocalState[i].Position.Y < lowestSubPos);
                            if (pointHull != clientHull && ((pointHull == null) || (clientHull == null) || (pointHull.Submarine == clientHull.Submarine))) break;
                            character.MemLocalState[i].Translate(positionError, rotationError);
                        }

                        float errorMagnitude = positionError.Length();
                        if (errorMagnitude > 0.5f)
                        {
                            character.MemLocalState.Clear();
                            SetPosition(serverPos.Position, lerp: true, ignorePlatforms: false);
                        }
                        else if (errorMagnitude > 0.01f)
                        {
                            Collider.TargetPosition = Collider.SimPosition + positionError;
                            Collider.TargetRotation = Collider.Rotation + rotationError;
                            Collider.MoveToTargetPosition(lerp: true);
                        }
                    }

                }

                if (character.MemLocalState.Count > 120) character.MemLocalState.RemoveRange(0, character.MemLocalState.Count - 120);
                character.MemState.Clear();
            }
        }
        
        partial void ImpactProjSpecific(float impact, Body body)
        {
            float volume = MathHelper.Clamp(impact - 3.0f, 0.5f, 1.0f);

            if (body.UserData is Limb limb && character.Stun <= 0f)
            {
                if (impact > 3.0f) { PlayImpactSound(limb); }
            }
            else if (body.UserData is Limb || body == Collider.FarseerBody)
            {
                if (!character.IsRemotelyControlled && impact > ImpactTolerance)
                {
                    SoundPlayer.PlayDamageSound("LimbBlunt", strongestImpact, Collider);
                }
            }
            if (Character.Controlled == character)
            {
                GameMain.GameScreen.Cam.Shake = Math.Min(Math.Max(strongestImpact, GameMain.GameScreen.Cam.Shake), 3.0f);
            }
        }

        public void PlayImpactSound(Limb limb)
        {
            limb.LastImpactSoundTime = (float)Timing.TotalTime;
            if (!string.IsNullOrWhiteSpace(limb.HitSoundTag))
            {
                bool inWater = limb.InWater;
                if (character.CurrentHull != null &&
                    character.CurrentHull.Surface > character.CurrentHull.Rect.Y - character.CurrentHull.Rect.Height + 5.0f &&
                    limb.SimPosition.Y < ConvertUnits.ToSimUnits(character.CurrentHull.Rect.Y - character.CurrentHull.Rect.Height) + limb.body.GetMaxExtent())
                {
                    inWater = true;
                }
                SoundPlayer.PlaySound(inWater ? "footstep_water" : limb.HitSoundTag, limb.WorldPosition, hullGuess: character.CurrentHull);
            }
            foreach (WearableSprite wearable in limb.WearingItems)
            {
                if (limb.type == wearable.Limb && !string.IsNullOrWhiteSpace(wearable.Sound))
                {
                    SoundPlayer.PlaySound(wearable.Sound, limb.WorldPosition, hullGuess: character.CurrentHull);
                }
            }
        }

        partial void Splash(Limb limb, Hull limbHull)
        {
            //create a splash particle
            for (int i = 0; i < MathHelper.Clamp(Math.Abs(limb.LinearVelocity.Y), 1.0f, 5.0f); i++)
            {
                var splash = GameMain.ParticleManager.CreateParticle("watersplash",
                    new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                    new Vector2(0.0f, Math.Abs(-limb.LinearVelocity.Y * 20.0f)) + Rand.Vector(Math.Abs(limb.LinearVelocity.Y * 10)),
                    Rand.Range(0.0f, MathHelper.TwoPi), limbHull);

                if (splash != null)
                {
                    splash.Size *= MathHelper.Clamp(Math.Abs(limb.LinearVelocity.Y) * 0.1f, 1.0f, 2.0f);
                }
            }

            GameMain.ParticleManager.CreateParticle("bubbles",
                new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                limb.LinearVelocity * 0.001f,
                0.0f, limbHull);

            //if the Character dropped into water, create a wave
            if (limb.LinearVelocity.Y < 0.0f)
            {
                if (splashSoundTimer <= 0.0f)
                {
                    SoundPlayer.PlaySplashSound(limb.WorldPosition, Math.Abs(limb.LinearVelocity.Y) + Rand.Range(-5.0f, 0.0f));
                    splashSoundTimer = 0.5f;
                }

                //+ some extra bubbles to follow the character underwater
                GameMain.ParticleManager.CreateParticle("bubbles",
                    new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                    limb.LinearVelocity * 10.0f,
                    0.0f, limbHull);
            }
        }

        partial void SetupDrawOrder()
        {
            //make sure every character  gets drawn at a distinct "layer" 
            //(instead of having some of the limbs appear behind and some in front of other characters)
            float startDepth = 0.1f;
            float increment = 0.001f;
            foreach (Character otherCharacter in Character.CharacterList)
            {
                if (otherCharacter == character) { continue; }
                startDepth += increment;
            }
            //make sure each limb has a distinct depth value
            List<Limb> depthSortedLimbs = Limbs.OrderBy(l => l.DefaultSpriteDepth).ToList();
            foreach (Limb limb in Limbs)
            {
                var sprite = limb.GetActiveSprite();
                if (sprite == null) { continue; }
                sprite.Depth = startDepth + depthSortedLimbs.IndexOf(limb) * 0.00001f;
                foreach (var conditionalSprite in limb.ConditionalSprites)
                {
                    if (conditionalSprite.Exclusive)
                    {
                        conditionalSprite.ActiveSprite.Depth = sprite.Depth;
                    }
                }
            }
            foreach (Limb limb in Limbs)
            {
                if (limb.ActiveSprite == null) { continue; }
                if (limb.Params.InheritLimbDepth == LimbType.None) { continue; }
                var matchingLimb = GetLimb(limb.Params.InheritLimbDepth);
                if (matchingLimb != null)
                {
                    limb.ActiveSprite.Depth = matchingLimb.ActiveSprite.Depth - 0.0000001f;
                }
            }

            depthSortedLimbs.Reverse();
            inversedLimbDrawOrder = depthSortedLimbs.ToArray();
        }
        
        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (!character.IsVisible) { return; }

            LimbJoints.ForEach(j => j.UpdateDeformations(deltaTime));
            foreach (var deformation in SpriteDeformations)
            {
                if (character.IsDead && deformation.Params.StopWhenHostIsDead) { continue; }
                if (!character.AnimController.InWater && deformation.Params.OnlyInWater) { continue; }
                if (deformation.Params.UseMovementSine)
                {
                    if (this is AnimController animator)
                    {
                        deformation.Phase = MathUtils.WrapAngleTwoPi(animator.WalkPos * deformation.Params.Frequency + MathHelper.Pi * deformation.Params.SineOffset);
                    }
                }
                else
                {
                    deformation.Update(deltaTime);
                }
            }
        }

        partial void FlipProjSpecific()
        {
            foreach (Limb limb in Limbs)
            {
                if (limb == null || limb.IsSevered || limb.ActiveSprite == null) { continue; }

                Vector2 spriteOrigin = limb.ActiveSprite.Origin;
                spriteOrigin.X = limb.ActiveSprite.SourceRect.Width - spriteOrigin.X;
                limb.ActiveSprite.Origin = spriteOrigin;                
            }
        }

        partial void SeverLimbJointProjSpecific(LimbJoint limbJoint, bool playSound)
        {
            foreach (Limb limb in new Limb[] { limbJoint.LimbA, limbJoint.LimbB })
            {
                float gibParticleAmount = MathHelper.Clamp(limb.Mass / character.AnimController.Mass, 0.1f, 1.0f);
                foreach (ParticleEmitter emitter in character.GibEmitters)
                {
                    if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) { continue; }
                    if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) { continue; }

                    emitter.Emit(1.0f, limb.WorldPosition, character.CurrentHull, amountMultiplier: gibParticleAmount);
                }
            }

            if (playSound)
            {
                var damageSound = character.GetSound(s => s.Type == CharacterSound.SoundType.Damage);
                float range = damageSound != null ? damageSound.Range * 2 : ConvertUnits.ToDisplayUnits(character.AnimController.Collider.GetSize().Length() * 10);
                SoundPlayer.PlayDamageSound(limbJoint.Params.BreakSound, 1.0f, limbJoint.LimbA.body.DrawPosition, range: range);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            if (simplePhysicsEnabled) { return; }

            Collider.UpdateDrawPosition();

            if (Limbs == null)
            {
                DebugConsole.ThrowError("Failed to draw a ragdoll, limbs have been removed. Character: \"" + character.Name + "\", removed: " + character.Removed + "\n" + Environment.StackTrace.CleanupStackTrace());
                GameAnalyticsManager.AddErrorEventOnce("Ragdoll.Draw:LimbsRemoved", 
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Failed to draw a ragdoll, limbs have been removed. Character: \"" + character.Name + "\", removed: " + character.Removed + "\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            Color? color = null;
            if (character.ExternalHighlight)
            {
                color = Color.Lerp(Color.White, GUI.Style.Orange, (float)Math.Sin(Timing.TotalTime * 3.5f));
            }

            float depthOffset = GetDepthOffset();
            for (int i = 0; i < limbs.Length; i++)
            {
                var limb = inversedLimbDrawOrder[i];
                if (depthOffset != 0.0f) { limb.ActiveSprite.Depth += depthOffset; }
                limb.Draw(spriteBatch, cam, color);
                if (depthOffset != 0.0f) { limb.ActiveSprite.Depth -= depthOffset; }
            }
            LimbJoints.ForEach(j => j.Draw(spriteBatch));
        }

        /// <summary>
        /// Offset added to the default draw depth of the character's limbs. For example, climbing on ladders affects the depth of the character to get it to render behind the ladders.
        /// </summary>
        public float GetDepthOffset()
        {
            float maxDepth = 0.0f;
            float minDepth = 1.0f;
            float depthOffset = 0.0f;
            var ladder = character.SelectedConstruction?.GetComponent<Ladder>();
            
            if (ladder != null)
            {
                CalculateLimbDepths();
                if (character.WorldPosition.X < character.SelectedConstruction.WorldPosition.X)
                {
                    //at the left side of the ladder, needs to be drawn in front of the rungs
                    depthOffset = Math.Max(ladder.BackgroundSpriteDepth - 0.01f - maxDepth, 0.0f);
                }
                else
                {
                    //at the right side of the ladder, needs to be drawn behind the rungs
                    depthOffset = Math.Max(ladder.BackgroundSpriteDepth + 0.01f - minDepth, 0.0f);
                }
            }
            else
            {
                CalculateLimbDepths();
                var controller = character.SelectedConstruction?.GetComponent<Controller>();
                if (controller != null && controller.ControlCharacterPose && controller.User == character && controller.UserInCorrectPosition)
                {
                    if (controller.Item.SpriteDepth <= maxDepth || controller.DrawUserBehind)
                    {
                        depthOffset = Math.Max(controller.Item.GetDrawDepth() + 0.0001f - minDepth, -minDepth);
                    }
                    else
                    {
                        depthOffset = Math.Max(controller.Item.GetDrawDepth() - 0.0001f - maxDepth, 0.0f);
                    }
                }
            }

            void CalculateLimbDepths()
            {
                foreach (Limb limb in Limbs)
                {
                    var activeSprite = limb.ActiveSprite;
                    if (activeSprite != null)
                    {
                        maxDepth = Math.Max(activeSprite.Depth, maxDepth);
                        minDepth = Math.Min(activeSprite.Depth, minDepth);
                    }
                }
            }

            return depthOffset;
        }

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            if (!GameMain.DebugDraw || !character.Enabled) { return; }
            if (simplePhysicsEnabled) { return; }

            foreach (Limb limb in Limbs)
            {
                if (limb.PullJointEnabled)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.PullJointWorldAnchorB);
                    if (currentHull?.Submarine != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), GUI.Style.Red, true, 0.01f);

                    pos = ConvertUnits.ToDisplayUnits(limb.PullJointWorldAnchorA);
                    if (currentHull?.Submarine != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Cyan, true, 0.01f);
                }

                limb.body.DebugDraw(spriteBatch, inWater ? (currentHull == null ? Color.Blue : Color.Cyan) : Color.White);
            }

            Collider.DebugDraw(spriteBatch, frozen ? GUI.Style.Red : (inWater ? Color.SkyBlue : Color.Gray));
            GUI.Font.DrawString(spriteBatch, Collider.LinearVelocity.X.FormatSingleDecimal(), new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y), Color.Orange);

            foreach (var joint in LimbJoints)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorA);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);

                pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.body.TargetPosition != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)limb.body.TargetPosition);
                    if (currentHull?.Submarine != null)
                    {
                        pos += currentHull.Submarine.DrawPosition;
                    }
                    pos.Y = -pos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 10, (int)pos.Y - 10, 20, 20), Color.Cyan, false, 0.01f);
                    GUI.DrawLine(spriteBatch, pos, new Vector2(limb.WorldPosition.X, -limb.WorldPosition.Y), Color.Cyan);
                }
            }

            if (this is HumanoidAnimController humanoid)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(humanoid.RightHandIKPos);
                if (humanoid.character.Submarine != null) { pos += humanoid.character.Submarine.Position; }
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 4, 4), GUI.Style.Green, true);
                pos = ConvertUnits.ToDisplayUnits(humanoid.LeftHandIKPos);
                if (humanoid.character.Submarine != null) { pos += humanoid.character.Submarine.Position; }
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 4, 4), GUI.Style.Green, true);

                Vector2 aimPos = humanoid.AimSourceWorldPos;
                aimPos.Y = -aimPos.Y;
                GUI.DrawLine(spriteBatch, aimPos - Vector2.UnitY * 3, aimPos + Vector2.UnitY * 3, Color.Red);
                GUI.DrawLine(spriteBatch, aimPos - Vector2.UnitX * 3, aimPos + Vector2.UnitX * 3, Color.Red);
            }

            if (character.MemState.Count > 1)
            {
                Vector2 prevPos = ConvertUnits.ToDisplayUnits(character.MemState[0].Position);
                if (currentHull?.Submarine != null)
                {
                    prevPos += currentHull.Submarine.DrawPosition;
                }
                prevPos.Y = -prevPos.Y;

                for (int i = 1; i < character.MemState.Count; i++)
                {
                    Vector2 currPos = ConvertUnits.ToDisplayUnits(character.MemState[i].Position);
                    if (currentHull?.Submarine != null)
                    {
                        currPos += currentHull.Submarine.DrawPosition;
                    }
                    currPos.Y = -currPos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 3, (int)currPos.Y - 3, 6, 6), Color.Cyan * 0.6f, true, 0.01f);
                    GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.6f, 0, 3);

                    prevPos = currPos;
                }
            }

            if (currentHull != null)
            {
                Vector2 displayFloorPos = ConvertUnits.ToDisplayUnits(new Vector2(Collider.SimPosition.X, floorY));
                if (currentHull?.Submarine != null) { displayFloorPos += currentHull.Submarine.DrawPosition; }
                displayFloorPos.Y = -displayFloorPos.Y;
                GUI.DrawLine(spriteBatch, displayFloorPos, displayFloorPos + new Vector2(floorNormal.X, -floorNormal.Y) * 50.0f, Color.Cyan * 0.5f, 0, 2);
            }

            if (IgnorePlatforms)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y),
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y + 50),
                    Color.Orange, 0, 5);
            }
        }
    }
}
