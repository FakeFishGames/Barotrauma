using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Networking;
using Subsurface.Particles;

namespace Subsurface
{
    class Character : Entity, IDamageable
    {
        public static List<Character> characterList = new List<Character>();
        
        public static Queue<CharacterInfo> newCharacterQueue = new Queue<CharacterInfo>();

        public static bool disableControls;

        //the character that the player is currently controlling
        private static Character controlled;

        public static Character Controlled
        {
            get { return controlled; }
            set { controlled = value; }
        }

        public readonly bool IsNetworkPlayer;

        private Inventory inventory;

        public double lastNetworkUpdate;

        public byte largeUpdateTimer;

        public readonly Dictionary<string, ObjectProperty> properties;

        protected Key selectKeyHit;
        protected Key actionKeyHit;
        protected Key actionKeyDown;
        protected Key secondaryKeyHit;
        protected Key secondaryKeyDown;
                
        private Item selectedConstruction;
        private Item[] selectedItems;
        
        public AnimController animController;
        private AIController aiController;

        private Vector2 cursorPosition;

        protected bool needsAir;
        protected float oxygen;
        protected float drowningTime;

        protected Item closestItem;

        protected bool isDead;
        
        bool isHumanoid;

        //the name of the species (e.q. human)
        public readonly string speciesName;

        public CharacterInfo info;

        protected float soundTimer;
        protected float soundInterval;

        private float blood;
                
        private Sound[] sounds;
        //which AIstate each sound is for
        private AIController.AiState[] soundStates;
        
        public Inventory Inventory
        {
            get { return inventory; }
        }

        public Vector2 CursorPosition
        {
            get { return cursorPosition; }
        }

        public float SoundRange
        {
            get { return aiTarget.SoundRange; }
        }

        public float SightRange
        {
            get { return aiTarget.SightRange; }
        }
        private float pressureProtection;
        public float PressureProtection
        {
            get { return pressureProtection; }
            set
            {
                pressureProtection = MathHelper.Clamp(value, 0.0f, 100.0f);
            }
        }

        public float Oxygen
        {
            get { return oxygen; }
            set 
            { 
                oxygen = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (oxygen == 0.0f) Kill();
            }
        }

        public float Health
        {
            get 
            {
                float totalHealth = 0.0f;
                foreach (Limb l in animController.limbs)
                {
                    totalHealth += (l.MaxHealth - l.Damage);

                }
                return totalHealth/animController.limbs.Count();
            }
        }

        public float Blood
        {
            get { return blood; }
            set
            {
                blood = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (blood == 0.0f) Kill();
            }
        }

        public Item[] SelectedItems
        {
            get { return selectedItems; }
        }
        
        public bool HasSelectedItem(Item item)
        {
            return selectedItems.Contains(item);
        }

        public bool TrySelectItem(Item item)
        {
            bool rightHand = ((CharacterInventory)inventory).IsInLimbSlot(item, LimbSlot.RightHand);
            bool leftHand = ((CharacterInventory)inventory).IsInLimbSlot(item, LimbSlot.LeftHand);

            bool selected = false;
            if (rightHand && SelectedItems[0] == null)
            {
                selectedItems[0] = item;
                selected = true;
            }
            if (leftHand && SelectedItems[1] == null)
            {
                selectedItems[1] = item;
                selected = true;
            }

            return selected;
        }

        public bool TrySelectItem(Item item, int index)
        {
            if (selectedItems[index] != null) return false;

            selectedItems[index] = item;
            return true;
        }

        public void DeselectItem(Item item)
        {
            for (int i = 0; i < selectedItems.Length; i++)
            {
                if (selectedItems[i] == item) selectedItems[i] = null;
            }
        }

        public Item SelectedConstruction
        {
            get { return selectedConstruction; }
            set { selectedConstruction = value; }
        }

        public Item ClosestItem
        {
            get { return closestItem; }
        }

        public Key SelectKeyHit
        {
            get { return selectKeyHit; }
        }

        public Key ActionKeyHit
        {
            get { return actionKeyHit; }
        }

        public Key ActionKeyDown
        {
            get { return actionKeyDown; }
        }

        public Key SecondaryKeyHit
        {
            get { return secondaryKeyHit; }
        }

        public Key SecondaryKeyDown
        {
            get { return secondaryKeyDown; }
        }

