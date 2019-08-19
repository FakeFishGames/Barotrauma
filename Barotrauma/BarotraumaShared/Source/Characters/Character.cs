using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using FarseerPhysics.Dynamics;
using Barotrauma.Extensions;
using System.Text;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerSerializable, ISpatialEntity
    {
        public static List<Character> CharacterList = new List<Character>();

        partial void UpdateLimbLightSource(Limb limb);

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
                    UpdateLimbLightSource(limb);
                }
                AnimController.Collider.Enabled = value;
            }
        }

        public Hull PreviousHull = null;
        public Hull CurrentHull = null;

        public bool IsRemotePlayer;
        public readonly Dictionary<string, SerializableProperty> Properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get { return Properties; }
        }

        public Key[] Keys
        {
            get { return keys; }
        }

        protected Key[] keys;
        private Item[] selectedItems;

        public enum TeamType
        {
            None,
            Team1,
            Team2,
            FriendlyNPC
        }

        private TeamType teamID;
        public TeamType TeamID
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
        protected float oxygenAvailable;

        //seed used to generate this character
        private readonly string seed;
        protected Item focusedItem;
        private Character focusedCharacter, selectedCharacter, selectedBy;
        public Character LastAttacker;
        public Entity LastDamageSource;

        public readonly bool IsHumanoid;

        public bool IsTraitor;
        public string TraitorCurrentObjective = "";

        //the name of the species (e.q. human)
        public readonly string SpeciesName;
        
        private float attackCoolDown;

        private Order currentOrder;
        public Order CurrentOrder
        {
            get { return currentOrder; }
        }

        private string currentOrderOption;

        private List<StatusEffect> statusEffects = new List<StatusEffect>();
        private List<float> speedMultipliers = new List<float>();

        public Entity ViewTarget
        {
            get;
            set;
        }

        public Vector2 AimRefPosition
        {
            get
            {
                if (ViewTarget == null) { return AnimController.AimSourcePos; }

                Vector2 viewTargetWorldPos = ViewTarget.WorldPosition;
                if (ViewTarget is Item targetItem)
                {
                    Turret turret = targetItem.GetComponent<Turret>();
                    if (turret != null)
                    {
                        viewTargetWorldPos = new Vector2(
                            targetItem.WorldRect.X + turret.TransformedBarrelPos.X, 
                            targetItem.WorldRect.Y - turret.TransformedBarrelPos.Y);
                    }
                }
                return Position + (viewTargetWorldPos - WorldPosition);
            }
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

        private string displayName;
        public string DisplayName
        {
            get
            {
                return displayName != null && displayName.Length > 0 ? displayName : Name;
            }
        }

        //Only used by server logs to determine "true identity" of the player for cases when they're disguised
        public string LogName
        {
            get
            {
                if (GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowDisguises) return Name;
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

        public CharacterInventory Inventory { get; private set; }

        private Color speechBubbleColor;
        private float speechBubbleTimer;

        public bool ResetInteract;

        //text displayed when the character is highlighted if custom interact is set
        public string customInteractHUDText;
        private Action<Character, Character> onCustomInteract;
        
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
            get { return !IsUnconscious && Stun <= 0.0f && !IsDead; }
        }

        public bool CanInteract
        {
            get { return AllowInput && IsHumanoid && !LockHands && !Removed; }
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

        public Vector2 SmoothedCursorPosition
        {
            get;
            private set;
        }

        public Vector2 CursorWorldPosition
        {
            get { return Submarine == null ? cursorPosition : cursorPosition + Submarine.Position; }
        }

        public Character FocusedCharacter
        {
            get { return focusedCharacter; }
            set { focusedCharacter = value; }
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

        private float Noise { get; set; }

        private float pressureProtection;
        public float PressureProtection
        {
            get { return pressureProtection; }
            set
            {
                pressureProtection = MathHelper.Clamp(value, 0.0f, 100.0f);
            }
        }
        
        private float ragdollingLockTimer;
        public bool IsRagdolled;
        public bool IsForceRagdolled;
        public bool dontFollowCursor;

        public bool IsUnconscious
        {
            get { return CharacterHealth.IsUnconscious; }
        }

        public bool NeedsAir
        {
            get { return needsAir; }
            set { needsAir = value; }
        }

        public float Oxygen
        {
            get { return CharacterHealth.OxygenAmount; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                CharacterHealth.OxygenAmount = MathHelper.Clamp(value, -100.0f, 100.0f);
            }
        }

        public float OxygenAvailable
        {
            get { return oxygenAvailable; }
            set { oxygenAvailable = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }
                
        public float Stun
        {
            get { return IsRagdolled ? 1.0f : CharacterHealth.StunTimer; }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) return;

                SetStun(value, true);
            }
        }

        public CharacterHealth CharacterHealth { get; private set; }

        public float Vitality
        {
            get { return CharacterHealth.Vitality; }
        }

        public float Health
        {
            get { return CharacterHealth.Vitality; }
        }

        public float MaxVitality
        {
            get { return CharacterHealth.MaxVitality; }
        }

        public float Bloodloss
        {
            get { return CharacterHealth.BloodlossAmount; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                CharacterHealth.BloodlossAmount = MathHelper.Clamp(value, 0.0f, 100.0f);
            }
        }

        public float Bleeding
        {
            get { return CharacterHealth.GetAfflictionStrength("bleeding", true); }
        }
        
        public float HuskInfectionState
        {
            get
            {
                var huskAffliction = CharacterHealth.GetAffliction("huskinfection", false) as AfflictionHusk;
                return huskAffliction == null ? 0.0f : huskAffliction.Strength;
            }
            set
            {
                var huskAffliction = CharacterHealth.GetAffliction("huskinfection", false) as AfflictionHusk;
                if (huskAffliction == null)
                {
                    CharacterHealth.ApplyAffliction(null, AfflictionPrefab.Husk.Instantiate(value));
                }
                else
                {
                    huskAffliction.Strength = value;
                }
            }
        }

        public bool CanSpeak;

        private bool speechImpedimentSet;

        //value between 0-100 (50 = speech range is reduced by 50%)
        private float speechImpediment;
        public float SpeechImpediment
        {
            get
            {
                if (!CanSpeak || IsUnconscious || Stun > 0.0f || IsDead) return 100.0f;
                return speechImpediment;
            }
            set
            {
                if (value < speechImpediment) return;
                speechImpedimentSet = true;
                speechImpediment = MathHelper.Clamp(value, 0.0f, 100.0f);
            }
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

        public Item[] SelectedItems
        {
            get { return selectedItems; }
        }

        public Item SelectedConstruction { get; set; }

        public Item FocusedItem
        {
            get { return focusedItem; }
            set { focusedItem = value; }
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

        public bool IsDead { get; private set; }

        public CauseOfDeath CauseOfDeath
        {
            get;
            private set;
        }

        //can other characters select (= grab) this character
        public bool CanBeSelected
        {
            get
            {
                return !Removed;
            }
        }

        private bool canBeDragged = true;
        public bool CanBeDragged
        {
            get
            {
                if (!canBeDragged) { return false; }
                if (Removed || !AnimController.Draggable) { return false; }
                return IsDead || Stun > 0.0f || LockHands || IsUnconscious;
            }
            set { canBeDragged = value; }
        }

        //can other characters access the inventory of this character
        private bool canInventoryBeAccessed = true;
        public bool CanInventoryBeAccessed
        {
            get
            {
                if (!canInventoryBeAccessed || Removed || Inventory == null) { return false; }
                if (!Inventory.AccessibleWhenAlive)
                {
                    return IsDead;
                }
                else
                {
                    return (IsDead || Stun > 0.0f || LockHands || IsUnconscious);
                }
            }
            set { canInventoryBeAccessed = value; }
        }

        public override Vector2 SimPosition
        {
            get
            {
                if (AnimController?.Collider == null)
                {
                    string errorMsg = "Attempted to access a potentially removed character. Character: " + Name + ", id: " + ID + ", removed: " + Removed+".";
                    if (AnimController == null)
                    {
                        errorMsg += " AnimController == null";
                    }
                    else if (AnimController.Collider == null)
                    {
                        errorMsg += " AnimController.Collider == null";
                    }

                    DebugConsole.NewMessage(errorMsg, Color.Red);
                    GameAnalyticsManager.AddErrorEventOnce(
                        "Character.SimPosition:AccessRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        errorMsg + "\n" + Environment.StackTrace);

                    return Vector2.Zero;
                }

                return AnimController.Collider.SimPosition;
            }
        }

        public override Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(SimPosition); }
        }

        public override Vector2 DrawPosition
        {
            get
            {
                if (AnimController.MainLimb == null) { return Vector2.Zero; }
                return AnimController.MainLimb.body.DrawPosition;
            }
        }

        public delegate void OnDeathHandler(Character character, CauseOfDeath causeOfDeath);
        public OnDeathHandler OnDeath;

        public delegate void OnAttackedHandler(Character attacker, AttackResult attackResult);
        public OnAttackedHandler OnAttacked;

        /// <summary>
        /// Create a new character
        /// </summary>
        /// <param name="characterInfo">The name, gender, config file, etc of the character.</param>
        /// <param name="position">Position in display units.</param>
        /// <param name="seed">RNG seed to use if the character config has randomizable parameters.</param>
        /// <param name="isRemotePlayer">Is the character controlled by a remote player.</param>
        /// <param name="hasAi">Is the character controlled by AI.</param>
        /// <param name="ragdoll">Ragdoll configuration file. If null, will select the default.</param>
        public static Character Create(CharacterInfo characterInfo, Vector2 position, string seed, bool isRemotePlayer = false, bool hasAi = true, RagdollParams ragdoll = null)
        {
            return Create(characterInfo.File, position, seed, characterInfo, isRemotePlayer, hasAi, true, ragdoll);
        }

        /// <summary>
        /// Create a new character
        /// </summary>
        /// <param name="file">The path to the character's config file.</param>
        /// <param name="position">Position in display units.</param>
        /// <param name="seed">RNG seed to use if the character config has randomizable parameters.</param>
        /// <param name="characterInfo">The name, gender, etc of the character. Only used for humans, and if the parameter is not given, a random CharacterInfo is generated.</param>
        /// <param name="isRemotePlayer">Is the character controlled by a remote player.</param>
        /// <param name="hasAi">Is the character controlled by AI.</param>
        /// <param name="createNetworkEvent">Should clients receive a network event about the creation of this character?</param>
        /// <param name="ragdoll">Ragdoll configuration file. If null, will select the default.</param>
        public static Character Create(string file, Vector2 position, string seed, CharacterInfo characterInfo = null, bool isRemotePlayer = false, bool hasAi = true, bool createNetworkEvent = true, RagdollParams ragdoll = null)
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
                DebugConsole.ThrowError("Spawning a character failed - file \"" + file + "\" not found!");
                return null;
            }
#endif
            Character newCharacter = null;
            if (file != HumanConfigFile)
            {
                var aiCharacter = new AICharacter(file, position, seed, characterInfo, isRemotePlayer, ragdoll);
                var ai = new EnemyAIController(aiCharacter, file, seed);
                aiCharacter.SetAI(ai);

                //aiCharacter.minVitality = 0.0f;
                
                newCharacter = aiCharacter;
            }
            else if (hasAi)
            {
                var aiCharacter = new AICharacter(file, position, seed, characterInfo, isRemotePlayer, ragdoll);
                var ai = new HumanAIController(aiCharacter);
                aiCharacter.SetAI(ai);

                //aiCharacter.minVitality = -100.0f;

                newCharacter = aiCharacter;
            }
            else
            {
                newCharacter = new Character(file, position, seed, characterInfo, isRemotePlayer, ragdoll);
                //newCharacter.minVitality = -100.0f;
            }

#if SERVER
            if (GameMain.Server != null && Spawner != null && createNetworkEvent)
            {
                Spawner.CreateNetworkEvent(newCharacter, false);
            }
#endif
            return newCharacter;
        }

        protected Character(string file, Vector2 position, string seed, CharacterInfo characterInfo = null, bool isRemotePlayer = false, RagdollParams ragdollParams = null)
            : base(null)
        {
            ConfigPath = file;
            this.seed = seed;
            MTRandom random = new MTRandom(ToolBox.StringToInt(seed));

            selectedItems = new Item[2];

            IsRemotePlayer = isRemotePlayer;
            
            oxygenAvailable = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

            Properties = SerializableProperty.GetProperties(this);

            Info = characterInfo;
            if (file == HumanConfigFile || file == GetConfigFile("humanhusk"))
            {
                if (characterInfo == null)
                {
                    Info = new CharacterInfo(HumanConfigFile);
                }
            }

            keys = new Key[Enum.GetNames(typeof(InputType)).Length];
            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key((InputType)i);
            }

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            InitProjSpecific(doc);
            SpeciesName = doc.Root.GetAttributeString("name", "Unknown");
            displayName = TextManager.Get($"Character.{Path.GetFileName(Path.GetDirectoryName(file))}", true);

            IsHumanoid = doc.Root.GetAttributeBool("humanoid", false);
            CanSpeak = doc.Root.GetAttributeBool("canspeak", false);
            needsAir = doc.Root.GetAttributeBool("needsair", false);
            Noise = doc.Root.GetAttributeFloat("noise", 100f);

            //List<XElement> ragdollElements = new List<XElement>();
            //List<float> ragdollCommonness = new List<float>();
            //foreach (XElement element in doc.Root.Elements())
            //{
            //    if (element.Name.ToString().ToLowerInvariant() != "ragdoll") continue;                
            //    ragdollElements.Add(element);
            //    ragdollCommonness.Add(element.GetAttributeFloat("commonness", 1.0f));                
            //}

            ////choose a random ragdoll element
            //XElement ragdollElement = ragdollElements.Count == 1 ?
            //    ragdollElements[0] : ToolBox.SelectWeightedRandom(ragdollElements, ragdollCommonness, random);

            if (IsHumanoid)
            {
                AnimController = new HumanoidAnimController(this, seed, ragdollParams as HumanRagdollParams);
                AnimController.TargetDir = Direction.Right;
                
            }
            else
            {
                AnimController = new FishAnimController(this, seed, ragdollParams as FishRagdollParams);
                PressureProtection = 100.0f;
            }

            List<XElement> inventoryElements = new List<XElement>();
            List<float> inventoryCommonness = new List<float>();
            List<XElement> healthElements = new List<XElement>();
            List<float> healthCommonness = new List<float>();
            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "inventory":
                        inventoryElements.Add(subElement);
                        inventoryCommonness.Add(subElement.GetAttributeFloat("commonness", 1.0f));
                        break;
                    case "health":
                        healthElements.Add(subElement);
                        healthCommonness.Add(subElement.GetAttributeFloat("commonness", 1.0f));
                        break;
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement, Name));
                        break;
                }
            }
            if (inventoryElements.Count > 0)
            {
                Inventory = new CharacterInventory(
                    inventoryElements.Count == 1 ? inventoryElements[0] : ToolBox.SelectWeightedRandom(inventoryElements, inventoryCommonness, random), 
                    this);
            }
            if (healthElements.Count == 0)
            {
                CharacterHealth = new CharacterHealth(this);
            }
            else
            {
                CharacterHealth = new CharacterHealth(
                    healthElements.Count == 1 ? healthElements[0] : ToolBox.SelectWeightedRandom(healthElements, healthCommonness, random), 
                    this);
            }

            AnimController.SetPosition(ConvertUnits.ToSimUnits(position));

            AnimController.FindHull(null);
            if (AnimController.CurrentHull != null) Submarine = AnimController.CurrentHull.Submarine;

            CharacterList.Add(this);

            //characters start disabled in the multiplayer mode, and are enabled if/when
            //  - controlled by the player
            //  - client receives a position update from the server
            //  - server receives an input message from the client controlling the character
            //  - if an AICharacter, the server enables it when close enough to any of the players
            Enabled = GameMain.NetworkMember == null;

            if (Info != null)
            {
                LoadHeadAttachments();
            }
        }
        partial void InitProjSpecific(XDocument doc);

        public void ReloadHead(int? headId = null, int hairIndex = -1, int beardIndex = -1, int moustacheIndex = -1, int faceAttachmentIndex = -1)
        {
            if (Info == null) { return; }
            var head = AnimController.GetLimb(LimbType.Head);
            if (head == null) { return; }
            Info.RecreateHead(headId ?? Info.HeadSpriteId, Info.Race, Info.Gender, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
#if CLIENT
            head.RecreateSprite();
#endif
            LoadHeadAttachments();
        }

        public void LoadHeadAttachments()
        {
            if (Info == null) { return; }
            if (AnimController == null) { return; }
            var head = AnimController.GetLimb(LimbType.Head);
            if (head == null) { return; }
            // Note that if there are any other wearables on the head, they are removed here.
            head.OtherWearables.ForEach(w => w.Sprite.Remove());
            head.OtherWearables.Clear();

            //if the element has not been set at this point, the character has no hair and the index should be zero (= no hair)
            if (info.FaceAttachment == null) { info.FaceAttachmentIndex = 0; }
            Info.FaceAttachment?.Elements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.FaceAttachment)));
            if (info.BeardElement == null) { info.BeardIndex = 0; }
            Info.BeardElement?.Elements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Beard)));
            if (info.MoustacheElement == null) { info.MoustacheIndex = 0; }
            Info.MoustacheElement?.Elements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Moustache)));
            if (info.HairElement == null) { info.HairIndex = 0; }
            Info.HairElement?.Elements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Hair)));

