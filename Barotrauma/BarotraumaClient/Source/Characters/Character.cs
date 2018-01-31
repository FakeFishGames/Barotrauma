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
        protected float nameTimer;
        protected bool nameVisible;

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
            if (moveCam)
            {
                if (DebugConsole.IsOpen || GUI.PauseMenuOpen || IsUnconscious ||
                    (GameMain.GameSession?.CrewManager?.CrewCommander != null && GameMain.GameSession.CrewManager.CrewCommander.IsOpen))
                {
                    if (deltaTime > 0.0f) cam.OffsetAmount = 0.0f;
                }
                else if (Lights.LightManager.ViewTarget == this && Vector2.DistanceSquared(AnimController.Limbs[0].SimPosition, mouseSimPos) > 1.0f)
                {
                    Body body = Submarine.CheckVisibility(AnimController.Limbs[0].SimPosition, mouseSimPos);
                    Structure structure = body == null ? null : body.UserData as Structure;

                    float sightDist = Submarine.LastPickedFraction;
                    if (body?.UserData is Structure && !((Structure)body.UserData).CastShadow)
                    {
                        sightDist = 1.0f;
                    }
                    cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, Math.Max(250.0f, sightDist * 500.0f), 0.05f);
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
            if (GameMain.NetworkMember != null && controlled == this)
            {
                string chatMessage = TextManager.Get("Self_CauseOfDeathDescription." + causeOfDeath.ToString());
                if (GameMain.Client != null) chatMessage += " " + TextManager.Get("DeathChatNotification");

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

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (info != null)
            {
                nameTimer -= deltaTime;
                if (nameTimer <= 0.0f)
                {
                    if (controlled == null)
                    {
                        nameVisible = true;
                    }

                    //if the character is not in the camera view, the name can't be visible and we can avoid the expensive visibility checks
                    else if (WorldPosition.X < cam.WorldView.X || WorldPosition.X > cam.WorldView.Right || 
                            WorldPosition.Y > cam.WorldView.Y || WorldPosition.Y < cam.WorldView.Y - cam.WorldView.Height)
                    {
                        nameVisible = false;
                    }
                    else
                    {
                        //Ideally it shouldn't send the character entirely if we can't see them but /shrug, this isn't the most hacker-proof game atm
                        nameVisible = controlled.CanSeeCharacter(this);                    
                    }
                    nameTimer = Rand.Range(0.5f, 1.0f);
                }
            }
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
            
            if (GUI.DisableHUD) return;

            Vector2 pos = DrawPosition;
            pos.Y = -pos.Y;

            if (speechBubbleTimer > 0.0f)
            {
                GUI.SpeechBubbleIcon.Draw(spriteBatch, pos - Vector2.UnitY * 100.0f,
                    speechBubbleColor * Math.Min(speechBubbleTimer, 1.0f), 0.0f,
                    Math.Min(speechBubbleTimer, 1.0f));
            }

            if (this == controlled) return;

            if (nameVisible && info != null)
            {
                string name = Info.DisplayName;
                if (controlled == null && name != Info.Name) name += " " + TextManager.Get("Disguised");

                Vector2 namePos = new Vector2(pos.X, pos.Y - 110.0f - (5.0f / cam.Zoom)) - GUI.Font.MeasureString(Info.Name) * 0.5f / cam.Zoom;
            	Vector2 screenSize = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            	Vector2 viewportSize = new Vector2(cam.WorldView.Width, cam.WorldView.Height);
            	namePos.X -= cam.WorldView.X; namePos.Y += cam.WorldView.Y;
            	namePos *= screenSize / viewportSize;
            	namePos.X = (float)Math.Floor(namePos.X); namePos.Y = (float)Math.Floor(namePos.Y);
            	namePos *= viewportSize / screenSize;
            	namePos.X += cam.WorldView.X; namePos.Y -= cam.WorldView.Y;

                Color nameColor = Color.White;

                if (Controlled != null && TeamID != Controlled.TeamID)
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

                GameMain.ParticleManager.CreateParticle("gib",
                    WorldPosition + Rand.Vector(Rand.Range(0.0f, 50.0f)),
                    Rand.Range(0.0f, MathHelper.TwoPi),
                    Rand.Range(200.0f, 700.0f), null);
            }

            for (int i = 0; i < 30; i++)
            {
                GameMain.ParticleManager.CreateParticle("heavygib",
                    WorldPosition + Rand.Vector(Rand.Range(0.0f, 50.0f)),
                    Rand.Range(0.0f, MathHelper.TwoPi),
                    Rand.Range(50.0f, 500.0f), null);
            }
        }
    }
}
