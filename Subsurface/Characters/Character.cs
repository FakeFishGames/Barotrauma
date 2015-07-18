using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Networking;
using Subsurface.Particles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Subsurface
{
    class Character : Entity, IDamageable, IPropertyObject
    {
        public static List<Character> CharacterList = new List<Character>();
        
        public static Queue<CharacterInfo> NewCharacterQueue = new Queue<CharacterInfo>();

        public static bool DisableControls;

        //the character that the player is currently controlling
        private static Character controlled;

        public static Character Controlled
        {
            get { return controlled; }
            set { controlled = value; }
        }

        public readonly bool IsNetworkPlayer;

        private Inventory inventory;

        public double LastNetworkUpdate;

        public float LargeUpdateTimer;

        public readonly Dictionary<string, ObjectProperty> Properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return Properties; }
        }

        protected Key selectKeyHit;
        protected Key actionKeyHit, actionKeyDown;
        protected Key secondaryKeyHit, secondaryKeyDown;
                
        private Item selectedConstruction;
        private Item[] selectedItems;
        
        public AnimController AnimController;
        private AIController aiController;

        private Vector2 cursorPosition;

        protected bool needsAir;
        protected float oxygen;
        protected float drowningTime;

        protected float health;
        protected float maxHealth;

        protected Item closestItem;

        protected bool isDead;
        
        bool isHumanoid;

        //the name of the species (e.q. human)
        public readonly string SpeciesName;

        private CharacterInfo info;

        public CharacterInfo Info
        {
            get
            { 
                return info;
            }
            set 
            {
                info = value;
                if (info != null) info.Character = this;
            }
        }

        protected float soundTimer;
        protected float soundInterval;

        private float bleeding;
        //private float blood;
                
        private Sound[] sounds;
        private float[] soundRange;
        //which AIstate each sound is for
        private AIController.AiState[] soundStates;
        
        public string Name
        {
            get
            {
                return SpeciesName;
            }
        }

        public float Mass
        {
            get { return AnimController.Mass; }
        }

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
                return health;
                //float totalHealth = 0.0f;
                //foreach (Limb l in animController.limbs)
                //{
                //    totalHealth += (l.MaxHealth - l.Damage);

                //}
                //return totalHealth/animController.limbs.Count();
            }
            set
            {
                health = MathHelper.Clamp(value, 0.0f, maxHealth);
                if (health==0.0f) Kill();
            }
        }

        //public float Blood
        //{
        //    get { return blood; }
        //    set
        //    {
        //        blood = MathHelper.Clamp(value, 0.0f, 100.0f);
        //        if (blood == 0.0f) Kill();
        //    }
        //}

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
            get { return AnimController.limbs[0].SimPosition; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(AnimController.limbs[0].SimPosition); }
        }

        public Character(string file) : this(file, Vector2.Zero, null)
        {
        }

        public Character(string file, Vector2 position)
            : this(file, position, null)
        {
        }

        public Character(CharacterInfo characterInfo, WayPoint spawnPoint, bool isNetworkPlayer = false)
            : this(characterInfo.File, spawnPoint.SimPosition, characterInfo, isNetworkPlayer)
        {

        }

        public Character(CharacterInfo characterInfo, Vector2 position, bool isNetworkPlayer = false)
            : this(characterInfo.File, position, characterInfo, isNetworkPlayer)
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
            //blood = 100.0f;
            aiTarget = new AITarget(this);

            Properties = ObjectProperty.GetProperties(this);

            Info = characterInfo==null ? new CharacterInfo(file) : characterInfo;

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null) return;
            
            SpeciesName = ToolBox.GetAttributeString(doc.Root, "name", "Unknown");

            isHumanoid = ToolBox.GetAttributeBool(doc.Root, "humanoid", false);
            
            if (isHumanoid)
            {
                AnimController = new HumanoidAnimController(this, doc.Root.Element("ragdoll"));
                AnimController.TargetDir = Direction.Right;
                inventory = new CharacterInventory(10, this);
            }
            else
            {
                AnimController = new FishAnimController(this, doc.Root.Element("ragdoll"));
                PressureProtection = 100.0f;
                //FishAnimController fishAnim = (FishAnimController)animController;

                aiController = new EnemyAIController(this, file);
            }

            foreach (Limb limb in AnimController.limbs)
            {
                limb.body.SetTransform(position+limb.SimPosition, 0.0f);
                //limb.prevPosition = ConvertUnits.ToDisplayUnits(position);
            }

            maxHealth = ToolBox.GetAttributeFloat(doc.Root, "health", 100.0f);
            health = maxHealth;

            needsAir = ToolBox.GetAttributeBool(doc.Root, "needsair", false);
            drowningTime = ToolBox.GetAttributeFloat(doc.Root, "drowningtime", 10.0f);

            soundInterval = ToolBox.GetAttributeFloat(doc.Root, "soundinterval", 10.0f);

            var soundElements = doc.Root.Elements("sound").ToList();
            if (soundElements.Any())
            {
                sounds = new Sound[soundElements.Count()];
                soundStates = new AIController.AiState[soundElements.Count()];
                soundRange = new float[soundElements.Count()];
                int i = 0;
                foreach (XElement soundElement in soundElements)
                {
                    sounds[i] = Sound.Load(soundElement.Attribute("file").Value);
                    soundRange[i] = ToolBox.GetAttributeFloat(soundElement, "range", 1000.0f);
                    if (soundElement.Attribute("state") == null)
                    {
                        soundStates[i] = AIController.AiState.None;
                    }
                    else
                    {
                        soundStates[i] = (AIController.AiState)Enum.Parse(
                            typeof(AIController.AiState), soundElement.Attribute("state").Value, true);
                    }
                    i++;
                }
            }

            if (Info.PickedItemIDs.Any())
            {
                foreach (int id in Info.PickedItemIDs)
                {
                    Item item = FindEntityByID(id) as Item;
                    if (item == null) continue;

                    item.Pick(this);
                }
            }

            AnimController.FindHull();

            //if (info.ID >= 0)
            //{
            //    ID = info.ID;
            //}

            CharacterList.Add(this);
        }

        public void GiveJobItems(WayPoint spawnPoint)
        {
            if (Info == null || Info.Job == null) return;
            
            foreach (string itemName in Info.Job.SpawnItemNames)
            {
                ItemPrefab itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Tried to spawn ''" + Name + "'' with the item ''" + itemName + "''. Matching item prefab not found.");
                    continue;
                }

                Item item = new Item(itemPrefab, Position);
                inventory.TryPutItem(item, item.AllowedSlots, false);
                
                if (item.Prefab.Name == "ID Card" && spawnPoint!=null)
                {
                    foreach (string s in spawnPoint.IdCardTags)
                    {
                        
                        item.AddTag(s);
                    }                    
                }
            }            
        }

        public int GetSkillLevel(string skillName)
        {
            return Info.Job.GetSkillLevel(skillName);
        }

        public void Control(float deltaTime, Camera cam, bool forcePick = false)
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
                
                if (actionKeyDown.State) selectedItems[i].Use(deltaTime, this);
                if (secondaryKeyDown.State && selectedItems[i] != null) selectedItems[i].SecondaryUse(deltaTime, this);
                
            }

            if (selectedConstruction != null)
            {
                if (actionKeyDown.State) selectedConstruction.Use(deltaTime, this);
                if (secondaryKeyDown.State) selectedConstruction.SecondaryUse(deltaTime, this);
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
            Limb torso = AnimController.GetLimb(LimbType.Torso);
            Vector2 pos = (torso.body.TargetPosition != Vector2.Zero) ? torso.body.TargetPosition : torso.SimPosition;

            return Item.FindPickable(pos, selectedConstruction == null ? mouseSimPos : selectedConstruction.SimPosition, null, selectedItems);
        }

        /// <summary>
        /// Control the character according to player input
        /// </summary>
        public void ControlLocalPlayer(Camera cam, bool moveCam = true)
        {
            //if (isDead)
            //{

            //    return;
            //}

            Limb head = AnimController.GetLimb(LimbType.Head);

            Lights.LightManager.ViewPos = ConvertUnits.ToDisplayUnits(head.SimPosition);

            Vector2 targetMovement = Vector2.Zero;

            if (!DisableControls)
            {
                if (PlayerInput.KeyDown(Keys.W)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.S)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.A)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.D)) targetMovement.X += 1.0f;

                //the vertical component is only used for falling through platforms and climbing ladders when not in water,
                //so the movement can't be normalized or the character would walk slower when pressing down/up
                if (AnimController.InWater)
                {
                    float length = targetMovement.Length();
                    if (length > 0.0f) targetMovement = targetMovement / length;
                }

                if (Keyboard.GetState().IsKeyDown(Keys.LeftShift) && Math.Sign(targetMovement.X) == Math.Sign(AnimController.Dir))
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

            AnimController.TargetMovement = targetMovement;
            AnimController.IsStanding = true;

            if (moveCam)
            {
                cam.TargetPos = ConvertUnits.ToDisplayUnits(AnimController.limbs[0].SimPosition);
                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, 250.0f, 0.05f);
            }
            
            cursorPosition = cam.ScreenToWorld(PlayerInput.MousePosition);            
            Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);
            if (Vector2.Distance(AnimController.limbs[0].SimPosition, mouseSimPos)>1.0f)
            {
                Body body = Submarine.PickBody(AnimController.limbs[0].SimPosition, mouseSimPos);
                Structure structure = null;
                if (body != null) structure = body.UserData as Structure;
                if (structure!=null)
                {
                    if (!structure.CastShadow && moveCam)
                    {
                        cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, 500.0f, 0.05f);
                    }
                }
            }


            if (AnimController.onGround &&
                !AnimController.InWater &&
                AnimController.Anim != AnimController.Animation.UsingConstruction)
            {                
                if (mouseSimPos.X < head.SimPosition.X-1.0f)
                {
                    AnimController.TargetDir = Direction.Left;
                }
                else if (mouseSimPos.X > head.SimPosition.X + 1.0f)
                {
                    AnimController.TargetDir = Direction.Right;
                }
            }

            DisableControls = false;
        }
        

        public static void UpdateAnimAll(float deltaTime)
        {
            foreach (Character c in CharacterList)
            {
                if (c.isDead) continue;
                c.AnimController.UpdateAnim(deltaTime);
            }
        }
        
        public static void UpdateAll(Camera cam, float deltaTime)
        {
            if (NewCharacterQueue.Count>0)
            {
                new Character(NewCharacterQueue.Dequeue(), Vector2.Zero);
            }

            foreach (Character c in CharacterList)
            {
                c.Update(cam, deltaTime);
            }
        }

        public void Update(Camera cam, float deltaTime)
        {
            if (isDead)
            {
                if (controlled == this)
                {
                    cam.Zoom = MathHelper.Lerp(cam.Zoom, 1.5f, 0.1f);
                    cam.TargetPos = ConvertUnits.ToDisplayUnits(AnimController.limbs[0].SimPosition);
                    cam.OffsetAmount = 0.0f;
                }
                return;
            }

            if (PressureProtection==0.0f && 
                (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 100.0f))
            {
                Implode();
                return;
            }

            if (controlled == this) ControlLocalPlayer(cam);

            Control(deltaTime, cam);

            UpdateSightRange();
            aiTarget.SoundRange = 0.0f;

            if (needsAir)
            {
                if (AnimController.HeadInWater)
                {
                    Oxygen -= deltaTime*100.0f / drowningTime;
                }
                else if (AnimController.CurrentHull != null)
                {
                    float hullOxygen = AnimController.CurrentHull.OxygenPercentage;
                    hullOxygen -= 30.0f;

                    Oxygen += deltaTime * 100.0f * (hullOxygen / 500.0f);

                    AnimController.CurrentHull.Oxygen -= Hull.OxygenConsumptionSpeed * deltaTime;
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

            //foreach (Limb limb in animController.limbs)
            //{
                Health = health - bleeding * deltaTime;
            //}

            if (aiController != null) aiController.Update(deltaTime);
        }

        private void UpdateSightRange()
        {
            aiTarget.SightRange = 0.0f;

            //distance is approximated based on the mass of the character 
            //(which corresponds to size because all the characters have the same limb density)
            foreach (Limb limb in AnimController.limbs)
            {
                aiTarget.SightRange += limb.Mass * 1000.0f;
            }
            //the faster the character is moving, the easier it is to see it
            Limb torso = AnimController.GetLimb(LimbType.Torso);
            if (torso !=null)
            {
                aiTarget.SightRange += torso.LinearVelocity.Length() * 500.0f;
            }
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            AnimController.Draw(spriteBatch);

            if (IsNetworkPlayer)
            {
                Vector2 namePos = new Vector2(Position.X, -Position.Y - 80.0f) - GUI.Font.MeasureString(Info.Name) * 0.5f;
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos, Color.White);

                if (Game1.DebugDraw)
                {
                    spriteBatch.DrawString(GUI.Font, ID.ToString(), namePos - new Vector2(0.0f, 20.0f), Color.White);
                }
            }

            Vector2 pos = ConvertUnits.ToDisplayUnits(AnimController.limbs[0].SimPosition);
            pos.Y = -pos.Y;


            if (this == Character.controlled) return;

            Vector2 healthBarPos = new Vector2(Position.X - 50, -Position.Y - 50.0f);
            GUI.DrawRectangle(spriteBatch, new Rectangle((int)healthBarPos.X-2, (int)healthBarPos.Y-2, 100+4, 15+4), Color.Black, false);
            GUI.DrawRectangle(spriteBatch, new Rectangle((int)healthBarPos.X, (int)healthBarPos.Y, (int)(100.0f*(health/maxHealth)), 15), Color.Red, true);


            //GUI.DrawLine(spriteBatch, ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y),
            //    ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y) +
            //    ConvertUnits.ToDisplayUnits(animController.targetMovement.X, animController.targetMovement.Y), Color.Green);
        }


        private static GUIProgressBar drowningBar, healthBar;
        public void DrawHud(SpriteBatch spriteBatch, Camera cam)
        {
            if (drowningBar==null)
            {
                int width = 100, height = 20;
                drowningBar = new GUIProgressBar(new Rectangle(20, Game1.GraphicsHeight/2, width, height), Color.Blue, 1.0f);

                healthBar = new GUIProgressBar(new Rectangle(20, Game1.GraphicsHeight / 2 + 30, width, height), Color.Red, 1.0f);
            }

            drowningBar.BarSize = Controlled.Oxygen / 100.0f;
            if (drowningBar.BarSize < 0.95f) drowningBar.Draw(spriteBatch);

            healthBar.BarSize = health / maxHealth;
            if (healthBar.BarSize < 1.0f) healthBar.Draw(spriteBatch);

            if (Controlled.Inventory != null) Controlled.Inventory.Draw(spriteBatch);

            if (closestItem!=null)
            {
                Color color = Color.Orange;

                Vector2 startPos = Position + (closestItem.Position - Position) * 0.7f;
                startPos = cam.WorldToScreen(startPos);

                Vector2 textPos = startPos;

                float stringWidth = GUI.Font.MeasureString(closestItem.Prefab.Name).X;
                textPos -= new Vector2(stringWidth / 2, 20);
                spriteBatch.DrawString(GUI.Font, closestItem.Prefab.Name, textPos, Color.Black);
                spriteBatch.DrawString(GUI.Font, closestItem.Prefab.Name, textPos + new Vector2(1, -1), Color.Orange);
                
                textPos.Y += 50.0f;
                foreach (ColoredText coloredText in closestItem.GetHUDTexts(Controlled))
                {
                    textPos.X = startPos.X - GUI.Font.MeasureString(coloredText.text).X / 2;

                    spriteBatch.DrawString(GUI.Font, coloredText.text, textPos, Color.Black);
                    spriteBatch.DrawString(GUI.Font, coloredText.text, textPos + new Vector2(1, -1), coloredText.color);

                    textPos.Y += 25;
                }                
            }
            
        }

        public void PlaySound(AIController.AiState state)
        {
            if (sounds == null || !sounds.Any()) return;
            var matchingSoundStates = soundStates.Where(x => x == state).ToList();

            int selectedSound = Rand.Int(matchingSoundStates.Count());

            int n = 0;
            for (int i = 0; i < sounds.Count(); i++)
            {
                if (soundStates[i] != state) continue;
                if (n == selectedSound)
                {
                    sounds[i].Play(1.0f, 2000.0f,
                            AnimController.limbs[0].body.FarseerBody);
                    Debug.WriteLine("playing: " + sounds[i]);
                    return;
                }
                n++;
            }
        }

        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound = false)
        {
            AnimController.StunTimer = Math.Max(AnimController.StunTimer, stun);

            Limb closestLimb = null;
            float closestDistance = 0.0f;
            foreach (Limb limb in AnimController.limbs)
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


            AttackResult attackResult = closestLimb.AddDamage(position, damageType, amount, bleedingAmount, playSound);
            health -= attackResult.Damage;
            bleeding += attackResult.Bleeding;

            return attackResult;

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
            Limb torso= AnimController.GetLimb(LimbType.Torso);
            if (torso == null) torso = AnimController.GetLimb(LimbType.Head);

            Vector2 centerOfMass = Vector2.Zero;
            foreach (Limb limb in AnimController.limbs)
            {
                centerOfMass += limb.Mass * limb.SimPosition;
            }

            centerOfMass /= AnimController.Mass;

            health = 0.0f;

            foreach (Limb limb in AnimController.limbs)
            {
                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 10.0f);
               // limb.Damage = 100.0f;
            }

            AmbientSoundManager.PlayDamageSound(DamageSoundType.Implode, 50.0f, torso.body.FarseerBody);
            
            for (int i = 0; i < 10; i++)
            {
                Particle p = Game1.particleManager.CreateParticle("waterblood",
                    torso.SimPosition + new Vector2(Rand.Range(-0.5f, 0.5f), Rand.Range(-0.5f, 0.5f)),
                    Vector2.Zero);
                if (p!=null) p.Size *= 2.0f;

                Game1.particleManager.CreateParticle("bubbles",
                    torso.SimPosition,
                    new Vector2(Rand.Range(-0.5f, 0.5f), Rand.Range(-1.0f,0.5f)));
            }

            foreach (var joint in AnimController.limbJoints)
            {
                joint.LimitEnabled = false;
            }
            Kill(true);
        }

        public void Kill(bool networkMessage = false)
        {
            if (isDead) return;

            isDead = true;
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }

                
            aiTarget.Remove();
            aiTarget = null;

            foreach (Limb limb in AnimController.limbs)
            {
                if (limb.pullJoint == null) continue;
                limb.pullJoint.Enabled = false;
            }

            foreach (RevoluteJoint joint in AnimController.limbJoints)
            {
                joint.MotorEnabled = false;
                joint.MaxMotorTorque = 0.0f;
            }


            //if the game is run by a client, characters are only killed when the server says so
            if (Game1.Client != null)
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

            if (Game1.Server != null)
            {
                new NetworkEvent(NetworkEventType.KillCharacter, ID, false);
            }

            if (Game1.GameSession != null)
            {
                Game1.GameSession.KillCharacter(this);
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
            message.Write(AnimController.TargetMovement.X);
            message.Write(AnimController.TargetMovement.Y);

            message.Write(AnimController.TargetDir==Direction.Right);

            message.Write(cursorPosition.X);
            message.Write(cursorPosition.Y);
            
            message.Write(LargeUpdateTimer <= 0);

            if (LargeUpdateTimer<=0)
            {
                int i = 0;
                foreach (Limb limb in AnimController.limbs)
                {
                    message.Write(limb.body.Position.X);
                    message.Write(limb.body.Position.Y);

                    message.Write(limb.body.LinearVelocity.X);
                    message.Write(limb.body.LinearVelocity.Y);

                    message.Write(limb.body.Rotation);
                    message.Write(limb.body.AngularVelocity);
                    i++;
                }

                message.Write(AnimController.StunTimer);

                LargeUpdateTimer = 5;
            }
            else
            {
                Limb torso = AnimController.GetLimb(LimbType.Torso);
                message.Write(torso.body.Position.X);
                message.Write(torso.body.Position.Y);

                LargeUpdateTimer = Math.Max(0, LargeUpdateTimer-1);
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
                if (Game1.Client != null && controlled == this)
                {
                    Game1.Client.AddChatMessage("YOU HAVE DIED. Your chat messages will only be visible to other dead players.", ChatMessageType.Dead);
                }
                return;
            }

            bool actionKeyState     = false;
            bool secondaryKeyState  = false;
            double sendingTime      = 0.0f;
            Vector2 targetMovement  = Vector2.Zero;
            bool targetDir          = false;
            Vector2 cursorPos       = Vector2.Zero;

            try
            {
                actionKeyState      = message.ReadBoolean();
                secondaryKeyState   = message.ReadBoolean();
            
                sendingTime         = message.ReadDouble();

                targetMovement      = new Vector2 (message.ReadFloat(), message.ReadFloat());
                targetDir           = message.ReadBoolean();

                cursorPos           = new Vector2(message.ReadFloat(), message.ReadFloat());
            }

            catch
            {
                return;
            }

            AnimController.IsStanding = true;

            actionKeyDown.State = actionKeyState;
            secondaryKeyDown.State = secondaryKeyState;

            if (sendingTime <= LastNetworkUpdate) return;
            
            cursorPosition = cursorPos;

            AnimController.TargetMovement= targetMovement;
            AnimController.TargetDir = (targetDir) ? Direction.Right : Direction.Left;
                
            if (message.ReadBoolean())
            {
                foreach (Limb limb in AnimController.limbs)
                {
                    Vector2 pos = Vector2.Zero;
                    pos.X = message.ReadFloat();
                    pos.Y = message.ReadFloat();

                    Vector2 vel = Vector2.Zero;
                    vel.X = message.ReadFloat();
                    vel.Y = message.ReadFloat();

                    float rotation = message.ReadFloat();
                    float angularVel = message.ReadFloat();                    

                    //if (vel != Vector2.Zero && vel.Length() > 100.0f) { }

                    //if (pos != Vector2.Zero && pos.Length() > 100.0f) { }
                    
                    if (limb.body != null)
                    {
                        limb.body.TargetVelocity = vel;
                        limb.body.TargetPosition = pos;// +vel * (float)(deltaTime / 60.0);
                        limb.body.TargetRotation = rotation;// +angularVel * (float)(deltaTime / 60.0);
                        limb.body.TargetAngularVelocity = angularVel;
                    }

                }

                AnimController.StunTimer = message.ReadFloat();

                LargeUpdateTimer = 1;
            }
            else
            {
                Vector2 pos = Vector2.Zero;
                pos.X = message.ReadFloat();
                pos.Y = message.ReadFloat();

                Limb torso = AnimController.GetLimb(LimbType.Torso);
                torso.body.TargetPosition = pos;

                LargeUpdateTimer = 0;
            }

            if (aiController != null) aiController.ReadNetworkData(message);

            LastNetworkUpdate = sendingTime;
            
        }

        public override void Remove()
        {
            base.Remove();

            CharacterList.Remove(this);

            if (controlled == this) controlled = null;

            if (Game1.Client!=null && Game1.Client.Character == this) Game1.Client.Character = null;

            if (inventory != null) inventory.Remove();

            if (aiTarget != null)
                aiTarget.Remove();

            if (AnimController!=null)
                AnimController.Remove();
        }

    }
}