#if CLIENT
            head.LoadHuskSprite();
            head.LoadHerpesSprite();
#endif
        }

        private static string humanConfigFile;
        public static string HumanConfigFile
        {
            get
            {
                if (string.IsNullOrEmpty(humanConfigFile))
                {
                    humanConfigFile = GameMain.Instance.GetFilesOfType(ContentType.Character)?
                            .FirstOrDefault(c => Path.GetFileName(c).ToLowerInvariant() == "human.xml");

                    if (humanConfigFile == null)
                    {
                        DebugConsole.ThrowError($"Couldn't find a human config file from the selected content packages!");
                        DebugConsole.ThrowError($"(The config file must end with \"human.xml\")");
                        return string.Empty;
                    }
                }
                return humanConfigFile;
            }
        }

        private static IEnumerable<string> characterConfigFiles;
        private static IEnumerable<string> CharacterConfigFiles
        {
            get
            {
                if (characterConfigFiles == null)
                {
                    characterConfigFiles = GameMain.Instance.GetFilesOfType(ContentType.Character);
                }
                return characterConfigFiles;
            }
        }

        /// <summary>
        /// Searches for a character config file from all currently selected content packages, 
        /// or from a specific package if the contentPackage parameter is given.
        /// </summary>
        public static string GetConfigFile(string speciesName, ContentPackage contentPackage = null)
        {
            string configFile = null;
            if (contentPackage == null)
            {
                configFile = GameMain.Instance.GetFilesOfType(ContentType.Character)
                    .FirstOrDefault(c => Path.GetFileName(c).ToLowerInvariant() == $"{speciesName.ToLowerInvariant()}.xml");
            }
            else
            {
                configFile = contentPackage.GetFilesOfType(ContentType.Character)?
                    .FirstOrDefault(c => Path.GetFileName(c).ToLowerInvariant() == $"{speciesName.ToLowerInvariant()}.xml");
            }

            if (configFile == null)
            {
                DebugConsole.ThrowError($"Couldn't find a config file for {speciesName} from the selected content packages!");
                DebugConsole.ThrowError($"(The config file must end with \"{speciesName}.xml\")");
                return string.Empty;
            }
            return configFile;
        }

        public bool IsKeyHit(InputType inputType)
        {
#if SERVER
            if (GameMain.Server != null && IsRemotePlayer)
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
                    case InputType.Deselect:
                        return dequeuedInput.HasFlag(InputNetFlags.Deselect);
                    case InputType.Health:
                        return dequeuedInput.HasFlag(InputNetFlags.Health);
                    case InputType.Grab:
                        return dequeuedInput.HasFlag(InputNetFlags.Grab);
                    case InputType.Use:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Use)) && (prevDequeuedInput.HasFlag(InputNetFlags.Use));
                    case InputType.Shoot:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Shoot)) && (prevDequeuedInput.HasFlag(InputNetFlags.Shoot));
                    case InputType.Ragdoll:
                        return !(dequeuedInput.HasFlag(InputNetFlags.Ragdoll)) && (prevDequeuedInput.HasFlag(InputNetFlags.Ragdoll));
                    default:
                        return false;
                }
            }
