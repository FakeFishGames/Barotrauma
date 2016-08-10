
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
        public static List<Character> CharacterList = new List<Character>();
        
        public static bool DisableControls;

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
            if (!System.IO.File.Exists(file)) return null;

            if (file != humanConfigFile)
            {
                var enemyCharacter = new AICharacter(file, position, characterInfo, isNetworkPlayer);
                var ai = new EnemyAIController(enemyCharacter, file);
                enemyCharacter.SetAI(ai);

                enemyCharacter.minHealth = 0.0f;

                return enemyCharacter;
            }

            if (hasAi && !isNetworkPlayer)
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

            if (file == humanConfigFile)
            {
                Info = characterInfo == null ? new CharacterInfo(file) : characterInfo;
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
                        inventory.TryPutItem(item, i, false, false);
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
                if (Vector2.Distance(selectedCharacter.SimPosition, SimPosition) > 3.0f || !selectedCharacter.CanBeSelected)
                {
                    DeselectCharacter(controlled == this);
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

        private void SelectCharacter(Character character, bool createNetworkEvent = true)
        {
            if (character == null) return;

            selectedCharacter = character;

           if (createNetworkEvent) new NetworkEvent(NetworkEventType.SelectCharacter, ID, true, selectedCharacter.ID);
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
                        new NetworkEvent(NetworkEventType.PickItem, ID, true,
                            new int[] 
                        { 
                            closestItem.ID, 
                            IsKeyHit(InputType.Select) ? 1 : 0, 
                            IsKeyHit(InputType.Use) ? 1 : 0 
                        });
                    }
                }

                if (IsKeyHit(InputType.Select))
                {
                    if (selectedCharacter != null)
                    {
                        DeselectCharacter(controlled == this);
                    }
                    else if (closestCharacter != null && closestCharacter.IsHumanoid && closestCharacter.CanBeSelected)
                    {
                        SelectCharacter(closestCharacter);
                    }
                }
            }
            else
            {
                if (selectedCharacter != null) DeselectCharacter(controlled==this);
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

            foreach (Character c in CharacterList)
            {
                c.Update(cam, deltaTime);
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

            if (controlled == this || !(this is AICharacter)) Control(deltaTime, cam);

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

            if (IsNetworkPlayer && info!=null)
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

            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            health = minHealth;

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
                if (p!=null) p.Size *= 2.0f;

                GameMain.ParticleManager.CreateParticle("bubbles",
                    ConvertUnits.ToDisplayUnits(centerOfMass) + Rand.Vector(5.0f),
                    new Vector2(Rand.Range(-50f, 50f), Rand.Range(-100f,50f)));
            }

            foreach (var joint in AnimController.limbJoints)
            {
                joint.LimitEnabled = false;
            }
            Kill(CauseOfDeath.Pressure, isNetworkMessage);
        }

        //private IEnumerable<object> DeathAnim(Camera cam)
        //{
        //    if (controlled != this) yield return CoroutineStatus.Success;

        //    Character.controlled = null;

        //    float dimDuration = 8.0f;
        //    float timer = 0.0f;

        //    Color prevAmbientLight = GameMain.LightManager.AmbientLight;
        //    Color darkLight = new Color(0.2f, 0.2f, 0.2f, 1.0f);

        //    while (timer < dimDuration && Character.controlled == null)
        //    {
        //        timer += CoroutineManager.UnscaledDeltaTime;

        //        if (cam != null) cam.OffsetAmount = 0.0f;

        //        cam.TargetPos = WorldPosition;

        //        GameMain.LightManager.AmbientLight = Color.Lerp(prevAmbientLight, darkLight, timer / dimDuration);
                
        //        yield return CoroutineStatus.Running;
        //    }
            
        //    float lerpLightBack = 0.0f;
        //    while (lerpLightBack < 1.0f)
        //    {
        //        lerpLightBack = Math.Min(lerpLightBack + CoroutineManager.UnscaledDeltaTime*5.0f, 1.0f);

        //        GameMain.LightManager.AmbientLight = Color.Lerp(darkLight, prevAmbientLight, lerpLightBack);
        //        yield return CoroutineStatus.Running;
        //    }

        //    cam.TargetPos = Vector2.Zero;

        //    yield return CoroutineStatus.Success;
        //}

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

                    new NetworkEvent(NetworkEventType.KillCharacter, ID, true, causeOfDeath);
                }
                //if it's an ai Character, only let the server kill it
                else if (GameMain.Server != null && this is AICharacter)
                {
                    new NetworkEvent(NetworkEventType.KillCharacter, ID, false, causeOfDeath);
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
                    message.Write(AnimController.Anim == AnimController.Animation.CPR);
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
                case NetworkEventType.ApplyStatusEffect:
                    message.Write((ushort)data);
                    return true;
                case NetworkEventType.ImportantEntityUpdate:

                    message.WriteRangedSingle(health, minHealth, maxHealth, 8);

                    //if (health > 0.0f)
                    //{
                    //    message.Write(Math.Max((byte)((health / maxHealth) * 255.0f), (byte)1));
                    //}
                    //else
                    //{
                    //    message.Write((byte)0);
                    //    message.WriteRangedInteger(0, Enum.GetValues(typeof(CauseOfDeath)).Length-1, (int)lastAttackCauseOfDeath);
                    //}

                    if (AnimController.StunTimer <= 0.0f && bleeding <= 0.0f && oxygen > 99.0f)
                    {
                        message.Write(true);
                    }
                    else
                    {
                        message.Write(false);

                        message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);

                        message.WriteRangedSingle(oxygen, -100.0f, 100.0f, 8);

                        bleeding = MathHelper.Clamp(bleeding, 0.0f, 5.0f);
                        message.WriteRangedSingle(bleeding, 0.0f, 5.0f, 8);
                    }

                    return true;
                case NetworkEventType.EntityUpdate:
                    message.Write(keys[(int)InputType.Use].GetHeldQueue);

                    bool secondaryHeld = keys[(int)InputType.Aim].GetHeldQueue;
                    message.Write(secondaryHeld);
                        
                    message.Write(keys[(int)InputType.Left].Held);
                    message.Write(keys[(int)InputType.Right].Held);

                    message.Write(keys[(int)InputType.Up].Held);
                    message.Write(keys[(int)InputType.Down].Held);

                    message.Write(keys[(int)InputType.Run].Held);

                    message.Write(((HumanoidAnimController)AnimController).Crouching);

                    
                    if (secondaryHeld)
                    {
                        if (Character.controlled==this)
                        {
                            ViewTarget = Lights.LightManager.ViewTarget == null ? this : Lights.LightManager.ViewTarget;
                        }
                        if (ViewTarget == null) ViewTarget = this;

                        Vector2 relativeCursorPosition = cursorPosition;
                        relativeCursorPosition -= ViewTarget.Position;

                        if (relativeCursorPosition.Length()>500.0f)
                        {
                            relativeCursorPosition = Vector2.Normalize(relativeCursorPosition) * 495.0f;
                        }

                        message.Write(ViewTarget.ID);

                        message.WriteRangedSingle(relativeCursorPosition.X, -500.0f, 500.0f, 8);
                        message.WriteRangedSingle(relativeCursorPosition.Y, -500.0f, 500.0f, 8);
                    }
                    else
                    {
                        message.Write(AnimController.Dir > 0.0f);
                    }

                    message.Write(Submarine != null);

                    //Vector2 position = Submarine == null ? SimPosition : SimPosition - Submarine.SimPosition;

                    //if ((AnimController.RefLimb.SimPosition - Submarine.Loaded.SimPosition).Length() > NetConfig.CharacterIgnoreDistance) return true;

                    message.Write(SimPosition.X);
                    message.Write(SimPosition.Y);

                    networkUpdateSent = true;

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
                    ushort itemId = message.ReadUInt16();

                    bool pickHit = message.ReadBoolean();
                    bool actionHit = message.ReadBoolean();

                    data = new int[] { (int)itemId, pickHit ? 1 : 0, actionHit ? 1: 0 };

                    System.Diagnostics.Debug.WriteLine("item id: "+itemId);

                    Item pickedItem = FindEntityByID(itemId) as Item;
                    if (pickedItem != null)
                    {
                        if (pickedItem == selectedConstruction)
                        {
                            GameServer.Log(Name + " deselected " + pickedItem.Name, Color.Orange);
                        }
                        else
                        {
                            GameServer.Log(Name + " selected " + pickedItem.Name, Color.Orange);
                        }
                        pickedItem.Pick(this, false, pickHit, actionHit);

                    }

                    return;
                case NetworkEventType.SelectCharacter:
                    bool performingCPR = message.ReadBoolean();

                    ushort characterId = message.ReadUInt16();
                    data = characterId;

                    if (characterId==0)
                    {
                        DeselectCharacter(false);
                        return;
                    }
                 
                    Character character = FindEntityByID(characterId) as Character;
                    if (character == null || !character.IsHumanoid) return;
                    
                    SelectCharacter(character, false);
                    if (performingCPR)
                    {
                        AnimController.Anim = AnimController.Animation.CPR;

                        foreach (Limb limb in selectedCharacter.AnimController.Limbs)
                        {
                            limb.pullJoint.Enabled = false;
                        }
                    }
                    else if (AnimController.Anim == AnimController.Animation.CPR)
                    {
                        AnimController.Anim = AnimController.Animation.None;
                    }
                    
                    return;
                case NetworkEventType.KillCharacter:
                    if (GameMain.Server != null)
                    {
                        Client sender = GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
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

                    if (causeOfDeath == CauseOfDeath.Pressure)
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
                case NetworkEventType.ApplyStatusEffect:
                    ushort id = message.ReadUInt16();

                    data = id;

                    var item = FindEntityByID(id) as Item;
                    if (item == null) return;

                    item.ApplyStatusEffects(ActionType.OnUse, 1.0f, this);

                    break;
                case NetworkEventType.ImportantEntityUpdate:

                    health = message.ReadRangedSingle(minHealth, 100.0f, 8);
                        
                    bool allOk = message.ReadBoolean();
                    if (allOk)
                    {
                        bleeding = 0.0f;
                        Oxygen = 100.0f;
                        AnimController.StunTimer = 0.0f;
                        return;
                    }

                    float newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                    StartStun(newStunTimer, true);

                    Oxygen = message.ReadRangedSingle(-100.0f,100.0f, 8);
                    Bleeding = message.ReadRangedSingle(0.0f, 5.0f, 8);     

                    return;
                case NetworkEventType.EntityUpdate:
                    Vector2 relativeCursorPos = Vector2.Zero;

                    bool actionKeyState, secondaryKeyState;
                    bool leftKeyState, rightKeyState, upKeyState, downKeyState;
                    bool runState, crouchState;

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
                        crouchState         = message.ReadBoolean();
                    }

                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Error in Character.ReadNetworkData: " + e.Message);
