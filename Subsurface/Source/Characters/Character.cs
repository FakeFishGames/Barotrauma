
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
   
    class Character : Entity, IDamageable, IPropertyObject, IClientSerializable, IServerSerializable
    {
        public static List<Character> CharacterList = new List<Character>();
        
        public static bool DisableControls;

        private UInt32 netStateID;
        public UInt32 NetStateID
        {
            get { return netStateID; }
        }

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
            }
        }

        public bool SpawnedMidRound;

        public List<Item> SpawnItems = new List<Item>();

        private bool enabled;

        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                enabled = value;

                foreach (Limb limb in AnimController.Limbs)
                {
                    limb.body.Enabled = enabled;
                }
            }
        }

        public readonly bool IsNetworkPlayer;

        private bool networkUpdateSent;

        private CharacterInventory inventory;

        public float LastNetworkUpdate;

        //public int LargeUpdateTimer;

        public readonly Dictionary<string, ObjectProperty> Properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return Properties; }
        }

        protected Key[] keys;
        
        private Item selectedConstruction;
        private Item[] selectedItems;
        
        public AnimController AnimController;

        private Vector2 cursorPosition;

        protected bool needsAir;
        protected float oxygen, oxygenAvailable;
        protected float drowningTime;

        protected float health;
        protected float minHealth, maxHealth;

        protected Item closestItem;
        private Character closestCharacter, selectedCharacter;

        protected bool isDead;
        private CauseOfDeath lastAttackCauseOfDeath;
        private CauseOfDeath causeOfDeath;
        
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

        private float attackCoolDown;

        public Entity ViewTarget
        {
            get;
            private set;
        }        

        private CharacterInfo info;

        public CharacterInfo Info
        {
            get
            { 
                return info;
            }
            set 
            {
                if (info != null && info != value) info.Remove();

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

        public string ConfigPath
        {
            get;
            private set;
        }

        public float Mass
        {
            get { return AnimController.Mass; }
        }

        public CharacterInventory Inventory
        {
            get { return inventory; }
        }
        
        private Color speechBubbleColor;
        private float speechBubbleTimer;

        private float lockHandsTimer;
        public bool LockHands
        {
            get
            {
                return lockHandsTimer > 0.0f;
            }
            set
            {
                lockHandsTimer = MathHelper.Clamp(lockHandsTimer + (value ? 1.0f : -0.5f), 0.0f, 10.0f);
            }
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

        public Vector2 CursorWorldPosition
        {
            get { return Submarine == null ? cursorPosition : cursorPosition + Submarine.Position; }
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

        public bool IsUnconscious
        {
            get { return (needsAir && oxygen <= 0.0f) || health <= 0.0f; }
        }

        public bool NeedsAir
        {
            get { return needsAir; }
            set { needsAir = value; }
        }
        
        public float Oxygen
        {
            get { return oxygen; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, -100.0f, 100.0f);
                if (oxygen == -100.0f) Kill(AnimController.InWater ? CauseOfDeath.Drowning : CauseOfDeath.Suffocation);
            }
        }

        public float OxygenAvailable
        {
            get { return oxygenAvailable; }
            set { oxygenAvailable = MathHelper.Clamp(value, 0.0f, 100.0f); }
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
                health = MathHelper.Clamp(value, minHealth, maxHealth);
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

        public HuskInfection huskInfection;
        public float HuskInfectionState
        {
            get 
            { 
                return huskInfection == null ? 0.0f : huskInfection.IncubationTimer; 
            }
            set
            {
                if (ConfigPath != humanConfigFile) return;
                
                if (value <= 0.0f)
                {
                    if (huskInfection != null && huskInfection.State == HuskInfection.InfectionState.Active) return; 
                    huskInfection = null;
                }
                else
                {
                    if (huskInfection == null) huskInfection = new HuskInfection(this);
                    huskInfection.IncubationTimer = MathHelper.Clamp(value, 0.0f, 1.0f);
                }
            }
        }

        public bool CanSpeak
        {
            get
            {
                return !IsUnconscious && Stun <= 0.0f && (huskInfection == null || huskInfection.CanSpeak);
            }
        }

        public bool DoesBleed
        {
            get;
            private set;
        }

        public float BleedingDecreaseSpeed
        {
            get;
            private set;
        }

        public float PressureTimer
        {
            get;
            private set;
        }

        public float DisableImpactDamageTimer
        {
            get;
            set;
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

        public CauseOfDeath CauseOfDeath
        {
            get { return causeOfDeath; }
        }

        public bool CanBeSelected
        {
            get
            {
                return isDead || Stun > 0.0f || LockHands;
            }
        }

        public override Vector2 SimPosition
        {
            get { return AnimController.RefLimb.SimPosition; }
        }

        public override Vector2 Position
        {
            get { return AnimController.RefLimb.Position; }
        }

        public override Vector2 DrawPosition
        {
            get { return AnimController.RefLimb.body.DrawPosition; }
        }

        public delegate void OnDeathHandler(Character character, CauseOfDeath causeOfDeath);
        public OnDeathHandler OnDeath;
        
        public static Character Create(CharacterInfo characterInfo, Vector2 position, bool isNetworkPlayer = false, bool hasAi=true)
        {
            return Create(characterInfo.File, position, characterInfo, isNetworkPlayer, hasAi);
        }

        public static Character Create(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false, bool hasAi=true)
        {
#if LINUX            
            if (!System.IO.File.Exists(file)) 
            {

                //if the file was not found, attempt to convert the name of the folder to upper case
                var splitPath = file.Split('/');
                if (splitPath.Length > 2)
                {
                    splitPath[splitPath.Length-2] = 
                        splitPath[splitPath.Length-2].First().ToString().ToUpper() + splitPath[splitPath.Length-2].Substring(1);
                    
                    file = string.Join("/", splitPath);
                }

                if (!System.IO.File.Exists(file))
                {
                    DebugConsole.ThrowError("Spawning a character failed - file ''"+file+"'' not found!");
                    return null;
                }
            }
#else
            if (!System.IO.File.Exists(file))
            {
                DebugConsole.ThrowError("Spawning a character failed - file ''"+file+"'' not found!");
                return null;
            }
#endif
            

            if (file != humanConfigFile)
            {
                var enemyCharacter = new AICharacter(file, position, characterInfo, isNetworkPlayer);
                var ai = new EnemyAIController(enemyCharacter, file);
                enemyCharacter.SetAI(ai);

                enemyCharacter.minHealth = 0.0f;

                return enemyCharacter;
            }
            else if (hasAi)
            {
                var aiCharacter = new AICharacter(file, position, characterInfo, isNetworkPlayer);
                var ai = new HumanAIController(aiCharacter);
                aiCharacter.SetAI(ai);

                aiCharacter.minHealth = -100.0f;

                return aiCharacter;
            }

            var character = new Character(file, position, characterInfo, isNetworkPlayer);
            character.minHealth = -100.0f;

            return character;
        }

        protected Character(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
            : base(null)
        {

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];

            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key(GameMain.Config.KeyBind((InputType)i));
            }

            ConfigPath = file;
            
            selectedItems = new Item[2];

            IsNetworkPlayer = isNetworkPlayer;

            oxygen = 100.0f;
            oxygenAvailable = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

            Properties = ObjectProperty.GetProperties(this);

            Info = characterInfo;
            if (file == humanConfigFile && characterInfo == null)
            {
                Info = new CharacterInfo(file);
            }

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            
            
            SpeciesName = ToolBox.GetAttributeString(doc.Root, "name", "Unknown");

            IsHumanoid = ToolBox.GetAttributeBool(doc.Root, "humanoid", false);
            
            if (IsHumanoid)
            {
                AnimController = new HumanoidAnimController(this, doc.Root.Element("ragdoll"));
                AnimController.TargetDir = Direction.Right;
                inventory = new CharacterInventory(16, this);
            }
            else
            {
                AnimController = new FishAnimController(this, doc.Root.Element("ragdoll"));
                PressureProtection = 100.0f;
                //FishAnimController fishAnim = (FishAnimController)animController;
            }

            foreach (Limb limb in AnimController.Limbs)
            {
                limb.body.SetTransform(ConvertUnits.ToSimUnits(position)+limb.SimPosition, 0.0f);
                //limb.prevPosition = ConvertUnits.ToDisplayUnits(position);
            }

            maxHealth = ToolBox.GetAttributeFloat(doc.Root, "health", 100.0f);
            health = maxHealth;

            DoesBleed = ToolBox.GetAttributeBool(doc.Root, "doesbleed", true);
            BleedingDecreaseSpeed = ToolBox.GetAttributeFloat(doc.Root, "bleedingdecreasespeed", 0.05f);

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

            if (file == humanConfigFile)
            {
                if (Info.PickedItemIDs.Any())
                {
                    for (ushort i = 0; i < Info.PickedItemIDs.Count; i++ )
                    {
                        if (Info.PickedItemIDs[i] == 0) continue;

                        Item item = FindEntityByID(Info.PickedItemIDs[i]) as Item;

                        System.Diagnostics.Debug.Assert(item != null);
                        if (item == null) continue;

                        item.Pick(this, true, true, true);
                        inventory.TryPutItem(item, i, false);
                    }
                }
            }


            AnimController.FindHull(null);
            if (AnimController.CurrentHull != null) Submarine = AnimController.CurrentHull.Submarine;

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

        public void SetInput(InputType inputType, bool hit, bool held)
        {
            keys[(int)inputType].Hit = hit;
            keys[(int)inputType].Held = held;
            
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

            info.Job.GiveJobItems(this, spawnPoint);
        }

        public int GetSkillLevel(string skillName)
        {
            return (Info==null || Info.Job==null) ? 0 : Info.Job.GetSkillLevel(skillName);
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
            AnimController.IgnorePlatforms = AnimController.TargetMovement.Y < 0.0f;

            if (AnimController is HumanoidAnimController)
            {
                ((HumanoidAnimController) AnimController).Crouching = IsKeyDown(InputType.Crouch);
            }

            if (AnimController.onGround &&
                !AnimController.InWater &&
                AnimController.Anim != AnimController.Animation.UsingConstruction &&
                AnimController.Anim != AnimController.Animation.CPR)
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

            if (attackCoolDown >0.0f)
            {
                attackCoolDown -= deltaTime;
            }
            else if (IsKeyDown(InputType.Attack))
            {
                var attackLimb = AnimController.Limbs.FirstOrDefault(l => l.attack != null);

                if (attackLimb != null)
                {
                    Vector2 attackPos =
                        attackLimb.SimPosition + Vector2.Normalize(cursorPosition - attackLimb.Position) * ConvertUnits.ToSimUnits(attackLimb.attack.Range);

                    var body = Submarine.PickBody(
                        attackLimb.SimPosition,
                        attackPos,
                        AnimController.Limbs.Select(l => l.body.FarseerBody).ToList(),
                        Physics.CollisionCharacter | Physics.CollisionWall);

                    IDamageable attackTarget = null;
                    if (body != null)
                    {
                        if (body.UserData is IDamageable)
                        {
                            attackTarget = (IDamageable)body.UserData;
                        }
                        else if (body.UserData is Limb)
                        {
                            attackTarget = ((Limb)body.UserData).character;                            
                        }
                        attackPos = Submarine.LastPickedPosition;
                    }

                    attackLimb.UpdateAttack(deltaTime, attackPos, attackTarget);

                    if (attackLimb.AttackTimer > attackLimb.attack.Duration)
                    {
                        attackLimb.AttackTimer = 0.0f;
                        attackCoolDown = 1.0f;
                    }
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

            if (selectedCharacter!=null)
            {
                if (Vector2.Distance(selectedCharacter.WorldPosition, WorldPosition) > 300.0f || !selectedCharacter.CanBeSelected)
                {
                    DeselectCharacter();
                }
            }

                  
            if (IsNetworkPlayer)
            {
                foreach (Key key in keys)
                {
                    key.ResetHit();
                }
            }
        }
        
        public bool HasEquippedItem(Item item)
        {
            return !inventory.IsInLimbSlot(item, InvSlotType.Any);
        }

        public bool HasSelectedItem(Item item)
        {
            return selectedItems.Contains(item);
        }

        public bool TrySelectItem(Item item)
        {
            bool rightHand = inventory.IsInLimbSlot(item, InvSlotType.RightHand);
            bool leftHand = inventory.IsInLimbSlot(item, InvSlotType.LeftHand);

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

        public bool CanAccessInventory(Inventory inventory)
        {
            if (inventory.Owner is Character && inventory.Owner != this)
            {
                var owner = (Character)inventory.Owner;

                return owner.isDead || owner.IsUnconscious || owner.Stun > 0.0f || owner.LockHands;
            }

            if (inventory.Owner is Item)
            {
                var owner = (Item)inventory.Owner;
                if (!CanAccessItem(owner))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanAccessItem(Item item)
        {
            if (item.ParentInventory != null)
            {
                return CanAccessInventory(item.ParentInventory);
            }

            float maxDist = item.PickDistance * 1.2f;
            if (maxDist <= 0.01f)
            {
                maxDist = 150.0f;
            }

            if (Vector2.Distance(WorldPosition, item.WorldPosition) < maxDist ||
                item.IsInsideTrigger(WorldPosition))
            {
                return true;
            }

            return item.GetComponent<Items.Components.Ladder>() != null;
        }

        private Item FindClosestItem(Vector2 mouseSimPos, out float distance)
        {
            distance = 0.0f;

            Limb torso = AnimController.GetLimb(LimbType.Torso);

            if (torso == null) return null;

            Vector2 pos = (torso.body.TargetPosition != Vector2.Zero) ? torso.body.TargetPosition : torso.SimPosition;
            Vector2 pickPos = mouseSimPos;

            if (Submarine != null)
            {
                pos += Submarine.SimPosition;
                pickPos += Submarine.SimPosition;
            }

            if (selectedConstruction != null) pickPos = ConvertUnits.ToSimUnits(selectedConstruction.WorldPosition);

            return Item.FindPickable(pos, pickPos, AnimController.CurrentHull, selectedItems, out distance);
        }

        private Character FindClosestCharacter(Vector2 mouseSimPos, float maxDist = 150.0f)
        {
            Character closestCharacter = null;
            float closestDist = 0.0f;

            maxDist = ConvertUnits.ToSimUnits(maxDist);
            
            foreach (Character c in CharacterList)
            {
                if (c == this || !c.enabled) continue;

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

        private void SelectCharacter(Character character)
        {
            if (character == null) return;

            selectedCharacter = character;
        }

        private void DeselectCharacter()
        {
            if (selectedCharacter == null) return;
            
            foreach (Limb limb in selectedCharacter.AnimController.Limbs)
            {
                if (limb.pullJoint != null) limb.pullJoint.Enabled = false;
            }

            selectedCharacter = null;
        }

        /// <summary>
        /// Control the Character according to player input
        /// </summary>
        public void ControlLocalPlayer(float deltaTime, Camera cam, bool moveCam = true)
        {
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
                float pressureEffect = 0.0f;

                if (pressureProtection < 80.0f && AnimController.CurrentHull != null && AnimController.CurrentHull.LethalPressure > 50.0f)
                {
                    cam.Zoom = MathHelper.Lerp(cam.Zoom,
                        (AnimController.CurrentHull.LethalPressure / 50.0f) * Rand.Range(1.0f, 1.05f),
                        (AnimController.CurrentHull.LethalPressure - 50.0f) / 50.0f);
                }
                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, (Submarine == null ? 400.0f : 250.0f)+pressureEffect, 0.05f);
            }
            
            cursorPosition = cam.ScreenToWorld(PlayerInput.MousePosition);
            if (AnimController.CurrentHull != null && AnimController.CurrentHull.Submarine != null)
            {
                cursorPosition -= AnimController.CurrentHull.Submarine.Position;
            }

            Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);

            if (Lights.LightManager.ViewTarget == this && Vector2.Distance(AnimController.Limbs[0].SimPosition, mouseSimPos) > 1.0f)
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

            if (!LockHands)
            {
                //find the closest item if selectkey has been hit, or if the Character is being
                //controlled by the player (in order to highlight it)

                if (findClosestTimer <= 0.0f || Screen.Selected == GameMain.EditMapScreen)
                {
                    closestCharacter = FindClosestCharacter(mouseSimPos);
                    if (closestCharacter != null && closestCharacter.info==null)
                    {
                        closestCharacter = null;
                    }

                    float closestItemDist = 0.0f;
                    closestItem = FindClosestItem(mouseSimPos, out closestItemDist);

                    if (closestCharacter != null && closestItem != null)
                    {
                        if (Vector2.Distance(closestCharacter.SimPosition, mouseSimPos) < ConvertUnits.ToSimUnits(closestItemDist))
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

                if (selectedCharacter == null && closestItem != null)
                {
                    closestItem.IsHighlighted = true;
                    if (!LockHands && closestItem.Pick(this))
                    {
                        
                    }
                }

                if (IsKeyHit(InputType.Select))
                {
                    if (selectedCharacter != null)
                    {
                        DeselectCharacter();
                    }
                    else if (closestCharacter != null && closestCharacter.IsHumanoid && closestCharacter.CanBeSelected)
                    {
                        SelectCharacter(closestCharacter);
                    }
                }
            }
            else
            {
                if (selectedCharacter != null) DeselectCharacter();
                selectedConstruction = null;
                closestItem = null;
                closestCharacter = null;
            }

            DisableControls = false;
        }
        

        public static void UpdateAnimAll(float deltaTime)
        {
            foreach (Character c in CharacterList)
            {
                if (c.isDead || c.health <= 0.0f || !c.Enabled) continue;
                c.AnimController.UpdateAnim(deltaTime);
            }
        }
        
        public static void UpdateAll(Camera cam, float deltaTime)
        {
            //if (NewCharacterQueue.Count>0)
            //{
            //    new Character(NewCharacterQueue.Dequeue(), Vector2.Zero);
            //}

            for (int i = 0; i<CharacterList.Count; i++)
            {
                CharacterList[i].Update(cam, deltaTime);
            }
        }

        public virtual void Update(Camera cam, float deltaTime)
        {
            if (!Enabled) return;

            speechBubbleTimer = Math.Max(0.0f, speechBubbleTimer - deltaTime);

            obstructVisionAmount = Math.Max(obstructVisionAmount - deltaTime, 0.0f);
            
            if (inventory!=null)
            {
                foreach (Item item in inventory.Items)
                {
                    if (item == null || item.body == null || item.body.Enabled) continue;

                    item.SetTransform(SimPosition, 0.0f);
                    item.Submarine = Submarine;
                }
            }

            if (huskInfection != null) huskInfection.Update(deltaTime, this);
            
            if (isDead) return;

            if (networkUpdateSent)
            {
                foreach (Key key in keys)
                {
                    key.DequeueHit();
                    key.DequeueHeld();
                }

                networkUpdateSent = false;
            }

            DisableImpactDamageTimer -= deltaTime;
            
            if (needsAir)
            {
                bool protectedFromPressure = PressureProtection > 0.0f;
                
                protectedFromPressure = protectedFromPressure && WorldPosition.Y > SubmarineBody.DamageDepth;
                           
                if (!protectedFromPressure && 
                    (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f))
                {
                    PressureTimer += ((AnimController.CurrentHull == null) ?
                        100.0f : AnimController.CurrentHull.LethalPressure) * deltaTime;

                    if (PressureTimer >= 100.0f)
                    {
                        if (controlled == this) cam.Zoom = 5.0f;
                        Implode();
                        return;
                    }
                }
                else
                {
                    PressureTimer = 0.0f;
                }
            }

            if (controlled == this)
            {
                Lights.LightManager.ViewTarget = this;
                CharacterHUD.Update(deltaTime, this);
            }

            if (IsUnconscious)
            {
                UpdateUnconscious(deltaTime);
                return;
            }

            if (controlled == this)
            {
                ControlLocalPlayer(deltaTime, cam);
            }

            if (controlled == this || 
                !(this is AICharacter) || 
                !((AICharacter)this).AIController.Enabled)
            {
                Control(deltaTime, cam);
            }

            if (selectedCharacter != null && AnimController.Anim == AnimController.Animation.CPR)
            {
                if (GameMain.Client == null) selectedCharacter.Oxygen += (GetSkillLevel("Medical") / 10.0f) * deltaTime;
            }

            UpdateSightRange();
            if (aiTarget != null) aiTarget.SoundRange = 0.0f;

            lowPassMultiplier = MathHelper.Lerp(lowPassMultiplier, 1.0f, 0.1f);

            if (needsAir) UpdateOxygen(deltaTime);

            Health -= bleeding * deltaTime;
            Bleeding -= BleedingDecreaseSpeed * deltaTime;

            if (health <= minHealth) Kill(CauseOfDeath.Bloodloss);

            if (!IsDead) LockHands = false;
        }

        private void UpdateOxygen(float deltaTime)
        {
            Oxygen += deltaTime * (oxygenAvailable < 30.0f ? -5.0f : 10.0f);

            PressureProtection -= deltaTime * 100.0f;

            float hullAvailableOxygen = 0.0f;

            if (!AnimController.HeadInWater && AnimController.CurrentHull != null)
            {
                hullAvailableOxygen = AnimController.CurrentHull.OxygenPercentage;

                AnimController.CurrentHull.Oxygen -= Hull.OxygenConsumptionSpeed * deltaTime;
            }

            OxygenAvailable += Math.Sign(hullAvailableOxygen - oxygenAvailable) * deltaTime * 50.0f;
        }

        private void UpdateUnconscious(float deltaTime)
        {
            Stun = Math.Max(5.0f, Stun);

            AnimController.ResetPullJoints();
            selectedConstruction = null;

            if (oxygen <= 0.0f) Oxygen -= deltaTime * 0.5f;

            if (health <= 0.0f)
            {
                AddDamage(bleeding > 0.5f ? CauseOfDeath.Bloodloss : CauseOfDeath.Damage, Math.Max(bleeding, 1.0f) * deltaTime, null);
            }
        }

        private void UpdateSightRange()
        {
            if (aiTarget == null) return;

            aiTarget.SightRange = 0.0f;

            aiTarget.SightRange = Mass*10.0f + AnimController.RefLimb.LinearVelocity.Length()*500.0f;
        }
        
        public void ShowSpeechBubble(float duration, Color color)
        {
            speechBubbleTimer = Math.Max(speechBubbleTimer, duration);
            speechBubbleColor = color;
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

            Vector2 pos = DrawPosition;
            pos.Y = -pos.Y;

            if (GameMain.DebugDraw)
            {
                AnimController.DebugDraw(spriteBatch);

                if (aiTarget != null) aiTarget.Draw(spriteBatch);
            }

            if (this == controlled) return;

            if (info != null)
            {
                Vector2 namePos = new Vector2(pos.X, pos.Y - 120.0f) - GUI.Font.MeasureString(Info.Name) * 0.5f;
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, Info.Name, namePos, Color.White);

                if (GameMain.DebugDraw)
                {
                    spriteBatch.DrawString(GUI.Font, ID.ToString(), namePos - new Vector2(0.0f, 20.0f), Color.White);
                }
            }

            if (isDead) return;

            if (health < maxHealth * 0.98f)
            {
                Vector2 healthBarPos = new Vector2(pos.X - 50, DrawPosition.Y + 100.0f);
            
                GUI.DrawProgressBar(spriteBatch, healthBarPos, new Vector2(100.0f, 15.0f), health / maxHealth, Color.Lerp(Color.Red, Color.Green, health / maxHealth) * 0.8f);
            }
            
            if (speechBubbleTimer > 0.0f)
            {
                GUI.SpeechBubbleIcon.Draw(spriteBatch, pos - Vector2.UnitY * 100.0f, 
                    speechBubbleColor * Math.Min(speechBubbleTimer, 1.0f), 0.0f, 
                    Math.Min((float)speechBubbleTimer, 1.0f));
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
                if (n == selectedSound && sounds[i]!=null)
                {
                    sounds[i].Play(1.0f, soundRange[i], AnimController.Limbs[0].WorldPosition);
                    return;
                }
                n++;
            }
        }

        public virtual void AddDamage(CauseOfDeath causeOfDeath, float amount, IDamageable attacker)
        {
            Health = health-amount;
            if (amount > 0.0f)
            {
                lastAttackCauseOfDeath = causeOfDeath;
                if (controlled == this) CharacterHUD.TakeDamage(amount);
            }
            if (health <= minHealth) Kill(causeOfDeath);
        }

        public virtual AttackResult AddDamage(IDamageable attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            var attackResult = AddDamage(worldPosition, attack.DamageType, attack.GetDamage(deltaTime), attack.GetBleedingDamage(deltaTime), attack.Stun, playSound, attack.TargetForce);

            var attackingCharacter = attacker as Character;
            if (attackingCharacter != null && attackingCharacter.AIController == null)
            {
                GameServer.Log(Name + " attacked by " + attackingCharacter.Name+". Damage: "+attackResult.Damage+" Bleeding damage: "+attackResult.Bleeding, Color.Orange);
            }

            return attackResult;
        }

        public AttackResult AddDamage(Vector2 worldPosition, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound, float attackForce = 0.0f)
        {
            StartStun(stun);

            Limb closestLimb = null;
            float closestDistance = 0.0f;
            foreach (Limb limb in AnimController.Limbs)
            {
                float distance = Vector2.Distance(worldPosition, limb.WorldPosition);
                if (closestLimb == null || distance < closestDistance)
                {
                    closestLimb = limb;
                    closestDistance = distance;
                }
            }
            
            if (Math.Abs(attackForce) > 0.0f)
            {
                closestLimb.body.ApplyForce((closestLimb.WorldPosition - worldPosition) * attackForce);
            }

            AttackResult attackResult = closestLimb.AddDamage(worldPosition, damageType, amount, bleedingAmount, playSound);

            AddDamage(damageType == DamageType.Burn ? CauseOfDeath.Burn : causeOfDeath, attackResult.Damage, null);
                        
            //health -= attackResult.Damage;
            //if (health <= 0.0f && damageType == DamageType.Burn) Kill(CauseOfDeath.Burn);
            if (DoesBleed)
            {
                Bleeding += attackResult.Bleeding;
            }
            
            return attackResult;
        }

        public void StartStun(float stunTimer, bool allowStunDecrease = false)
        {
            if ((stunTimer <= 0.0f && !allowStunDecrease) || !MathUtils.IsValid(stunTimer)) return;

            if (Math.Sign(stunTimer) != Math.Sign(AnimController.StunTimer)) AnimController.ResetPullJoints();
            AnimController.StunTimer = Math.Max(AnimController.StunTimer, stunTimer);
                
            selectedConstruction = null;
        }

        private void Implode(bool isNetworkMessage = false)
        {
            if (!isNetworkMessage)
            {
                if (GameMain.NetworkMember != null && controlled != this) return; 
            }


            health = minHealth;

            BreakJoints();

            Kill(CauseOfDeath.Pressure, isNetworkMessage);
        }

        public void BreakJoints()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            foreach (Limb limb in AnimController.Limbs)
            {
                limb.AddDamage(limb.SimPosition, DamageType.Blunt, 500.0f, 0.0f, false);

                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 10.0f);
                // limb.Damage = 100.0f;
            }

            SoundPlayer.PlayDamageSound(DamageSoundType.Implode, 50.0f, AnimController.RefLimb.body);

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

            foreach (var joint in AnimController.limbJoints)
            {
                joint.LimitEnabled = false;
            }
        }
        
        public void Kill(CauseOfDeath causeOfDeath, bool isNetworkMessage = false)
        {
            if (isDead) return;

            if (GameMain.NetworkMember != null)
            {
                //if the Character is controlled by this client/server, let others know that the Character has died
                if (Character.controlled == this)
                {
                    string chatMessage = InfoTextManager.GetInfoText("Self_CauseOfDeath." + causeOfDeath.ToString());
                    if (GameMain.Client!=null) chatMessage += " Your chat messages will only be visible to other dead players.";

                    GameMain.NetworkMember.AddChatMessage(chatMessage, ChatMessageType.Dead);
                    GameMain.LightManager.LosEnabled = false;
                    controlled = null;
                }
                //if it's an ai Character, only let the server kill it
                else if (GameMain.Server != null && this is AICharacter)
                {
                    
                }
                //don't kill the Character unless received a message about the Character dying
                else if (!isNetworkMessage)
                {
                    return;
                }
            }

            GameServer.Log(Name+" has died (Cause of death: "+causeOfDeath+")", Color.Red);

            if (OnDeath != null) OnDeath(this, causeOfDeath);

            //CoroutineManager.StartCoroutine(DeathAnim(GameMain.GameScreen.Cam));

            //health = 0.0f;

            isDead = true;
            this.causeOfDeath = causeOfDeath;
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }

            if (aiTarget!=null)
            {
                aiTarget.Remove();
                aiTarget = null;
            }


            foreach (Limb limb in AnimController.Limbs)
            {
                if (limb.pullJoint == null) continue;
                limb.pullJoint.Enabled = false;
            }

            foreach (RevoluteJoint joint in AnimController.limbJoints)
            {
                joint.MotorEnabled = false;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.KillCharacter(this);
            }
        }

        public void Revive(bool isNetworkMessage)
        {
            isDead = false;

            aiTarget = new AITarget(this);

            health = Math.Max(maxHealth * 0.1f, health);

            foreach (RevoluteJoint joint in AnimController.limbJoints)
            {
                joint.MotorEnabled = true;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.ReviveCharacter(this);
            }
        }
        
        public override void Remove()
        {
            base.Remove();

            if (info != null) info.Remove();

            CharacterList.Remove(this);

            if (controlled == this) controlled = null;

            if (GameMain.Client != null && GameMain.Client.Character == this) GameMain.Client.Character = null;

            if (aiTarget != null) aiTarget.Remove();

            if (AnimController != null) AnimController.Remove();
        }

        public virtual void ClientWrite(NetOutgoingMessage msg) 
        { 
            //TODO: write inputs
        }
        public virtual void ServerRead(NetIncomingMessage msg, Client c) 
        { 
            //TODO: read inputs
        }

        public virtual void ServerWrite(NetOutgoingMessage msg, Client c) 
        {
            //TODO: write position, health, etc
        }

        public virtual void ClientRead(NetIncomingMessage msg) 
        { 
            //TODO: read positions health, etc
        }

        public void WriteSpawnData(NetOutgoingMessage msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Info == null);
            msg.Write(ID);
            msg.Write(ConfigPath);

            msg.Write(WorldPosition.X);
            msg.Write(WorldPosition.Y);

            msg.Write(Enabled);

            //character with no characterinfo (e.g. some monster)
            if (Info == null) return;
            
            Client ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == this);                
            if (ownerClient != null)
            {
                msg.Write(true);
                msg.Write(ownerClient.ID);
            }
            else if (GameMain.Server.Character == this)
            {
                msg.Write(true);
                msg.Write((byte)0);
            }
            else
            {
                msg.Write(false);
            }

            msg.Write(Info.Name);

            msg.Write(this is AICharacter);
            msg.Write(Info.Gender == Gender.Female);
            msg.Write((byte)Info.HeadSpriteId);
            msg.Write(Info.Job == null ? "" : Info.Job.Name);            
        }

        public static Character ReadSpawnData(NetIncomingMessage inc)
        {
            if (GameMain.Server != null) return null;

            bool noInfo         = inc.ReadBoolean();
            ushort id           = inc.ReadUInt16();
            string configPath   = inc.ReadString();

            Vector2 position    = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            bool enabled        = inc.ReadBoolean();

            Character character = null;

            if (noInfo)
            {
                character = Character.Create(configPath, position, null, true);
                character.ID = id;
            }
            else
            {
                bool hasOwner       = inc.ReadBoolean();
                int ownerId         = hasOwner ? inc.ReadByte() : -1;

                string newName      = inc.ReadString();

                bool hasAi          = inc.ReadBoolean();
                bool isFemale       = inc.ReadBoolean();
                int headSpriteID    = inc.ReadByte();
                string jobName      = inc.ReadString();

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);

                CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab);
                ch.HeadSpriteId = headSpriteID;

                character = Character.Create(configPath, position, ch, true, hasAi);
                character.ID = id;

                if (configPath == Character.HumanConfigFile)
                {
                    GameMain.GameSession.CrewManager.characters.Add(character);
                }
            }

            character.Enabled = enabled;

            return character;
        }
    }
}
