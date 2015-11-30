
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Barotrauma.Particles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
   
    class Character : Entity, IDamageable, IPropertyObject
    {
        public static string[] DeathMsg = new string[Enum.GetNames(typeof(CauseOfDeath)).Length];

        public static List<Character> CharacterList = new List<Character>();
        
        public static Queue<CharacterInfo> NewCharacterQueue = new Queue<CharacterInfo>();

        public static bool DisableControls;

        //the Character that the player is currently controlling
        private static Character controlled;

        public static Character Controlled
        {
            get { return controlled; }
            set { controlled = value; }
        }

        public bool Enabled
        {
            get;
            set;
        }

        public readonly bool IsNetworkPlayer;

        private CharacterInventory inventory;

        public float LastNetworkUpdate;

        //public int LargeUpdateTimer;

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
        
        public readonly bool IsHumanoid;

        //the name of the species (e.q. human)
        public readonly string SpeciesName;

        protected float soundTimer;
        protected float soundInterval;

        private float bleeding;

        private Sound[] sounds;
        private float[] soundRange;
        //which AIstate each sound is for
        private AIController.AiState[] soundStates;
        

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

        public string Name
        {
            get
            {
                return info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name : SpeciesName;
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
            set 
            { 
                if (!MathUtils.IsValid(value)) return;
                cursorPosition = value;
            }
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

        private float obstructVisionAmount;
        public bool ObstructVision
        {
            get
            {
                return obstructVisionAmount > 0.5f;
            }
            set
            {
                obstructVisionAmount = 1.0f;
            }
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

        public bool NeedsAir
        {
            get { return needsAir; }
        }

        public float Oxygen
        {
            get { return oxygen; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (oxygen == 0.0f) Kill(CauseOfDeath.Suffocation);
            }
        }

        public float Stun
        {
            get { return AnimController.StunTimer; }
            set { StartStun(value); }
        }

        public float Health
        {
            get { return health; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                health = MathHelper.Clamp(value, 0.0f, maxHealth);
                if (health <= 0.0f) Kill(CauseOfDeath.Damage);
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
                if (!MathUtils.IsValid(value)) return;
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
            get { return AnimController.RefLimb.SimPosition; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(AnimController.RefLimb.SimPosition); }
        }

        static Character()
        {
            DeathMsg[(int)CauseOfDeath.Damage] = "succumbed to your injuries";
            DeathMsg[(int)CauseOfDeath.Bloodloss] = "bled out";
            DeathMsg[(int)CauseOfDeath.Drowning] = "drowned";
            DeathMsg[(int)CauseOfDeath.Suffocation] = "suffocated";
            DeathMsg[(int)CauseOfDeath.Pressure] = "been crushed by water pressure";
            DeathMsg[(int)CauseOfDeath.Burn] = "burnt to death";
        }
        
        public static Character Create(string file, Vector2 position)
        {
            return Create(file, position, null);
        }

        public static Character Create(CharacterInfo characterInfo, WayPoint spawnPoint, bool isNetworkPlayer = false)
        {
            return Create(characterInfo.File, spawnPoint.SimPosition, characterInfo, isNetworkPlayer);
        }


        public static Character Create(CharacterInfo characterInfo, Vector2 position, bool isNetworkPlayer = false)
        {
            return Create(characterInfo.File, position, characterInfo, isNetworkPlayer);
        }

        public static Character Create(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
        {
            if (file != humanConfigFile)
            {
                var enemyCharacter = new AICharacter(file, position, characterInfo, isNetworkPlayer);
                var ai = new EnemyAIController(enemyCharacter, file);
                enemyCharacter.SetAI(ai);

                return enemyCharacter;
            }
            else
            {
                if (isNetworkPlayer)
                {
                    var netCharacter = new Character(file, position, characterInfo, isNetworkPlayer);

                    return netCharacter;
                }
                else
                {
                    var character = new AICharacter(file, position, characterInfo, isNetworkPlayer);
                    var ai = new HumanAIController(character);
                    character.SetAI(ai);

                    return character;
                }
            }
        }

        protected Character(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
        {

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];

            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key(GameMain.Config.KeyBind((InputType)i));
            }

            //keys[(int)InputType.Select] = new Key(GameMain.Config.KeyBind(InputType.Select));
            //keys[(int)InputType.ActionHeld] = new Key(true);
            //keys[(int)InputType.ActionHit] = new Key(false);
            //keys[(int)InputType.SecondaryHit] = new Key(false);
            //keys[(int)InputType.SecondaryHeld] = new Key(true);

            //keys[(int)InputType.Left] = new Key(true);
            //keys[(int)InputType.Right] = new Key(true);
            //keys[(int)InputType.Up] = new Key(true);
            //keys[(int)InputType.Down] = new Key(true);

            //keys[(int)InputType.Run] = new Key(true);

            selectedItems = new Item[2];

            IsNetworkPlayer = isNetworkPlayer;

            oxygen = 100.0f;
            //blood = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

            Properties = ObjectProperty.GetProperties(this);

            Info = characterInfo==null ? new CharacterInfo(file) : characterInfo;

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;
            
            SpeciesName = ToolBox.GetAttributeString(doc.Root, "name", "Unknown");

            IsHumanoid = ToolBox.GetAttributeBool(doc.Root, "humanoid", false);
            
            if (IsHumanoid)
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
                for (ushort i = 0; i < Info.PickedItemIDs.Count; i++ )
                {
                    if (Info.PickedItemIDs[i] == 0) continue;

                    Item item = FindEntityByID(Info.PickedItemIDs[i]) as Item;
                    if (item == null) continue;

                    item.Pick(this, true, true, true);
                    inventory.TryPutItem(item, i, false);
                }
            }

            AnimController.FindHull();

            CharacterList.Add(this);

            Enabled = true;
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

        public bool IsKeyHit(InputType inputType)
        {
            return keys[(int)inputType].Hit;
        }

        public bool IsKeyDown(InputType inputType)
        {
            return keys[(int)inputType].Held;
        }

        public void ClearInput(InputType inputType)
        {
            keys[(int)inputType].Hit = false;
            keys[(int)inputType].Held = false;            
        }

        public void ClearInputs()
        {
            foreach (Key key in keys)
            {
                key.Hit = false;
                key.Held = false;
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
                    List<LimbSlot> allowedSlots = new List<LimbSlot>(item.AllowedSlots);
                    allowedSlots.Remove(LimbSlot.Any);

                    inventory.TryPutItem(item, allowedSlots, false);
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

        public Vector2 GetTargetMovement()
        {
            Vector2 targetMovement = Vector2.Zero;
            if (IsKeyDown(InputType.Left))  targetMovement.X -= 1.0f;
            if (IsKeyDown(InputType.Right)) targetMovement.X += 1.0f;
            if (IsKeyDown(InputType.Up))    targetMovement.Y += 1.0f;
            if (IsKeyDown(InputType.Down))  targetMovement.Y -= 1.0f;

            //the vertical component is only used for falling through platforms and climbing ladders when not in water,
            //so the movement can't be normalized or the Character would walk slower when pressing down/up
            if (AnimController.InWater)
            {
                float length = targetMovement.Length();
                if (length > 0.0f) targetMovement = targetMovement / length;
            }

            if (Math.Sign(targetMovement.X) == Math.Sign(AnimController.Dir) && IsKeyDown(InputType.Run))
                targetMovement *= 3.0f;

            targetMovement *= SpeedMultiplier;
            SpeedMultiplier = 1.0f;

            return targetMovement;
        }

        public void Control(float deltaTime, Camera cam)
        {
            if (isDead || AnimController.StunTimer>0.0f) return;
            
            Vector2 targetMovement = GetTargetMovement();

            AnimController.TargetMovement = targetMovement;

            AnimController.IgnorePlatforms = targetMovement.Y < 0.0f;

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

                if (IsKeyDown(InputType.Use)) selectedItems[i].Use(deltaTime, this);
                if (IsKeyDown(InputType.Aim) && selectedItems[i] != null) selectedItems[i].SecondaryUse(deltaTime, this);                
            }

            if (selectedConstruction != null)
            {
                if (IsKeyDown(InputType.Use)) selectedConstruction.Use(deltaTime, this);
                if (selectedConstruction != null && IsKeyDown(InputType.Aim)) selectedConstruction.SecondaryUse(deltaTime, this);
            }
                  
            if (IsNetworkPlayer)
            {
                foreach (Key key in keys)
                {
                    key.ResetHit();
                }
            }
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
            
            foreach (Character c in CharacterList)
            {
                if (c == this) continue;

                if (Vector2.Distance(SimPosition, c.SimPosition) > maxDist) continue;

                float dist = Vector2.Distance(mouseSimPos, c.SimPosition);
                if (dist < maxDist && (closestCharacter==null || dist<closestDist))
                {
                    closestCharacter = c;
                    closestDist = dist;
                    continue;
                }
            }

            return closestCharacter;
        }

        private void SelectCharacter(Character character, bool createNetworkEvent = true)
        {
            if (character == null) return;

            selectedCharacter = character;

           if (createNetworkEvent) 
                new NetworkEvent(NetworkEventType.SelectCharacter, ID, true, selectedCharacter.ID);

        }

        private void DeselectCharacter(bool createNetworkEvent = true)
        {
            if (selectedCharacter == null) return;
            
            foreach (Limb limb in selectedCharacter.AnimController.Limbs)
            {
                limb.pullJoint.Enabled = false;
            }

            selectedCharacter = null;

            if (createNetworkEvent) new NetworkEvent(NetworkEventType.SelectCharacter, ID, true, (ushort)0);
        }

        /// <summary>
        /// Control the Character according to player input
        /// </summary>
        public void ControlLocalPlayer(float deltaTime, Camera cam, bool moveCam = true)
        {
            Limb head = AnimController.GetLimb(LimbType.Head);

            Lights.LightManager.ViewPos = ConvertUnits.ToDisplayUnits(head.SimPosition);

            if (!DisableControls)
            {
                for (int i = 0; i < keys.Length; i++ )
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


            //find the closest item if selectkey has been hit, or if the Character is being
            //controlled by the player (in order to highlight it)

            if (findClosestTimer <= 0.0f || Screen.Selected == GameMain.EditMapScreen)
            {
                closestCharacter = FindClosestCharacter(mouseSimPos);
                if (closestCharacter != null)
                {
                //    if (closestCharacter != selectedCharacter) selectedCharacter = null;
                    if (!closestCharacter.IsHumanoid) closestCharacter = null;
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
                    if (closestItem.Pick(this))
                    {
                        new NetworkEvent(NetworkEventType.PickItem, ID, true,
                            new int[] 
                            { 
                                closestItem.ID, 
                                IsKeyHit(InputType.Select) ? 1 : 0, 
                                IsKeyHit(InputType.Use) ? 1 : 0 
                            });
                    }
                }
            }
            else
            {
                if (Vector2.Distance(selectedCharacter.SimPosition, SimPosition) > 2.0f ||
                    (!selectedCharacter.isDead && selectedCharacter.Stun <= 0.0f))
                {
                    DeselectCharacter();
                }
            }

            if (IsKeyHit(InputType.Select))
            {
                if (selectedCharacter != null)
                {
                    DeselectCharacter();
                }
                else if (closestCharacter != null && closestCharacter.IsHumanoid &&
                    (closestCharacter.isDead || closestCharacter.AnimController.StunTimer > 0.0f))
                {
                    SelectCharacter(closestCharacter);
                }
            }            

            DisableControls = false;
        }
        

        public static void UpdateAnimAll(float deltaTime)
        {
            foreach (Character c in CharacterList)
            {
                if (c.isDead || !c.Enabled) continue;
                c.AnimController.UpdateAnim(deltaTime);
            }
        }
        
        public static void UpdateAll(Camera cam, float deltaTime)
        {
            //if (NewCharacterQueue.Count>0)
            //{
            //    new Character(NewCharacterQueue.Dequeue(), Vector2.Zero);
            //}

            foreach (Character c in CharacterList)
            {
                c.Update(cam, deltaTime);
            }
        }

        public virtual void Update(Camera cam, float deltaTime)
        {
            if (!Enabled) return;

            obstructVisionAmount = Math.Max(obstructVisionAmount - deltaTime, 0.0f);
            
            AnimController.SimplePhysicsEnabled = (Character.controlled != this && Vector2.Distance(cam.WorldViewCenter, Position) > 5000.0f);
            
            if (isDead) return;

            if (!(AnimController is FishAnimController))
            {
                bool protectedFromPressure = PressureProtection > 0.0f;
                
                if (Submarine.Loaded!=null && Level.Loaded !=null)
                {
                    protectedFromPressure = protectedFromPressure && (Position-Level.Loaded.Position).Y > SubmarineBody.DamageDepth;
                }
                
                if (!protectedFromPressure && 
                    (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 100.0f))
                {
                    Implode();
                    return;
                }
            }

            if (controlled == this)
            {
                CharacterHUD.Update(deltaTime,this);
                ControlLocalPlayer(deltaTime, cam);
            }

            if (controlled==this || !(this is AICharacter)) Control(deltaTime, cam);

            UpdateSightRange();
            if (aiTarget != null) aiTarget.SoundRange = 0.0f;

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

            health = MathHelper.Clamp(health - bleeding * deltaTime, 0.0f, maxHealth);
            if (health <= 0.0f) Kill(CauseOfDeath.Bloodloss, false);
        }



        private void UpdateSightRange()
        {
            if (aiTarget == null) return;

            aiTarget.SightRange = 0.0f;

            //distance is approximated based on the mass of the Character 
            //(which corresponds to size because all the characters have the same limb density)
            foreach (Limb limb in AnimController.Limbs)
            {
                aiTarget.SightRange += limb.Mass * 1000.0f;
            }
            //the faster the Character is moving, the easier it is to see it
            Limb torso = AnimController.GetLimb(LimbType.Torso);
            if (torso !=null)
            {
                aiTarget.SightRange += torso.LinearVelocity.Length() * 500.0f;
            }
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            if (!Enabled) return;

            AnimController.Draw(spriteBatch);
            
            //GUI.DrawLine(spriteBatch, ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y),
            //    ConvertUnits.ToDisplayUnits(animController.limbs[0].SimPosition.X, animController.limbs[0].SimPosition.Y) +
            //    ConvertUnits.ToDisplayUnits(animController.targetMovement.X, animController.targetMovement.Y), Color.Green);
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera cam)
        {
            CharacterHUD.Draw(spriteBatch, this, cam);
        }

        public virtual void DrawFront(SpriteBatch spriteBatch)
        {
            if (!Enabled) return;

            Vector2 pos = ConvertUnits.ToDisplayUnits(AnimController.Limbs[0].SimPosition);
            pos.Y = -pos.Y;
            
            if (this == controlled) return;

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

            if (GameMain.DebugDraw)
            {
                AnimController.DebugDraw(spriteBatch);
            }

            Vector2 healthBarPos = new Vector2(Position.X - 50, -Position.Y - 100.0f);
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
            if (health <= 0.0f && damageType == DamageType.Burn) Kill(CauseOfDeath.Burn);

            Bleeding += attackResult.Bleeding;

            return attackResult;
        }

        public void StartStun(float stunTimer)
        {
            if (stunTimer <= 0.0f || !MathUtils.IsValid(stunTimer)) return;

            AnimController.ResetPullJoints();
            AnimController.StunTimer = Math.Max(AnimController.StunTimer, stunTimer);
                
            selectedConstruction = null;
        }

        private void Implode(bool isNetworkMessage = false)
        {
            if (!isNetworkMessage)
            {
                if (GameMain.NetworkMember != null && controlled != this) return; 
            }

            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            health = 0.0f;

            foreach (Limb limb in AnimController.Limbs)
            {
                limb.AddDamage(limb.SimPosition, DamageType.Blunt, 500.0f, 0.0f, false);

                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 10.0f);
               // limb.Damage = 100.0f;
            }

            SoundPlayer.PlayDamageSound(DamageSoundType.Implode, 50.0f, AnimController.RefLimb.body.FarseerBody);
            
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
            Kill(CauseOfDeath.Pressure, isNetworkMessage);
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

        public void Kill(CauseOfDeath causeOfDeath, bool isNetworkMessage = false)
        {
            if (isDead) return;

            if (GameMain.NetworkMember != null)
            {
                //if the Character is controlled by this client/server, let others know that the Character has died
                if (Character.controlled == this)
                {
                    string chatMessage = "You have " + DeathMsg[(int)causeOfDeath] + ".";
                    if (GameMain.Client!=null) chatMessage += " Your chat messages will only be visible to other dead players.";

                    GameMain.NetworkMember.AddChatMessage(chatMessage, ChatMessageType.Dead);
                    GameMain.LightManager.LosEnabled = false;

                    new NetworkEvent(NetworkEventType.KillCharacter, ID, true, causeOfDeath);
                }
                //if it's an ai Character, only let the server kill it
                else if (GameMain.Server != null && this is AICharacter)
                {
                    new NetworkEvent(NetworkEventType.KillCharacter, ID, true, causeOfDeath);
                }
                //otherwise don't kill the Character unless received a message about the Character dying
                else if (!isNetworkMessage)
                {
                    return;
                }
            }

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

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.KillCharacter(this);
            }
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            switch (type)
            {
                case NetworkEventType.PickItem:
                    int[] pickData = (int[])data;
                    if (pickData.Length != 3) return false;

                    message.Write((ushort)pickData[0]);
                    message.Write((int)pickData[1] == 1);
                    message.Write((int)pickData[2] == 1);
                    message.WritePadBits();

                    return true;
                case NetworkEventType.SelectCharacter:
                    message.Write((ushort)data);
                    return true;
                case NetworkEventType.KillCharacter:
                    CauseOfDeath causeOfDeath = CauseOfDeath.Damage;
                    try
                    {
                        causeOfDeath = (CauseOfDeath)data;
                    }
                    catch
                    {
                        causeOfDeath = CauseOfDeath.Damage;
                    }

                    message.Write((byte)causeOfDeath);

                    return true;  
                case NetworkEventType.InventoryUpdate:
                    if (inventory == null) return false;
                    return inventory.FillNetworkData(NetworkEventType.InventoryUpdate, message, data);
                case NetworkEventType.ImportantEntityUpdate:
                    
                    //int i = 0;
                    //foreach (Limb limb in AnimController.Limbs)
                    //{
                    //    if (limb.SimPosition.Length() > NetConfig.CharacterIgnoreDistance) return false;

                    //    message.WriteRangedSingle(limb.body.SimPosition.X, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                    //    message.WriteRangedSingle(limb.body.SimPosition.Y, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);

                    //    //message.Write(limb.body.LinearVelocity.X);
                    //    //message.Write(limb.body.LinearVelocity.Y);

                    //    message.Write(limb.body.Rotation);
                    //    //message.WriteRangedSingle(MathHelper.Clamp(limb.body.AngularVelocity, -10.0f, 10.0f), -10.0f, 10.0f, 8);
                    //    i++;
                    //}

                    message.Write((byte)((health / maxHealth) * 255.0f));

                    if (AnimController.StunTimer<=0.0f && bleeding<=0.0f && oxygen>99.0f)
                    {
                        message.Write(true);
                    }
                    else
                    {
                        message.Write(false);

                        message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);

                        message.Write((byte)(MathHelper.Clamp(oxygen * 2.55f, 0.0f, 255.0f)));

                        bleeding = MathHelper.Clamp(bleeding, 0.0f, 5.0f);
                        message.WriteRangedSingle(bleeding, 0.0f, 5.0f, 8);

                    }


                    return true;
                case NetworkEventType.EntityUpdate:
                    message.Write(keys[(int)InputType.Use].DequeueHeld);

                    bool secondaryHeld = keys[(int)InputType.Aim].DequeueHeld;
                    message.Write(secondaryHeld);
                        
                    message.Write(keys[(int)InputType.Left].Held);
                    message.Write(keys[(int)InputType.Right].Held);

                    message.Write(keys[(int)InputType.Up].Held);
                    message.Write(keys[(int)InputType.Down].Held);

                    message.Write(keys[(int)InputType.Run].Held);
                    
                    if (secondaryHeld)
                    {
                        Vector2 relativeCursorPosition = cursorPosition - Position;

                        if (relativeCursorPosition.Length()>4950.0f)
                        {
                            relativeCursorPosition = Vector2.Normalize(relativeCursorPosition) * 4950.0f;
                        }

                        message.WriteRangedSingle(relativeCursorPosition.X, -5000.0f, 5000.0f, 16);
                        message.WriteRangedSingle(relativeCursorPosition.Y, -5000.0f, 5000.0f, 16);
                    }
                    else
                    {
                        message.Write(AnimController.Dir > 0.0f);
                    }

                    if (AnimController.RefLimb.SimPosition.Length() > NetConfig.CharacterIgnoreDistance) return true;
                    
                    message.WriteRangedSingle(AnimController.RefLimb.SimPosition.X, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                    message.WriteRangedSingle(AnimController.RefLimb.SimPosition.Y, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);

                    return true;
                default:
#if DEBUG
                    DebugConsole.ThrowError("Character "+this+" tried to fill a networkevent of the wrong type: "+type);
#endif
                    return false;
            }
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            Enabled = true;
            data = null;

            switch (type)
            {
                case NetworkEventType.PickItem:
                    System.Diagnostics.Debug.WriteLine("**************** PickItem networkevent received");

                    ushort itemId = message.ReadUInt16();

                    bool pickHit = message.ReadBoolean();
                    bool actionHit = message.ReadBoolean();

                    data = new int[] { (int)itemId, pickHit ? 1 : 0, actionHit ? 1: 0 };

                    System.Diagnostics.Debug.WriteLine("item id: "+itemId);

                    Item item = FindEntityByID(itemId) as Item;
                    if (item != null) item.Pick(this, false, pickHit, actionHit);                    

                    return;
                case NetworkEventType.SelectCharacter:
                    ushort characterId = message.ReadUInt16();
                    data = characterId;

                    if (characterId==0)
                    {
                        DeselectCharacter(false);
                    }
                    else
                    {
                        Character character = FindEntityByID(characterId) as Character;
                        if (character != null) SelectCharacter(character, false);
                    }
                    return;
                case NetworkEventType.KillCharacter:
                    if (GameMain.Server != null)
                    {
                        Client sender =GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
                        if (sender == null || sender.Character != this) 
                            throw new Exception("Received a KillCharacter message from someone else than the client controlling the Character!");
                    }

                    CauseOfDeath causeOfDeath = CauseOfDeath.Damage;                    
                    try
                    {
                        byte causeOfDeathByte = message.ReadByte();
                        causeOfDeath = (CauseOfDeath)causeOfDeathByte;
                    }
                    catch
                    {
                        causeOfDeath = CauseOfDeath.Damage;
                    }

                    data = causeOfDeath;

                    if (causeOfDeath==CauseOfDeath.Pressure)
                    {
                        Implode(true);
                    }
                    else
                    {
                        Kill(causeOfDeath, true);
                    }
                    return;
                case NetworkEventType.InventoryUpdate:
                    if (inventory == null) return;
                    inventory.ReadNetworkData(NetworkEventType.InventoryUpdate, message, sendingTime);
                    return;
                case NetworkEventType.ImportantEntityUpdate:
                   
                    Health = (message.ReadByte() / 255.0f) * maxHealth;

                    bool allOk = message.ReadBoolean();
                    if (allOk) return;

                    float newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                    StartStun(newStunTimer);
                    
                    Oxygen = (message.ReadByte() / 2.55f);
                    Bleeding = message.ReadRangedSingle(0.0f, 5.0f, 8);     

                    return;
                case NetworkEventType.EntityUpdate:
                    Vector2 relativeCursorPos = Vector2.Zero;

                    bool actionKeyState, secondaryKeyState;
                    bool leftKeyState, rightKeyState, upKeyState, downKeyState;
                    bool runState;

                    try
                    {
                        if (sendingTime > LastNetworkUpdate) ClearInputs();                        

                        actionKeyState      = message.ReadBoolean();
                        secondaryKeyState   = message.ReadBoolean();
            
                        leftKeyState        = message.ReadBoolean();
                        rightKeyState       = message.ReadBoolean();
                        upKeyState          = message.ReadBoolean();
                        downKeyState        = message.ReadBoolean();

                        runState            = message.ReadBoolean();
                    }

                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Error in Character.ReadNetworkData: " + e.Message);
#endif
                        return;
                    }

                    keys[(int)InputType.Use].Held = actionKeyState;
                    keys[(int)InputType.Use].SetState(false, actionKeyState);

                    keys[(int)InputType.Aim].Held = secondaryKeyState;
                    keys[(int)InputType.Aim].SetState(false, secondaryKeyState);

                    if (sendingTime <= LastNetworkUpdate) return;

                    keys[(int)InputType.Left].Held      = leftKeyState;
                    keys[(int)InputType.Right].Held     = rightKeyState;

                    keys[(int)InputType.Up].Held        = upKeyState;
                    keys[(int)InputType.Down].Held      = downKeyState;

                    keys[(int)InputType.Run].Held = runState;

                    float dir = 1.0f;
                    Vector2 pos = Vector2.Zero;

                    try
                    {
                        if (secondaryKeyState)
                        {
                            relativeCursorPos = new Vector2(
                                message.ReadRangedSingle(-5000.0f, 5000.0f, 16),
                                message.ReadRangedSingle(-5000.0f, 5000.0f, 16));
                        }
                        else
                        {
                            dir = message.ReadBoolean() ? 1.0f : -1.0f;
                        }
                    }
                    catch
                    {
#if DEBUG
                        DebugConsole.ThrowError("Failed to read networkevent for "+this.ToString());
#endif
                        return;
                    }
                    try
                    {
                        pos.X = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                        pos.Y = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                    }

                    catch
                    {
                        //failed to read position, Character may be further than NetConfig.CharacterIgnoreDistance
                        pos = SimPosition;
                    }                    

                    if (secondaryKeyState)
                    {
                        cursorPosition = MathUtils.IsValid(relativeCursorPos) ? 
                            ConvertUnits.ToDisplayUnits(pos)+relativeCursorPos : Vector2.Zero;
                    }
                    else
                    {
                        cursorPosition = Position + new Vector2(1000.0f, 0.0f) * dir;
                    }   

                    AnimController.RefLimb.body.TargetPosition = AnimController.EstimateCurrPosition(pos, (float)(NetTime.Now + message.SenderConnection.RemoteTimeOffset) - sendingTime);

                    LastNetworkUpdate = sendingTime;

                    return;
                default:
#if DEBUG
                    DebugConsole.ThrowError("Character " + this + " tried to read a networkevent of the wrong type: " + type);
#endif
                    return;
            }
        }

        public override void Remove()
        {
            base.Remove();

            CharacterList.Remove(this);

            if (controlled == this) controlled = null;

            if (GameMain.Client!=null && GameMain.Client.Character == this) GameMain.Client.Character = null;

            if (aiTarget != null)
                aiTarget.Remove();

            if (AnimController!=null)
                AnimController.Remove();
        }

    }
}
