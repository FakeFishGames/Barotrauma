using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using FarseerPhysics.Dynamics;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerSerializable
    {
        public static List<Character> CharacterList = new List<Character>();

        public static bool DisableControls;

        private bool enabled = true;
        public bool Enabled
        {
            get
            {
                return enabled && !Removed;
            }
            set
            {
                if (value == enabled) return;

                if (Removed)
                {
                    enabled = false;
                    return;
                }

                enabled = value;

                foreach (Limb limb in AnimController.Limbs)
                {
                    if (limb.body != null)
                    {
                        limb.body.Enabled = enabled;
                    }
#if CLIENT
                    if (limb.LightSource != null)
                    {
                        limb.LightSource.Enabled = enabled;
                    }
#endif
                }
                AnimController.Collider.Enabled = value;
            }
        }

        public Hull PreviousHull = null;
        public Hull CurrentHull = null;

        public bool IsRemotePlayer;

        private CharacterInventory inventory;

        protected float lastRecvPositionUpdateTime;

        public readonly Dictionary<string, SerializableProperty> Properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get { return Properties; }
        }

        protected Key[] keys;

        private Item selectedConstruction;
        private Item[] selectedItems;

        private byte teamID;
        public byte TeamID
        {
            get { return teamID; }
            set
            {
                teamID = value;
                if (info != null) info.TeamID = value;
            }
        }

        public AnimController AnimController;

        private Vector2 cursorPosition;

        protected bool needsAir;
        protected float oxygen, oxygenAvailable;
        public float lastSentOxygen;

        private float health;
        //public float lastSentHealth;
        protected float minHealth, maxHealth;

        protected Item focusedItem;
        private Character focusedCharacter, selectedCharacter, selectedBy;

        private bool isDead;
        private CauseOfDeath lastAttackCauseOfDeath;
        private CauseOfDeath causeOfDeath;

        public readonly bool IsHumanoid;

        //the name of the species (e.q. human)
        public readonly string SpeciesName;

        private float bleeding;
        //public float lastSentBleeding;

        private float attackCoolDown;

        public Entity ViewTarget
        {
            get;
            set;
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
        //Only used by server logs to determine "true identity" of the player for cases when they're disguised
        public string LogName
        {
            get
            {
                return info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name + (info.DisplayName != info.Name ? " (as " + info.DisplayName + ")" : "") : SpeciesName;
            }
        }

        private float hideFaceTimer;
        public bool HideFace
        {
            get
            {
                return hideFaceTimer > 0.0f;
            }
            set
            {
                hideFaceTimer = MathHelper.Clamp(hideFaceTimer + (value ? 1.0f : -0.5f), 0.0f, 10.0f);
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

        public bool AllowInput
        {
            get
            {
                //Clients should not be recording FrozenCharacters
                if (GameMain.Client == null)
                {
                    return (!IsUnconscious && Stun <= 0.0f && !isDead && GameMain.NilMod.FrozenCharacters.Find(c => c == this) == null) || (GameMain.NilMod.DisconnectedCharacters.Find(dc => dc.character == this) != null && !isDead && !IsUnconscious);
                }
                else
                {
                    return (!IsUnconscious && Stun <= 0.0f && !isDead);
                }
            }
        }

        public bool CanInteract
        {
            get { return AllowInput && IsHumanoid && !LockHands; }
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

        public Character FocusedCharacter
        {
            get { return focusedCharacter; }
        }

        public Character SelectedCharacter
        {
            get { return selectedCharacter; }
            set
            {
                if (value == selectedCharacter) return;
                if (selectedCharacter != null)
                    selectedCharacter.selectedBy = null;
                selectedCharacter = value;
                if (selectedCharacter != null)
                    selectedCharacter.selectedBy = this;
            }
        }

        public Character SelectedBy
        {
            get { return selectedBy; }
            set
            {
                if (selectedBy != null)
                    selectedBy.selectedCharacter = null;
                selectedBy = value;
                if (selectedBy != null)
                    selectedBy.selectedCharacter = this;
            }
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

        public bool IsRagdolled;
        public bool IsForceRagdolled;

        public bool IsUnconscious
        {
            get { return (needsAir && Oxygen <= 0.0f) || Health <= 0.0f; }
        }

        public bool NeedsAir
        {
            get { return needsAir; }
            set { needsAir = value; }
        }

        public float Oxygen
        {
            get
            {
                return oxygen;
            }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                float newoxygen = CheckOxygen(value);

                if (oxygen == newoxygen) return;
                oxygen = newoxygen;

                /*
                if (GameMain.Server != null)
                {
                    if (Math.Abs(oxygen - lastSentOxygen) > (100f - -100f) / 255.0f || Math.Sign(oxygen) != Math.Sign(lastSentOxygen))
                    {
                        GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                        lastSentOxygen = oxygen;
                    }
                }
                */

            }
        }

        public float CheckOxygen(float newoxygen)
        {
            //Nilmod Prevent oxygen gains during progressive implode death
            if (PressureTimer >= 100.0f)
            {
                if (GameMain.NilMod.UseProgressiveImplodeDeath && GameMain.NilMod.PreventImplodeOxygen)
                {
                    if (newoxygen > Oxygen)
                    {
                        newoxygen = Oxygen;
                    }
                }
            }

            oxygen = MathHelper.Clamp(newoxygen, -100.0f, 100.0f);

            //NILMOD: Deny Player Death suffocation code
            if (!GameMain.NilMod.PlayerCanSuffocateDeath && (GameMain.Server != null ? GameMain.Server.TraitorsEnabled == YesNoMaybe.No : true))
            {
                if (!IsRemotePlayer)
                {
                    if (controlled != this)
                    {
                        if (oxygen == -100.0f)
                        {
                            float pressureFactor = (AnimController.CurrentHull == null) ?
                            100.0f : Math.Min(AnimController.CurrentHull.LethalPressure, 100.0f);
                            if (PressureProtection > 0.0f && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null))) pressureFactor = 0.0f;

                            Kill(pressureFactor == 100f ? CauseOfDeath.Pressure : (AnimController.InWater ? CauseOfDeath.Drowning : CauseOfDeath.Suffocation));
                        }
                    }
                }
            }
            else
            {
                if (oxygen == -100.0f)
                {
                    float pressureFactor = (AnimController.CurrentHull == null) ?
                            100.0f : Math.Min(AnimController.CurrentHull.LethalPressure, 100.0f);
                    if (PressureProtection > 0.0f && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null))) pressureFactor = 0.0f;

                    Kill(pressureFactor == 100f ? CauseOfDeath.Pressure : (AnimController.InWater ? CauseOfDeath.Drowning : CauseOfDeath.Suffocation));
                }
            }

            return oxygen;
        }

        public float OxygenAvailable
        {
            get { return oxygenAvailable; }
            set { oxygenAvailable = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public const float MaxStun = 60.0f;

        private float stunTimer;
        public float Stun
        {
            get { return IsRagdolled ? 1.0f : stunTimer; }
            set
            {
                if (GameMain.Client != null) return;

                SetStun(value, true);
            }
        }

        private float stunresistance;

        //NilMod Stun Additions
        public float Stunresistance
        {
            get { return stunresistance; }
            set
            {
                if (GameMain.Client != null) return;

                if (value <= stunresistbase)
                {
                    stunresistance = stunresistbase;
                }
                else
                {
                    stunresistance = MathHelper.Clamp(value,0f,1f);
                }
            }
        }
        //Default to 0% resistance
        public float stunresistbase = 0f;
        //Lose 1% resistance / second
        public float stunresistdecreasespeed = 0.01f;
        //% based on the new inflicted stun of the max stun timer converted into stunresistance additively
        public float stunresistincreasemult = 0f;

        public float stunresistendmult = 1f;

        //Calculates the new health with multipliers
        public float CalculateMultiplierHealth(float healthcurrent, float healthdifference)
        {
            //Code for resistance multipliers
            float healthcalculation;

            if (!IsRemotePlayer)
            {
                //This is an AI of some form / creature etc. Give them creature health multipliers
                if (controlled != this)
                {
                    healthcalculation = ((healthcurrent * GameMain.NilMod.CreatureHealthMultiplier) + healthdifference) / GameMain.NilMod.CreatureHealthMultiplier;
                }
                //This creature/player is the host controlled, give them the Player Health Mult
                else
                {
                    //Base Player Health Calculation
                    healthcalculation = ((healthcurrent * GameMain.NilMod.PlayerHealthMultiplier) + healthdifference) / GameMain.NilMod.PlayerHealthMultiplier;

                    //Give them the Husk health multipliers instead if infected
                    if (huskInfection != null)
                    {

                        //Husk infection is maxed out, use husk multipliers
                        if (huskInfection.State == HuskInfection.InfectionState.Active)
                        {
                            //Is trying to heal
                            if (healthdifference > 0)
                            {
                                healthcalculation = healthcurrent + (healthdifference / (GameMain.NilMod.PlayerHuskHealthMultiplier / GameMain.NilMod.HuskHealingMultiplierincurable));
                            }
                            //Is taking damage
                            else
                            {
                                healthcalculation = healthcurrent + (healthdifference / GameMain.NilMod.PlayerHuskHealthMultiplier);
                            }
                        }
                        //Is only infected with husk atm, use player values
                        else
                        {
                            //Is trying to heal
                            if (healthdifference > 0)
                            {
                                healthcalculation = healthcurrent + (healthdifference / (GameMain.NilMod.PlayerHealthMultiplier * GameMain.NilMod.HuskHealingMultiplierinfected));
                            }
                            //Is taking damage
                            else
                            {
                                healthcalculation = healthcurrent + (healthdifference / GameMain.NilMod.PlayerHealthMultiplier);
                            }
                        }
                    }
                }
            }
            //This is a remote player, give them the Player Health Mult
            else
            {
                //Base Player Health Calculation
                healthcalculation = ((healthcurrent * GameMain.NilMod.PlayerHealthMultiplier) + healthdifference) / GameMain.NilMod.PlayerHealthMultiplier;

                //Give them the Husk health multipliers instead if infected
                if (huskInfection != null)
                {

                    //Husk infection is maxed out, use husk multipliers
                    if (huskInfection.State == HuskInfection.InfectionState.Active)
                    {
                        //Is trying to heal
                        if (healthdifference > 0)
                        {
                            // 80 + (20 / (5 / 0.2)) 
                            healthcalculation = healthcurrent + (healthdifference / (GameMain.NilMod.PlayerHuskHealthMultiplier / GameMain.NilMod.HuskHealingMultiplierincurable));
                        }
                        //Is taking damage
                        else
                        {
                            healthcalculation = healthcurrent + (healthdifference / GameMain.NilMod.PlayerHuskHealthMultiplier);
                        }
                    }
                    //Is only infected with husk atm, use player values
                    else
                    {
                        //Is trying to heal
                        if (healthdifference > 0)
                        {
                            healthcalculation = healthcurrent + (healthdifference / (GameMain.NilMod.PlayerHealthMultiplier * GameMain.NilMod.HuskHealingMultiplierinfected));
                        }
                        //Is taking damage
                        else
                        {
                            healthcalculation = healthcurrent + (healthdifference / GameMain.NilMod.PlayerHealthMultiplier);
                        }
                    }
                }
            }
            return healthcalculation;
        }

        public float Health
        {
            get
            {
                return health;
            }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                if (GameMain.Client != null) return;

                //float newHealth = MathHelper.Clamp(value, minHealth, maxHealth);

                float newHealth = value;

                newHealth = CheckHealth(newHealth);

                if (health == newHealth) return;
                health = MathHelper.Clamp(newHealth, minHealth, maxHealth);

                /*
                if (GameMain.Server != null)
                {
                    if (Math.Abs(health - lastSentHealth) > (maxHealth - minHealth) / 255.0f || Math.Sign(health) != Math.Sign(lastSentHealth))
                    {
                        GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                        lastSentHealth = health;
                    }
                }
                */
            }
        }

        public float CheckHealth(float newHealth)
        {
            //if (newHealth == health) return;

            //Take Multipliers of the character into account then Clamp health again in case it went with weird values
            if (GameMain.Client == null)
            {
                newHealth = CalculateMultiplierHealth(Health, newHealth - Health);


                //Nilmod Check and autokill husk infected on any health update if at 0 health
                if (huskInfection != null)
                {
                    if (huskInfection.State == HuskInfection.InfectionState.Active)
                    {
                        if (newHealth <= 0)
                        {
                            //Instantly kill the player if they are incapacitated as a husk instead of going to negative health
                            Kill(CauseOfDeath.Husk, true);
                        }
                    }
                }
            }

            //Now return it and cancel all the above IF its simply unchanged
            if (newHealth == Health) return newHealth;


            //NilMod Progressive Implosion Death Anti Healing (Allow health loss but not health gains during pressure-death)
            if (PressureTimer >= 100.0f && GameMain.Client == null)
            {
                if (GameMain.NilMod.UseProgressiveImplodeDeath && GameMain.NilMod.PreventImplodeHealing)
                {
                    if (newHealth > Health)
                    {
                        newHealth = Health;
                    }
                }
            }

            //NilMod Death code (Help from pressure-deaths)
            if (newHealth > 0 && Health < 0)
            {
                if (AnimController.LimbJoints != null && AnimController.Limbs != null)
                {
                    foreach (LimbJoint joint in AnimController.LimbJoints)
                    {
                        joint.MotorEnabled = true;
                        joint.Enabled = true;
                        joint.IsSevered = false;
                    }

                    foreach (Limb limb in AnimController.Limbs)
                    {
                        limb.IsSevered = false;
                    }

                    PressureProtection = 0.0f;
                }
            }

            //Nilmod Deny healing of the infected
            if (huskInfection != null && GameMain.Client == null)
            {
                if (huskInfection.State == HuskInfection.InfectionState.Active)
                {
                    if (newHealth > Health)
                    {
                        newHealth = Health;
                    }
                }
            }

            return newHealth;
        }
    
        public float MaxHealth
        {
            get { return maxHealth; }
        }

        public float MinHealth
        {
            get { return minHealth; }
        }

        public float Bleeding
        {
            get
            {
                return bleeding;
            }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                if (GameMain.Client != null) return;
                if (!DoesBleed) return;

                float newBleeding = MathHelper.Clamp(value, 0.0f, 5.0f);
                if (newBleeding == Bleeding) return;


                newBleeding = CheckBleeding(newBleeding);

                if (newBleeding == bleeding) return;
                bleeding = newBleeding;
                
                /*if (GameMain.Server != null)
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });*/
            }
        }

        public float CheckBleeding(float newBleed)
        {
            if (GameMain.Client == null)
            {
                //NilMod Progressive Implosion Death Anti Healing (Allow Bleeding gain but not bleed reduction during pressure-death)
                if (PressureTimer >= 100.0f && GameMain.NilMod.UseProgressiveImplodeDeath && GameMain.NilMod.PreventImplodeClotting && newBleed < Bleeding) return Bleeding;


                //Get difference and factor in bleed multiplier instead for adding bleeding, then reclamp the value in case
                newBleed = MathHelper.Clamp(((Bleeding * GameMain.NilMod.CreatureBleedMultiplier) + (newBleed - Bleeding)) / GameMain.NilMod.CreatureBleedMultiplier, 0.0f, 5.0f);

                if (newBleed * GameMain.NilMod.CreatureBleedMultiplier <= 0.0001f) newBleed = 0f;
            }

            return newBleed;
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
                    if (huskInfection != null)
                    {
                        GameServer.Log(Name + " has been cured of the husk infection.", Networking.ServerLog.MessageType.Husk);
                        huskInfection.Remove(this);
                        huskInfection = null;
                    }
                }
                else
                {
                    if (huskInfection == null)
                    {
                        huskInfection = new HuskInfection(this);
                        //NilMod Log husk Infection starts
                        GameServer.Log(Name + " has been husk infected!", Networking.ServerLog.MessageType.Husk);
                    }
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

        public bool UseBloodParticles
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

        public Item FocusedItem
        {
            get { return focusedItem; }
        }

        public Item PickingItem
        {
            get;
            set;
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
                return isDead || Stun > 0.0f || LockHands || IsUnconscious || Removed;
            }
        }

        public override Vector2 SimPosition
        {
            get { return AnimController.Collider.SimPosition; }
        }

        public override Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(SimPosition); }
        }

        public override Vector2 DrawPosition
        {
            get { return AnimController.MainLimb.body.DrawPosition; }
        }

        public delegate void OnDeathHandler(Character character, CauseOfDeath causeOfDeath);
        public OnDeathHandler OnDeath;
        
        public static Character Create(CharacterInfo characterInfo, Vector2 position, bool isRemotePlayer = false, bool hasAi=true)
        {
            return Create(characterInfo.File, position, characterInfo, isRemotePlayer, hasAi);
        }

        public static Character Create(string file, Vector2 position, CharacterInfo characterInfo = null, bool isRemotePlayer = false, bool hasAi=true, bool createNetworkEvent = true)
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
                    DebugConsole.ThrowError("Spawning a character failed - file \""+file+"\" not found!");
                    return null;
                }
            }