#endif

            return keys[(int)inputType].Hit;
        }

        public bool IsKeyDown(InputType inputType)
        {
#if SERVER
            if (GameMain.Server != null && IsRemotePlayer)
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
                    case InputType.Deselect:
                        return false;
                    case InputType.Aim:
                        return dequeuedInput.HasFlag(InputNetFlags.Aim);
                    case InputType.Use:
                        return dequeuedInput.HasFlag(InputNetFlags.Use);
                    case InputType.Shoot:
                        return dequeuedInput.HasFlag(InputNetFlags.Shoot);
                    case InputType.Attack:
                        return dequeuedInput.HasFlag(InputNetFlags.Attack);
                    case InputType.Ragdoll:
                        return dequeuedInput.HasFlag(InputNetFlags.Ragdoll);
                }
                return false;
            }
#endif
            if (inputType == InputType.Up || inputType == InputType.Down ||
                inputType == InputType.Left || inputType == InputType.Right)
            {
                var invertControls = CharacterHealth.GetAffliction("invertcontrols");
                if (invertControls != null)
                {
                    switch (inputType)
                    {
                        case InputType.Left:
                            inputType = InputType.Right;
                            break;
                        case InputType.Right:
                            inputType = InputType.Left;
                            break;
                        case InputType.Up:
                            inputType = InputType.Down;
                            break;
                        case InputType.Down:
                            inputType = InputType.Up;
                            break;
                    }
                }
            }

            return keys[(int)inputType].Held;
        }

        public void SetInput(InputType inputType, bool hit, bool held)
        {
            keys[(int)inputType].Hit = hit;
            keys[(int)inputType].Held = held;
            keys[(int)inputType].SetState(hit, held);
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

        public void GiveJobItems(WayPoint spawnPoint = null)
        {
            if (info == null || info.Job == null) return;

            info.Job.GiveJobItems(this, spawnPoint);
        }

        public float GetSkillLevel(string skillIdentifier)
        {
            return (Info == null || Info.Job == null) ? 0.0f : Info.Job.GetSkillLevel(skillIdentifier);
        }
        
        // TODO: reposition? there's also the overrideTargetMovement variable, but it's not in the same manner
        public Vector2? OverrideMovement { get; set; }
        public bool ForceRun { get; set; }

        public bool IsClimbing => AnimController.Anim == AnimController.Animation.Climbing;

        public Vector2 GetTargetMovement()
        {
            Vector2 targetMovement = Vector2.Zero;
            if (OverrideMovement.HasValue)
            {
                targetMovement = OverrideMovement.Value;
            }
            else
            {
                if (IsKeyDown(InputType.Left)) targetMovement.X -= 1.0f;
                if (IsKeyDown(InputType.Right)) targetMovement.X += 1.0f;
                if (IsKeyDown(InputType.Up)) targetMovement.Y += 1.0f;
                if (IsKeyDown(InputType.Down)) targetMovement.Y -= 1.0f;
            }

            //the vertical component is only used for falling through platforms and climbing ladders when not in water,
            //so the movement can't be normalized or the Character would walk slower when pressing down/up
            if (AnimController.InWater)
            {
                float length = targetMovement.Length();
                if (length > 0.0f) targetMovement = targetMovement / length;
            }

            bool run = false;
            if ((IsKeyDown(InputType.Run) && AnimController.ForceSelectAnimationType == AnimationType.NotDefined) || ForceRun)
            {
                //can't run if
                //  - dragging someone
                //  - crouching
                //  - moving backwards
                run = (SelectedCharacter == null || !SelectedCharacter.CanBeDragged) &&
                    (!(AnimController is HumanoidAnimController) || !((HumanoidAnimController)AnimController).Crouching) &&
                    !AnimController.IsMovingBackwards;
            }
            
            float currentSpeed = AnimController.GetCurrentSpeed(run);
            targetMovement *= currentSpeed;
            float maxSpeed = ApplyTemporarySpeedLimits(currentSpeed);
            targetMovement.X = MathHelper.Clamp(targetMovement.X, -maxSpeed, maxSpeed);
            targetMovement.Y = MathHelper.Clamp(targetMovement.Y, -maxSpeed, maxSpeed);

            //apply speed multiplier if 
            //  a. it's boosting the movement speed and the character is trying to move fast (= running)
            //  b. it's a debuff that decreases movement speed
            float speedMultiplier = SpeedMultiplier;
            if (run || speedMultiplier <= 0.0f) targetMovement *= speedMultiplier;

            ResetSpeedMultiplier(); // Reset, items will set the value before the next update

            return targetMovement;
        }

        /// <summary>
        /// Can be used to modify the character's speed via StatusEffects
        /// </summary>
        public float SpeedMultiplier
        {
            get
            {
                if (speedMultipliers.Count == 0) return 1f;

                float greatestPositive = 1f;
                float greatestNegative = 1f;

                for (int i = 0; i < speedMultipliers.Count; i++)
                {
                    float val = speedMultipliers[i];
                    if (val < 1f)
                    {
                        if (val < greatestNegative)
                        {
                            greatestNegative = val;
                        }
                    }
                    else
                    {
                        if (val > greatestPositive)
                        {
                            greatestPositive = val;
                        }
                    }
                }

                return greatestPositive - (1f - greatestNegative);
            }
            set
            {
                if (value == 1f) return;
                speedMultipliers.Add(value);
            }
        }

        public void ResetSpeedMultiplier()
        {
            speedMultipliers.Clear();
        }

        public float ApplyTemporarySpeedLimits(float speed)
        {
            var leftFoot = AnimController.GetLimb(LimbType.LeftFoot);
            if (leftFoot != null)
            {
                float footAfflictionStrength = CharacterHealth.GetAfflictionStrength("damage", leftFoot, true);
                speed *= MathHelper.Lerp(1.0f, 0.4f, MathHelper.Clamp(footAfflictionStrength / 80.0f, 0.0f, 1.0f));
            }

            var rightFoot = AnimController.GetLimb(LimbType.RightFoot);
            if (rightFoot != null)
            {
                float footAfflictionStrength = CharacterHealth.GetAfflictionStrength("damage", rightFoot, true);
                speed *= MathHelper.Lerp(1.0f, 0.4f, MathHelper.Clamp(footAfflictionStrength / 80.0f, 0.0f, 1.0f));
            }

            return speed;
        }

        public void Control(float deltaTime, Camera cam)
        {
            ViewTarget = null;
            if (!AllowInput) return;

            if (Controlled == this || (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer))
            {
                SmoothedCursorPosition = cursorPosition;
            }
            else
            {
                //apply some smoothing to the cursor positions of remote players when playing as a client
                //to make aiming look a little less choppy
                Vector2 smoothedCursorDiff = cursorPosition - SmoothedCursorPosition;
                smoothedCursorDiff = NetConfig.InterpolateCursorPositionError(smoothedCursorDiff);
                SmoothedCursorPosition = cursorPosition - smoothedCursorDiff;
            }
            
            if (!(this is AICharacter) || Controlled == this || IsRemotePlayer)
            {
                Vector2 targetMovement = GetTargetMovement();

                AnimController.TargetMovement = targetMovement;
                AnimController.IgnorePlatforms = AnimController.TargetMovement.Y < -0.1f;
            }

            if (AnimController is HumanoidAnimController)
            {
                ((HumanoidAnimController) AnimController).Crouching = IsKeyDown(InputType.Crouch);
            }

            if (AnimController.onGround &&
                !AnimController.InWater &&
                AnimController.Anim != AnimController.Animation.UsingConstruction &&
                AnimController.Anim != AnimController.Animation.CPR &&
                (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient || Controlled == this))
            {
                //Limb head = AnimController.GetLimb(LimbType.Head);
                // Values lower than this seem to cause constantious flipping when the mouse is near the player and the player is running, because the root collider moves after flipping.
                float followMargin = 40;
                if (dontFollowCursor)
                {
                    AnimController.TargetDir = Direction.Right;
                }
                else if (cursorPosition.X < AnimController.Collider.Position.X - followMargin)
                {
                    AnimController.TargetDir = Direction.Left;
                }
                else if (cursorPosition.X > AnimController.Collider.Position.X + followMargin)
                {
                    AnimController.TargetDir = Direction.Right;
                }
            }
            
            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetworkMember.IsServer)
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
                else if (GameMain.NetworkMember.IsClient && Controlled != this)
                {
                    if (memState.Count > 0)
                    {
                        AnimController.TargetDir = memState[0].Direction;
                    }
                }
            }

