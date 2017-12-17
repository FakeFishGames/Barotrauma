using Barotrauma.Networking;
using Barotrauma.Particles;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerSerializable
    {
        protected float soundTimer;
        protected float soundInterval;

        private List<CharacterSound> sounds;

        //the Character that the player is currently controlling
        private static Character controlled;

        public static Character Controlled
        {
            get { return controlled; }
            set
            {
                if (controlled == value) return;
                controlled = value;
                CharacterHUD.Reset();

                if (controlled != null)
                {
                    controlled.Enabled = true;
                }
            }
        }
        
        private Dictionary<object, HUDProgressBar> hudProgressBars;

        public Dictionary<object, HUDProgressBar> HUDProgressBars
        {
            get { return hudProgressBars; }
        }

        partial void InitProjSpecific(XDocument doc)
        {
            soundInterval = doc.Root.GetAttributeFloat("soundinterval", 10.0f);

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];

            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key(GameMain.Config.KeyBind((InputType)i));
            }

            var soundElements = doc.Root.Elements("sound").ToList();

            sounds = new List<CharacterSound>();
            foreach (XElement soundElement in soundElements)
            {
                sounds.Add(new CharacterSound(soundElement));
            }

            hudProgressBars = new Dictionary<object, HUDProgressBar>();
        }


        /// <summary>
        /// Control the Character according to player input
        /// </summary>
        public void ControlLocalPlayer(float deltaTime, Camera cam, bool moveCam = true)
        {
            if (!DisableControls)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].SetState();
                }
            }
            else
            {
                foreach (Key key in keys)
                {
                    if (key == null) continue;
                    key.Reset();
                }
            }

            if (moveCam)
            {
                if (needsAir &&
                    pressureProtection < 80.0f &&
                    (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure > 50.0f))
                {
                    float pressure = AnimController.CurrentHull == null ? 100.0f : AnimController.CurrentHull.LethalPressure;

                    cam.Zoom = MathHelper.Lerp(cam.Zoom,
                        (pressure / 50.0f) * Rand.Range(1.0f, 1.05f),
                        (pressure - 50.0f) / 50.0f);
                }

                if (IsHumanoid)
                {
                    cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, 250.0f, deltaTime);
                }
                else
                {
                    //increased visibility range when controlling large a non-humanoid
                    cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, MathHelper.Clamp(Mass, 250.0f, 800.0f), deltaTime);
                }
            }

            cursorPosition = cam.ScreenToWorld(PlayerInput.MousePosition);
            if (AnimController.CurrentHull != null && AnimController.CurrentHull.Submarine != null)
            {
                cursorPosition -= AnimController.CurrentHull.Submarine.Position;
            }

            Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);

            if (Lights.LightManager.ViewTarget == this && Vector2.DistanceSquared(AnimController.Limbs[0].SimPosition, mouseSimPos) > 1.0f)
            {
                Body body = Submarine.PickBody(AnimController.Limbs[0].SimPosition, mouseSimPos);
                Structure structure = null;
                if (body != null) structure = body.UserData as Structure;
                if (structure != null)
                {
                    if (!structure.CastShadow && moveCam)
                    {
                        cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, 500.0f, 0.05f);
                    }
                }
            }

            DoInteractionUpdate(deltaTime, mouseSimPos);

            DisableControls = false;
        }

        partial void UpdateControlled(float deltaTime,Camera cam)
        {
            if (controlled != this) return;
            
            ControlLocalPlayer(deltaTime, cam);            

            Lights.LightManager.ViewTarget = this;
            CharacterHUD.Update(deltaTime, this);

            foreach (HUDProgressBar progressBar in hudProgressBars.Values)
            {
                progressBar.Update(deltaTime);
            }

            foreach (var pb in hudProgressBars.Where(pb => pb.Value.FadeTimer <= 0.0f).ToList())
            {
                hudProgressBars.Remove(pb.Key);
            }
        }

        partial void DamageHUD(float amount)
        {
            if (controlled == this) CharacterHUD.TakeDamage(amount);
        }

        partial void UpdateOxygenProjSpecific(float prevOxygen)
        {
            if (prevOxygen > 0.0f && Oxygen <= 0.0f && controlled == this)
            {
                SoundPlayer.PlaySound("drown");
            }
        }

        partial void KillProjSpecific()
        {
            if (GameMain.NetworkMember != null && Character.controlled == this)
            {
                string chatMessage = InfoTextManager.GetInfoText("Self_CauseOfDeath." + causeOfDeath.ToString());
                if (GameMain.Client != null) chatMessage += " Your chat messages will only be visible to other dead players.";

                GameMain.NetworkMember.AddChatMessage(chatMessage, ChatMessageType.Dead);
                GameMain.LightManager.LosEnabled = false;
                controlled = null;
            }

            PlaySound(CharacterSound.SoundType.Die);
        }

        partial void DisposeProjSpecific()
        {
            if (controlled == this) controlled = null;

            if (GameMain.GameSession?.CrewManager != null &&
                GameMain.GameSession.CrewManager.GetCharacters().Contains(this))
            {
                GameMain.GameSession.CrewManager.RemoveCharacter(this);
            }

            if (GameMain.Client != null && GameMain.Client.Character == this) GameMain.Client.Character = null;

            if (Lights.LightManager.ViewTarget == this) Lights.LightManager.ViewTarget = null;
        }

        public static void AddAllToGUIUpdateList()
        {
            for (int i = 0; i < CharacterList.Count; i++)
            {
                CharacterList[i].AddToGUIUpdateList();
            }
        }

        public virtual void AddToGUIUpdateList()
        {
            if (controlled == this)
            {
                CharacterHUD.AddToGUIUpdateList(this);
            }
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            if (!Enabled) return;

            AnimController.Draw(spriteBatch);
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera cam)
        {
            CharacterHUD.Draw(spriteBatch, this, cam);
        }

        public virtual void DrawFront(SpriteBatch spriteBatch, Camera cam)
        {
            if (!Enabled) return;

            if (GameMain.DebugDraw)
            {
                AnimController.DebugDraw(spriteBatch);

                if (aiTarget != null) aiTarget.Draw(spriteBatch);
            }

            /*if (memPos != null && memPos.Count > 0 && controlled == this)
            {
                PosInfo serverPos = memPos.Last();
                Vector2 remoteVec = ConvertUnits.ToDisplayUnits(serverPos.Position);
                if (Submarine != null)
                {
                    remoteVec += Submarine.DrawPosition;
                }
                remoteVec.Y = -remoteVec.Y;

                PosInfo localPos = memLocalPos.Find(m => m.ID == serverPos.ID);
                int mpind = memLocalPos.FindIndex(lp => lp.ID == localPos.ID);
                PosInfo localPos1 = mpind > 0 ? memLocalPos[mpind - 1] : null;
                PosInfo localPos2 = mpind < memLocalPos.Count-1 ? memLocalPos[mpind + 1] : null;

                Vector2 localVec = ConvertUnits.ToDisplayUnits(localPos.Position);
                Vector2 localVec1 = localPos1 != null ? ConvertUnits.ToDisplayUnits(((PosInfo)localPos1).Position) : Vector2.Zero;
                Vector2 localVec2 = localPos2 != null ? ConvertUnits.ToDisplayUnits(((PosInfo)localPos2).Position) : Vector2.Zero;
                if (Submarine != null)
                {
                    localVec += Submarine.DrawPosition;
                    localVec1 += Submarine.DrawPosition;
                    localVec2 += Submarine.DrawPosition;
                }
                localVec.Y = -localVec.Y;
                localVec1.Y = -localVec1.Y;
                localVec2.Y = -localVec2.Y;

                //GUI.DrawLine(spriteBatch, remoteVec, localVec, Color.Yellow, 0, 10);
                if (localPos1 != null) GUI.DrawLine(spriteBatch, remoteVec, localVec1, Color.Lime, 0, 2);
                if (localPos2 != null) GUI.DrawLine(spriteBatch, remoteVec + Vector2.One, localVec2 + Vector2.One, Color.Red, 0, 2);
            }

            Vector2 mouseDrawPos = CursorWorldPosition;
            mouseDrawPos.Y = -mouseDrawPos.Y;
            GUI.DrawLine(spriteBatch, mouseDrawPos - new Vector2(0, 5), mouseDrawPos + new Vector2(0, 5), Color.Red, 0, 10);

            Vector2 closestItemPos = closestItem != null ? closestItem.DrawPosition : Vector2.Zero;
            closestItemPos.Y = -closestItemPos.Y;
            GUI.DrawLine(spriteBatch, closestItemPos - new Vector2(0, 5), closestItemPos + new Vector2(0, 5), Color.Lime, 0, 10);*/

            if (this == controlled || GUI.DisableHUD) return;

            Vector2 pos = DrawPosition;
            pos.Y = -pos.Y;

            if (speechBubbleTimer > 0.0f)
            {
                GUI.SpeechBubbleIcon.Draw(spriteBatch, pos - Vector2.UnitY * 100.0f,
                    speechBubbleColor * Math.Min(speechBubbleTimer, 1.0f), 0.0f,
                    Math.Min((float)speechBubbleTimer, 1.0f));
            }

            if (this == controlled) return;

            //Ideally it shouldn't send the character entirely if we can't see them but /shrug, this isn't the most hacker-proof game atm
            Limb selfHead = controlled != null ? controlled.AnimController.GetLimb(LimbType.Head) : null;
            Limb targHead = this.AnimController.GetLimb(LimbType.Head);
            if (controlled != null && selfHead != null && targHead != null && Submarine.CheckVisibility(selfHead.SimPosition, targHead.SimPosition) != null) //TODO: use Line of Sight instead of CheckVisibility
                return;

            if (info != null)
            {
                string name = Info.DisplayName;
                if (controlled == null && name != Info.Name)
                    name += " (Disguised)";

                Vector2 namePos = new Vector2(pos.X, pos.Y - 110.0f - (5.0f / cam.Zoom)) - GUI.Font.MeasureString(name) * 0.5f / cam.Zoom;
                Color nameColor = Color.White;

                if (Character.Controlled != null && TeamID != Character.Controlled.TeamID)
                {
                    nameColor = Color.Red;
                }
                GUI.Font.DrawString(spriteBatch, name, namePos + new Vector2(1.0f / cam.Zoom, 1.0f / cam.Zoom), Color.Black, 0.0f, Vector2.Zero, 1.0f / cam.Zoom, SpriteEffects.None, 0.001f);
                GUI.Font.DrawString(spriteBatch, name, namePos, nameColor, 0.0f, Vector2.Zero, 1.0f / cam.Zoom, SpriteEffects.None, 0.0f);

                if (GameMain.DebugDraw)
                {
                    GUI.Font.DrawString(spriteBatch, ID.ToString(), namePos - new Vector2(0.0f, 20.0f), Color.White);
                }
            }

            if (isDead) return;

            if (health < maxHealth * 0.98f)
            {
                Vector2 healthBarPos = new Vector2(pos.X - 50, DrawPosition.Y + 100.0f);

                GUI.DrawProgressBar(spriteBatch, healthBarPos, new Vector2(100.0f, 15.0f), health / maxHealth, Color.Lerp(Color.Red, Color.Green, health / maxHealth) * 0.8f);
            }
        }

        /// <summary>
        /// Creates a progress bar that's "linked" to the specified object (or updates an existing one if there's one already linked to the object)
        /// The progress bar will automatically fade out after 1 sec if the method hasn't been called during that time
        /// </summary>
        public HUDProgressBar UpdateHUDProgressBar(object linkedObject, Vector2 worldPosition, float progress, Color emptyColor, Color fullColor)
        {
            if (controlled != this) return null;

            HUDProgressBar progressBar = null;
            if (!hudProgressBars.TryGetValue(linkedObject, out progressBar))
            {
                progressBar = new HUDProgressBar(worldPosition, Submarine, emptyColor, fullColor);
                hudProgressBars.Add(linkedObject, progressBar);
            }

            progressBar.WorldPosition = worldPosition;
            progressBar.FadeTimer = Math.Max(progressBar.FadeTimer, 1.0f);
            progressBar.Progress = progress;

            return progressBar;
        }

        public void PlaySound(CharacterSound.SoundType soundType)
        {
            if (sounds == null || sounds.Count == 0) return;

            var matchingSounds = sounds.FindAll(s => s.Type == soundType);
            if (matchingSounds.Count == 0) return;

            var selectedSound = matchingSounds[Rand.Int(matchingSounds.Count)];
            selectedSound.Sound.Play(1.0f, selectedSound.Range, AnimController.WorldPosition);
        }

        partial void ImplodeFX()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            SoundPlayer.PlaySound("implode", 1.0f, 150.0f, WorldPosition);

            for (int i = 0; i < 10; i++)
            {
                Particle p = GameMain.ParticleManager.CreateParticle("waterblood",
                    ConvertUnits.ToDisplayUnits(centerOfMass) + Rand.Vector(5.0f),
                    Rand.Vector(10.0f));
                if (p != null) p.Size *= 2.0f;

                GameMain.ParticleManager.CreateParticle("bubbles",
                    ConvertUnits.ToDisplayUnits(centerOfMass) + Rand.Vector(5.0f),
                    new Vector2(Rand.Range(-50f, 50f), Rand.Range(-100f, 50f)));
            }
        }
    }
}
