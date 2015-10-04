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

        private CharacterInventory inventory;

        public float LastNetworkUpdate;

        public int LargeUpdateTimer;

        public readonly Dictionary<string, ObjectProperty> Properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return Properties; }
        }

        protected Key[] keys;

        //protected Key selectKeyHit;
        //protected Key actionKeyHit, actionKeyDown;
        //protected Key secondaryKeyHit, secondaryKeyDown;
                
        private Item selectedConstruction;
        private Item[] selectedItems;
        
        public AnimController AnimController;

        private Vector2 cursorPosition;

        protected bool needsAir;
        protected float oxygen;
        protected float drowningTime;

        protected float health;
        protected float maxHealth;

        protected Item closestItem;
        private Character closestCharacter, selectedCharacter;

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

        public CharacterInventory Inventory
        {
            get { return inventory; }
        }

        public Vector2 CursorPosition
        {
            get { return cursorPosition; }
        }

        public Character ClosestCharacter
        {
            get { return closestCharacter; }
        }

        public Character SelectedCharacter
        {
            get { return selectedCharacter; }
        }

        private float lowPassMultiplier;
        public float LowPassMultiplier
        {
            get { return lowPassMultiplier; }
            set { lowPassMultiplier = MathHelper.Clamp(value, 0.0f, 1.0f); }
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

        public float Stun
        {
            get { return AnimController.StunTimer; }
            set { StartStun(value); }
        }

        public float Health
        {
            get 
            {
                return health;
            }
            set
            {
                health = MathHelper.Clamp(value, 0.0f, maxHealth);
                if (health == 0.0f) Kill();
            }
        }

        public float MaxHealth
        {
            get { return maxHealth; }
        }

        public float Bleeding
        {
            get { return bleeding; }
            set 
            {
                if (MathUtils.IsValid(value)) return;
                bleeding = Math.Max(value, 0.0f); 
            }
        }

        public float SpeedMultiplier
        {
            get;
            set;
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

        public virtual AIController AIController
        {
            get { return null; }
        }

        public bool IsDead
        {
            get { return isDead; }
        }

        public override Vector2 SimPosition
        {
            get { return AnimController.Limbs[0].SimPosition; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(AnimController.Limbs[0].SimPosition); }
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

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];
            keys[(int)InputType.Select] = new Key(false);
            keys[(int)InputType.ActionHeld] = new Key(true);
            keys[(int)InputType.ActionHit] = new Key(false);
            keys[(int)InputType.SecondaryHit] = new Key(false);
            keys[(int)InputType.SecondaryHeld] = new Key(true);

            keys[(int)InputType.Left] = new Key(true);
            keys[(int)InputType.Right] = new Key(true);
            keys[(int)InputType.Up] = new Key(true);
            keys[(int)InputType.Down] = new Key(true);

            keys[(int)InputType.Run] = new Key(true);

            selectedItems = new Item[2];

            IsNetworkPlayer = isNetworkPlayer;

            oxygen = 100.0f;
            //blood = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

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
                inventory = new CharacterInventory(15, this);
            }
            else
            {
                AnimController = new FishAnimController(this, doc.Root.Element("ragdoll"));
                PressureProtection = 100.0f;
                //FishAnimController fishAnim = (FishAnimController)animController;
            }

            foreach (Limb limb in AnimController.Limbs)
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

            CharacterList.Add(this);
        }

        private static string humanConfigFile;
        public static string HumanConfigFile
        {
            get 
            {
                if (string.IsNullOrEmpty(humanConfigFile))
                {
                    var characterFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.Character);

                    humanConfigFile = characterFiles.Find(c => c.EndsWith("human.xml"));
                    if (humanConfigFile == null)
                    {
                        DebugConsole.ThrowError("Couldn't find a config file for humans from the selected content package!");
                        DebugConsole.ThrowError("(The config file must end with ''human.xml'')");
                        return "";
                    }
                }
                return humanConfigFile; 
            }
        }

        public bool GetInputState(InputType inputType)
        {
            return keys[(int)inputType].State;
        }

        public void ClearInputs()
        {
            foreach (Key key in keys)
            {
                key.State = false;
            }
        }

        public override string ToString()
        {
            return (info != null && !string.IsNullOrWhiteSpace(info.Name)) ? info.Name : SpeciesName;
        }

        public void GiveJobItems(WayPoint spawnPoint)
        {
            if (info == null || info.Job == null) return;

            for (int i = 0; i < info.Job.SpawnItemNames.Count; i++ )
            {
                string itemName = info.Job.SpawnItemNames[i];

                ItemPrefab itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Tried to spawn ''" + Name + "'' with the item ''" + itemName + "''. Matching item prefab not found.");
                    continue;
                }

                Item item = new Item(itemPrefab, Position);

                if (info.Job.EquipSpawnItem[i])
                {
                    inventory.TryPutItem(item, 
                        item.AllowedSlots.HasFlag(LimbSlot.Any) ? item.AllowedSlots & ~LimbSlot.Any : item.AllowedSlots, false);
                }
                else
                {
                    inventory.TryPutItem(item, item.AllowedSlots, false);
                }

                if (item.Prefab.Name == "ID Card" && spawnPoint != null)
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

        float findClosestTimer;

        public void Control(float deltaTime, Camera cam)
        {
            if (isDead) return;

            Vector2 targetMovement = Vector2.Zero;
            if (GetInputState(InputType.Left))  targetMovement.X -= 1.0f;
            if (GetInputState(InputType.Right)) targetMovement.X += 1.0f;
            if (GetInputState(InputType.Up))    targetMovement.Y += 1.0f;
            if (GetInputState(InputType.Down))  targetMovement.Y -= 1.0f;
            
            //the vertical component is only used for falling through platforms and climbing ladders when not in water,
            //so the movement can't be normalized or the character would walk slower when pressing down/up
            if (AnimController.InWater)
            {
                float length = targetMovement.Length();
                if (length > 0.0f) targetMovement = targetMovement / length;
            }

            if (Math.Sign(targetMovement.X) == Math.Sign(AnimController.Dir) && GetInputState(InputType.Run))
                targetMovement *= 3.0f;

            targetMovement *= SpeedMultiplier;
            SpeedMultiplier = 1.0f;

            AnimController.TargetMovement = targetMovement;
            AnimController.IsStanding = true;

            if (AnimController.onGround &&
                !AnimController.InWater &&
                AnimController.Anim != AnimController.Animation.UsingConstruction)
            {
                Limb head = AnimController.GetLimb(LimbType.Head);

                if (cursorPosition.X < head.Position.X - 10.0f)
                {
                    AnimController.TargetDir = Direction.Left;
                }
                else if (cursorPosition.X > head.Position.X + 10.0f)
                {
                    AnimController.TargetDir = Direction.Right;
                }
            }

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] == null) continue;
                if (i == 1 && selectedItems[0] == selectedItems[1]) continue;
                
                if (GetInputState(InputType.ActionHeld)) selectedItems[i].Use(deltaTime, this);
                if (GetInputState(InputType.SecondaryHeld) && selectedItems[i] != null) selectedItems[i].SecondaryUse(deltaTime, this);                
            }

            if (selectedConstruction != null)
            {
                if (GetInputState(InputType.ActionHeld)) selectedConstruction.Use(deltaTime, this);
                if (GetInputState(InputType.SecondaryHeld)) selectedConstruction.SecondaryUse(deltaTime, this);
            }
                  
            if (IsNetworkPlayer)
            {
                foreach (Key key in keys)
                {
                    key.Reset();
                }
            }
        }

        private Item FindClosestItem(Vector2 mouseSimPos)
        {
            Limb torso = AnimController.GetLimb(LimbType.Torso);
            Vector2 pos = (torso.body.TargetPosition != Vector2.Zero) ? torso.body.TargetPosition : torso.SimPosition;

            return Item.FindPickable(pos, selectedConstruction == null ? mouseSimPos : selectedConstruction.SimPosition, AnimController.CurrentHull, selectedItems);
        }

        private Character FindClosestCharacter(Vector2 mouseSimPos, float maxDist = 150.0f)
        {
            Character closestCharacter = null;
            float closestDist = 0.0f;

            maxDist = ConvertUnits.ToSimUnits(maxDist);
            
            foreach (Character c in Character.CharacterList)
            {
                if (c == this) continue;

                if (Vector2.Distance(SimPosition, c.SimPosition) > maxDist) continue;

                float dist = Vector2.Distance(mouseSimPos, c.SimPosition);
                if (dist < maxDist && closestCharacter==null || dist<closestDist)
                {
                    closestCharacter = c;
                    closestDist = dist;
                    continue;
                }
            }

            return closestCharacter;
        }

        /// <summary>
        /// Control the character according to player input
        /// </summary>
        public void ControlLocalPlayer(float deltaTime, Camera cam, bool moveCam = true)
        {
            Limb head = AnimController.GetLimb(LimbType.Head);

            Lights.LightManager.ViewPos = ConvertUnits.ToDisplayUnits(head.SimPosition);

            if (!DisableControls)
            {
                keys[(int)InputType.Left].SetState(PlayerInput.KeyDown(Keys.A));
                keys[(int)InputType.Right].SetState(PlayerInput.KeyDown(Keys.D));
                keys[(int)InputType.Up].SetState(PlayerInput.KeyDown(Keys.W));
                keys[(int)InputType.Down].SetState(PlayerInput.KeyDown(Keys.S));

                keys[(int)InputType.Select].SetState(PlayerInput.KeyHit(Keys.E));
                keys[(int)InputType.ActionHit].SetState(PlayerInput.LeftButtonClicked());
                keys[(int)InputType.ActionHeld].SetState(PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed);
                keys[(int)InputType.SecondaryHit].SetState(PlayerInput.RightButtonClicked());
                keys[(int)InputType.SecondaryHeld].SetState(PlayerInput.GetMouseState.RightButton == ButtonState.Pressed);

                keys[(int)InputType.Run].SetState(PlayerInput.KeyDown(Keys.LeftShift));
            }
            else
            {
                foreach (Key key in keys)
                {
                    key.SetState(false);
                }
            }

            if (moveCam)
            {
                cam.TargetPos = ConvertUnits.ToDisplayUnits(AnimController.Limbs[0].SimPosition);
                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, 250.0f, 0.05f);
            }
            
            cursorPosition = cam.ScreenToWorld(PlayerInput.MousePosition);            
            Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);
            if (Vector2.Distance(AnimController.Limbs[0].SimPosition, mouseSimPos)>1.0f)
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


            //find the closest item if selectkey has been hit, or if the character is being
            //controlled by the player (in order to highlight it)

            if (findClosestTimer <= 0.0f || Screen.Selected == GameMain.EditMapScreen)
            {
                closestCharacter = FindClosestCharacter(mouseSimPos);
                if (closestCharacter != null)
                {
                    if (closestCharacter != selectedCharacter) selectedCharacter = null;
                    if (!closestCharacter.isHumanoid) closestCharacter = null;
                }

                closestItem = FindClosestItem(mouseSimPos);

                if (closestCharacter != null && closestItem != null)
                {
                    if (Vector2.Distance(closestCharacter.SimPosition, mouseSimPos) < Vector2.Distance(closestItem.SimPosition, mouseSimPos))
                    {
                        if (selectedConstruction != closestItem) closestItem = null;
                    }
                    else
                    {
                        closestCharacter = null;
                    }
                }

                findClosestTimer = 0.1f;
            }
            else
            {
                findClosestTimer -= deltaTime;
            }

            if (selectedCharacter == null)
            {
                if (closestItem != null)
                {
                    closestItem.IsHighlighted = true;
                    if (GetInputState(InputType.Select) && closestItem.Pick(this, false))
                    {
                        new NetworkEvent(NetworkEventType.PickItem, ID, true, closestItem.ID);
                    }
                }
            }
            else
            {
                if (Vector2.Distance(selectedCharacter.SimPosition, SimPosition) > 2.0f) selectedCharacter = null;
            }

            if (GetInputState(InputType.Select))
            {
                if (selectedCharacter != null)
                {
                    selectedCharacter = null;
                }
                else if (closestCharacter != null && closestCharacter.isDead && closestCharacter.isHumanoid)
                {
                    selectedCharacter = closestCharacter;
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

        public virtual void Update(Camera cam, float deltaTime)
        {
            AnimController.SimplePhysicsEnabled = (Character.controlled!=this && Vector2.Distance(cam.WorldViewCenter, Position)>5000.0f);
            
            if (isDead) return;
            
            if (PressureProtection==0.0f && 
                (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 100.0f))
            {
                Implode();
                return;
            }

            if (controlled == this)
            {
                CharacterHUD.Update(deltaTime,this);
                ControlLocalPlayer(deltaTime, cam);
            }

            Control(deltaTime, cam);

            UpdateSightRange();
            aiTarget.SoundRange = 0.0f;

            lowPassMultiplier = MathHelper.Lerp(lowPassMultiplier, 1.0f, 0.1f);

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

            Health = health - bleeding * deltaTime;
        }



        private void UpdateSightRange()
        {
            aiTarget.SightRange = 0.0f;

            //distance is approximated based on the mass of the character 
            //(which corresponds to size because all the characters have the same limb density)
            foreach (Limb limb in AnimController.Limbs)
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
            
            //GUI.DrawLine(spriteBatch, ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y),
            //    ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y) +
            //    ConvertUnits.ToDisplayUnits(animController.targetMovement.X, animController.targetMovement.Y), Color.Green);
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera cam)
        {
            CharacterHUD.Draw(spriteBatch, this, cam);
        }

        public void DrawFront(SpriteBatch spriteBatch)
        {
            Vector2 pos = ConvertUnits.ToDisplayUnits(AnimController.Limbs[0].SimPosition);
            pos.Y = -pos.Y;
            
            if (this == Character.controlled) return;

            if (IsNetworkPlayer)
            {
                Vector2 namePos = new Vector2(pos.X, pos.Y - 80.0f) - GUI.Font.MeasureString(Info.Name) * 0.5f;
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos, Color.White);

                if (GameMain.DebugDraw)
                {
                    spriteBatch.DrawString(GUI.Font, ID.ToString(), namePos - new Vector2(0.0f, 20.0f), Color.White);
                }
            }

            Vector2 healthBarPos = new Vector2(Position.X - 50, -Position.Y - 50.0f);
            GUI.DrawRectangle(spriteBatch, new Rectangle((int)healthBarPos.X - 2, (int)healthBarPos.Y - 2, 100 + 4, 15 + 4), Color.Black, false);
            GUI.DrawRectangle(spriteBatch, new Rectangle((int)healthBarPos.X, (int)healthBarPos.Y, (int)(100.0f * (health / maxHealth)), 15), Color.Red, true);
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
                if (n == selectedSound && sounds[i]!=null)
                {
                    sounds[i].Play(1.0f, 2000.0f,
                            AnimController.Limbs[0].body.FarseerBody);
                    return;
                }
                n++;
            }
        }

        public virtual AttackResult AddDamage(IDamageable attacker, Vector2 simPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            return AddDamage(simPosition, attack.DamageType, attack.GetDamage(deltaTime), attack.GetBleedingDamage(deltaTime), attack.Stun, playSound);
        }

        public AttackResult AddDamage(Vector2 simPosition, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound)
        {
            StartStun(stun);
            if (controlled == this) CharacterHUD.TakeDamage();
            
            Limb closestLimb = null;
            float closestDistance = 0.0f;
            foreach (Limb limb in AnimController.Limbs)
            {
                float distance = Vector2.Distance(simPosition, limb.SimPosition);
                if (closestLimb == null || distance < closestDistance)
                {
                    closestLimb = limb;
                    closestDistance = distance;
                }
            }

            Vector2 pull = simPosition - closestLimb.SimPosition;
            if (pull != Vector2.Zero) pull = Vector2.Normalize(pull);
            closestLimb.body.ApplyForce(pull*Math.Min(amount*100.0f, 100.0f));


            AttackResult attackResult = closestLimb.AddDamage(simPosition, damageType, amount, bleedingAmount, playSound);
            health -= attackResult.Damage;
            bleeding += attackResult.Bleeding;

            return attackResult;
        }

        public void StartStun(float stunTimer)
        {
            if (stunTimer <= 0.0f) return;

            AnimController.StunTimer = Math.Max(AnimController.StunTimer, stunTimer);
                
            selectedConstruction = null;
        }

        private void Implode()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            health = 0.0f;

            foreach (Limb limb in AnimController.Limbs)
            {
                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 10.0f);
               // limb.Damage = 100.0f;
            }

            AmbientSoundManager.PlayDamageSound(DamageSoundType.Implode, 50.0f, AnimController.RefLimb.body.FarseerBody);
            
            for (int i = 0; i < 10; i++)
            {
                Particle p = GameMain.ParticleManager.CreateParticle("waterblood",
                    centerOfMass + Rand.Vector(50.0f),
                    Vector2.Zero);
                if (p!=null) p.Size *= 2.0f;

                GameMain.ParticleManager.CreateParticle("bubbles",
                    centerOfMass + Rand.Vector(50.0f),
                    new Vector2(Rand.Range(-50f, 50f), Rand.Range(-100f,50f)));
            }

            foreach (var joint in AnimController.limbJoints)
            {
                joint.LimitEnabled = false;
            }
            Kill();
        }

        private IEnumerable<object> DeathAnim(Camera cam)
        {
            if (controlled != this) yield return CoroutineStatus.Success;

            float dimDuration = 8.0f;
            float timer = 0.0f;

            Color prevAmbientLight = GameMain.LightManager.AmbientLight;
            Color darkLight = new Color(0.2f,0.2f,0.2f, 1.0f);

            while (timer < dimDuration)
            {
                timer += 1.0f / 60.0f;

                if (Character.controlled == this)
                {
                    if (cam != null)
                    {
                        cam.TargetPos = ConvertUnits.ToDisplayUnits(AnimController.Limbs[0].SimPosition);
                        cam.OffsetAmount = 0.0f;
                    }

                    GameMain.LightManager.AmbientLight = Color.Lerp(prevAmbientLight, darkLight, timer / dimDuration);
                }

                yield return CoroutineStatus.Running;
            }

            while (Character.Controlled == this)
            {
                yield return CoroutineStatus.Running;
            }

            float lerpLightBack = 0.0f;
            while (lerpLightBack < 1.0f)
            {
                lerpLightBack = Math.Min(lerpLightBack + 0.05f, 1.0f);

                GameMain.LightManager.AmbientLight = Color.Lerp(darkLight, prevAmbientLight, lerpLightBack);
                yield return CoroutineStatus.Running;
            }

            yield return CoroutineStatus.Success;
        }

        public void Kill(bool networkMessage = false)
        {
            if (isDead) return;

            //if the game is run by a client, characters are only killed when the server says so
            if (GameMain.Client != null && GameMain.Server==null && !networkMessage) return;               

            CoroutineManager.StartCoroutine(DeathAnim(GameMain.GameScreen.Cam));

            health = 0.0f;

            isDead = true;
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }
                
            aiTarget.Remove();
            aiTarget = null;

            foreach (Limb limb in AnimController.Limbs)
            {
                if (limb.pullJoint == null) continue;
                limb.pullJoint.Enabled = false;
            }

            foreach (RevoluteJoint joint in AnimController.limbJoints)
            {
                joint.MotorEnabled = false;
                joint.MaxMotorTorque = 0.0f;
            }

            if (GameMain.Server != null)
            {
                new NetworkEvent(NetworkEventType.KillCharacter, ID, false);
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.KillCharacter(this);
            }
        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            if (type == NetworkEventType.PickItem)
            {
                message.Write((int)data);
                return;
            }
            else if (type == NetworkEventType.KillCharacter)
            {
                return;            
            }

            var hasInputs =  
                    (GetInputState(InputType.Left) ||
                    GetInputState(InputType.Right) ||
                    GetInputState(InputType.Up) ||
                    GetInputState(InputType.Down) ||
                    GetInputState(InputType.ActionHeld) ||
                    GetInputState(InputType.SecondaryHeld));

            message.Write(hasInputs || LargeUpdateTimer <= 0);

            message.Write((float)NetTime.Now);
                                   
            // Write byte = move direction
            //message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -10.0f, 10.0f), -10.0f, 10.0f, 8);
            //message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.Y, -10.0f, 10.0f), -10.0f, 10.0f, 8);

            message.Write(keys[(int)InputType.ActionHeld].Dequeue);
            message.Write(keys[(int)InputType.SecondaryHeld].Dequeue);
                        
            message.Write(keys[(int)InputType.Left].Dequeue);
            message.Write(keys[(int)InputType.Right].Dequeue);

            message.Write(keys[(int)InputType.Up].Dequeue);
            message.Write(keys[(int)InputType.Down].Dequeue);

            message.Write(keys[(int)InputType.Run].Dequeue);

            message.Write(cursorPosition.X);
            message.Write(cursorPosition.Y);
                        
            message.Write(LargeUpdateTimer <= 0);

            if (LargeUpdateTimer<=0)
            {
                int i = 0;
                foreach (Limb limb in AnimController.Limbs)
                {
                    message.Write(limb.body.SimPosition.X);
                    message.Write(limb.body.SimPosition.Y);

                    //message.Write(limb.body.LinearVelocity.X);
                    //message.Write(limb.body.LinearVelocity.Y);

                    message.Write(limb.body.Rotation);
                    //message.WriteRangedSingle(MathHelper.Clamp(limb.body.AngularVelocity, -10.0f, 10.0f), -10.0f, 10.0f, 8);
                    i++;
                }

                message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer,0.0f,60.0f), 0.0f, 60.0f, 8);
                message.Write((byte)((health/maxHealth)*255.0f));

                LargeUpdateTimer = 10;
            }
            else
            {
                message.Write(AnimController.RefLimb.SimPosition.X);
                message.Write(AnimController.RefLimb.SimPosition.Y);

                LargeUpdateTimer = Math.Max(0, LargeUpdateTimer-1);
            }            
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            if (type == NetworkEventType.PickItem)
            {
                int itemId = -1;

                try
                {
                    itemId = message.ReadInt32();
                }
                catch
                {
                    return;
                }

                Item item = FindEntityByID(itemId) as Item;
                if (item != null)
                {
                    item.Pick(this);
                }

                return;
            } 
            else if (type == NetworkEventType.KillCharacter)
            {
                Kill(true);
                if (GameMain.NetworkMember != null && controlled == this)
                {
                    GameMain.NetworkMember.AddChatMessage("YOU HAVE DIED. Your chat messages will only be visible to other dead players.", ChatMessageType.Dead);
                    GameMain.LightManager.LosEnabled = false;
                }
                return;
            }

            bool actionKeyState     = false;
            bool secondaryKeyState  = false;
            float sendingTime       = 0.0f;
            Vector2 cursorPos       = Vector2.Zero;

            bool leftKeyState = false, rightKeyState = false;
            bool upKeyState = false, downKeyState = false;

            bool runState = false;

            try
            {
                bool hasInputs = message.ReadBoolean();
                if (!hasInputs)
                {
                    ClearInputs();
                    return;
                }

                sendingTime         = message.ReadFloat();

                actionKeyState      = message.ReadBoolean();
                secondaryKeyState   = message.ReadBoolean();
            
                leftKeyState        = message.ReadBoolean();
                rightKeyState       = message.ReadBoolean();
                upKeyState          = message.ReadBoolean();
                downKeyState        = message.ReadBoolean();

                runState            = message.ReadBoolean();
            }

            catch
            {
                return;
            }

            AnimController.IsStanding = true;

            keys[(int)InputType.ActionHeld].State       = actionKeyState;
            keys[(int)InputType.SecondaryHeld].State    = secondaryKeyState;

            if (sendingTime <= LastNetworkUpdate) return;

            keys[(int)InputType.Left].State     = leftKeyState;
            keys[(int)InputType.Right].State    = rightKeyState;

            keys[(int)InputType.Up].State       = upKeyState;
            keys[(int)InputType.Down].State     = downKeyState;

            keys[(int)InputType.Run].State = runState;

            bool isLargeUpdate;

            try
            {
                cursorPos = new Vector2(
                    message.ReadFloat(),
                    message.ReadFloat());
                isLargeUpdate = message.ReadBoolean();
            }
            catch
            {
                return;
            }

            cursorPosition = cursorPos;
                                      
            if (isLargeUpdate)
            {
                foreach (Limb limb in AnimController.Limbs)
                {
                    Vector2 pos = Vector2.Zero, vel = Vector2.Zero;
                    float rotation = 0.0f;

                    try
                    {
                        pos.X = message.ReadFloat();
                        pos.Y = message.ReadFloat();

                        //vel.X = message.ReadFloat();
                        //vel.Y = message.ReadFloat();

                        rotation = message.ReadFloat();
                        //angularVel = message.ReadFloat();
                    }
                    catch
                    {
                        return;
                    }

                    if (limb.body != null)
                    {
                        limb.body.TargetVelocity = limb.body.LinearVelocity;
                        limb.body.TargetPosition = pos;// +vel * (float)(deltaTime / 60.0);
                        limb.body.TargetRotation = rotation;// +angularVel * (float)(deltaTime / 60.0);
                        limb.body.TargetAngularVelocity = limb.body.AngularVelocity;
                    }

                }

                float newStunTimer = 0.0f, newHealth = 0.0f;

                try
                {
                    newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                    newHealth = (message.ReadByte() / 255.0f) * maxHealth;
                }
                catch { return; }

                StartStun(newStunTimer);
                Health = newHealth;

                LargeUpdateTimer = 1;
            }
            else
            {
                Vector2 pos = Vector2.Zero;
                try
                {
                    pos.X = message.ReadFloat();
                    pos.Y = message.ReadFloat();
                }

                catch { return; }


                Limb torso = AnimController.GetLimb(LimbType.Torso);
                if (torso == null) torso = AnimController.GetLimb(LimbType.Head);
                torso.body.TargetPosition = pos;

                LargeUpdateTimer = 0;
            }

            LastNetworkUpdate = sendingTime;
            
        }

        public override void Remove()
        {
            base.Remove();

            CharacterList.Remove(this);

            if (controlled == this) controlled = null;

            if (GameMain.Client!=null && GameMain.Client.Character == this) GameMain.Client.Character = null;

            if (inventory != null) inventory.Remove();

            if (aiTarget != null)
                aiTarget.Remove();

            if (AnimController!=null)
                AnimController.Remove();
        }

    }
}