#if DEBUG
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.F))
            {
                AnimController.ReleaseStuckLimbs();
                if (AIController != null && AIController is EnemyAIController enemyAI)
                {
                    enemyAI.LatchOntoAI?.DeattachFromBody();
                }
            }
#endif

            if (attackCoolDown > 0.0f)
            {
                attackCoolDown -= deltaTime;
            }
            else if (IsKeyDown(InputType.Attack))
            {
                AttackContext currentContext = GetAttackContext();
                var validLimbs = AnimController.Limbs.Where(l => !l.IsSevered && !l.IsStuck && l.attack != null && l.attack.IsValidContext(currentContext));
                var sortedLimbs = validLimbs.OrderBy(l => Vector2.DistanceSquared(ConvertUnits.ToDisplayUnits(l.SimPosition), cursorPosition));
                // Select closest
                var attackLimb = sortedLimbs.FirstOrDefault();
                if (attackLimb != null)
                {
                    Vector2 attackPos = attackLimb.SimPosition + Vector2.Normalize(cursorPosition - attackLimb.Position) * ConvertUnits.ToSimUnits(attackLimb.attack.Range);

                    List<Body> ignoredBodies = AnimController.Limbs.Select(l => l.body.FarseerBody).ToList();
                    ignoredBodies.Add(AnimController.Collider.FarseerBody);

                    var body = Submarine.PickBody(
                        attackLimb.SimPosition,
                        attackPos,
                        ignoredBodies,
                        Physics.CollisionCharacter | Physics.CollisionWall);

                    IDamageable attackTarget = null;
                    if (body != null)
                    {
                        attackPos = Submarine.LastPickedPosition;

                        if (body.UserData is Submarine sub)
                        {
                            body = Submarine.PickBody(
                                attackLimb.SimPosition - ((Submarine)body.UserData).SimPosition,
                                attackPos - ((Submarine)body.UserData).SimPosition,
                                ignoredBodies,
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

                    attackLimb.UpdateAttack(deltaTime, attackPos, attackTarget, out AttackResult attackResult);

                    if (!attackLimb.attack.IsRunning)
                    {
                        attackCoolDown = 1.0f;
                    }
                }
            }

            if (SelectedConstruction == null || !SelectedConstruction.Prefab.DisableItemUsageWhenSelected)
            {
                for (int i = 0; i < selectedItems.Length; i++ )
                {
                    if (selectedItems[i] == null) { continue; }
                    if (i == 1 && selectedItems[0] == selectedItems[1]) { continue; }
                    var item = selectedItems[i];
                    if (item == null) { continue; }
                    if (IsKeyDown(InputType.Aim) || !item.RequireAimToSecondaryUse)
                    {
                        item.SecondaryUse(deltaTime, this);
                    }
                    if (IsKeyDown(InputType.Use) && !item.IsShootable)
                    {
                        if (!item.RequireAimToUse || IsKeyDown(InputType.Aim))
                        {
                            item.Use(deltaTime, this);
                        }
                    }
                    if (IsKeyDown(InputType.Shoot) && item.IsShootable)
                    {
                        if (!item.RequireAimToUse || IsKeyDown(InputType.Aim))
                        {
                            item.Use(deltaTime, this);
                        }
                    }
                }
            }
            
            if (SelectedConstruction != null)
            {
                if (IsKeyDown(InputType.Aim) || !SelectedConstruction.RequireAimToSecondaryUse)
                {
                    SelectedConstruction.SecondaryUse(deltaTime, this);
                }
                if (IsKeyDown(InputType.Use) && !SelectedConstruction.IsShootable)
                {
                    if (!SelectedConstruction.RequireAimToUse || IsKeyDown(InputType.Aim))
                    {
                        SelectedConstruction.Use(deltaTime, this);
                    }
                }
                if (IsKeyDown(InputType.Shoot) && SelectedConstruction.IsShootable)
                {
                    if (!SelectedConstruction.RequireAimToUse || IsKeyDown(InputType.Aim))
                    {
                        SelectedConstruction.Use(deltaTime, this);
                    }
                }
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

        public bool CanSeeCharacter(Character target)
        {
            Limb seeingLimb = GetSeeingLimb();
            foreach (var targetLimb in target.AnimController.Limbs)
            {
                if (CanSeeTarget(targetLimb, seeingLimb))
                {
                    return true;
                }
            }
            return false;
        }

        private Limb GetSeeingLimb()
        {
            Limb selfLimb = AnimController.GetLimb(LimbType.Head);
            if (selfLimb == null) { selfLimb = AnimController.GetLimb(LimbType.Torso); }
            if (selfLimb == null) { selfLimb = AnimController.Limbs.FirstOrDefault(); }
            return selfLimb;
        }

        public bool CanSeeTarget(ISpatialEntity target, Limb seeingLimb = null)
        {
            seeingLimb = seeingLimb ?? GetSeeingLimb();
            if (seeingLimb == null) { return false; }
            // TODO: Could we just use the method below? If not, let's refactor it so that we can.
            Vector2 diff = ConvertUnits.ToSimUnits(target.WorldPosition - seeingLimb.WorldPosition);
            Body closestBody;
            //both inside the same sub (or both outside)
            //OR the we're inside, the other character outside
            if (target.Submarine == Submarine || target.Submarine == null)
            {
                closestBody = Submarine.CheckVisibility(seeingLimb.SimPosition, seeingLimb.SimPosition + diff);
            }
            //we're outside, the other character inside
            else if (Submarine == null)
            {
                closestBody = Submarine.CheckVisibility(target.SimPosition, target.SimPosition - diff);
            }
            //both inside different subs
            else
            {
                closestBody = Submarine.CheckVisibility(seeingLimb.SimPosition, seeingLimb.SimPosition + diff);
                if (!IsBlocking(closestBody))
                {
                    closestBody = Submarine.CheckVisibility(target.SimPosition, target.SimPosition - diff);
                }
            }
            return !IsBlocking(closestBody);

            bool IsBlocking(Body body)
            {
                if (body == null) { return false; }
                if (body.UserData is Structure wall && wall.CastShadow)
                {
                    return wall != target;
                }
                else if (body.UserData is Item item && item != target)
                {
                    var door = item.GetComponent<Door>();
                    if (door != null)
                    {
                        return !door.IsOpen;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// TODO: ensure that works. CheckVisibility takes positions in sim space, but this method uses world positions
        /// </summary>
        public bool CanSeeCharacter(Character target, Vector2 sourceWorldPos)
        {
            Vector2 diff = ConvertUnits.ToSimUnits(target.WorldPosition - sourceWorldPos);
            Body closestBody;
            if (target.Submarine == null)
            {
                closestBody = Submarine.CheckVisibility(sourceWorldPos, sourceWorldPos + diff);
                if (closestBody == null) return true;
            }
            else
            {
                closestBody = Submarine.CheckVisibility(target.WorldPosition, target.WorldPosition - diff);
                if (closestBody == null) return true;
            }
            Structure wall = closestBody.UserData as Structure;
            Item item = closestBody.UserData as Item;
            Door door = item?.GetComponent<Door>();
            return (wall == null || !wall.CastShadow) && (door == null || door.IsOpen);
        }

        public bool HasEquippedItem(Item item)
        {
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                if (Inventory.Items[i] == item && Inventory.SlotTypes[i] != InvSlotType.Any) return true;
            }

            return false;
        }

        public bool HasEquippedItem(string itemIdentifier, bool allowBroken = true)
        {
            if (Inventory == null) return false;
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                if (Inventory.SlotTypes[i] == InvSlotType.Any || Inventory.Items[i] == null) continue;
                if (!allowBroken && Inventory.Items[i].Condition <= 0.0f) continue;
                if (Inventory.Items[i].Prefab.Identifier == itemIdentifier || Inventory.Items[i].HasTag(itemIdentifier)) return true;
            }

            return false;
        }

        public bool HasSelectedItem(Item item)
        {
            return selectedItems.Contains(item);
        }

        public bool TrySelectItem(Item item)
        {
            bool rightHand = Inventory.IsInLimbSlot(item, InvSlotType.RightHand);
            bool leftHand = Inventory.IsInLimbSlot(item, InvSlotType.LeftHand);

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
            if (!CanInteract || inventory.Locked) { return false; }

            //the inventory belongs to some other character
            if (inventory.Owner is Character && inventory.Owner != this)
            {
                var owner = (Character)inventory.Owner;

                //can only be accessed if the character is incapacitated and has been selected
                return SelectedCharacter == owner && owner.CanInventoryBeAccessed;
            }

            if (inventory.Owner is Item)
            {
                var owner = (Item)inventory.Owner;
                if (!CanInteractWith(owner)) { return false; }
                ItemContainer container = owner.GetComponents<ItemContainer>().FirstOrDefault(ic => ic.Inventory == inventory);
                if (container != null && !container.HasRequiredItems(this, addMessage: false)) { return false; }
            }
            return true;
        }

        public bool CanInteractWith(Character c, float maxDist = 200.0f, bool checkVisibility = true)
        {
            if (c == this || Removed || !c.Enabled || !c.CanBeSelected) return false;
            if (!c.CharacterHealth.UseHealthWindow && !c.CanBeDragged && c.onCustomInteract == null) return false;

            maxDist = ConvertUnits.ToSimUnits(maxDist);
            if (Vector2.DistanceSquared(SimPosition, c.SimPosition) > maxDist * maxDist) return false;

            return checkVisibility ? CanSeeCharacter(c) : true;
        }
        
        public bool CanInteractWith(Item item)
        {
            return CanInteractWith(item, out float distanceToItem, checkLinked: true);
        }

        public bool CanInteractWith(Item item, out float distanceToItem, bool checkLinked)
        {
            distanceToItem = -1.0f;

            bool hidden = item.HiddenInGame;
#if CLIENT
            if (Screen.Selected == GameMain.SubEditorScreen) { hidden = false; }
#endif  
            if (!CanInteract || hidden) return false;

            if (item.ParentInventory != null)
            {
                return CanAccessInventory(item.ParentInventory);
            }

            Wire wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                //locked wires are never interactable
                if (wire.Locked) return false;

                //wires are interactable if the character has selected an item the wire is connected to,
                //and it's disconnected from the other end
                if (wire.Connections[0]?.Item != null && SelectedConstruction == wire.Connections[0].Item)
                {
                    return wire.Connections[1] == null;
                }
                if (wire.Connections[1]?.Item != null && SelectedConstruction == wire.Connections[1].Item)
                {
                    return wire.Connections[0] == null;
                }
            }

            if (checkLinked && item.DisplaySideBySideWhenLinked)
            {
                foreach (MapEntity linked in item.linkedTo)
                {
                    if (linked is Item linkedItem)
                    {
                        if (CanInteractWith(linkedItem, out float distToLinked, checkLinked: false))
                        {
                            distanceToItem = distToLinked;
                            return true;
                        }
                    }
                }
            }
            
            if (item.InteractDistance == 0.0f && !item.Prefab.Triggers.Any()) { return false; }
            
            Pickable pickableComponent = item.GetComponent<Pickable>();
            if (pickableComponent != null && (pickableComponent.Picker != null && !pickableComponent.Picker.IsDead)) { return false; }
                        
            Vector2 characterDirection = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(AnimController.Collider.Rotation));

            Vector2 upperBodyPosition = Position + (characterDirection * 20.0f);
            Vector2 lowerBodyPosition = Position - (characterDirection * 60.0f);

            if (Submarine != null)
            {
                upperBodyPosition += Submarine.Position;
                lowerBodyPosition += Submarine.Position;
            }

            bool insideTrigger = item.IsInsideTrigger(upperBodyPosition) || item.IsInsideTrigger(lowerBodyPosition);
            if (item.Prefab.Triggers.Count > 0 && !insideTrigger && item.Prefab.RequireBodyInsideTrigger) { return false; }

            Rectangle itemDisplayRect = new Rectangle(item.InteractionRect.X, item.InteractionRect.Y - item.InteractionRect.Height, item.InteractionRect.Width, item.InteractionRect.Height);

            // Get the point along the line between lowerBodyPosition and upperBodyPosition which is closest to the center of itemDisplayRect
            Vector2 playerDistanceCheckPosition = Vector2.Clamp(itemDisplayRect.Center.ToVector2(), lowerBodyPosition, upperBodyPosition);
            
            // If playerDistanceCheckPosition is inside the itemDisplayRect then we consider the character to within 0 distance of the item
            if (itemDisplayRect.Contains(playerDistanceCheckPosition))
            {
                distanceToItem = 0.0f;
            }
            else
            {
                // Here we get the point on the itemDisplayRect which is closest to playerDistanceCheckPosition
                Vector2 rectIntersectionPoint = new Vector2(
                    MathHelper.Clamp(playerDistanceCheckPosition.X, itemDisplayRect.X, itemDisplayRect.Right),
                    MathHelper.Clamp(playerDistanceCheckPosition.Y, itemDisplayRect.Y, itemDisplayRect.Bottom));
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
        /// Set an action that's invoked when another character interacts with this one.
        /// </summary>
        /// <param name="onCustomInteract">Action invoked when another character interacts with this one. T1 = this character, T2 = the interacting character</param>
        /// <param name="hudText">Displayed on the character when highlighted.</param>
        public void SetCustomInteract(Action<Character, Character> onCustomInteract, string hudText)
        {
            this.onCustomInteract = onCustomInteract;
            customInteractHUDText = hudText;
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
            SelectedCharacter.AnimController?.ResetPullJoints();
            SelectedCharacter = null;
        }

        public void DoInteractionUpdate(float deltaTime, Vector2 mouseSimPos)
        {
            bool isLocalPlayer = Controlled == this;

            if (!isLocalPlayer && (this is AICharacter && !IsRemotePlayer))
            {
                return;
            }

            if (ResetInteract)
            {
                ResetInteract = false;
                return;
            }

            if (!CanInteract)
            {
                SelectedConstruction = null;
                focusedItem = null;
                if (!AllowInput)
                {
                    focusedCharacter = null;
                    if (SelectedCharacter != null) DeselectCharacter();
                    return;
                }
            }

#if CLIENT
            if (isLocalPlayer)
            {
                if (GUI.MouseOn == null && 
                    (!CharacterInventory.IsMouseOnInventory() || CharacterInventory.DraggingItemToWorld))
                {
                    if (findFocusedTimer <= 0.0f || Screen.Selected == GameMain.SubEditorScreen)
                    {
                        focusedCharacter = FindCharacterAtPosition(mouseSimPos);
                        focusedItem = CanInteract ?
                            FindItemAtPosition(mouseSimPos, GameMain.Config.AimAssistAmount * (AnimController.InWater ? 1.5f : 1.0f)) : null;
                        findFocusedTimer = 0.05f;
                    }
                }
                else
                {
                    focusedItem = null; 
                }
                findFocusedTimer -= deltaTime;
            }
#endif
            //climb ladders automatically when pressing up/down inside their trigger area
            Ladder currentLadder = SelectedConstruction?.GetComponent<Ladder>();
            if ((SelectedConstruction == null || currentLadder != null) && 
                !AnimController.InWater && Screen.Selected != GameMain.SubEditorScreen)
            {
                bool climbInput = IsKeyDown(InputType.Up) || IsKeyDown(InputType.Down);
                bool isControlled = Controlled == this;

                Ladder nearbyLadder = null;
                if (isControlled || climbInput)
                {
                    float minDist = float.PositiveInfinity;
                    foreach (Ladder ladder in Ladder.List)
                    {
                        if (ladder == currentLadder)
                        {
                            continue;
                        }
                        else if (currentLadder != null)
                        {
                            //only switch from ladder to another if the ladders are above the current ladders and pressing up, or vice versa
                            if (ladder.Item.WorldPosition.Y > currentLadder.Item.WorldPosition.Y != IsKeyDown(InputType.Up))
                            {
                                continue;
                            }
                        }

                        if (CanInteractWith(ladder.Item, out float dist, checkLinked: false) && dist < minDist)
                        {
                            minDist = dist;
                            nearbyLadder = ladder;
                            if (isControlled) ladder.Item.IsHighlighted = true;
                            break;
                        }
                    }
                }

                if (nearbyLadder != null && climbInput)
                {
                    if (nearbyLadder.Select(this)) SelectedConstruction = nearbyLadder.Item;
                }
            }
            
            if (SelectedCharacter != null && (IsKeyHit(InputType.Grab) || IsKeyHit(InputType.Health))) //Let people use ladders and buttons and stuff when dragging chars
            {
                DeselectCharacter();
            }
            else if (focusedCharacter != null && IsKeyHit(InputType.Grab) && FocusedCharacter.CanBeDragged)
            {
                SelectCharacter(focusedCharacter);
            }
            else if (focusedCharacter != null && IsKeyHit(InputType.Health) && focusedCharacter.CharacterHealth.UseHealthWindow && CanInteractWith(focusedCharacter, 160f, false))
            {
                if (focusedCharacter == SelectedCharacter)
                {
                    DeselectCharacter();
#if CLIENT
                    if (Controlled == this) CharacterHealth.OpenHealthWindow = null;
#endif
                }
                else
                {
                    SelectCharacter(focusedCharacter);
#if CLIENT
                    if (Controlled == this) CharacterHealth.OpenHealthWindow = focusedCharacter.CharacterHealth;
#endif
                }
            }
            else if (focusedCharacter != null && IsKeyHit(InputType.Select) && FocusedCharacter.onCustomInteract != null)
            {
                FocusedCharacter.onCustomInteract(focusedCharacter, this);
            }
            else if (focusedItem != null)
            {
#if CLIENT
                if (CharacterInventory.DraggingItemToWorld) { return; }
#endif
                bool canInteract = focusedItem.TryInteract(this);
#if CLIENT
                if (Controlled == this)
                {
                    focusedItem.IsHighlighted = true;
                    if (canInteract)
                    {
                        CharacterHealth.OpenHealthWindow = null;
                    }
                }
#endif
            }
            else if (IsKeyHit(InputType.Deselect) && SelectedConstruction != null)
            {
                SelectedConstruction = null;
#if CLIENT
                CharacterHealth.OpenHealthWindow = null;
#endif
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
            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
            {
                foreach (Character c in CharacterList)
                {
                    if (!(c is AICharacter) && !c.IsRemotePlayer) continue;

                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        //disable AI characters that are far away from all clients and the host's character and not controlled by anyone
                        if (c == Controlled || c.IsRemotePlayer)
                        {
                            c.Enabled = true;
                        }
                        else
                        {
                            float distSqr = float.MaxValue;
                            foreach (Character otherCharacter in CharacterList)
                            {
                                if (otherCharacter == c || !otherCharacter.IsRemotePlayer) { continue; }
                                distSqr = Math.Min(distSqr, Vector2.DistanceSquared(otherCharacter.WorldPosition, c.WorldPosition));
                            }

#if SERVER
                            for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
                            {
                                var spectatePos = GameMain.Server.ConnectedClients[i].SpectatePos;
                                if (spectatePos != null)
                                {
                                    distSqr = Math.Min(distSqr, Vector2.DistanceSquared(spectatePos.Value, c.WorldPosition));
                                }
                            }
#endif

                            if (distSqr > NetConfig.DisableCharacterDistSqr)
                            {
                                c.Enabled = false;
                                if (c.IsDead && c.AIController is EnemyAIController)
                                {
                                    Entity.Spawner?.AddToRemoveQueue(c);
                                }
                            }
                            else if (distSqr < NetConfig.EnableCharacterDistSqr)
                            {
                                c.Enabled = true;
                            }
                        }
                    }
                    else if (Submarine.MainSub != null)
                    {
                        //disable AI characters that are far away from the sub and the controlled character
                        float distSqr = Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition);
                        if (Controlled != null)
                        {
                            distSqr = Math.Min(distSqr, Vector2.DistanceSquared(Controlled.WorldPosition, c.WorldPosition));
                        }
                        else
                        {
                            distSqr = Math.Min(distSqr, Vector2.DistanceSquared(GameMain.GameScreen.Cam.GetPosition(), c.WorldPosition));
                        }

                        if (distSqr > NetConfig.DisableCharacterDistSqr)
                        {
                            c.Enabled = false;
                            if (c.IsDead && c.AIController is EnemyAIController)
                            {
                                Entity.Spawner?.AddToRemoveQueue(c);
                            }
                        }
                        else if (distSqr < NetConfig.EnableCharacterDistSqr)
                        {
                            c.Enabled = true;
                        }
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
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && this == Controlled && !isSynced) return;

            if (!Enabled) return;

            if (Level.Loaded != null && WorldPosition.Y < Level.MaxEntityDepth ||
                (Submarine != null && Submarine.WorldPosition.Y < Level.MaxEntityDepth))
            {
                Enabled = false;
                Kill(CauseOfDeathType.Pressure, null);
                return;
            }

            ApplyStatusEffects(ActionType.Always, deltaTime);

            PreviousHull = CurrentHull;
            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull, true);

            speechBubbleTimer = Math.Max(0.0f, speechBubbleTimer - deltaTime);

            obstructVisionAmount = Math.Max(obstructVisionAmount - deltaTime, 0.0f);

            if (Inventory != null)
            {
                foreach (Item item in Inventory.Items)
                {
                    if (item == null || item.body == null || item.body.Enabled) continue;

                    item.SetTransform(SimPosition, 0.0f);
                    item.Submarine = Submarine;
                }
            }

            HideFace = false;

            if (IsDead) return;
            
            if (GameMain.NetworkMember != null)
            {
                UpdateNetInput();
            }
            else
            {
                AnimController.Frozen = false;
            }

            DisableImpactDamageTimer -= deltaTime;

            if (!speechImpedimentSet)
            {
                //if no statuseffect or anything else has set a speech impediment, allow speaking normally
                speechImpediment = 0.0f;
            }
            speechImpedimentSet = false;
            
            if (needsAir)
            {
                bool protectedFromPressure = PressureProtection > 0.0f;            
                //cannot be protected from pressure when below crush depth
                protectedFromPressure = protectedFromPressure && WorldPosition.Y > CharacterHealth.CrushDepth;
                //implode if not protected from pressure, and either outside or in a high-pressure hull
                if (!protectedFromPressure &&
                    (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f))
                {
                    if (CharacterHealth.PressureKillDelay <= 0.0f)
                    {
                        PressureTimer = 100.0f;
                    }
                    else
                    {
                        PressureTimer += ((AnimController.CurrentHull == null) ?
                            100.0f : AnimController.CurrentHull.LethalPressure) / CharacterHealth.PressureKillDelay * deltaTime;
                    }

                    if (PressureTimer >= 100.0f)
                    {
                        if (Controlled == this) { cam.Zoom = 5.0f; }
                        if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                        {
                            Implode();
                            return;
                        }
                    }
                }
                else
                {
                    PressureTimer = 0.0f;
                }
            }
            else if ((GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) && WorldPosition.Y < CharacterHealth.CrushDepth)
            {
                //implode if below crush depth, and either outside or in a high-pressure hull                
                if (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f)
                {
                    Implode();
                    return;
                }
            }

            ApplyStatusEffects(AnimController.InWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);            

            UpdateControlled(deltaTime, cam);
            
            //Health effects
            if (needsAir) UpdateOxygen(deltaTime);
            CharacterHealth.Update(deltaTime);
            
            if (IsUnconscious)
            {
                UpdateUnconscious(deltaTime);
                return;
            }

            UpdateAIChatMessages(deltaTime);

            //Do ragdoll shenanigans before Stun because it's still technically a stun, innit? Less network updates for us!
            bool allowRagdoll = GameMain.NetworkMember != null ? GameMain.NetworkMember.ServerSettings.AllowRagdollButton : true;
            bool tooFastToUnragdoll = AnimController.Collider.LinearVelocity.LengthSquared() > 1f;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                tooFastToUnragdoll = false;
            }
            if (IsForceRagdolled)
            {
                IsRagdolled = IsForceRagdolled;
            }
            else if (IsRemotePlayer)
            {
                IsRagdolled = IsKeyDown(InputType.Ragdoll);
            }
            //Keep us ragdolled if we were forced or we're too speedy to unragdoll
            else if (allowRagdoll && (!IsRagdolled || !tooFastToUnragdoll))
            {
                if (ragdollingLockTimer > 0.0f)
                {
                    ragdollingLockTimer -= deltaTime;
                }
                else
                {
                    bool wasRagdolled = IsRagdolled;
                    IsRagdolled = IsKeyDown(InputType.Ragdoll); //Handle this here instead of Control because we can stop being ragdolled ourselves
                    if (wasRagdolled != IsRagdolled) { ragdollingLockTimer = 0.25f; }
                }
           }
            
            UpdateSightRange();
            UpdateSoundRange();

            lowPassMultiplier = MathHelper.Lerp(lowPassMultiplier, 1.0f, 0.1f);
            
            //ragdoll button
            if (IsRagdolled)
            {
                if (AnimController is HumanoidAnimController) ((HumanoidAnimController)AnimController).Crouching = false;
                /*if(GameMain.Server != null)
                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });*/
                AnimController.ResetPullJoints();
                SelectedConstruction = null;
                return;
            }

            //AI and control stuff

            Control(deltaTime, cam);

            bool isNotControlled = Controlled != this;

            if (isNotControlled && (!(this is AICharacter) || IsRemotePlayer))
            {
                Vector2 mouseSimPos = ConvertUnits.ToSimUnits(cursorPosition);
                DoInteractionUpdate(deltaTime, mouseSimPos);
            }
                        
            if (SelectedConstruction != null && !CanInteractWith(SelectedConstruction))
            {
                SelectedConstruction = null;
            }
            
            if (!IsDead) LockHands = false;
        }

        partial void UpdateControlled(float deltaTime, Camera cam);

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        private void UpdateOxygen(float deltaTime)
        {
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
            Stun = Math.Max(5.0f, Stun);

            AnimController.ResetPullJoints();
            SelectedConstruction = null;
        }

        private void UpdateSightRange()
        {
            if (aiTarget == null) { return; }
            float range = (float)Math.Sqrt(Mass) * 250 + AnimController.Collider.LinearVelocity.Length() * 500;
            aiTarget.SightRange = MathHelper.Clamp(range, 0, 10000);
        }

        private void UpdateSoundRange()
        {
            if (aiTarget == null) { return; }
            float range = ((float)Math.Sqrt(Mass) / 3) * (AnimController.TargetMovement.Length() * 2) * Noise;
            aiTarget.SoundRange = MathHelper.Clamp(range, 0, 10000);
        }

        public void SetOrder(Order order, string orderOption, Character orderGiver, bool speak = true)
        {
            if (orderGiver != null)
            {
                //set the character order only if the character is close enough to hear the message
                ChatMessageType messageType = ChatMessage.CanUseRadio(orderGiver) && ChatMessage.CanUseRadio(this) ? 
                    ChatMessageType.Radio : ChatMessageType.Default;
                if (string.IsNullOrEmpty(ChatMessage.ApplyDistanceEffect("message", messageType, orderGiver, this))) return;
            }

            HumanAIController humanAI = AIController as HumanAIController;
            humanAI?.SetOrder(order, orderOption, orderGiver, speak);

            currentOrder = order;
            currentOrderOption = orderOption;
        }

        private List<AIChatMessage> aiChatMessageQueue = new List<AIChatMessage>();
        private List<AIChatMessage> prevAiChatMessages = new List<AIChatMessage>();

        public void DisableLine(string identifier)
        {
            var dummyMsg = new AIChatMessage("", ChatMessageType.Default, identifier)
            {
                SendTime = Timing.TotalTime
            };
            prevAiChatMessages.Add(dummyMsg);
        }

        public void Speak(string message, ChatMessageType? messageType = null, float delay = 0.0f, string identifier = "", float minDurationBetweenSimilar = 0.0f)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(message)) { return; }

            //already sent a similar message a moment ago
            if (!string.IsNullOrEmpty(identifier) && minDurationBetweenSimilar > 0.0f &&
                (aiChatMessageQueue.Any(m => m.Identifier == identifier) ||
                prevAiChatMessages.Any(m => m.Identifier == identifier && m.SendTime > Timing.TotalTime - minDurationBetweenSimilar)))
            {
                return;
            }
            aiChatMessageQueue.Add(new AIChatMessage(message, messageType, identifier, delay));
        }

        private void UpdateAIChatMessages(float deltaTime)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) return;

            List<AIChatMessage> sentMessages = new List<AIChatMessage>();
            foreach (AIChatMessage message in aiChatMessageQueue)
            {
                message.SendDelay -= deltaTime;
                if (message.SendDelay > 0.0f) continue;

                if (message.MessageType == null)
                {
                    message.MessageType = ChatMessage.CanUseRadio(this) ? ChatMessageType.Radio : ChatMessageType.Default;
                }
#if CLIENT
                if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.IsSinglePlayer)
                {
                    string modifiedMessage = ChatMessage.ApplyDistanceEffect(message.Message, message.MessageType.Value, this, Controlled);
                    if (!string.IsNullOrEmpty(modifiedMessage))
                    {
                        GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(info.Name, modifiedMessage, message.MessageType.Value, this);
                    }
                }
#endif
#if SERVER
                if (GameMain.Server != null && message.MessageType != ChatMessageType.Order)
                {
                    GameMain.Server.SendChatMessage(message.Message, message.MessageType.Value, null, this);
                }
#endif
                ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)message.MessageType.Value]);
                sentMessages.Add(message);
            }

            foreach (AIChatMessage sent in sentMessages)
            {
                sent.SendTime = Timing.TotalTime;
                aiChatMessageQueue.Remove(sent);
                prevAiChatMessages.Add(sent);
            }

            for (int i = prevAiChatMessages.Count - 1; i >= 0; i--)
            {
                if (prevAiChatMessages[i].SendTime < Timing.TotalTime - 60.0f)
                {
                    prevAiChatMessages.RemoveRange(0, i + 1);
                    break;
                }
            }
        }


        public void ShowSpeechBubble(float duration, Color color)
        {
            speechBubbleTimer = Math.Max(speechBubbleTimer, duration);
            speechBubbleColor = color;
        }

        partial void DamageHUD(float amount);

        public void SetAllDamage(float damageAmount, float bleedingDamageAmount, float burnDamageAmount)
        {
            CharacterHealth.SetAllDamage(damageAmount, bleedingDamageAmount, burnDamageAmount);
        }

        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            return ApplyAttack(attacker, worldPosition, attack, deltaTime, playSound, null);
        }

        /// <summary>
        /// Apply the specified attack to this character. If the targetLimb is not specified, the limb closest to worldPosition will receive the damage.
        /// </summary>
        public AttackResult ApplyAttack(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false, Limb targetLimb = null)
        {
            if (Removed)
            {
                string errorMsg = "Tried to apply an attack to a removed character (" + Name + ").\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Character.ApplyAttack:RemovedCharacter", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return new AttackResult();
            }

            Limb limbHit = targetLimb;

            float attackImpulse = attack.TargetImpulse + attack.TargetForce * deltaTime;

            var attackResult = targetLimb == null ?
                AddDamage(worldPosition, attack.Afflictions, attack.Stun, playSound, attackImpulse, out limbHit, attacker) :
                DamageLimb(worldPosition, targetLimb, attack.Afflictions, attack.Stun, playSound, attackImpulse, attacker);

            if (limbHit == null) return new AttackResult();
            
            limbHit.body?.ApplyLinearImpulse(attack.TargetImpulseWorld + attack.TargetForceWorld * deltaTime, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
#if SERVER
            if (attacker is Character attackingCharacter && attackingCharacter.AIController == null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(LogName + " attacked by " + attackingCharacter.LogName + ".");
                if (attackResult.Afflictions != null)
                {
                    foreach (Affliction affliction in attackResult.Afflictions)
                    {
                        if (affliction.Strength == 0.0f) continue;
                        sb.Append($" {affliction.Prefab.Name}: {affliction.Strength}");
                    }
                }
                GameServer.Log(sb.ToString(), ServerLog.MessageType.Attack);            
            }
#endif

            bool isNotClient = GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient;

            if (isNotClient &&
                IsDead && Rand.Range(0.0f, 1.0f) < attack.SeverLimbsProbability)
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
        
        public AttackResult AddDamage(Vector2 worldPosition, List<Affliction> afflictions, float stun, bool playSound, float attackImpulse = 0.0f, Character attacker = null)
        {
            return AddDamage(worldPosition, afflictions, stun, playSound, attackImpulse, out _, attacker);
        }

        public AttackResult AddDamage(Vector2 worldPosition, List<Affliction> afflictions, float stun, bool playSound, float attackImpulse, out Limb hitLimb, Character attacker = null)
        {
            hitLimb = null;

            if (Removed) { return new AttackResult(); }

            if (attacker != null && GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowFriendlyFire)
            {
                if (attacker.TeamID == TeamID) { return new AttackResult(); }
            }

            float closestDistance = 0.0f;
            foreach (Limb limb in AnimController.Limbs)
            {
                float distance = Vector2.DistanceSquared(worldPosition, limb.WorldPosition);
                if (hitLimb == null || distance < closestDistance)
                {
                    hitLimb = limb;
                    closestDistance = distance;
                }
            }

            return DamageLimb(worldPosition, hitLimb, afflictions, stun, playSound, attackImpulse, attacker);
        }

        public AttackResult DamageLimb(Vector2 worldPosition, Limb hitLimb, List<Affliction> afflictions, float stun, bool playSound, float attackImpulse, Character attacker = null)
        {
            if (Removed) { return new AttackResult(); }

            if (attacker != null && attacker != this && GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowFriendlyFire)
            {
                if (attacker.TeamID == TeamID) { return new AttackResult(); }
            }

            SetStun(stun);
            Vector2 dir = hitLimb.WorldPosition - worldPosition;
            if (Math.Abs(attackImpulse) > 0.0f)
            {
                Vector2 diff = dir;
                if (diff == Vector2.Zero) diff = Rand.Vector(1.0f);
                hitLimb.body.ApplyLinearImpulse(Vector2.Normalize(diff) * attackImpulse, hitLimb.SimPosition + ConvertUnits.ToSimUnits(diff),
                        maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }
            Vector2 simPos = hitLimb.SimPosition + ConvertUnits.ToSimUnits(dir);
            AttackResult attackResult = hitLimb.AddDamage(simPos, afflictions, playSound);
            CharacterHealth.ApplyDamage(hitLimb, attackResult);
            if (attacker != this)
            {
                OnAttacked?.Invoke(attacker, attackResult);
                OnAttackedProjSpecific(attacker, attackResult);
            };

            if (attacker != null && attackResult.Damage > 0.0f)
            {
                LastAttacker = attacker;
            }

            return attackResult;
        }

        partial void OnAttackedProjSpecific(Character attacker, AttackResult attackResult);

        public void SetStun(float newStun, bool allowStunDecrease = false, bool isNetworkMessage = false)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && !isNetworkMessage) return;
            
            if ((newStun <= Stun && !allowStunDecrease) || !MathUtils.IsValid(newStun)) return;
            
            if (Math.Sign(newStun) != Math.Sign(Stun)) AnimController.ResetPullJoints();

            CharacterHealth.StunTimer = newStun;
            if (newStun > 0.0f)
            {
                SelectedConstruction = null;
            }
        }

        public void ApplyStatusEffects(ActionType actionType, float deltaTime)
        {
            foreach (StatusEffect statusEffect in statusEffects)
            {
                if (statusEffect.type != actionType) { continue; }
                if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                    statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    var targets = new List<ISerializableEntity>();
                    statusEffect.GetNearbyTargets(WorldPosition, targets);
                    statusEffect.Apply(ActionType.OnActive, deltaTime, this, targets);
                }
                else
                {
                    statusEffect.Apply(actionType, deltaTime, this, this);
                }
            }
        }

        private void Implode(bool isNetworkMessage = false)
        {
            if (CharacterHealth.Unkillable) { return; }

            if (!isNetworkMessage)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) return; 
            }

            Kill(CauseOfDeathType.Pressure, null, isNetworkMessage);
            CharacterHealth.PressureAffliction.Strength = CharacterHealth.PressureAffliction.Prefab.MaxStrength;
            CharacterHealth.SetAllDamage(200.0f, 0.0f, 0.0f);
            BreakJoints();
        }

        public void BreakJoints()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();
            foreach (Limb limb in AnimController.Limbs)
            {
                limb.AddDamage(limb.SimPosition, 500.0f, 0.0f, 0.0f, false);

                Vector2 diff = centerOfMass - limb.SimPosition;

                if (!MathUtils.IsValid(diff))
                {
                    string errorMsg = "Attempted to apply an invalid impulse to a limb in Character.BreakJoints (" + diff + "). Limb position: " + limb.SimPosition + ", center of mass: " + centerOfMass + ".";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Ragdoll.GetCenterOfMass", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }

                if (diff == Vector2.Zero) { continue; }
                limb.body.ApplyLinearImpulse(diff * 50.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }

            ImplodeFX();

            foreach (var joint in AnimController.LimbJoints)
            {
                joint.LimitEnabled = false;
            }
        }

        partial void ImplodeFX();

        public void Kill(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction, bool isNetworkMessage = false)
        {
            if (IsDead || CharacterHealth.Unkillable) { return; }

            HealthUpdateInterval = 0.0f;
            
            //clients aren't allowed to kill characters unless they receive a network message
            if (!isNetworkMessage && GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return;
            }

            ApplyStatusEffects(ActionType.OnDeath, 1.0f);

            AnimController.Frozen = false;
            
            if (GameSettings.SendUserStatistics)
            {
                string characterType = "Unknown";

                if (this == Controlled)
                    characterType = "Player";
                else if (IsRemotePlayer)
                    characterType = "RemotePlayer";
                else if (AIController is EnemyAIController)
                    characterType = "Enemy";
                else if (AIController is HumanAIController)
                    characterType = "AICrew";

                string causeOfDeathStr = causeOfDeathAffliction == null ?
                    causeOfDeath.ToString() : causeOfDeathAffliction.Prefab.Name.Replace(" ", "");
                GameAnalyticsManager.AddDesignEvent("Kill:" + characterType + ":" + SpeciesName + ":" + causeOfDeathStr);
            }

            CauseOfDeath = new CauseOfDeath(
                causeOfDeath, causeOfDeathAffliction?.Prefab, 
                causeOfDeathAffliction?.Source ?? LastAttacker, LastDamageSource);
            OnDeath?.Invoke(this, CauseOfDeath);

            SteamAchievementManager.OnCharacterKilled(this, CauseOfDeath);

            KillProjSpecific(causeOfDeath, causeOfDeathAffliction);

            IsDead = true;

            if (info != null) info.CauseOfDeath = CauseOfDeath;
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            for (int i = 0; i < selectedItems.Length; i++ )
            {
                if (selectedItems[i] != null) selectedItems[i].Drop(this);            
            }
            SelectedConstruction = null;
            
            AnimController.ResetPullJoints();

            foreach (RevoluteJoint joint in AnimController.LimbJoints)
            {
                joint.MotorEnabled = false;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.KillCharacter(this);
            }
        }
        partial void KillProjSpecific(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction);

        public void Revive()
        {
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to revive an already removed character\n" + Environment.StackTrace);
                return;
            }

            IsDead = false;

            if (aiTarget != null)
            {
                aiTarget.Remove();
            }

            aiTarget = new AITarget(this);
            SetAllDamage(0.0f, 0.0f, 0.0f);
            CharacterHealth.RemoveAllAfflictions();

            foreach (LimbJoint joint in AnimController.LimbJoints)
            {
                joint.MotorEnabled = true;
                joint.Enabled = true;
                joint.IsSevered = false;
            }

            foreach (Limb limb in AnimController.Limbs)
            {
#if CLIENT
                if (limb.LightSource != null) limb.LightSource.Color = limb.InitialLightSourceColor;
#endif
                limb.body.Enabled = true;
                limb.IsSevered = false;
            }

            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.ReviveCharacter(this);
            }
        }

        public override void Remove()
        {
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to remove an already removed character\n" + Environment.StackTrace);
                return;
            }
            DebugConsole.Log("Removing character " + Name + " (ID: " + ID + ")");

            base.Remove();

            if (selectedItems[0] != null) { selectedItems[0].Drop(this); }
            if (selectedItems[1] != null) { selectedItems[1].Drop(this); }

            if (info != null) { info.Remove(); }