        public bool IsDead
        {
            get { return isDead; }
        }

        public override Vector2 SimPosition
        {
            get { return animController.limbs[0].SimPosition; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition); }
        }

        public Character(string file) : this(file, Vector2.Zero, null)
        {
        }

        public Character(string file, Vector2 position)
            : this(file, position, null)
        {
        }

        public Character(CharacterInfo characterInfo, Vector2 position, bool isNetworkPlayer = false)
            : this(characterInfo.file, position, characterInfo, isNetworkPlayer)
        {
        }

        public Character(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
        {
            selectKeyHit = new Key(false);
            actionKeyDown = new Key(true);
            actionKeyHit = new Key(false);
            secondaryKeyHit = new Key(false);
            secondaryKeyDown = new Key(true);

            selectedItems = new Item[2];

            IsNetworkPlayer = isNetworkPlayer;

            oxygen = 100.0f;
            blood = 100.0f;
            aiTarget = new AITarget(this);

            properties = ObjectProperty.GetProperties(this);

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null) return;

            speciesName = ToolBox.GetAttributeString(doc.Root, "name", "Unknown");

            isHumanoid = ToolBox.GetAttributeBool(doc.Root, "humanoid", false);

            info = characterInfo ?? new CharacterInfo(file);

            if (isHumanoid)
            {
                animController = new HumanoidAnimController(this, doc.Root.Element("ragdoll"));
                animController.targetDir = Direction.Right;
                inventory = new CharacterInventory(10, this);
            }
            else
            {
                animController = new FishAnimController(this, doc.Root.Element("ragdoll"));
                PressureProtection = 100.0f;
                //FishAnimController fishAnim = (FishAnimController)animController;

                aiController = new EnemyAIController(this, file);
            }

            foreach (Limb limb in animController.limbs)
            {
                limb.body.SetTransform(position+limb.SimPosition, 0.0f);
                //limb.prevPosition = ConvertUnits.ToDisplayUnits(position);
            }

            needsAir = ToolBox.GetAttributeBool(doc.Root, "needsair", false);
            drowningTime = ToolBox.GetAttributeFloat(doc.Root, "drowningtime", 10.0f);

            soundInterval = ToolBox.GetAttributeFloat(doc.Root, "soundinterval", 10.0f);

            var xSounds = doc.Root.Elements("sound").ToList();
            if (xSounds.Any())
            {
                sounds = new Sound[xSounds.Count()];
                soundStates = new AIController.AiState[xSounds.Count()];
                int i = 0;
                foreach (XElement xSound in xSounds)
                {
                    sounds[i] = Sound.Load(xSound.Attribute("file").Value);
                    if (xSound.Attribute("state") == null)
                    {
                        soundStates[i] = AIController.AiState.None;
                    }
                    else
                    {
                        soundStates[i] = (AIController.AiState)Enum.Parse(
                            typeof(AIController.AiState), xSound.Attribute("state").Value, true);
                    }
                    i++;
                }
            }


            animController.FindHull();

            if (info.ID >= 0)
            {
                ID = info.ID;
            }

            characterList.Add(this);
        }


        /// <summary>
        /// Control the characte
        /// </summary>
        public void Control(Camera cam, bool forcePick=false)
        {
            if (isDead) return;

            //find the closest item if selectkey has been hit, or if the character is being
            //controlled by the player (in order to highlight it)
            //closestItem = null;
            if (controlled==this)
            {
                Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition));
                closestItem = FindClosestItem(mouseSimPos);