#else
            if (!System.IO.File.Exists(file))
            {
                DebugConsole.ThrowError("Spawning a character failed - file \""+file+"\" not found!");
                return null;
            }
#endif

            Character newCharacter = null;

            if (file != humanConfigFile)
            {
                var aiCharacter = new AICharacter(file, position, characterInfo, isRemotePlayer);
                var ai = new EnemyAIController(aiCharacter, file);
                aiCharacter.SetAI(ai);

                aiCharacter.minHealth = 0.0f;
                
                newCharacter = aiCharacter;
            }
            else if (hasAi)
            {
                var aiCharacter = new AICharacter(file, position, characterInfo, isRemotePlayer);
                var ai = new HumanAIController(aiCharacter);
                aiCharacter.SetAI(ai);

                aiCharacter.minHealth = -100.0f;

                newCharacter = aiCharacter;
            }
            else
            {
                newCharacter = new Character(file, position, characterInfo, isRemotePlayer);
                newCharacter.minHealth = -100.0f;
            }

            if (GameMain.Server != null && Spawner != null && createNetworkEvent)
            {
                Spawner.CreateNetworkEvent(newCharacter, false);
            }

            return newCharacter;
        }

        protected Character(string file, Vector2 position, CharacterInfo characterInfo = null, bool isRemotePlayer = false)
            : base(null)
        {
            ConfigPath = file;
            
            selectedItems = new Item[2];

            IsRemotePlayer = isRemotePlayer;

            oxygen = 100.0f;
            oxygenAvailable = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

            Properties = SerializableProperty.GetProperties(this);

            Info = characterInfo;
            if (file == humanConfigFile && characterInfo == null)
            {
                Info = new CharacterInfo(file);
            }

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            InitProjSpecific(doc);

            SpeciesName = doc.Root.GetAttributeString("name", "Unknown");
            
            IsHumanoid = doc.Root.GetAttributeBool("humanoid", false);
            
            if (IsHumanoid)
            {
                AnimController = new HumanoidAnimController(this, doc.Root.Element("ragdoll"));
                AnimController.TargetDir = Direction.Right;
                inventory = new CharacterInventory(17, this);
            }
            else
            {
                AnimController = new FishAnimController(this, doc.Root.Element("ragdoll"));
                PressureProtection = 100.0f;
            }

            AnimController.SetPosition(ConvertUnits.ToSimUnits(position));
            
            maxHealth = doc.Root.GetAttributeFloat("health", 100.0f);
            health = maxHealth;

            DoesBleed = doc.Root.GetAttributeBool("doesbleed", true);
            UseBloodParticles = doc.Root.GetAttributeBool("usebloodparticles", true);
            BleedingDecreaseSpeed = doc.Root.GetAttributeFloat("bleedingdecreasespeed", 0.05f);

            needsAir = doc.Root.GetAttributeBool("needsair", false);

            //Nilmod stun resistance
            stunresistbase = MathHelper.Clamp(doc.Root.GetAttributeFloat("stunresistbase", 0.0f),0f,1f);
            stunresistincreasemult = MathHelper.Clamp(doc.Root.GetAttributeFloat("stunresistincreasemult", 0.0f), 0f, 1f);
            stunresistdecreasespeed = MathHelper.Clamp(doc.Root.GetAttributeFloat("stunresistdecreasespeed", 0.01f), 0f, 1f);
            stunresistendmult = MathHelper.Clamp(doc.Root.GetAttributeFloat("stunresistendmult", 1.0f), 0f, 1f);

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

                        item.TryInteract(this, true, true, true);
                        inventory.TryPutItem(item, i, false, null, false);
                    }
                }
            }


            AnimController.FindHull(null);
            if (AnimController.CurrentHull != null) Submarine = AnimController.CurrentHull.Submarine;

            CharacterList.Add(this);

            //characters start disabled in the multiplayer mode, and are enabled if/when
            //  - controlled by the player
            //  - client receives a position update from the server
            //  - server receives an input message from the client controlling the character
            //  - if an AICharacter, the server enables it when close enough to any of the players
            Enabled = GameMain.NetworkMember == null;
        }
        partial void InitProjSpecific(XDocument doc);

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
                        DebugConsole.ThrowError("(The config file must end with \"human.xml\")");
                        return "";
                    }
                }
                return humanConfigFile; 
            }
        }

        public bool IsKeyHit(InputType inputType)
        {
            if (GameMain.Server != null && Character.Controlled != this)
            {
                switch (inputType)
                {
                    case InputType.Left:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Left)) && (prevDequeuedInput.HasFlag(InputNetFlags.Left));
                    case InputType.Right:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Right)) && (prevDequeuedInput.HasFlag(InputNetFlags.Right));
                    case InputType.Up:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Up)) && (prevDequeuedInput.HasFlag(InputNetFlags.Up));
                    case InputType.Down:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Down)) && (prevDequeuedInput.HasFlag(InputNetFlags.Down));
                    case InputType.Run:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Run)) && (prevDequeuedInput.HasFlag(InputNetFlags.Run));
                    case InputType.Crouch:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Crouch)) && (prevDequeuedInput.HasFlag(InputNetFlags.Crouch));
                    case InputType.Select:
                        return dequeuedInput.HasFlag(InputNetFlags.Select); //TODO: clean up the way this input is registered                                                                           
                    case InputType.Use:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Use)) && (prevDequeuedInput.HasFlag(InputNetFlags.Use));
                    case InputType.Ragdoll:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Ragdoll)) && (prevDequeuedInput.HasFlag(InputNetFlags.Ragdoll));
                    default:
                        return false;
                }
            }

            return keys[(int)inputType].Hit;
        }

        public bool IsKeyDown(InputType inputType)
        {
            if (GameMain.Server != null && Character.Controlled != this)
            {
                switch (inputType)
                {
                    case InputType.Left:
                        return dequeuedInput.HasFlag(InputNetFlags.Left);
                    case InputType.Right:
                        return dequeuedInput.HasFlag(InputNetFlags.Right);
                    case InputType.Up:
                        return dequeuedInput.HasFlag(InputNetFlags.Up);                        
                    case InputType.Down:
                        return dequeuedInput.HasFlag(InputNetFlags.Down);
                    case InputType.Run:
                        return dequeuedInput.HasFlag(InputNetFlags.Run);
                    case InputType.Crouch:
                        return dequeuedInput.HasFlag(InputNetFlags.Crouch);
                    case InputType.Select:
                        return false; //TODO: clean up the way this input is registered
                    case InputType.Aim:
                        return dequeuedInput.HasFlag(InputNetFlags.Aim);
                    case InputType.Use:
                        return dequeuedInput.HasFlag(InputNetFlags.Use);
                    case InputType.Attack:
                        return dequeuedInput.HasFlag(InputNetFlags.Attack);
                    case InputType.Ragdoll:
                        return dequeuedInput.HasFlag(InputNetFlags.Ragdoll);
                }
                return false;
            }
            
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
            if (keys == null) return;
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
            //NilMod Host Skill code changes
            if (controlled == this && GameMain.NilMod.HostBypassSkills)
            {
                //return (Info == null || Info.Job == null) ? 0 : Info.Job.GetSkillLevel(skillName);
                return 100;
            }
            else
            {
                return (Info == null || Info.Job == null) ? 0 : Info.Job.GetSkillLevel(skillName);
            }
        }

        float findFocusedTimer;

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

            if (IsKeyDown(InputType.Run))
            {
                //can't run if
                //  - dragging someone
                //  - crouching
                //  - moving backwards
                if (SelectedCharacter == null &&
                    (!(AnimController is HumanoidAnimController) || !((HumanoidAnimController)AnimController).Crouching) &&
                    Math.Sign(targetMovement.X) != -Math.Sign(AnimController.Dir))
                {
                    targetMovement *= AnimController.InWater ? AnimController.SwimSpeedMultiplier : AnimController.RunSpeedMultiplier;
                }
            }


            targetMovement *= SpeedMultiplier;
            SpeedMultiplier = 1.0f;

            return targetMovement;
        }

        public void Control(float deltaTime, Camera cam)
        {
            ViewTarget = null;
            if (!AllowInput) return;

            if (!(this is AICharacter) || controlled == this || IsRemotePlayer)
            {
                Vector2 targetMovement = GetTargetMovement();

                AnimController.TargetMovement = targetMovement;
                AnimController.IgnorePlatforms = AnimController.TargetMovement.Y < 0.0f;
            }

            if (AnimController is HumanoidAnimController)
            {
                ((HumanoidAnimController) AnimController).Crouching = IsKeyDown(InputType.Crouch);
            }

            if (AnimController.onGround &&
                !AnimController.InWater &&
                AnimController.Anim != AnimController.Animation.UsingConstruction &&
                AnimController.Anim != AnimController.Animation.CPR)
            {
                //Limb head = AnimController.GetLimb(LimbType.Head);

                if (cursorPosition.X < AnimController.Collider.Position.X - 10.0f)
                {
                    AnimController.TargetDir = Direction.Left;
                }
                else if (cursorPosition.X > AnimController.Collider.Position.X + 10.0f)
                {
                    AnimController.TargetDir = Direction.Right;
                }
            }

            if (GameMain.Server != null && Character.Controlled != this)
            {
                if (dequeuedInput.HasFlag(InputNetFlags.FacingLeft))
                {
                    AnimController.TargetDir = Direction.Left;
                }
                else
                {
                    AnimController.TargetDir = Direction.Right;
                }
            }
            else if (GameMain.Client != null && Character.controlled != this)
            {
                if (memState.Count > 0)
                {
                    AnimController.TargetDir = memState[0].Direction;
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
                        attackPos = Submarine.LastPickedPosition;

                        if (body.UserData is Submarine)
                        {
                            var sub = ((Submarine)body.UserData);

                            body = Submarine.PickBody(
                                attackLimb.SimPosition - ((Submarine)body.UserData).SimPosition,
                                attackPos - ((Submarine)body.UserData).SimPosition,
                                AnimController.Limbs.Select(l => l.body.FarseerBody).ToList(),
                                Physics.CollisionWall);

                            if (body != null)
                            {
                                attackPos = Submarine.LastPickedPosition + sub.SimPosition;
                                attackTarget = body.UserData as IDamageable;
                            }
                        }
                        else
                        {
                            if (body.UserData is IDamageable)
                            {
                                attackTarget = (IDamageable)body.UserData;
                            }
                            else if (body.UserData is Limb)
                            {
                                attackTarget = ((Limb)body.UserData).character;                            
                            }                            
                        }
                    }

                    attackLimb.UpdateAttack(deltaTime, attackPos, attackTarget);

                    if (attackLimb.AttackTimer > attackLimb.attack.Duration)
                    {
                        attackLimb.AttackTimer = 0.0f;
                        attackCoolDown = 1.0f;
                    }
                }
            }

            if (SelectedConstruction == null || !SelectedConstruction.Prefab.DisableItemUsageWhenSelected)
            {
                for (int i = 0; i < selectedItems.Length; i++ )
                {
                    if (selectedItems[i] == null) continue;
                    if (i == 1 && selectedItems[0] == selectedItems[1]) continue;

                    if (IsKeyDown(InputType.Use)) selectedItems[i].Use(deltaTime, this);
                    if (IsKeyDown(InputType.Aim) && selectedItems[i] != null) selectedItems[i].SecondaryUse(deltaTime, this);                
                }
            }

            if (selectedConstruction != null)
            {
                if (IsKeyDown(InputType.Use)) selectedConstruction.Use(deltaTime, this);
                if (selectedConstruction != null && IsKeyDown(InputType.Aim)) selectedConstruction.SecondaryUse(deltaTime, this);
            }

            if (SelectedCharacter != null)
            {
                if (Vector2.DistanceSquared(SelectedCharacter.WorldPosition, WorldPosition) > 90000.0f || !SelectedCharacter.CanBeSelected)
                {
                    DeselectCharacter();
                }
            }

            
            if (IsRemotePlayer && keys!=null)
            {
                foreach (Key key in keys)
                {
                    key.ResetHit();
                }
            }
        }

        public bool CanSeeCharacter(Character character)
        {
            Limb selfLimb = AnimController.GetLimb(LimbType.Head);
            if (selfLimb == null) selfLimb = AnimController.GetLimb(LimbType.Torso);
            if (selfLimb == null) selfLimb = AnimController.Limbs[0];

            Limb targetLimb = character.AnimController.GetLimb(LimbType.Head);
            if (targetLimb == null) targetLimb = character.AnimController.GetLimb(LimbType.Torso);
            if (targetLimb == null) targetLimb = character.AnimController.Limbs[0];

            if (selfLimb != null && targetLimb != null)
            {
                Vector2 diff = ConvertUnits.ToSimUnits(targetLimb.WorldPosition - selfLimb.WorldPosition);
                
                Body closestBody = null;
                //both inside the same sub (or both outside)
                //OR the we're inside, the other character outside
                if (character.Submarine == Submarine || character.Submarine == null)
                {
                    closestBody = Submarine.CheckVisibility(selfLimb.SimPosition, selfLimb.SimPosition + diff);
                    if (closestBody == null) return true;
                }
                //we're outside, the other character inside
                else if (Submarine == null)
                {
                    closestBody = Submarine.CheckVisibility(targetLimb.SimPosition, targetLimb.SimPosition - diff);
                    if (closestBody == null) return true;
                }
                //both inside different subs
                else
                {
                    closestBody = Submarine.CheckVisibility(selfLimb.SimPosition, selfLimb.SimPosition + diff);
                    if (closestBody != null && closestBody.UserData is Structure)
                    {
                        if (((Structure)closestBody.UserData).CastShadow) return false;
                    }
                    closestBody = Submarine.CheckVisibility(targetLimb.SimPosition, targetLimb.SimPosition - diff);
                    if (closestBody == null) return true;

                }
                
                Structure wall = closestBody.UserData as Structure;
                return wall == null || !wall.CastShadow;
            }
            else
            {
                return false;
            }
        }
        
        public bool HasEquippedItem(Item item)
        {
            return !inventory.IsInLimbSlot(item, InvSlotType.Any);
        }

        public bool HasEquippedItem(string itemName)
        {
            for (int i = 0; i < inventory.Items.Length; i++)
            {
                if (CharacterInventory.limbSlots[i] == InvSlotType.Any || inventory.Items[i] == null) continue;
                if (inventory.Items[i].Prefab.NameMatches(itemName) || inventory.Items[i].HasTag(itemName)) return true;
            }

            return false;
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
            if (!CanInteract) return false;

            //the inventory belongs to some other character
            if (inventory.Owner is Character && inventory.Owner != this)
            {
                var owner = (Character)inventory.Owner;

                //can only be accessed if the character is incapacitated and has been selected
                return SelectedCharacter == owner && (!owner.CanInteract);
            }

            if (inventory.Owner is Item)
            {
                var owner = (Item)inventory.Owner;
                if (!CanInteractWith(owner))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanInteractWith(Character c, float maxDist = 200.0f)
        {
            if (c == this || !c.Enabled || !c.IsHumanoid || !c.CanBeSelected) return false;

            maxDist = ConvertUnits.ToSimUnits(maxDist);
            if (Vector2.DistanceSquared(SimPosition, c.SimPosition) > maxDist * maxDist) return false;

            return true;
        }
        
        public bool CanInteractWith(Item item)
        {
            float distanceToItem;
            return CanInteractWith(item, out distanceToItem);
        }

        public bool CanInteractWith(Item item, out float distanceToItem)
        {
            distanceToItem = -1.0f;

            if (!CanInteract) return false;

            if (item.ParentInventory != null)
            {
                return CanAccessInventory(item.ParentInventory);
            }

            Wire wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                //locked wires are never interactable
                if (wire.Locked) return false;

                //wires are interactable if the character has selected either of the items the wire is connected to
                if (wire.Connections[0]?.Item != null && selectedConstruction == wire.Connections[0].Item) return true;
                if (wire.Connections[1]?.Item != null && selectedConstruction == wire.Connections[1].Item) return true;
            }

            if (item.InteractDistance == 0.0f && !item.Prefab.Triggers.Any()) return false;

            Pickable pickableComponent = item.GetComponent<Pickable>();
            if (pickableComponent != null && (pickableComponent.Picker != null && !pickableComponent.Picker.IsDead)) return false;
                        
            Vector2 characterDirection = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(AnimController.Collider.Rotation));

            Vector2 upperBodyPosition = Position + (characterDirection * 20.0f);
            Vector2 lowerBodyPosition = Position - (characterDirection * 60.0f);

            if (Submarine != null)
            {
                upperBodyPosition += Submarine.Position;
                lowerBodyPosition += Submarine.Position;
            }

            bool insideTrigger = item.IsInsideTrigger(upperBodyPosition) || item.IsInsideTrigger(lowerBodyPosition);
            if (item.Prefab.Triggers.Count > 0 && !insideTrigger) return false;

            Rectangle itemDisplayRect = new Rectangle(item.InteractionRect.X, item.InteractionRect.Y - item.InteractionRect.Height, item.InteractionRect.Width, item.InteractionRect.Height);

            // Get the point along the line between lowerBodyPosition and upperBodyPosition which is closest to the center of itemDisplayRect
            Vector2 playerDistanceCheckPosition = Vector2.Clamp(itemDisplayRect.Center.ToVector2(), lowerBodyPosition, upperBodyPosition);

            // Here we get the point on the itemDisplayRect which is closest to playerDistanceCheckPosition
            Vector2 rectIntersectionPoint = new Vector2(
                MathHelper.Clamp(playerDistanceCheckPosition.X, itemDisplayRect.X, itemDisplayRect.Right),
                MathHelper.Clamp(playerDistanceCheckPosition.Y, itemDisplayRect.Y, itemDisplayRect.Bottom));

            // If playerDistanceCheckPosition is inside the itemDisplayRect then we consider the character to within 0 distance of the item
            if (!itemDisplayRect.Contains(playerDistanceCheckPosition))
            {
                distanceToItem = Vector2.Distance(rectIntersectionPoint, playerDistanceCheckPosition);
            }

            if (distanceToItem > item.InteractDistance && item.InteractDistance > 0.0f) return false;

            if (!item.Prefab.InteractThroughWalls && Screen.Selected != GameMain.SubEditorScreen && !insideTrigger)
            {
                Vector2 itemPosition = item.SimPosition;
                if (Submarine == null && item.Submarine != null)
                {
                    //character is outside, item inside
                    itemPosition += item.Submarine.SimPosition;
                }
                else if (Submarine != null && item.Submarine == null)
                {
                    //character is inside, item outside
                    itemPosition -= Submarine.SimPosition;
                }
                else if (Submarine != item.Submarine)
                {
                    //character and the item are inside different subs
                    itemPosition += item.Submarine.SimPosition;
                    itemPosition -= Submarine.SimPosition;
                }
                var body = Submarine.CheckVisibility(SimPosition, itemPosition, true);
                if (body != null && body.UserData as Item != item) return false;
            }

            return true;
        }

        /// <summary>
        ///   Finds the front (lowest depth) interactable item at a position. "Interactable" in this case means that the character can "reach" the item.
        /// </summary>
        /// <param name="character">The Character who is looking for the interactable item, only items that are close enough to this character are returned</param>
        /// <param name="simPosition">The item at the simPosition, with the lowest depth, is returned</param>
        /// <param name="allowFindingNearestItem">If this is true and an item cannot be found at simPosition then a nearest item will be returned if possible</param>
        /// <param name="hull">If a hull is specified, only items within that hull are returned</param>
        public Item FindItemAtPosition(Vector2 simPosition, float aimAssistModifier = 0.0f, Hull hull = null, Item[] ignoredItems = null)
        {
            if (Submarine != null)
            {
                simPosition += Submarine.SimPosition;
            }

            Vector2 displayPosition = ConvertUnits.ToDisplayUnits(simPosition);

            Item highestPriorityItemAtPosition = null;
            Item closestItem = null;
            float closestItemDistance = 0.0f;

            foreach (Item item in Item.ItemList)
            {
                if (ignoredItems != null && ignoredItems.Contains(item)) continue;
                if (hull != null && item.CurrentHull != hull) continue;
                if (item.body != null && !item.body.Enabled) continue;
                if (item.ParentInventory != null) continue;
                
                if (CanInteractWith(item))
                {
                    if (item.IsMouseOn(displayPosition) && (highestPriorityItemAtPosition == null || 
                        ((highestPriorityItemAtPosition.InteractPriority < item.InteractPriority) ||
                        (highestPriorityItemAtPosition.InteractPriority == item.InteractPriority && highestPriorityItemAtPosition.GetDrawDepth() > item.GetDrawDepth()))))
                    {
                        highestPriorityItemAtPosition = item;
                    }
                    else if (aimAssistModifier > 0.0f && SelectedConstruction == null)
                    {
                        float distanceToItem = item.IsInsideTrigger(displayPosition) ? 0.0f : Vector2.Distance(item.WorldPosition, displayPosition);

                        //aim assist can only be used if no item has been selected 
                        //= can't switch selection to another item without deselecting the current one first UNLESS the cursor is directly on the item
                        //otherwise it would be too easy to accidentally switch the selected item when rewiring items
                        if (distanceToItem < (100.0f * aimAssistModifier) && (closestItem == null || distanceToItem < closestItemDistance))
                        {
                            closestItem = item;
                            closestItemDistance = distanceToItem;
                        }
                    }
                }
            }

            if (highestPriorityItemAtPosition == null)
            {
                return closestItem;
            }

            return highestPriorityItemAtPosition;
        }

        private Character FindCharacterAtPosition(Vector2 mouseSimPos, float maxDist = 150.0f)
        {
            Character closestCharacter = null;
            float closestDist = 0.0f;

            maxDist = ConvertUnits.ToSimUnits(maxDist);
            
            foreach (Character c in CharacterList)
            {
                if (!CanInteractWith(c)) continue;

                float dist = Vector2.DistanceSquared(mouseSimPos, c.SimPosition);
                if (dist < maxDist*maxDist && (closestCharacter == null || dist < closestDist))
                {
                    closestCharacter = c;
                    closestDist = dist;
                }
                
                /*FarseerPhysics.Common.Transform transform;
                c.AnimController.Collider.FarseerBody.GetTransform(out transform);
                for (int i = 0; i < c.AnimController.Collider.FarseerBody.FixtureList.Count; i++)
                {
                    if (c.AnimController.Collider.FarseerBody.FixtureList[i].Shape.TestPoint(ref transform, ref mouseSimPos))
                    {
                        Console.WriteLine("Hit: " + i);
                        closestCharacter = c;
                    }
                }*/
            }

            return closestCharacter;
        }

        private void TransformCursorPos()
        {
            if (Submarine == null)
            {
                //character is outside but cursor position inside
                if (cursorPosition.Y > Level.Loaded.Size.Y)
                {
                    var sub = Submarine.FindContaining(cursorPosition);
                    if (sub != null) cursorPosition += sub.Position;
                }
            }
            else
            {
                //character is inside but cursor position is outside
                if (cursorPosition.Y < Level.Loaded.Size.Y)
                {
                    cursorPosition -= Submarine.Position;
                }
            }
        }

        public void SelectCharacter(Character character)
        {
            if (character == null) return;
            
            SelectedCharacter = character;
        }

        public void DeselectCharacter()
        {
            if (SelectedCharacter == null) return;

            if (SelectedCharacter.AnimController != null)
            {
                foreach (Limb limb in SelectedCharacter.AnimController.Limbs)
                {
                    if (limb.pullJoint != null) limb.pullJoint.Enabled = false;
                }
            }

            SelectedCharacter = null;
        }

        public void DoInteractionUpdate(float deltaTime, Vector2 mouseSimPos)
        {
            bool isLocalPlayer = (controlled == this);
            if (!isLocalPlayer && (this is AICharacter && !IsRemotePlayer))
            {
                return;
            }

            if (!CanInteract)
            {
                selectedConstruction = null;
                focusedItem = null;
                if (!AllowInput)
                {
                    focusedCharacter = null;
                    if (SelectedCharacter != null) DeselectCharacter();
                    return;
                }
            }
            if ((!isLocalPlayer && IsKeyHit(InputType.Select) && GameMain.Server == null) || 
                (isLocalPlayer && (findFocusedTimer <= 0.0f || Screen.Selected == GameMain.SubEditorScreen)))
            {
                focusedCharacter = FindCharacterAtPosition(mouseSimPos);
                focusedItem = CanInteract ? FindItemAtPosition(mouseSimPos, AnimController.InWater ? 0.5f : 0.25f) : null;

                if (focusedCharacter != null && focusedItem != null)
                {
                    if (Vector2.DistanceSquared(mouseSimPos, focusedCharacter.SimPosition) > Vector2.DistanceSquared(mouseSimPos, focusedItem.SimPosition))
                    {
                        focusedCharacter = null;
                    }
                    else
                    {
                        focusedItem = null;
                    }
                }
                findFocusedTimer = 0.05f;
            }
            else
            {
                findFocusedTimer -= deltaTime;
            }
            
            if (SelectedCharacter != null && focusedItem == null && IsKeyHit(InputType.Select)) //Let people use ladders and buttons and stuff when dragging chars
            {
                DeselectCharacter();
            }
            else if (focusedCharacter != null && IsKeyHit(InputType.Select))
            {
                SelectCharacter(focusedCharacter);                
            }
            else if (focusedItem != null)
            {
                if (Controlled == this)
                {
                    focusedItem.IsHighlighted = true;
                }
                focusedItem.TryInteract(this);
            }
            else if (IsKeyHit(InputType.Select) && selectedConstruction != null)
            {
                selectedConstruction = null;
            }
        }
        
        public static void UpdateAnimAll(float deltaTime)
        {
            foreach (Character c in CharacterList)
            {
                if (!c.Enabled || c.AnimController.Frozen) continue;
                
                c.AnimController.UpdateAnim(deltaTime);
            }
        }

        public static void UpdateAll(float deltaTime, Camera cam)
        {
#if CLIENT
            //Reset zoom modifier on characters
            cam.ZoomModifier = 0f;
#endif
            if (GameMain.Client == null)
            {
                foreach (Character c in CharacterList)
                {
                    if (!(c is AICharacter) && !c.IsRemotePlayer) continue;
                    
                    if (GameMain.Server != null)
                    {
                        //disable AI characters that are far away from all clients and the host's character and not controlled by anyone
                            //Enable visibility of far away corpses now (NilMod Edit)
                            c.Enabled =
                            c == controlled ||
                            c.IsRemotePlayer ||
                            CharacterList.Any(c2 => 
                                c != c2 &&
                                (c2.IsRemotePlayer || c2 == GameMain.Server.Character) && 
                                Vector2.DistanceSquared(c2.WorldPosition, c.WorldPosition) < NetConfig.CharacterIgnoreDistanceSqr && !c2.IsDead);
                    }
                    else if (Submarine.MainSub != null)
                    {
                        //disable AI characters that are far away from the sub and the controlled character
                        c.Enabled = Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition) < NetConfig.CharacterIgnoreDistanceSqr ||
                            (controlled != null && Vector2.DistanceSquared(controlled.WorldPosition, c.WorldPosition) < NetConfig.CharacterIgnoreDistanceSqr);
                    }
                }
            }

            for (int i = 0; i < CharacterList.Count; i++)
            {
                CharacterList[i].Update(deltaTime, cam);
            }
        }

        public virtual void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime, cam);

            if (GameMain.Client != null && this == Controlled && !isSynced) return;

            if (!Enabled) return;

            if (Level.Loaded != null && WorldPosition.Y < Level.MaxEntityDepth ||
                (Submarine != null && Submarine.WorldPosition.Y < Level.MaxEntityDepth))
            {
                Enabled = false;
                Kill(CauseOfDeath.Pressure);
                return;
            }

            PreviousHull = CurrentHull;
            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull, true);
            //if (PreviousHull != CurrentHull && Character.Controlled == this) Hull.DetectItemVisibility(this); //WIP item culling

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

            HideFace = false;
                        
            if (isDead) return;

            if (huskInfection != null) huskInfection.Update(deltaTime, this);

            if (GameMain.NetworkMember != null)
            {
                UpdateNetInput();
            }
            else
            {
                AnimController.Frozen = false;
            }

            DisableImpactDamageTimer -= deltaTime;
            
            if (needsAir)
            {
                bool protectedFromPressure = PressureProtection > 0.0f;
                
                protectedFromPressure = protectedFromPressure && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null));
                           
                if (!protectedFromPressure && 
                    (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f))
                {
                    PressureTimer += ((AnimController.CurrentHull == null) ?
                        100.0f : AnimController.CurrentHull.LethalPressure) * deltaTime;

                    if (PressureTimer >= 100.0f)
                    {
                        if (controlled == this) cam.Zoom = 5.0f;
                        //NilMod Progressive Implosion Death
                        if (GameMain.NilMod.UseProgressiveImplodeDeath)
                        {
                            //If progressive death
                            if (Health > minHealth && GameMain.NilMod.CharacterImplodeDeathAtMinHealth)
                            {
                                Health -= GameMain.NilMod.ImplodeHealthLoss * deltaTime;
                                Oxygen -= GameMain.NilMod.ImplodeOxygenLoss * deltaTime;
                                Bleeding += GameMain.NilMod.ImplodeBleedGain * deltaTime;
                            }
                            else if (Health > 0f && !GameMain.NilMod.CharacterImplodeDeathAtMinHealth)
                            {
                                Health -= GameMain.NilMod.ImplodeHealthLoss * deltaTime;
                                Oxygen -= GameMain.NilMod.ImplodeOxygenLoss * deltaTime;
                                Bleeding += GameMain.NilMod.ImplodeBleedGain * deltaTime;
                            }
                            else
                            {
                                if (GameMain.Client == null)
                                {
                                    Implode();
                                    //return;
                                }
                            }
                        }
                        else
                        {
                            if (GameMain.Client == null)
                            {
                                Implode();
                                //return;
                            }
                        }
                    }
                }
                else
                {
                    PressureTimer = 0.0f;
                }
            }

            UpdateControlled(deltaTime, cam);

            if (stunTimer > 0.0f)
            {
                stunTimer -= deltaTime;
                if (stunTimer < 0.0f && GameMain.Server != null)
                {
                    if (GameMain.Client == null) Stunresistance *= stunresistendmult;
                    //stun ended -> notify clients
                    //GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                } 
            }

            if (Stunresistance > stunresistbase && stunresistdecreasespeed > 0f) Stunresistance -= (stunresistdecreasespeed * deltaTime);
            //Skip health effects as critical health handles it differently
            if (IsUnconscious)
            {
                UpdateUnconscious(deltaTime);
                return;
            }

            //Do ragdoll shenanigans before Stun because it's still technically a stun, innit? Less network updates for us!
            if (IsForceRagdolled)
                IsRagdolled = IsForceRagdolled;
            else if ((GameMain.Server == null || GameMain.Server.AllowRagdollButton) && (!IsRagdolled || AnimController.Collider.LinearVelocity.Length() < 1f)) //Keep us ragdolled if we were forced or we're too speedy to unragdoll
                IsRagdolled = IsKeyDown(InputType.Ragdoll); //Handle this here instead of Control because we can stop being ragdolled ourselves

            //Health effects
            if (needsAir) UpdateOxygen(deltaTime);

            Health -= bleeding * deltaTime;
            Bleeding -= BleedingDecreaseSpeed * deltaTime;

            if (health <= minHealth) Kill(CauseOfDeath.Bloodloss);

            if (!IsDead) LockHands = false;

            //ragdoll button
            if (IsRagdolled)
            {
                if (AnimController is HumanoidAnimController) ((HumanoidAnimController)AnimController).Crouching = false;
                /*if(GameMain.Server != null)
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });*/
                AnimController.ResetPullJoints();
                selectedConstruction = null;
                return;
            }

            //AI and control stuff

            Control(deltaTime, cam);
            if (controlled != this && (!(this is AICharacter) || IsRemotePlayer))
            {
                Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);
                DoInteractionUpdate(deltaTime, mouseSimPos);
            }
                        
            if (selectedConstruction != null && !CanInteractWith(selectedConstruction))
            {
                selectedConstruction = null;
            }

            UpdateSightRange();
            if (aiTarget != null) aiTarget.SoundRange = 0.0f;

            lowPassMultiplier = MathHelper.Lerp(lowPassMultiplier, 1.0f, 0.1f);

            if (DoesBleed && Bleeding > 0f)
            {
                Health -= (Bleeding * GameMain.NilMod.CreatureBleedMultiplier) * deltaTime;
                Bleeding -= BleedingDecreaseSpeed * deltaTime;
            }

            //NilMod Anti death code
            if (Health <= minHealth)
            {
                if (!GameMain.NilMod.PlayerCanTraumaDeath && (GameMain.Server != null ? GameMain.Server.TraitorsEnabled == YesNoMaybe.No : true))
                {
                    if (!IsRemotePlayer)
                    {
                        if (controlled != this)
                        {
                            float pressureFactor = (AnimController.CurrentHull == null) ?
                            100.0f : Math.Min(AnimController.CurrentHull.LethalPressure, 100.0f);
                            if (PressureProtection > 0.0f && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null))) pressureFactor = 0.0f;

                            Kill(pressureFactor == 100f ? CauseOfDeath.Pressure : CauseOfDeath.Bloodloss);
                        }
                        else
                        {
                            health = minHealth;
                            //Their QUITE Dead, you can reduce the bleeding now.
                            if (Bleeding > GameMain.NilMod.MinHealthBleedCap)
                            {
                                bleeding = GameMain.NilMod.MinHealthBleedCap;
                            }
                        }
                    }
                    else
                    {
                        health = minHealth;
                        //They Died, you can reduce the bleeding now.
                        if (Bleeding > GameMain.NilMod.MinHealthBleedCap)
                        {
                            bleeding = GameMain.NilMod.MinHealthBleedCap;
                        }
                    }
                }
                else
                {
                    float pressureFactor = (AnimController.CurrentHull == null) ?
                    100.0f : Math.Min(AnimController.CurrentHull.LethalPressure, 100.0f);
                    if (PressureProtection > 0.0f && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null))) pressureFactor = 0.0f;

                    Kill(pressureFactor == 100f ? CauseOfDeath.Pressure : CauseOfDeath.Bloodloss);
                }
            }

            //NilMod Health Regen Code
            if (!IsRemotePlayer)
            {
                if (controlled != this && GameMain.NilMod.CreatureHealthRegen != 0f)
                {
                    if (Health >= ((GameMain.NilMod.CreatureHealthRegenMin / 100) * MaxHealth) && Health <= ((GameMain.NilMod.CreatureHealthRegenMax / 100) * MaxHealth))
                    {
                        Health += GameMain.NilMod.CreatureHealthRegen * deltaTime;
                    }
                }
                else if (GameMain.NilMod.PlayerHealthRegen != 0f)
                {
                    if (Health >= ((GameMain.NilMod.PlayerHealthRegenMin / 100) * MaxHealth) && Health <= ((GameMain.NilMod.PlayerHealthRegenMax / 100) * MaxHealth))
                    {
                        Health += GameMain.NilMod.PlayerHealthRegen * deltaTime;
                    }
                }
            }
            else if (GameMain.NilMod.PlayerHealthRegen != 0f)
            {
                if (Health >= ((GameMain.NilMod.PlayerHealthRegenMin / 100) * MaxHealth) && Health <= ((GameMain.NilMod.PlayerHealthRegenMax / 100) * MaxHealth))
                {
                    Health += GameMain.NilMod.PlayerHealthRegen * deltaTime;
                }
            }

            if (!IsDead) LockHands = false;
            //CPR stuff is handled in the UpdateCPR function in HumanoidAnimController
        }

        partial void UpdateControlled(float deltaTime, Camera cam);

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        private void UpdateOxygen(float deltaTime)
        {
            float prevOxygen = Oxygen;
            Oxygen += deltaTime * (oxygenAvailable < GameMain.NilMod.HullUnbreathablePercent ? GameMain.NilMod.PlayerOxygenUsageAmount : GameMain.NilMod.PlayerOxygenGainSpeed);

            UpdateOxygenProjSpecific(prevOxygen);

            PressureProtection -= deltaTime * 100.0f;

            float hullAvailableOxygen = 0.0f;

            if (!AnimController.HeadInWater && AnimController.CurrentHull != null)
            {
                //don't decrease the amount of oxygen in the hull if the character has more oxygen available than the hull
                //(i.e. if the character has some external source of oxygen)
                if (OxygenAvailable * 0.98f < AnimController.CurrentHull.OxygenPercentage)
                {
                    AnimController.CurrentHull.Oxygen -= Hull.OxygenConsumptionSpeed * deltaTime;
                }
                hullAvailableOxygen = AnimController.CurrentHull.OxygenPercentage;
            }
            
            OxygenAvailable += MathHelper.Clamp(hullAvailableOxygen - oxygenAvailable, -deltaTime * 50.0f, deltaTime * 50.0f);
        }
        partial void UpdateOxygenProjSpecific(float prevOxygen);

        private void UpdateUnconscious(float deltaTime)
        {
            Stun = Math.Max(GameMain.NilMod.PlayerUnconciousTimer, Stun);

            AnimController.ResetPullJoints();
            selectedConstruction = null;

            float HealthDamage = 0f;
            float BleedDamage = 0f;
            float OxygenDamage = 0f;

            //Oxygen -= deltaTime * 0.5f; //We're critical - our heart stopped!

            Boolean ReceivingCPR = CharacterList.Find(c => c.SelectedCharacter == this && c.AnimController.Anim == AnimController.Animation.CPR) != null;

            //NilMod stat loss at negative oxygen
            if (Oxygen <= 0.0f)
            {
                //Health decay only, no additional bleed rates as it isnt yet physical trauma killing them
                if (!GameMain.NilMod.PlayerCPRStopsHealthDecay || (GameMain.NilMod.PlayerCPRStopsHealthDecay && !(ReceivingCPR && oxygenAvailable >= GameMain.NilMod.HullUnbreathablePercent)))
                {
                    HealthDamage += GameMain.NilMod.OxygenUnconciousDecayHealth;
                }
                if (!GameMain.NilMod.PlayerCPRStopsBleedDecay || (GameMain.NilMod.PlayerCPRStopsBleedDecay && !(ReceivingCPR && oxygenAvailable >= GameMain.NilMod.HullUnbreathablePercent)))
                {
                    BleedDamage += GameMain.NilMod.OxygenUnconciousDecayBleed;
                }
                if (!GameMain.NilMod.PlayerCPRStopsOxygenDecay || (GameMain.NilMod.PlayerCPRStopsOxygenDecay && !(ReceivingCPR && oxygenAvailable >= GameMain.NilMod.HullUnbreathablePercent)))
                {
                    OxygenDamage += GameMain.NilMod.OxygenUnconciousDecayOxygen;
                }
            }

            //NilMod stat loss at negative health
            if (Health <= 0.0f)
            {
                //Health decay or if its higher, bleeding damage rate instead
                if (!GameMain.NilMod.PlayerCPRStopsHealthDecay || (GameMain.NilMod.PlayerCPRStopsHealthDecay && !ReceivingCPR))
                {
                    HealthDamage += GameMain.NilMod.HealthUnconciousDecayHealth;
                }
                if (!GameMain.NilMod.PlayerCPRStopsBleedDecay || (GameMain.NilMod.PlayerCPRStopsBleedDecay && !ReceivingCPR))
                {
                    BleedDamage += GameMain.NilMod.HealthUnconciousDecayBleed;
                }
                if (!GameMain.NilMod.PlayerCPRStopsOxygenDecay || (GameMain.NilMod.PlayerCPRStopsOxygenDecay && !ReceivingCPR))
                {
                    OxygenDamage += GameMain.NilMod.HealthUnconciousDecayOxygen;
                }
            }

            if(GameMain.NilMod.AverageDecayIfBothNegative && Health <= 0.0f && Oxygen <= 0.0f)
            {
                HealthDamage = HealthDamage / 2;
                BleedDamage = BleedDamage / 2;
                OxygenDamage = OxygenDamage / 2;
            }

            float pressureFactor = (AnimController.CurrentHull == null) ?
                    100.0f : Math.Min(AnimController.CurrentHull.LethalPressure, 100.0f);
            if (PressureProtection > 0.0f && (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && CurrentHull != null))) pressureFactor = 0.0f;

            Bleeding += BleedDamage * deltaTime;
            AddDamage(pressureFactor == 100f ? CauseOfDeath.Pressure : (Bleeding > 0.5f ? CauseOfDeath.Bloodloss : CauseOfDeath.Damage), (Math.Max(Bleeding * GameMain.NilMod.CreatureBleedMultiplier, HealthDamage)) * deltaTime, null);
            Oxygen -= OxygenDamage * deltaTime;

            //if (health <= 0.0f) //Critical health - use current state for crit time
            //{
            //    AddDamage(bleeding > 0.5f ? CauseOfDeath.Bloodloss : CauseOfDeath.Damage, Math.Max(bleeding, 1.0f) * deltaTime, null);
            //}
            //else //Keep on bleedin'
            //{
            //    Health -= bleeding * deltaTime;
            //    Bleeding -= BleedingDecreaseSpeed * deltaTime;
            //}
        }

        private void UpdateSightRange()
        {
            if (aiTarget == null) return;

            aiTarget.SightRange = Mass*100.0f + AnimController.Collider.LinearVelocity.Length()*500.0f;
        }
        
        public void ShowSpeechBubble(float duration, Color color)
        {
            speechBubbleTimer = Math.Max(speechBubbleTimer, duration);
            speechBubbleColor = color;
        }

        private void AdjustKarma(Character attacker, float amount)
        {
            if (GameMain.Server != null)
            {
                if (attacker is Character)
                {
                    Character attackerCharacter = attacker as Character;
                    Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attackerCharacter);
                    if (attackerClient != null)
                    {
                        Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == this);
                        if (targetClient != null || this == Controlled)
                        {
                            if (attackerCharacter.TeamID == TeamID)
                            {
                                attackerClient.Karma -= amount * 0.01f;
                                if (health <= minHealth) attackerClient.Karma = 0.0f;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Directly reduce the health of the character without any additional effects (particles, sounds, status effects...)
        /// </summary>
        public virtual void AddDamage(CauseOfDeath causeOfDeath, float amount, Character attacker)
        {
            Health = Health - amount;
            if (amount > 0.0f)
            {
                lastAttackCauseOfDeath = causeOfDeath;

                DamageHUD(amount);
            }
            AdjustKarma(attacker, amount);

            //Nilmod Death Code
            if (Health <= minHealth)
            {
                if (!GameMain.NilMod.PlayerCanTraumaDeath && (GameMain.Server != null ? GameMain.Server.TraitorsEnabled == YesNoMaybe.No : true))
                {
                    if (!IsRemotePlayer)
                    {
                        if (controlled != this)
                        {
                            Kill(causeOfDeath);
                        }
                        else
                        {
                            health = minHealth;
                        }
                    }
                    else
                    {
                        health = minHealth;
                    }
                }
                else
                {
                    Kill(causeOfDeath);
                }
            }
        }
        partial void DamageHUD(float amount);

        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            return ApplyAttack(attacker, worldPosition, attack, deltaTime, playSound, null);
        }

        /// <summary>
        /// Apply the specified attack to this character. If the targetLimb is not specified, the limb closest to worldPosition will receive the damage.
        /// </summary>
        public virtual AttackResult ApplyAttack(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false, Limb targetLimb = null)
        {
            Limb limbHit = targetLimb;
            var attackResult = targetLimb == null ?
                AddDamage(worldPosition, attack.DamageType, attack.GetDamage(deltaTime), attack.GetBleedingDamage(deltaTime), attack.Stun, playSound, attack.TargetForce, out limbHit, attacker) :
                DamageLimb(worldPosition, targetLimb, attack.DamageType, attack.GetDamage(deltaTime), attack.GetBleedingDamage(deltaTime), attack.Stun, playSound, attack.TargetForce, attacker);

            if (limbHit == null) return new AttackResult();

            var attackingCharacter = attacker as Character;

            Client attackingclient = null;
            Client attackedclient = null;
            if (GameMain.Server != null)
            {
                if(attackingCharacter != null) attackingclient = GameMain.Server.ConnectedClients.Find(c => c.Character != null && c.Character == attackingCharacter);
                attackedclient = GameMain.Server.ConnectedClients.Find(c => c.Character != null && c.Character == this);
            }
            if (attackingCharacter != null)
            {
                if (GameMain.NilMod.LogAIDamage)
                {
                    if (attackingclient != null)
                    {
                        GameServer.Log((attackedclient != null ? (attackedclient.Name != Name ? "PLR: " + attackedclient.Name + " As (" + Name + ")" : "PLR: " + Name) : (Character.Controlled == this ? "HOST: " + Name : "AI: " + Name))
                            + " attacked by PLR: " + (attackingclient.Name != attackingCharacter.Name ? attackingclient.Name + " As (" + attackingCharacter.Name + ")" : attackingCharacter.Name)
                            + ". Damage: " + Math.Round(attackResult.Damage, 2)
                            + " Bleed: " + Math.Round(attackResult.Bleeding, 2)
                            + " Stun: " + Math.Round((attack.Stun * (1f - Stunresistance)), 2)
                            + (Stunresistance > 0f ? " (" + Math.Round(Stunresistance * 100f, 2) + "% Resisted)" : ""), ServerLog.MessageType.Attack);
                    }
                    else if (Character.Controlled == attackingCharacter)
                    {
                        GameServer.Log((attackedclient != null ? (attackedclient.Name != Name ? "PLR: " + attackedclient.Name + " As (" + Name + ")" : "PLR: " + Name) : "AI: " + Name)
                            + " attacked by HOST: " + attackingCharacter.Name
                            + ". Damage: " + Math.Round(attackResult.Damage, 2)
                            + " Bleed: " + Math.Round(attackResult.Bleeding, 2)
                            + " Stun: " + Math.Round((attack.Stun * (1f - Stunresistance)), 2)
                            + (Stunresistance > 0f ? " (" + Math.Round(Stunresistance * 100f, 2) + "% Resisted)" : ""), ServerLog.MessageType.Attack);
                    }
                    else
                    {
                        GameServer.Log((attackedclient != null ? (attackedclient.Name != Name ? "PLR: " + attackedclient.Name + " As (" + Name + ")" : "PLR: " + Name) : (Character.Controlled == this ? "HOST: " + Name : "AI: " + Name))
                            + " attacked by AI: " + attackingCharacter.Name
                            + ". Damage: " + Math.Round(attackResult.Damage, 2)
                            + " Bleed: " + Math.Round(attackResult.Bleeding, 2)
                            + " Stun: " + Math.Round((attack.Stun * (1f - Stunresistance)), 2)
                            + (Stunresistance > 0f ? " (" + Math.Round(Stunresistance * 100f, 2) + "% Resisted)" : ""), ServerLog.MessageType.Attack);
                    }

                }
                else
                {
                    if (attackingCharacter != null && attackingclient != null)
                    {
                        GameServer.Log((attackedclient != null && attackedclient.Name != Name ? attackedclient.Name + " As (" + Name + ")" : Name)
                            + " attacked by " + attackingCharacter.Name
                            + ". Damage: " + Math.Round(attackResult.Damage, 2)
                            + " Bleed: " + Math.Round(attackResult.Bleeding, 2)
                            + " Stun: " + Math.Round(attack.Stun * (1f - Stunresistance), 2)
                            + (Stunresistance > 0f ? " (" + Math.Round(Stunresistance * 100f, 2) + "% Resisted)" : ""), ServerLog.MessageType.Attack);
                    }
                }
            }

            if (GameMain.Client == null &&
                isDead &&
                Health - attackResult.Damage <= minHealth && Rand.Range(0.0f, 1.0f) < attack.SeverLimbsProbability)
            {
                foreach (LimbJoint joint in AnimController.LimbJoints)
                {
                    if (joint.CanBeSevered && (joint.LimbA == limbHit || joint.LimbB == limbHit))
                    {
#if CLIENT
                        if (CurrentHull != null)
                        {
                            CurrentHull.AddDecal("blood", WorldPosition, Rand.Range(0.5f, 1.5f));                            
                        }
#endif

                        AnimController.SeverLimbJoint(joint);

                        if (joint.LimbA == limbHit)
                        {
                            joint.LimbB.body.LinearVelocity += limbHit.LinearVelocity * 0.5f;
                        }
                        else
                        {
                            joint.LimbA.body.LinearVelocity += limbHit.LinearVelocity * 0.5f;
                        }
                    }
                }
            }

            return attackResult;
        }

        public AttackResult AddDamage(Vector2 worldPosition, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound, float attackForce = 0.0f)
        {
            Limb temp = null;
            return AddDamage(worldPosition, damageType, amount, bleedingAmount, stun, playSound, attackForce, out temp);
        }

        public AttackResult AddDamage(Vector2 worldPosition, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound, float attackForce, out Limb hitLimb, Character attacker = null)
        {
            hitLimb = null;

            if (Removed) return new AttackResult();

            float closestDistance = 0.0f;
            foreach (Limb limb in AnimController.Limbs)
            {
                float distance = Vector2.Distance(worldPosition, limb.WorldPosition);
                if (hitLimb == null || distance < closestDistance)
                {
                    hitLimb = limb;
                    closestDistance = distance;
                }
            }

            return DamageLimb(worldPosition, hitLimb, damageType, amount, bleedingAmount, stun, playSound, attackForce, attacker);
        }

        public AttackResult DamageLimb(Vector2 worldPosition, Limb hitLimb, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound, float attackForce, Character attacker = null)
        {
            if (Removed) return new AttackResult();

            SetStun(stun);

            if (Math.Abs(attackForce) > 0.0f)
            {
                Vector2 diff = hitLimb.WorldPosition - worldPosition;
                if (diff == Vector2.Zero) diff = Rand.Vector(1.0f);
                hitLimb.body.ApplyForce(Vector2.Normalize(diff) * attackForce, hitLimb.SimPosition + ConvertUnits.ToSimUnits(diff));
            }            

            AttackResult attackResult = hitLimb.AddDamage(worldPosition, damageType, amount, bleedingAmount, playSound);

            AddDamage(damageType == DamageType.Burn ? CauseOfDeath.Burn : causeOfDeath, attackResult.Damage, attacker);

            if (DoesBleed)
            {
                Bleeding += attackResult.Bleeding;
            }
            
            return attackResult;
        }

        public void SetStun(float newStun, bool allowStunDecrease = false, bool isNetworkMessage = false)
        {
            if (GameMain.Client != null && !isNetworkMessage) return;

            if(newStun >= stunTimer)
            {
                float stunincrease = newStun - stunTimer;

                stunincrease = (stunincrease * (1f - Stunresistance));

                if(stunresistincreasemult > 0f) Stunresistance += ((stunincrease / MaxStun) * 100f) * stunresistincreasemult;

                newStun = stunTimer + stunincrease;
            }

            newStun = MathHelper.Clamp(newStun, 0.0f, MaxStun);

            if ((newStun <= stunTimer && !allowStunDecrease) || !MathUtils.IsValid(newStun)) return;

            /*if (GameMain.Server != null &&
                (Math.Sign(newStun) != Math.Sign(stunTimer) || Math.Abs(newStun - stunTimer) > 0.1f))
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
            }*/

            if (Math.Sign(newStun) != Math.Sign(stunTimer)) AnimController.ResetPullJoints();

            stunTimer = newStun;
            if (newStun > 0.0f)
            {
                selectedConstruction = null;
            }
        }

        private void Implode(bool isNetworkMessage = false)
        {
            //Nilmod Death code
            if (!GameMain.NilMod.PlayerCanImplodeDeath && (GameMain.Server != null ? GameMain.Server.TraitorsEnabled == YesNoMaybe.No : true))
            {
                if (!IsRemotePlayer)
                {
                    if (controlled != this)
                    {
                        if (!isNetworkMessage)
                        {
                            if (GameMain.Client != null) return;
                        }

                        health = minHealth;

                        BreakJoints();

                        Kill(CauseOfDeath.Pressure, isNetworkMessage);
                    }
                    else if (Health > 0)
                    {
                        if (!isNetworkMessage)
                        {
                            if (GameMain.Client != null) return;
                        }
                        health = minHealth;
                        oxygen = 0;
                        BreakJoints();
                    }
                }
                else if (Health > 0)
                {
                    if (!isNetworkMessage)
                    {
                        if (GameMain.Client != null) return;
                    }
                    health = minHealth;
                    oxygen = 0;
                    BreakJoints();

                }
            }
            else
            {
                if (!isNetworkMessage)
                {
                    if (GameMain.Client != null) return;
                }
                health = minHealth;

                BreakJoints();

                Kill(CauseOfDeath.Pressure, isNetworkMessage);
            }
        }

        public void BreakJoints()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();

            foreach (Limb limb in AnimController.Limbs)
            {
                limb.AddDamage(limb.SimPosition, DamageType.Blunt, 500.0f, 0.0f, false);

                Vector2 diff = centerOfMass - limb.SimPosition;
                if (diff == Vector2.Zero) continue;
                limb.body.ApplyLinearImpulse(diff * 50.0f);
            }

            ImplodeFX();

            foreach (var joint in AnimController.LimbJoints)
            {
                joint.LimitEnabled = false;
            }
        }

        partial void ImplodeFX();

        public void Kill(CauseOfDeath causeOfDeath, bool isNetworkMessage = false)
        {
            if (isDead) return;

            //clients aren't allowed to kill characters unless they receive a network message
            if (!isNetworkMessage && GameMain.Client != null)
            {
                return;
            }

            /*if (GameMain.NetworkMember != null)
            {
                if (GameMain.Server != null)
                {
                    //IsRemotePlayer = false;
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                }
            }*/

            AnimController.Frozen = false;

            GameServer.Log(LogName+" has died (Cause of death: "+causeOfDeath+")", ServerLog.MessageType.Attack);

            if (OnDeath != null) OnDeath(this, causeOfDeath);

            KillProjSpecific();

            isDead = true;
            
            this.causeOfDeath = causeOfDeath;
            if (info != null) info.CauseOfDeath = causeOfDeath;
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }
            
            foreach (Limb limb in AnimController.Limbs)
            {
                if (limb.pullJoint == null) continue;
                limb.pullJoint.Enabled = false;
            }

            foreach (RevoluteJoint joint in AnimController.LimbJoints)
            {
                joint.MotorEnabled = false;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.KillCharacter(this);
            }
        }
        partial void KillProjSpecific();

        public void Revive(bool isNetworkMessage)
        {
            isDead = false;

            aiTarget = new AITarget(this);
            if (health <= 0f) health = 0.01f;

            health = Math.Min(0.01f + Math.Max(health + (maxHealth * 0.15f), health),MaxHealth);
            oxygen = 100f;
            bleeding = 0f;
            SetStun(0.0f, true, true);

            foreach (LimbJoint joint in AnimController.LimbJoints)
            {
                joint.MotorEnabled = true;
                joint.Enabled = true;
                joint.IsSevered = false;
            }

            foreach (Limb limb in AnimController.Limbs)
            {
                limb.IsSevered = false;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.ReviveCharacter(this);
            }
            if (GameMain.Server != null)
            {
                //Nilmod set character back to remote player if revived and a player controls it.
                //if (GameMain.Server.ConnectedClients.Find(c => c.Character == this) != null) IsRemotePlayer = true;
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
            }
        }
        
        public override void Remove()
        {
#if DEBUG
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to remove an already removed character\n" + Environment.StackTrace);
            }
#endif

            base.Remove();

            if (info != null) info.Remove();

            if (GameMain.Server != null)
            {
                if (GameMain.NilMod.FrozenCharacters.Find(fc => fc == this) != null)
                {
                    GameMain.NilMod.FrozenCharacters.Remove(this);
                }

                if (GameMain.NilMod.DisconnectedCharacters.Count > 0)
                {
                    DisconnectedCharacter ReconnectedClient = null;

                    ReconnectedClient = GameMain.NilMod.DisconnectedCharacters.Find(dc => dc.character == this);

                    if (ReconnectedClient != null)
                    {
                        ReconnectedClient.character = null;
                        GameMain.NilMod.DisconnectedCharacters.Remove(ReconnectedClient);
                    }
                }
            }

            CharacterList.Remove(this);

            DisposeProjSpecific();

            if (aiTarget != null) aiTarget.Remove();            

            if (AnimController != null) AnimController.Remove();

            if (selectedItems[0] != null) selectedItems[0].Drop(this);
            if (selectedItems[1] != null) selectedItems[1].Drop(this);

            foreach (Character c in CharacterList)
            {
                if (c.focusedCharacter == this) c.focusedCharacter = null;
                if (c.SelectedCharacter == this) c.SelectedCharacter = null;
            }
        }
        partial void DisposeProjSpecific();

        public void Heal()
        {
            health = MaxHealth;
            oxygen = 100.0f;
            bleeding = 0.0f;
            SetStun(0.0f, true,true);
            if(huskInfection != null)
            {
                HuskInfectionState = 0f;
            }
            //Make a heal probably duplicate the event, but absolutely ensure they stand up now
            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
            }
        }

        /*
        public void CheckForStatusEvent()
        {
            //They should only check these if their alive and on loss of limb.
            if (isDead) return;

            if (Math.Abs(health - lastSentHealth) > (maxHealth - minHealth) / 255.0f || Math.Sign(health) != Math.Sign(lastSentHealth))
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                lastSentHealth = health;
                lastSentOxygen = oxygen;
                lastSentBleeding = bleeding;
            }
            else if (Math.Abs(oxygen - lastSentOxygen) > (100f - -100f) / 255.0f || Math.Sign(oxygen) != Math.Sign(lastSentOxygen))
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                lastSentHealth = health;
                lastSentOxygen = oxygen;
                lastSentBleeding = bleeding;
            }
            else if (Math.Abs(bleeding - lastSentBleeding) > (100f - -100f) / 255.0f || Math.Sign(bleeding) != Math.Sign(lastSentBleeding))
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                lastSentHealth = health;
                lastSentOxygen = oxygen;
                lastSentBleeding = bleeding;
            }
        }
        */
    }
}