#if CLIENT
            GameMain.GameSession?.CrewManager?.KillCharacter(this);
#endif

            CharacterList.Remove(this);

            if (Controlled == this) { Controlled = null; }

            if (Inventory != null)
            {
                foreach (Item item in Inventory.Items)
                {
                    if (item != null)
                    {
                        Spawner?.AddToRemoveQueue(item);
                    }
                }
            }

            DisposeProjSpecific();

            aiTarget?.Remove();
            AnimController?.Remove();
            CharacterHealth?.Remove();

            foreach (Character c in CharacterList)
            {
                if (c.focusedCharacter == this) { c.focusedCharacter = null; }
                if (c.SelectedCharacter == this) { c.SelectedCharacter = null; }
            }
        }
        partial void DisposeProjSpecific();

        public void TeleportTo(Vector2 worldPos)
        {
            AnimController.CurrentHull = null;
            Submarine = null;
            AnimController.SetPosition(ConvertUnits.ToSimUnits(worldPos), false);
            AnimController.FindHull(worldPos, true);
        }

        public void SaveInventory(Inventory inventory, XElement parentElement)
        {
            var items = Array.FindAll(inventory.Items, i => i != null).Distinct();
            foreach (Item item in items)
            {
                item.Submarine = inventory.Owner.Submarine;
                var itemElement = item.Save(parentElement);

                List<int> slotIndices = new List<int>();
                for (int i = 0; i < inventory.Capacity; i++)
                {
                    if (inventory.Items[i] == item) { slotIndices.Add(i); }
                }

                itemElement.Add(new XAttribute("i", string.Join(",", slotIndices)));

                foreach (ItemContainer container in item.GetComponents<ItemContainer>())
                {
                    XElement childInvElement = new XElement("inventory");
                    itemElement.Add(childInvElement);
                    SaveInventory(container.Inventory, childInvElement);
                }
            }
        }

        public AttackContext GetAttackContext() => AnimController.CurrentAnimationParams.IsGroundedAnimation ? AttackContext.Ground : AttackContext.Water;

        private readonly List<Hull> visibleHulls = new List<Hull>();
        private readonly HashSet<Hull> tempList = new HashSet<Hull>();
        /// <summary>
        /// Returns hulls that are visible to the player, including the current hull.
        /// Can be heavy if used every frame.
        /// </summary>
        public List<Hull> GetVisibleHulls()
        {
            visibleHulls.Clear();
            tempList.Clear();
            if (CurrentHull != null)
            {
                visibleHulls.Add(CurrentHull);
                var adjacentHulls = CurrentHull.GetConnectedHulls(true, 1);
                float maxDistance = 1000f;
                foreach (var hull in adjacentHulls)
                {
                    if (hull.ConnectedGaps.Any(g => g.Open > 0.9f && g.linkedTo.Contains(CurrentHull) &&
                        Vector2.DistanceSquared(g.WorldPosition, WorldPosition) < Math.Pow(maxDistance / 2, 2)))
                    {
                        if (Vector2.DistanceSquared(hull.WorldPosition, WorldPosition) < Math.Pow(maxDistance, 2))
                        {
                            visibleHulls.Add(hull);
                        }
                    }
                }
                visibleHulls.AddRange(CurrentHull.GetLinkedEntities<Hull>(tempList, filter: h =>
                {
                    // Ignore adjacent hulls because they were already handled above
                    if (adjacentHulls.Contains(h))
                    {
                        return false;
                    }
                    else
                    {
                        if (h.ConnectedGaps.Any(g =>
                            g.Open > 0.9f &&
                            Vector2.DistanceSquared(g.WorldPosition, WorldPosition) < Math.Pow(maxDistance / 2, 2) &&
                            CanSeeTarget(g)))
                        {
                            return Vector2.DistanceSquared(h.WorldPosition, WorldPosition) < Math.Pow(maxDistance, 2);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }));
            }
            return visibleHulls;
        }
    }
}
