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
        public static bool DisableControls;

        public static bool DebugDrawInteract;

        protected float soundTimer;
        protected float soundInterval;
        protected float hudInfoTimer;
        protected bool hudInfoVisible;

        protected float lastRecvPositionUpdateTime;

        private float hudInfoHeight;

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
                if (controlled != null) controlled.Enabled = true;
                GameMain.GameSession?.CrewManager?.SetCharacterSelected(controlled);
                CharacterHealth.OpenHealthWindow = null;
                
            }
        }
        
        private Dictionary<object, HUDProgressBar> hudProgressBars;

        public Dictionary<object, HUDProgressBar> HUDProgressBars
        {
            get { return hudProgressBars; }
        }

        private float blurStrength;
        public float BlurStrength
        {
            get { return blurStrength; }
            set { blurStrength = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        private float distortStrength;
        public float DistortStrength
        {
            get { return distortStrength; }
            set { distortStrength = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        private float radialDistortStrength;
        public float RadialDistortStrength
        {
            get { return radialDistortStrength; }
            set { radialDistortStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        private float chromaticAberrationStrength;
        public float ChromaticAberrationStrength
        {
            get { return chromaticAberrationStrength; }
            set { chromaticAberrationStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public string BloodDecalName
        {
            get;
            private set;
        }
                
        private List<ParticleEmitter> bloodEmitters = new List<ParticleEmitter>();
        public IEnumerable<ParticleEmitter> BloodEmitters
        {
            get { return bloodEmitters; }
        }

        private List<ParticleEmitter> gibEmitters = new List<ParticleEmitter>();
        public IEnumerable<ParticleEmitter> GibEmitters
        {
            get { return gibEmitters; }
        }

        partial void InitProjSpecific(XDocument doc)
        {
            soundInterval = doc.Root.GetAttributeFloat("soundinterval", 10.0f);

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];

            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key((InputType)i);
            }

            BloodDecalName = doc.Root.GetAttributeString("blooddecal", "");

            sounds = new List<CharacterSound>();
            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sound":
                        sounds.Add(new CharacterSound(subElement));
                        break;
                    case "bloodemitter":
                        bloodEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "gibemitter":
                        gibEmitters.Add(new ParticleEmitter(subElement));
                        break;
                }
            }

            hudProgressBars = new Dictionary<object, HUDProgressBar>();
        }


        /// <summary>
        /// Control the Character according to player input
        /// </summary>
        public void ControlLocalPlayer(float deltaTime, Camera cam, bool moveCam = true)
        {
            if (DisableControls)
            {
                foreach (Key key in keys)
                {
                    if (key == null) continue;
                    key.Reset();
                }
            }
            else
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].SetState();
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
                if (AnimController.CurrentHull?.Submarine != null)
                {
                    cursorPosition -= AnimController.CurrentHull.Submarine.Position;
                }

                Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);
                if (GUI.PauseMenuOpen)
                {
                    cam.OffsetAmount = 0.0f;
                }
                else if (SelectedConstruction != null && SelectedConstruction.components.Any(ic => (ic.GuiFrame != null && GUI.IsMouseOn(ic.GuiFrame))))
                {
                    cam.OffsetAmount = 0.0f;
                }
                else if (Lights.LightManager.ViewTarget == this && Vector2.DistanceSquared(AnimController.Limbs[0].SimPosition, mouseSimPos) > 1.0f)
                {
                    if (GUI.PauseMenuOpen || IsUnconscious)
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
            }

            DisableControls = false;
        }

        partial void UpdateControlled(float deltaTime, Camera cam)
        {
            if (controlled != this) return;

            ControlLocalPlayer(deltaTime, cam);

            Lights.LightManager.ViewTarget = this;
            CharacterHUD.Update(deltaTime, this, cam);

            bool removeProgressBars = false;

            foreach (HUDProgressBar progressBar in hudProgressBars.Values)
            {
                if (progressBar.FadeTimer <= 0.0f)
                {
                    removeProgressBars = true;
                    continue;
                }
                progressBar.Update(deltaTime);
            }

            if (removeProgressBars)
            {
                // TODO: this generates garbage, can we fix anything here?
                foreach (var pb in hudProgressBars.Where(pb => pb.Value.FadeTimer <= 0.0f).ToList())
                {
                    hudProgressBars.Remove(pb.Key);
                }
            }
        }
        
        partial void KillProjSpecific()
        {
            if (GameMain.NetworkMember != null && controlled == this)
            {
                string chatMessage = CauseOfDeath.Type == CauseOfDeathType.Affliction ?
                    CauseOfDeath.Affliction.SelfCauseOfDeathDescription :
                    TextManager.Get("Self_CauseOfDeathDescription." + CauseOfDeath.Type.ToString());

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
            
            if (GameMain.NetworkMember?.Character == this) GameMain.NetworkMember.Character = null;

            if (Lights.LightManager.ViewTarget == this) Lights.LightManager.ViewTarget = null;
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (info != null || Vitality < MaxVitality * 0.98f)
            {
                hudInfoTimer -= deltaTime;
                if (hudInfoTimer <= 0.0f)
                {
                    if (controlled == null)
                    {
                        hudInfoVisible = true;
                    }

                    //if the character is not in the camera view, the name can't be visible and we can avoid the expensive visibility checks
                    else if (WorldPosition.X < cam.WorldView.X || WorldPosition.X > cam.WorldView.Right || 
                            WorldPosition.Y > cam.WorldView.Y || WorldPosition.Y < cam.WorldView.Y - cam.WorldView.Height)
                    {
                        hudInfoVisible = false;
                    }
                    else
                    {
                        //Ideally it shouldn't send the character entirely if we can't see them but /shrug, this isn't the most hacker-proof game atm
                        hudInfoVisible = controlled.CanSeeCharacter(this);                    
                    }
                    hudInfoTimer = Rand.Range(0.5f, 1.0f);
                }
            }

            if (controlled == this)
            {
                CharacterHealth.UpdateHUD(deltaTime);
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
                CharacterHealth.AddToGUIUpdateList();
            }
        }
        
        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            if (!Enabled) return;

            AnimController.Draw(spriteBatch, cam);
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera cam, bool drawHealth = true)
        {
            CharacterHUD.Draw(spriteBatch, this, cam);
            if (drawHealth) CharacterHealth.DrawHUD(spriteBatch);
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
            pos.Y += hudInfoHeight;

            if (CurrentHull != null && DrawPosition.Y > CurrentHull.WorldRect.Y - 130.0f)
            {
                float lowerAmount = DrawPosition.Y - (CurrentHull.WorldRect.Y - 130.0f);
                hudInfoHeight = MathHelper.Lerp(hudInfoHeight, 100.0f - lowerAmount, 0.1f);
                hudInfoHeight = Math.Max(hudInfoHeight, 20.0f);
            }
            else
            {
                hudInfoHeight = MathHelper.Lerp(hudInfoHeight, 100.0f, 0.1f);
            }

            pos.Y = -pos.Y;

            if (speechBubbleTimer > 0.0f)
            {
                GUI.SpeechBubbleIcon.Draw(spriteBatch, pos - Vector2.UnitY * 30,
                    speechBubbleColor * Math.Min(speechBubbleTimer, 1.0f), 0.0f,
                    Math.Min(speechBubbleTimer, 1.0f));
            }

            if (this == controlled)
            {
                if (DebugDrawInteract)
                {
                    Vector2 cursorPos = cam.ScreenToWorld(PlayerInput.MousePosition);
                    cursorPos.Y = -cursorPos.Y;
                    foreach (Item item in debugInteractablesAtCursor)
                    {
                        GUI.DrawLine(spriteBatch, cursorPos,
                            new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), Color.LightGreen, width: 4);
                    }
                    foreach (Item item in debugInteractablesInRange)
                    {
                        GUI.DrawLine(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y),
                            new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), Color.White * 0.1f, width: 4);
                    }
                    foreach (Pair<Item, float> item in debugInteractablesNearCursor)
                    {
                        GUI.DrawLine(spriteBatch,
                            cursorPos,
                            new Vector2(item.First.DrawPosition.X, -item.First.DrawPosition.Y),
                            ToolBox.GradientLerp(item.Second, Color.Red, Color.Orange, Color.Green), width: 2);
                    }
                }
                return;
            }

            float hoverRange = 300.0f;
            float fadeOutRange = 200.0f;
            float cursorDist = Vector2.Distance(WorldPosition, cam.ScreenToWorld(PlayerInput.MousePosition));
            float hudInfoAlpha = MathHelper.Clamp(1.0f - (cursorDist - (hoverRange - fadeOutRange)) / fadeOutRange, 0.2f, 1.0f);
            
            if (hudInfoVisible && info != null &&
                (controlled == null || this != controlled.focusedCharacter))
            {
                string name = Info.DisplayName;
                if (controlled == null && name != Info.Name) name += " " + TextManager.Get("Disguised");

                Vector2 namePos = new Vector2(pos.X, pos.Y - 10.0f - (5.0f / cam.Zoom)) - GUI.Font.MeasureString(name) * 0.5f / cam.Zoom;

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
                GUI.Font.DrawString(spriteBatch, name, namePos, nameColor * hudInfoAlpha, 0.0f, Vector2.Zero, 1.0f / cam.Zoom, SpriteEffects.None, 0.0f);

                if (GameMain.DebugDraw)
                {
                    GUI.Font.DrawString(spriteBatch, ID.ToString(), namePos - new Vector2(0.0f, 20.0f), Color.White);
                }
            }

            if (IsDead) return;
            
            if (Vitality < MaxVitality * 0.98f && hudInfoVisible)
            {
                Vector2 healthBarPos = new Vector2(pos.X - 50, -pos.Y);
                GUI.DrawProgressBar(spriteBatch, healthBarPos, new Vector2(100.0f, 15.0f),
                    Vitality / MaxVitality, 
                    Color.Lerp(Color.Red, Color.Green, Vitality / MaxVitality) * 0.8f * hudInfoAlpha,
                    new Color(0.5f, 0.57f, 0.6f, 1.0f) * hudInfoAlpha);
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
            SoundPlayer.PlaySound(selectedSound.Sound, selectedSound.Volume, selectedSound.Range, AnimController.WorldPosition, CurrentHull);
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