                if (closestItem != null)
                {
                    closestItem.IsHighlighted = true;
                    if (selectKeyHit.State && closestItem.Pick(this, forcePick))
                    {
                        new NetworkEvent(NetworkEventType.PickItem, ID, true, closestItem.ID);
                    }
                }
            }

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] == null) continue;
                if (i == 1 && selectedItems[0] == selectedItems[1]) continue;
                
                if (actionKeyDown.State) selectedItems[i].Use(this);
                if (secondaryKeyDown.State && selectedItems[i] != null) selectedItems[i].SecondaryUse(this);
                
            }

            if (selectedConstruction != null)
            {
                if (actionKeyDown.State) selectedConstruction.Use(this);
                if (secondaryKeyDown.State) selectedConstruction.SecondaryUse(this);
            }

            if (IsNetworkPlayer)
            {
                selectKeyHit.Reset();
                actionKeyHit.Reset();
                actionKeyDown.Reset();
                secondaryKeyHit.Reset();
                secondaryKeyDown.Reset();
            }
        }

        private Item FindClosestItem(Vector2 mouseSimPos)
        {
            Limb torso = animController.GetLimb(LimbType.Torso);
            Vector2 pos = (torso.body.TargetPosition != Vector2.Zero) ? torso.body.TargetPosition : torso.SimPosition;

            return Item.FindPickable(pos, selectedConstruction == null ? mouseSimPos : selectedConstruction.SimPosition, null, selectedItems);
        }

        /// <summary>
        /// Control the character according to player input
        /// </summary>
        public void ControlLocalPlayer(Camera cam, bool moveCam = true)
        {
            if (isDead) return;

            Limb head = animController.GetLimb(LimbType.Head);

            Lights.LightManager.viewPos = ConvertUnits.ToDisplayUnits(head.SimPosition);

            Vector2 targetMovement = Vector2.Zero;

            if (!disableControls)
            {
                if (PlayerInput.KeyDown(Keys.W)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.S)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.A)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.D)) targetMovement.X += 1.0f;

                //the vertical component is only used for falling through platforms and climbing ladders when not in water,
                //so the movement can't be normalized or the character would walk slower when pressing down/up
                if (animController.InWater)
                {
                    float length = targetMovement.Length();
                    if (length > 0.0f) targetMovement = targetMovement / length;
                }

                if (Keyboard.GetState().IsKeyDown(Keys.LeftShift) && Math.Sign(targetMovement.X) == Math.Sign(animController.Dir))
                    targetMovement *= 3.0f;

                selectKeyHit.SetState(PlayerInput.KeyHit(Keys.E));
                actionKeyHit.SetState(PlayerInput.LeftButtonClicked());
                actionKeyDown.SetState(PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed);
                secondaryKeyHit.SetState(PlayerInput.RightButtonClicked());
                secondaryKeyDown.SetState(PlayerInput.GetMouseState.RightButton == ButtonState.Pressed);
            }
            else
            {
                selectKeyHit.SetState(false);
                actionKeyHit.SetState(false);
                actionKeyDown.SetState(false);
                secondaryKeyHit.SetState(false);
                secondaryKeyDown.SetState(false);
            }

            animController.TargetMovement = targetMovement;
            animController.isStanding = true;

            if (moveCam)
            {
                cam.TargetPos = ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition);
                cam.OffsetAmount = 250.0f;
            }
            
            cursorPosition = cam.ScreenToWorld(PlayerInput.MousePosition);            
            Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);

            if (animController.onGround &&
                !animController.InWater &&
                animController.anim != AnimController.Animation.UsingConstruction)
            {                
                if (mouseSimPos.X < head.SimPosition.X-1.0f)
                {
                    animController.targetDir = Direction.Left;
                }
                else if (mouseSimPos.X > head.SimPosition.X + 1.0f)
                {
                    animController.targetDir = Direction.Right;
                }
            }

            disableControls = false;
        }
        

        public static void UpdateAnimAll(float deltaTime)
        {
            foreach (Character c in characterList)
            {
                if (c.isDead) continue;
                c.animController.UpdateAnim(deltaTime);
            }
        }
        
        public static void UpdateAll(Camera cam, float deltaTime)
        {
            if (newCharacterQueue.Count>0)
            {
                new Character(newCharacterQueue.Dequeue(), Vector2.Zero);
            }

            foreach (Character c in characterList)
            {
                c.Update(cam, deltaTime);
            }
        }

        public void Update(Camera cam, float deltaTime)
        {
            if (isDead) return;

            if (PressureProtection==0.0f && 
                (animController.CurrentHull == null || animController.CurrentHull.LethalPressure >= 100.0f))
            {
                Implode();
                return;
            }

            if (controlled == this) ControlLocalPlayer(cam);
            
            Control(cam);

            UpdateSightRange();
            aiTarget.SoundRange = 0.0f;

            if (needsAir)
            {
                if (animController.HeadInWater)
                {
                    Oxygen -= deltaTime*100.0f / drowningTime;
                }
                else if (animController.CurrentHull != null)
                {
                    float hullOxygen = animController.CurrentHull.OxygenPercentage;
                    hullOxygen -= 30.0f;

                    Oxygen += deltaTime * 100.0f * (hullOxygen / 500.0f);

                    animController.CurrentHull.Oxygen -= Hull.OxygenConsumptionSpeed * deltaTime;
                }
                PressureProtection -= deltaTime*100.0f;
            }

            if (soundTimer > 0)
            {
                soundTimer -= deltaTime;
            }
            else
            {
                PlaySound((aiController == null) ? AIController.AiState.None : aiController.State);
                soundTimer = soundInterval;
            }

            foreach (Limb limb in animController.limbs)
            {
                Blood = blood - limb.Bleeding * deltaTime * 0.1f;
            }

            if (aiController != null) aiController.Update(deltaTime);
        }

        private void UpdateSightRange()
        {
            aiTarget.SightRange = 0.0f;

            //distance is approximated based on the mass of the character 
            //(which corresponds to size because all the characters have the same limb density)
            foreach (Limb limb in animController.limbs)
            {
                aiTarget.SightRange += limb.Mass * 1000.0f;
            }
            //the faster the character is moving, the easier it is to see it
            Limb torso = animController.GetLimb(LimbType.Torso);
            if (torso !=null)
            {
                aiTarget.SightRange += torso.LinearVelocity.Length() * 500.0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            animController.Draw(spriteBatch);

            if (IsNetworkPlayer)
            {
                Vector2 pos = new Vector2(Position.X, -Position.Y - 50.0f) - GUI.font.MeasureString(info.name) * 0.5f;
                spriteBatch.DrawString(GUI.font, info.name, pos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.font, info.name, pos, Color.White);
            }

            //spriteBatch.DrawString(GUI.font, ID.ToString(), ConvertUnits.ToDisplayUnits(animController.limbs[0].Position), Color.White);
            //GUI.DrawLine(spriteBatch, ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y),
            //    ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y) +
            //    ConvertUnits.ToDisplayUnits(animController.targetMovement.X, animController.targetMovement.Y), Color.Green);
        }


        private static GUIProgressBar drowningBar;
        public void DrawHud(SpriteBatch spriteBatch, Camera cam)
        {
            if (drowningBar==null)
            {
                int width = 200, height = 20;
                drowningBar = new GUIProgressBar(new Rectangle(Game1.GraphicsWidth / 2 - width / 2, 20, width, height), Color.Blue, 1.0f);
            }

            drowningBar.BarSize = Controlled.Oxygen / 100.0f;
            if (drowningBar.BarSize < 1.0f)
                drowningBar.Draw(spriteBatch);

            if (Controlled.Inventory != null)
                Controlled.Inventory.Draw(spriteBatch);

            if (closestItem!=null)
            {
                Color color = Color.Orange;

                Vector2 startPos = Position + (closestItem.Position - Position) * 0.7f;
                startPos = cam.WorldToScreen(startPos);

                Vector2 textPos = startPos;

                float stringWidth = GUI.font.MeasureString(closestItem.Prefab.Name).X;
                textPos -= new Vector2(stringWidth / 2, 20);
                spriteBatch.DrawString(GUI.font, closestItem.Prefab.Name, textPos, Color.Black);
                spriteBatch.DrawString(GUI.font, closestItem.Prefab.Name, textPos + new Vector2(1, -1), Color.Orange);
                
                textPos.Y += 50.0f;
                foreach (string text in closestItem.HighlightText)
                {
                    textPos.X = startPos.X - GUI.font.MeasureString(text).X / 2;

                    spriteBatch.DrawString(GUI.font, text, textPos, Color.Black);
                    spriteBatch.DrawString(GUI.font, text, textPos + new Vector2(1, -1), Color.Orange);

                    textPos.Y += 25;
                }                
            }
            
        }

        public void PlaySound(AIController.AiState state)
        {
            if (sounds == null || !sounds.Any()) return;
            var matchingSoundStates = soundStates.Where(x => x == state).ToList();

            int selectedSound = Game1.localRandom.Next(matchingSoundStates.Count());

            int n = 0;
            for (int i = 0; i < sounds.Count(); i++)
            {
                if (soundStates[i] != state) continue;
                if (n == selectedSound)
                {
                    sounds[i].Play(1.0f, 2000.0f,
                            animController.limbs[0].body.FarseerBody);
                    Debug.WriteLine("playing: " + sounds[i]);
                    return;
                }
                n++;
            }
        }

        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound = false)
        {
            animController.StunTimer = Math.Max(animController.StunTimer, stun);

            Limb closestLimb = null;
            float closestDistance = 0.0f;
            foreach (Limb limb in animController.limbs)
            {
                float distance = Vector2.Distance(position, limb.SimPosition);
                if (closestLimb == null || distance < closestDistance)
                {
                    closestLimb = limb;
                    closestDistance = distance;
                }
            }

            Vector2 pull = position - closestLimb.SimPosition;
            if (pull != Vector2.Zero) pull = Vector2.Normalize(pull);
            closestLimb.body.ApplyForce(pull*Math.Min(amount*100.0f, 100.0f));


            return closestLimb.AddDamage(position, damageType, amount, bleedingAmount, playSound);

        }

        public void Stun()
        {
            //for (int i = 0; i < selectedItems.Length; i++ )
            //{
            //    if (selectedItems[i] == null) continue;
            //    selectedItems[i].Drop();
            //    selectedItems[i] = null;
            //}
                
            selectedConstruction = null;
        }

        private void Implode()
        {
            Limb torso= animController.GetLimb(LimbType.Torso);
            if (torso == null) torso = animController.GetLimb(LimbType.Head);

            Vector2 centerOfMass = Vector2.Zero;
            float totalMass = 0.0f;
            foreach (Limb limb in animController.limbs)
            {
                centerOfMass += limb.Mass * limb.SimPosition;
                totalMass += limb.Mass;
            }

            centerOfMass /= totalMass;

            foreach (Limb limb in animController.limbs)
            {
                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 10.0f);
                limb.Damage = 100.0f;
            }

            AmbientSoundManager.PlayDamageSound(DamageSoundType.Implode, 50.0f, torso.body.FarseerBody);
            
            for (int i = 0; i < 10; i++)
            {
                Particle p = Game1.particleManager.CreateParticle("waterblood",
                    torso.SimPosition + new Vector2(ToolBox.RandomFloat(-0.5f, 0.5f), ToolBox.RandomFloat(-0.5f, 0.5f)),
                    Vector2.Zero);
                if (p!=null) p.Size *= 2.0f;

                Game1.particleManager.CreateParticle("bubbles",
                    torso.SimPosition,
                    new Vector2(ToolBox.RandomFloat(-0.5f, 0.5f), ToolBox.RandomFloat(-1.0f,0.5f)));
            }

            foreach (var joint in animController.limbJoints)
            {
                joint.LimitEnabled = false;
            }
            Kill(true);
        }

        public void Kill(bool networkMessage = false)
        {
            if (isDead) return;

            //if the game is run by a client, characters are only killed when the server says so
            if (Game1.client != null)
            {
                if (networkMessage)
                {
                    new NetworkEvent(NetworkEventType.KillCharacter, ID, true);
                }
                else
                {
                    return;
                }
            }

            if (Game1.server != null)
            {
                new NetworkEvent(NetworkEventType.KillCharacter, ID, false);
            }

            if (Game1.gameSession.crewManager!=null)
            {
                Game1.gameSession.crewManager.KillCharacter(this);
            }

            isDead = true;
            animController.movement = Vector2.Zero;
            animController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }

                
            aiTarget.Remove();
            aiTarget = null;

            foreach (Limb limb in animController.limbs)
            {
                if (limb.pullJoint == null) continue;
                limb.pullJoint.Enabled = false;
            }

            foreach (RevoluteJoint joint in animController.limbJoints)
            {
                joint.MotorEnabled = false;
                joint.MaxMotorTorque = 0.0f;
            }
        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            if (type == NetworkEventType.PickItem)
            {
                message.Write((int)data);
                Debug.WriteLine("pickitem");
                return;
            }
            else if (type == NetworkEventType.KillCharacter)
            {
                return;
            }


            //if (type == Networking.NetworkEventType.KeyHit)
            //{
            //    message.Write(selectKeyHit.Dequeue);
                message.Write(actionKeyDown.Dequeue);
                message.Write(secondaryKeyDown.Dequeue);
            //}

            message.Write(NetTime.Now);

            // Write byte = move direction
            message.Write(animController.TargetMovement.X);
            message.Write(animController.TargetMovement.Y);

            message.Write(animController.targetDir==Direction.Right);

            message.Write(cursorPosition.X);
            message.Write(cursorPosition.Y);

            message.Write(largeUpdateTimer <= 0);

            if (largeUpdateTimer<=0)
            {
                foreach (Limb limb in animController.limbs)
                {
                    message.Write(limb.body.Position.X);
                    message.Write(limb.body.Position.Y);

                    message.Write(limb.body.LinearVelocity.X);
                    message.Write(limb.body.LinearVelocity.Y);

                    message.Write(limb.body.Rotation);
                    message.Write(limb.body.AngularVelocity);
                }

                message.Write(animController.StunTimer);

                largeUpdateTimer = 5;
            }
            else
            {
                Limb torso = animController.GetLimb(LimbType.Torso);
                message.Write(torso.body.Position.X);
                message.Write(torso.body.Position.Y);

                largeUpdateTimer = (byte)Math.Max(0, largeUpdateTimer-1);
            }



            if (aiController != null) aiController.FillNetworkData(message);
            
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            if (type == NetworkEventType.PickItem)
            {
                int itemId = message.ReadInt32();
                Item item = FindEntityByID(itemId) as Item;
                if (item != null)
                {
                    Debug.WriteLine("pickitem "+itemId );
                    item.Pick(this);
                }
                else
                {
                }
                
                //DebugConsole.ThrowError("pickitem");

                return;
            } 
            else if (type == NetworkEventType.KillCharacter)
            {
                Kill(true);
                if (Game1.client != null && controlled == this)
                {
                    Game1.client.AddChatMessage("YOU HAVE DIED. Your chat messages will only be visible to other dead players.", ChatMessageType.Dead);
                }
                return;
            }

            //if (type == Networking.NetworkEventType.KeyHit)
            //{
            //    selectKeyHit.State = message.ReadBoolean();

            //}
            actionKeyDown.State = message.ReadBoolean();
            secondaryKeyDown.State = message.ReadBoolean();

            double sendingTime = message.ReadDouble();

            Vector2 targetMovement = Vector2.Zero;

            targetMovement.X = message.ReadFloat();
            targetMovement.Y = message.ReadFloat();
            
            animController.isStanding = true;

            bool targetDir = message.ReadBoolean();

            Vector2 cursorPos = Vector2.Zero;
            cursorPos.X = message.ReadFloat();
            cursorPos.Y = message.ReadFloat();

            if (sendingTime > lastNetworkUpdate)
            {
                cursorPosition = cursorPos;

                animController.TargetMovement= targetMovement;
                animController.targetDir = (targetDir) ? Direction.Right : Direction.Left;
                
                if (message.ReadBoolean())
                {
                    foreach (Limb limb in animController.limbs)
                    {
                        Vector2 pos = Vector2.Zero;
                        pos.X = message.ReadFloat();
                        pos.Y = message.ReadFloat();

                        Vector2 vel = Vector2.Zero;
                        vel.X = message.ReadFloat();
                        vel.Y = message.ReadFloat();

                        float rotation = message.ReadFloat();
                        float angularVel = message.ReadFloat();

                        if (limb.body == null) continue;

                        if (vel != Vector2.Zero && vel.Length() > 100.0f) { }

                        if (pos != Vector2.Zero && pos.Length() > 100.0f) { }

                        limb.body.TargetVelocity = vel;
                        limb.body.TargetPosition = pos;// +vel * (float)(deltaTime / 60.0);
                        limb.body.TargetRotation = rotation;// +angularVel * (float)(deltaTime / 60.0);
                        limb.body.TargetAngularVelocity = angularVel;
                    }

                    animController.StunTimer = message.ReadFloat();

                    largeUpdateTimer = 1;
                }
                else
                {
                    Vector2 pos = Vector2.Zero;
                    pos.X = message.ReadFloat();
                    pos.Y = message.ReadFloat();

                    Limb torso = animController.GetLimb(LimbType.Torso);
                    torso.body.TargetPosition = pos;

                    largeUpdateTimer = 0;
                }

                if (aiController != null) aiController.ReadNetworkData(message);

                lastNetworkUpdate = sendingTime;
            }
        }

        public override void Remove()
        {
            base.Remove();

            characterList.Remove(this);

            if (controlled == this) controlled = null;

            if (Game1.client!=null && Game1.client.Character == this) Game1.client.Character = null; 

            if (aiTarget != null)
                aiTarget.Remove();

            if (animController!=null)
                animController.Remove();
        }

    }
}