#endif
                        return;
                    }

                    if (GameMain.Server != null && (isDead || IsUnconscious)) return;

                    keys[(int)InputType.Use].Held = actionKeyState;
                    keys[(int)InputType.Use].SetState(false, actionKeyState);

                    keys[(int)InputType.Aim].Held = secondaryKeyState;
                    keys[(int)InputType.Aim].SetState(false, secondaryKeyState);

                    if (sendingTime <= LastNetworkUpdate) return;

                    keys[(int)InputType.Left].Held      = leftKeyState;
                    keys[(int)InputType.Right].Held     = rightKeyState;

                    keys[(int)InputType.Up].Held        = upKeyState;
                    keys[(int)InputType.Down].Held      = downKeyState;

                    keys[(int)InputType.Run].Held       = runState;

                    keys[(int)InputType.Crouch].Held    = crouchState;


                    float dir = 1.0f;
                    Vector2 pos = Vector2.Zero;

                    ushort viewTargetID = 0;
                    ViewTarget = null;

                    try
                    {
                        if (secondaryKeyState)
                        {
                            viewTargetID = message.ReadUInt16();

                            relativeCursorPos = new Vector2(
                                message.ReadRangedSingle(-500.0f, 500.0f, 8),
                                message.ReadRangedSingle(-500.0f, 500.0f, 8));
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

                    bool inSub = message.ReadBoolean();

                    pos.X = message.ReadFloat();
                    pos.Y = message.ReadFloat();

                    if (inSub != (Submarine != null))
                    {
                        AnimController.Teleport(pos - SimPosition, Vector2.Zero);
                    }

                    if (inSub)
                    {
                        //AnimController.FindHull(ConvertUnits.ToDisplayUnits(pos) - Submarine.Loaded.WorldPosition);

                        Hull newHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(pos), AnimController.CurrentHull, false);
                        if (newHull != null)
                        {
                            AnimController.CurrentHull = newHull;
                            Submarine = newHull.Submarine;
                        }
                    }
                    else
                    {
                        AnimController.CurrentHull = null;
                        Submarine = null;
                    }

                    if (secondaryKeyState)
                    {
                        cursorPosition = MathUtils.IsValid(relativeCursorPos) ? relativeCursorPos : Vector2.Zero;
                        ViewTarget = viewTargetID == 0 ? this : Entity.FindEntityByID(viewTargetID);
                        if (ViewTarget == null) ViewTarget = this;

                        cursorPosition += ViewTarget.Position;
                    }
                    else
                    {
                        cursorPosition = Position + new Vector2(1000.0f, 0.0f) * dir;

                        AnimController.TargetDir = dir < 0 ? Direction.Left : Direction.Right;
                    }

                    AnimController.RefLimb.body.TargetPosition = 
                        AnimController.EstimateCurrPosition(pos, (float)(NetTime.Now) - sendingTime);

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

            if (info != null) info.Remove();

            CharacterList.Remove(this);

            if (controlled == this) controlled = null;

            if (GameMain.Client != null && GameMain.Client.Character == this) GameMain.Client.Character = null;

            if (aiTarget != null) aiTarget.Remove();

            if (AnimController!=null) AnimController.Remove();
        }

    }
}
