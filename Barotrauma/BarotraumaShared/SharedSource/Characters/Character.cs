using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using Barotrauma.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using FarseerPhysics.Dynamics;
using Barotrauma.Extensions;
using System.Collections.Immutable;
using Barotrauma.Abilities;
using System.Diagnostics;
#if SERVER
using System.Text;
#endif

namespace Barotrauma
{
    public enum CharacterTeamType
    {
        None = 0,
        Team1 = 1,
        Team2 = 2,
        FriendlyNPC = 3
    }

    public readonly record struct TalentResistanceIdentifier(Identifier ResistanceIdentifier, Identifier TalentIdentifier);

    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerPositionSync
    {
        public readonly static List<Character> CharacterList = new List<Character>();

        public const float MaxHighlightDistance = 150.0f;
        public const float MaxDragDistance = 200.0f;

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
                if (value == enabled) { return; }

                if (Removed)
                {
                    enabled = false;
                    return;
                }

                enabled = value;

                foreach (Limb limb in AnimController.Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    if (limb.body != null)
                    {
                        limb.body.Enabled = enabled;
                    }
                    UpdateLimbLightSource(limb);
                }
                AnimController.Collider.Enabled = value;
            }
        }


        private bool disabledByEvent;
        /// <summary>
        /// MonsterEvents disable monsters (which includes removing them from the character list, so they essentially "don't exist") until they're ready to spawn
        /// </summary>
        public bool DisabledByEvent
        {
            get { return disabledByEvent; }
            set 
            {
                if (value == disabledByEvent) { return; }
                disabledByEvent = value;
                if (disabledByEvent)
                {
                    Enabled = false;
                    CharacterList.Remove(this);
                    if (AiTarget != null) { AITarget.List.Remove(AiTarget); }
                }
                else
                {
                    if (!CharacterList.Contains(this)) { CharacterList.Add(this); }
                    if (AiTarget != null && !AITarget.List.Contains(AiTarget)) { AITarget.List.Add(AiTarget); }
                }
            }
        }

        public Hull PreviousHull = null;
        public Hull CurrentHull = null;

        /// <summary>
        /// Is the character controlled remotely (either by another player, or a server-side AIController)
        /// </summary>
        public bool IsRemotelyControlled
        {
            get
            {
                if (GameMain.NetworkMember == null)
                {
                    return false;
                }
                else if (GameMain.NetworkMember.IsClient)
                {
                    //all characters except the client's own character are controlled by the server
                    return this != Controlled;
                }
                else
                {
                    return IsRemotePlayer;
                }
            }
        }

        /// <summary>
        /// Is the character controlled by another human player (should always be false in single player)
        /// </summary>
        public bool IsRemotePlayer { get; set; }

        public bool IsLocalPlayer => Controlled == this;
        public bool IsPlayer => Controlled == this || IsRemotePlayer;

        /// <summary>
        /// Is the character player or does it have an active ship command manager (an AI controlled sub)? Bots in the player team are not treated as commanders.
        /// </summary>
        public bool IsCommanding => IsPlayer || (AIController is HumanAIController humanAI && humanAI.ShipCommandManager != null && humanAI.ShipCommandManager.Active);
        public bool IsBot => !IsPlayer && AIController is HumanAIController humanAI && humanAI.Enabled;
        public bool IsEscorted { get; set; }
        public Identifier JobIdentifier => Info?.Job?.Prefab.Identifier ?? Identifier.Empty;

        public bool DoesBleed
        {
            get => Params.Health.DoesBleed;
            set => Params.Health.DoesBleed = value;
        }

        public readonly Dictionary<Identifier, SerializableProperty> Properties;
        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get { return Properties; }
        }

        public Key[] Keys
        {
            get { return keys; }
        }

        protected Key[] keys;

        private HumanPrefab humanPrefab;
        public HumanPrefab HumanPrefab
        {
            get { return humanPrefab; }
            set
            {
                if (humanPrefab == value) { return; }
                humanPrefab = value;

                if (humanPrefab != null)
                {
                    HumanPrefabHealthMultiplier = humanPrefab.HealthMultiplier;
                    if (GameMain.NetworkMember != null)
                    {
                        HumanPrefabHealthMultiplier *= humanPrefab.HealthMultiplierInMultiplayer;
                    }
                }
                else
                {
                    HumanPrefabHealthMultiplier = 1.0f;
                }
            }
        }

        private Identifier? faction;
        public Identifier Faction
        {
            get { return faction ?? HumanPrefab?.Faction ?? Identifier.Empty; }
            set { faction = value; }
        }

        private CharacterTeamType teamID;
        public CharacterTeamType TeamID
        {
            get { return teamID; }
            set
            {
                teamID = value;
                if (info != null) { info.TeamID = value; }
            }
        }


        private CharacterTeamType? originalTeamID;
        public CharacterTeamType OriginalTeamID
        {
            get { return originalTeamID ?? teamID; }
        }

        private Wallet wallet;

        public Wallet Wallet
        {
            get
            {
                ThrowIfAccessingWalletsInSingleplayer();
                return wallet;
            }
            set
            {
                ThrowIfAccessingWalletsInSingleplayer();
                wallet = value;
            }
        }

        public readonly HashSet<LatchOntoAI> Latchers = new HashSet<LatchOntoAI>();
        public readonly HashSet<Projectile> AttachedProjectiles = new HashSet<Projectile>();

        protected readonly Dictionary<string, ActiveTeamChange> activeTeamChanges = new Dictionary<string, ActiveTeamChange>();
        protected ActiveTeamChange currentTeamChange;
        private const string OriginalChangeTeamIdentifier = "original";

        private void ThrowIfAccessingWalletsInSingleplayer()
        {
#if CLIENT && DEBUG
            if (Screen.Selected is TestScreen) { return; }
#endif
            if ((GameMain.NetworkMember is null || GameMain.IsSingleplayer) && IsPlayer)
            {
                throw new InvalidOperationException($"Tried to access crew wallets in singleplayer. Use {nameof(CampaignMode)}.{nameof(CampaignMode.Bank)} or {nameof(CampaignMode)}.{nameof(CampaignMode.GetWallet)} instead.");
            }
        }

        public void SetOriginalTeam(CharacterTeamType newTeam)
        {
            TryRemoveTeamChange(OriginalChangeTeamIdentifier);
            currentTeamChange = new ActiveTeamChange(newTeam, ActiveTeamChange.TeamChangePriorities.Base);
            TryAddNewTeamChange(OriginalChangeTeamIdentifier, currentTeamChange);
        }

        private void ChangeTeam(CharacterTeamType newTeam)
        {
            if (newTeam == teamID) { return; }
            if (originalTeamID == null) { originalTeamID = teamID; }
            TeamID = newTeam;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return;
            }
            // clear up any duties the character might have had from its old team (autonomous objectives are automatically recreated)
            var order = OrderPrefab.Dismissal.CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: this).WithManualPriority(CharacterInfo.HighestManualOrderPriority);
            SetOrder(order, isNewOrder: true, speak: false);

#if SERVER
            GameMain.NetworkMember.CreateEntityEvent(this, new TeamChangeEventData());
#endif
        }

        public bool HasTeamChange(string identifier)
        {
            return activeTeamChanges.ContainsKey(identifier);
        }

        public bool TryAddNewTeamChange(string identifier, ActiveTeamChange newTeamChange)
        {
            bool success = activeTeamChanges.TryAdd(identifier, newTeamChange);
            if (success)
            {
                if (currentTeamChange == null)
                {
                    // set team logic to use active team changes as soon as the first team change is added
                    SetOriginalTeam(TeamID);
                }
            }
            else
            {
#if DEBUG
                DebugConsole.ThrowError("Tried to add an existing team change! Make sure to check if the team change exists first.");
#endif
            }
            return success;
        }
        public bool TryRemoveTeamChange(string identifier)
        {
            if (activeTeamChanges.TryGetValue(identifier, out ActiveTeamChange removedTeamChange))
            {
                if (currentTeamChange == removedTeamChange)
                {
                    currentTeamChange = activeTeamChanges[OriginalChangeTeamIdentifier];
                }
            }
            return activeTeamChanges.Remove(identifier);
        }

        public void UpdateTeam()
        {
            if (currentTeamChange == null)
            {
                return;
            }

            ActiveTeamChange bestTeamChange = currentTeamChange;
            foreach (var desiredTeamChange in activeTeamChanges) // order of iteration matters because newest is preferred when multiple same-priority team changes exist
            {
                if (bestTeamChange.TeamChangePriority < desiredTeamChange.Value.TeamChangePriority)
                {
                    bestTeamChange = desiredTeamChange.Value;
                }
            }
            if (TeamID != bestTeamChange.DesiredTeamId) 
            {
                ChangeTeam(bestTeamChange.DesiredTeamId);
                currentTeamChange = bestTeamChange;

                if (bestTeamChange.AggressiveBehavior) // this seemed like the least disruptive way to induce aggressive behavior
                {
                    var order = OrderPrefab.Prefabs["fightintruders"].CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: this).WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                    SetOrder(order, isNewOrder: true, speak: false);
                }
            }
        }

        public bool IsOnPlayerTeam => teamID == CharacterTeamType.Team1 || teamID == CharacterTeamType.Team2;

        public bool IsOriginallyOnPlayerTeam => originalTeamID == CharacterTeamType.Team1 || originalTeamID == CharacterTeamType.Team2;

        public bool IsInstigator => CombatAction != null && CombatAction.IsInstigator;
        public CombatAction CombatAction;

        public AnimController AnimController;

        private Vector2 cursorPosition;

        protected float oxygenAvailable;

        //seed used to generate this character
        public readonly string Seed;
        protected Item focusedItem;
        private Character selectedCharacter, selectedBy;

        private const int maxLastAttackerCount = 4;

        public class Attacker
        {
            public Character Character;
            public float Damage;
        }

        private readonly List<Attacker> lastAttackers = new List<Attacker>();
        public IEnumerable<Attacker> LastAttackers => lastAttackers;
        public Character LastAttacker => lastAttackers.LastOrDefault()?.Character;
        public Character LastOrderedCharacter { get; private set; }
        public Character SecondLastOrderedCharacter { get; private set; }

        public Entity LastDamageSource;

        public AttackResult LastDamage;

        public Dictionary<ItemPrefab, double> ItemSelectedDurations
        {
            get { return itemSelectedDurations; }
        }
        private readonly Dictionary<ItemPrefab, double> itemSelectedDurations = new Dictionary<ItemPrefab, double>();
        private double itemSelectedTime;

        public float InvisibleTimer { get; set; }

        public readonly CharacterPrefab Prefab;

        public readonly CharacterParams Params;

        public Identifier SpeciesName => Params?.SpeciesName ?? "null".ToIdentifier();

        public Identifier Group => HumanPrefab is HumanPrefab humanPrefab && !humanPrefab.Group.IsEmpty ? humanPrefab.Group : Params.Group;

        public bool IsHumanoid => Params.Humanoid;

        public bool IsMachine => Params.IsMachine;

        public bool IsHusk => Params.Husk;

        public bool IsMale => info?.IsMale ?? false;

        public bool IsFemale => info?.IsFemale ?? false;

        public string BloodDecalName => Params.BloodDecal;

        public bool CanSpeak
        {
            get => Params.CanSpeak;
            set => Params.CanSpeak = value;
        }

        public bool NeedsAir
        {
            get => Params.NeedsAir;
            set => Params.NeedsAir = value;
        }

        public bool NeedsWater
        {
            get => Params.NeedsWater;
            set => Params.NeedsWater = value;
        }

        public bool NeedsOxygen => NeedsAir || NeedsWater && !AnimController.InWater;

        public float Noise
        {
            get => Params.Noise;
            set => Params.Noise = value;
        }

        public float Visibility
        {
            get => Params.Visibility;
            set => Params.Visibility = value;
        }

        public bool IsTraitor
        {
            get;
            set;
        }

        public LocalizedString TraitorCurrentObjective = "";
        public bool IsHuman => SpeciesName == CharacterPrefab.HumanSpeciesName;

        private float attackCoolDown;

        public List<Order> CurrentOrders => Info?.CurrentOrders;
        public bool IsDismissed => GetCurrentOrderWithTopPriority() == null;

        private readonly Dictionary<ActionType, List<StatusEffect>> statusEffects = new Dictionary<ActionType, List<StatusEffect>>();

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
                if (info != null && info != value)
                {
                    info.Remove();
                }
                info = value;
                if (info != null)
                {
                    info.Character = this;
                }
            }
        }

        public Identifier VariantOf => Prefab.VariantOf;

        public string Name
        {
            get
            {
                return info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name : SpeciesName.Value;
            }
        }

        public string DisplayName
        {
            get
            {
                if (IsPet)
                {
                    string petName = (AIController as EnemyAIController).PetBehavior.GetTagName();
                    if (!string.IsNullOrEmpty(petName)) { return petName; }
                }

                if (info != null && !string.IsNullOrWhiteSpace(info.Name)) { return info.Name; }
                LocalizedString displayName = Params.DisplayName;
                if (displayName.IsNullOrWhiteSpace())
                {
                    if (Params.SpeciesTranslationOverride.IsEmpty)
                    {
                        displayName = TextManager.Get($"Character.{SpeciesName}");
                    }
                    else
                    {
                        displayName = TextManager.Get($"Character.{Params.SpeciesTranslationOverride}");
                    }
                }
                return displayName.IsNullOrWhiteSpace() ? Name : displayName.Value;
            }
        }

        //Only used by server logs to determine "true identity" of the player for cases when they're disguised
        public string LogName
        {
            get
            {
                if (GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowDisguises) return Name;
                return info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name + (info.DisplayName != info.Name ? " (as " + info.DisplayName + ")" : "") : SpeciesName.Value;
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
                bool wasHidden = HideFace;
                hideFaceTimer = MathHelper.Clamp(hideFaceTimer + (value ? 1.0f : -0.5f), 0.0f, 10.0f);
                bool isHidden = HideFace;
                if (isHidden != wasHidden && info != null && info.IsDisguisedAsAnother != isHidden)
                {
                    info.CheckDisguiseStatus(true);
                }
            }
        }

        public string ConfigPath => Params.File.Path.Value;

        public float Mass
        {
            get { return AnimController.Mass; }
        }

        public CharacterInventory Inventory { get; private set; }

        private Color speechBubbleColor;
        private float speechBubbleTimer;

        /// <summary>
        /// Prevents the character from interacting with items or characters
        /// </summary>
        public bool DisableInteract { get; set; }

        /// <summary>
        /// Prevents the character from highlighting items or characters with the cursor, 
        /// meaning it can't interact with anything but the things it has currently selected/equipped
        /// </summary>
        public bool DisableFocusingOnEntities { get; set; }

        //text displayed when the character is highlighted if custom interact is set
        public LocalizedString CustomInteractHUDText { get; private set; }
        private Action<Character, Character> onCustomInteract;
        public ConversationAction ActiveConversation;

        public bool RequireConsciousnessForCustomInteract = true;
        public bool AllowCustomInteract
        {
            get { return (!RequireConsciousnessForCustomInteract || (!IsIncapacitated && Stun <= 0.0f)) && !Removed; }
        }

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
                if (value)
                {
                    SelectedCharacter = null;
                }
#if CLIENT
                HintManager.OnHandcuffed(this);
#endif
            }
        }

        public bool AllowInput => !Removed && !IsIncapacitated && Stun <= 0.0f;

        public bool CanMove
        {
            get
            {
                if (!AnimController.InWater && !AnimController.CanWalk) { return false; }
                if (!AllowInput) { return false; }
                return true;
            }
        }
        public bool CanInteract => AllowInput && Params.CanInteract && !LockHands;

        // Eating is not implemented for humanoids. If we implement that at some point, we could remove this restriction.
        public bool CanEat => !IsHumanoid && Params.CanEat && AllowInput && AnimController.GetLimb(LimbType.Head) != null;

        public Vector2 CursorPosition
        {
            get { return cursorPosition; }
            set
            {
                if (!MathUtils.IsValid(value)) { return; }
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

        public Character FocusedCharacter { get; set; }

        public Character SelectedCharacter
        {
            get { return selectedCharacter; }
            set
            {
                if (value == selectedCharacter) { return; }
                if (selectedCharacter != null) { selectedCharacter.selectedBy = null; }                   
                selectedCharacter = value;
                if (selectedCharacter != null) {selectedCharacter.selectedBy = this; }
#if CLIENT
                CharacterHealth.SetHealthBarVisibility(value == null);
#endif
                bool isServerOrSingleplayer = GameMain.IsSingleplayer || GameMain.NetworkMember is { IsServer: true };
                CheckTalents(AbilityEffectType.OnLootCharacter, new AbilityCharacterLoot(value));

                if (IsPlayer && isServerOrSingleplayer && value is { IsDead: true, Wallet: { Balance: var balance and > 0 } grabbedWallet })
                {
#if SERVER
                    if (GameMain.GameSession.Campaign is MultiPlayerCampaign mpCampaign && GameMain.Server is { ServerSettings: { } settings })
                    {
                        switch (settings.LootedMoneyDestination)
                        {
                            case LootedMoneyDestination.Wallet when IsPlayer:
                                Wallet.Give(balance);
                                break;
                             default:
                                mpCampaign.Bank.Give(balance);
                                break;

                        }
                    }

                    GameServer.Log($"{GameServer.CharacterLogName(this)} grabbed {value.Name}'s body and received {grabbedWallet.Balance} mk.", ServerLog.MessageType.Money);
#elif CLIENT
                    if (GameMain.GameSession.Campaign is SinglePlayerCampaign spCampaign)
                    {
                        spCampaign.Bank.Give(balance);
                    }
#endif

                    grabbedWallet.Deduct(balance);
                }
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

        /// <summary>
        /// Items the character has in their hand slots. Doesn't return nulls and only returns items held in both hands once.
        /// </summary>
        public IEnumerable<Item> HeldItems
        {
            get
            {
                var item1 = Inventory?.GetItemInLimbSlot(InvSlotType.RightHand);
                var item2 = Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand);
                if (item1 != null) { yield return item1; }
                if (item2 != null && item2 != item1) { yield return item2; }
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
                obstructVisionAmount = value ? 1.0f : 0.0f;
            }
        }

        private double pressureProtectionLastSet;
        private float pressureProtection;
        public float PressureProtection
        {
            get { return pressureProtection; }
            set
            {
                pressureProtection = Math.Max(value, pressureProtection);
                pressureProtectionLastSet = Timing.TotalTime;
            }
        }

        /// <summary>
        /// Can be used by status effects to check whether the characters is in a high-pressure environment
        /// </summary>
        public bool InPressure
        {
            get { return CurrentHull == null || CurrentHull.LethalPressure > 0.0f; }
        }

        /// <summary>
        /// Can be used by status effects
        /// </summary>
        public AnimController.Animation Anim
        {
            get { return AnimController?.Anim ?? AnimController.Animation.None; }
        }

        public const float KnockbackCooldown = 5.0f;
        public float KnockbackCooldownTimer;

        private float ragdollingLockTimer;
        public bool IsRagdolled;
        public bool IsForceRagdolled;
        public bool dontFollowCursor;

        public bool IsIncapacitated
        {
            get
            {
                if (IsUnconscious) { return true; }
                return CharacterHealth.GetAllAfflictions().Any(a => a.Prefab.AfflictionType == AfflictionPrefab.ParalysisType && a.Strength >= a.Prefab.MaxStrength);
            }
        }

        public bool IsUnconscious
        {
            get { return CharacterHealth.IsUnconscious; }
        }

        public bool IsArrested
        {
            get { return IsHuman && HasEquippedItem("handlocker"); }
        }

        public bool IsPet
        {
            get { return AIController is EnemyAIController enemyController && enemyController.PetBehavior != null; }
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

        public float HullOxygenPercentage
        {
            get { return CurrentHull?.OxygenPercentage ?? 0.0f; }
        }

        public bool UseHullOxygen { get; set; } = true;

        public float Stun
        {
            get { return IsRagdolled && !AnimController.IsHanging ? 1.0f : CharacterHealth.Stun; }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
                SetStun(value, true);
            }
        }

        public CharacterHealth CharacterHealth { get; private set; }

        public bool DisableHealthWindow;

        // These properties needs to be exposed for status effects
        public float Vitality => CharacterHealth.Vitality;
        public float Health => Vitality;
        public float HealthPercentage => CharacterHealth.HealthPercentage;
        public float MaxVitality => CharacterHealth.MaxVitality;
        public float MaxHealth => MaxVitality;

        /// <summary>
        /// Was the character in full health at the beginning of the frame?
        /// </summary>
        public bool WasFullHealth => CharacterHealth.WasInFullHealth;
        public AIState AIState => AIController is EnemyAIController enemyAI ? enemyAI.State : AIState.Idle;
        public bool IsLatched => AIController is EnemyAIController enemyAI && enemyAI.LatchOntoAI != null && enemyAI.LatchOntoAI.IsAttached;
        public float EmpVulnerability => Params.Health.EmpVulnerability;
        public float PoisonVulnerability => Params.Health.PoisonVulnerability;

        public float Bloodloss
        {
            get { return CharacterHealth.BloodlossAmount; }
            set
            {
                if (!MathUtils.IsValid(value)) { return; }
                CharacterHealth.BloodlossAmount = value;
            }
        }

        public float Bleeding
        {
            get { return CharacterHealth.GetAfflictionStrength(AfflictionPrefab.BleedingType, true); }
        }

        private bool speechImpedimentSet;

        //value between 0-100 (50 = speech range is reduced by 50%)
        private float speechImpediment;
        public float SpeechImpediment
        {
            get
            {
                if (!CanSpeak || IsUnconscious || IsKnockedDown) { return 100.0f; }
                return speechImpediment;
            }
            set
            {
                if (value < speechImpediment) { return; }
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

        public bool IgnoreMeleeWeapons
        {
            get;
            set;
        }

        /// <summary>
        /// Current speed of the character's collider. Can be used by status effects to check if the character is moving.
        /// </summary>
        public float CurrentSpeed
        {
            get { return AnimController?.Collider?.LinearVelocity.Length() ?? 0.0f; }
        }

        private Item _selectedItem;
        /// <summary>
        /// The primary selected item. It can be any device that character interacts with. This excludes items like ladders and chairs which are assigned to <see cref="SelectedSecondaryItem"/>.
        /// </summary>
        public Item SelectedItem
        {
            get => _selectedItem;
            set
            {
                var prevSelectedItem = _selectedItem;
                _selectedItem = value;
#if CLIENT
                HintManager.OnSetSelectedItem(this, prevSelectedItem, _selectedItem);
                if (Controlled == this)
                {
                    if (_selectedItem == null)
                    {
                        GameMain.GameSession?.CrewManager?.ResetCrewList();
                    }
                    else if (!_selectedItem.IsLadder)
                    {
                        GameMain.GameSession?.CrewManager?.AutoHideCrewList();
                    }
                }
#endif
                if (prevSelectedItem != null && (_selectedItem == null || _selectedItem != prevSelectedItem) && itemSelectedTime > 0)
                {
                    double selectedDuration = Timing.TotalTime - itemSelectedTime;
                    if (itemSelectedDurations.ContainsKey(prevSelectedItem.Prefab))
                    {
                        itemSelectedDurations[prevSelectedItem.Prefab] += selectedDuration;
                    }
                    else
                    {
                        itemSelectedDurations.Add(prevSelectedItem.Prefab, selectedDuration);
                    }
                    itemSelectedTime = 0;
                }
                if (_selectedItem != null && (prevSelectedItem == null || prevSelectedItem != _selectedItem))
                {
                    itemSelectedTime = Timing.TotalTime;
                }
                if (prevSelectedItem != _selectedItem && prevSelectedItem?.OnDeselect != null)
                {
                    prevSelectedItem.OnDeselect(this);
                }
            }
        }
        /// <summary>
        /// The secondary selected item. It's an item other than a device (see <see cref="SelectedItem"/>), e.g. a ladder or a chair.
        /// </summary>
        public Item SelectedSecondaryItem { get; set; }
        public void ReleaseSecondaryItem() => SelectedSecondaryItem = null;
        /// <summary>
        /// Has the characters selected a primary or a secondary item?
        /// </summary>
        public bool HasSelectedAnyItem => SelectedItem != null || SelectedSecondaryItem != null;
        /// <summary>
        /// Is the item either the primary or the secondary selected item?
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool IsAnySelectedItem(Item item) => item == SelectedItem || item == SelectedSecondaryItem;
        public bool HasSelectedAnotherSecondaryItem(Item item) => SelectedSecondaryItem != null && SelectedSecondaryItem != item;

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

        private bool isDead;
        public bool IsDead
        {
            get { return isDead; }
            set
            {
                if (isDead == value) { return; }
                if (value)
                {
                    Kill(CauseOfDeathType.Unknown, causeOfDeathAffliction: null);
                }
                else
                {
                    Revive();
                }
            }
        }

        public bool EnableDespawn { get; set; } = true;

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
                return IsKnockedDown || LockHands || IsPet || CanInventoryBeAccessed;
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
                    return IsKnockedDown || LockHands || IsBot && IsOnPlayerTeam;
                }
            }
            set { canInventoryBeAccessed = value; }
        }

        public bool CanAim
        {
            get
            {
                return (SelectedItem == null || SelectedItem.GetComponent<Controller>() is { AllowAiming: true }) && !IsIncapacitated && !IsRagdolled;
            }
        }

        public bool InWater => AnimController is AnimController { InWater: true };

        public bool IsLowInOxygen => CharacterHealth.OxygenAmount < 100;

        public bool GodMode = false;

        public CampaignMode.InteractionType CampaignInteractionType;
        public Identifier MerchantIdentifier;

        private bool accessRemovedCharacterErrorShown;
        public override Vector2 SimPosition
        {
            get
            {
                if (AnimController?.Collider == null)
                {
                    if (!accessRemovedCharacterErrorShown)
                    {
                        string errorMsg = "Attempted to access a potentially removed character. Character: [name], id: " + ID + ", removed: " + Removed + ".";
                        if (AnimController == null)
                        {
                            errorMsg += " AnimController == null";
                        }
                        else if (AnimController.Collider == null)
                        {
                            errorMsg += " AnimController.Collider == null";
                        }
                        errorMsg += '\n' + Environment.StackTrace.CleanupStackTrace();
                        DebugConsole.NewMessage(errorMsg.Replace("[name]", Name), Color.Red);
                        GameAnalyticsManager.AddErrorEventOnce(
                            "Character.SimPosition:AccessRemoved",
                            GameAnalyticsManager.ErrorSeverity.Error,
                            errorMsg.Replace("[name]", SpeciesName.Value) + "\n" + Environment.StackTrace.CleanupStackTrace());
                        accessRemovedCharacterErrorShown = true;
                    }
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

        public HashSet<Identifier> MarkedAsLooted = new();

        public bool IsInFriendlySub => Submarine != null && Submarine.TeamID == TeamID;
        public bool IsInPlayerSub => Submarine != null && Submarine.Info.IsPlayer;

        public float AITurretPriority
        {
            get => Params.AITurretPriority;
            private set => Params.AITurretPriority = value;
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
        public static Character Create(CharacterInfo characterInfo, Vector2 position, string seed, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, bool hasAi = true, RagdollParams ragdoll = null, bool spawnInitialItems = true)
        {
            return Create(characterInfo.SpeciesName, position, seed, characterInfo, id, isRemotePlayer, hasAi, createNetworkEvent: true, ragdoll, spawnInitialItems);
        }

        /// <summary>
        /// Create a new character
        /// </summary>
        /// <param name="speciesName">Name of the species (or the path to the config file)</param>
        /// <param name="position">Position in display units.</param>
        /// <param name="seed">RNG seed to use if the character config has randomizable parameters.</param>
        /// <param name="characterInfo">The name, gender, etc of the character. Only used for humans, and if the parameter is not given, a random CharacterInfo is generated.</param>
        /// <param name="id">ID to assign to the character. If set to 0, automatically find an available ID.</param>
        /// <param name="isRemotePlayer">Is the character controlled by a remote player.</param>
        /// <param name="hasAi">Is the character controlled by AI.</param>
        /// <param name="createNetworkEvent">Should clients receive a network event about the creation of this character?</param>
        /// <param name="ragdoll">Ragdoll configuration file. If null, will select the default.</param>
        public static Character Create(string speciesName, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, bool hasAi = true, bool createNetworkEvent = true, RagdollParams ragdoll = null, bool throwErrorIfNotFound = true, bool spawnInitialItems = true)
        {
            if (speciesName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                speciesName = Path.GetFileNameWithoutExtension(speciesName);
            }
            return Create(speciesName.ToIdentifier(), position, seed, characterInfo, id, isRemotePlayer, hasAi, createNetworkEvent, ragdoll, throwErrorIfNotFound, spawnInitialItems);
        }

        public static Character Create(Identifier speciesName, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, bool hasAi = true, bool createNetworkEvent = true, RagdollParams ragdoll = null, bool throwErrorIfNotFound = true, bool spawnInitialItems = true)
        {
            var prefab = CharacterPrefab.FindBySpeciesName(speciesName);
            if (prefab == null)
            {
                string errorMsg = $"Failed to create character \"{speciesName}\". Matching prefab not found.\n" + Environment.StackTrace;
                if (throwErrorIfNotFound)
                {
                    DebugConsole.ThrowError(errorMsg);
                }
                else
                {
                    DebugConsole.AddWarning(errorMsg);
                }

                return null;
            }
            return Create(prefab, position, seed, characterInfo, id, isRemotePlayer, hasAi, createNetworkEvent, ragdoll, spawnInitialItems);
        }

        public static Character Create(CharacterPrefab prefab, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, bool hasAi = true, bool createNetworkEvent = true, RagdollParams ragdoll = null, bool spawnInitialItems = true)
        {
            Character newCharacter = null;
            if (prefab.Identifier != CharacterPrefab.HumanSpeciesName)
            {
                var aiCharacter = new AICharacter(prefab, position, seed, characterInfo, id, isRemotePlayer, ragdoll, spawnInitialItems);
                var ai = new EnemyAIController(aiCharacter, seed);
                aiCharacter.SetAI(ai);
                newCharacter = aiCharacter;
            }
            else if (hasAi)
            {
                var aiCharacter = new AICharacter(prefab, position, seed, characterInfo, id, isRemotePlayer, ragdoll, spawnInitialItems);
                var ai = new HumanAIController(aiCharacter);
                aiCharacter.SetAI(ai);
                newCharacter = aiCharacter;
            }
            else
            {
                newCharacter = new Character(prefab, position, seed, characterInfo, id, isRemotePlayer, ragdoll, spawnInitialItems);
            }

#if SERVER
            if (GameMain.Server != null && Spawner != null && createNetworkEvent)
            {
                Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(newCharacter));
            }
#endif
            return newCharacter;
        }

        private Character(Submarine submarine, ushort id): base(submarine, id)
        {
            wallet = new Wallet(Option<Character>.Some(this));
        }

        protected Character(CharacterPrefab prefab, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, RagdollParams ragdollParams = null, bool spawnInitialItems = true)
            : this(null, id)
        {
            this.Seed = seed;
            this.Prefab = prefab;
            MTRandom random = new MTRandom(ToolBox.StringToInt(seed));

            IsRemotePlayer = isRemotePlayer;

            oxygenAvailable = 100.0f;
            aiTarget = new AITarget(this);

            lowPassMultiplier = 1.0f;

            Properties = SerializableProperty.GetProperties(this);

            Params = new CharacterParams(prefab.ContentFile as CharacterFile);

            Info = characterInfo;

            Identifier speciesName = prefab.Identifier;

            if (VariantOf == CharacterPrefab.HumanSpeciesName || speciesName == CharacterPrefab.HumanSpeciesName)
            {
                if (!VariantOf.IsEmpty)
                {
                    DebugConsole.ThrowError("The variant system does not yet support humans, sorry. It does support other humanoids though!");
                }
                if (characterInfo == null)
                {
                    Info = new CharacterInfo(CharacterPrefab.HumanSpeciesName);
                }
            }
            if (Info != null)
            {
                teamID = Info.TeamID;
            }
            keys = new Key[Enum.GetNames(typeof(InputType)).Length];
            for (int i = 0; i < Enum.GetNames(typeof(InputType)).Length; i++)
            {
                keys[i] = new Key((InputType)i);
            }

            var mainElement = prefab.ConfigElement;
            InitProjSpecific(mainElement);

            List<ContentXElement> inventoryElements = new List<ContentXElement>();
            List<float> inventoryCommonness = new List<float>();
            List<ContentXElement> healthElements = new List<ContentXElement>();
            List<float> healthCommonness = new List<float>();
            foreach (var subElement in mainElement.Elements())
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
                        var statusEffect = StatusEffect.Load(subElement, Name);
                        if (statusEffect != null)
                        {
                            if (!statusEffects.ContainsKey(statusEffect.type))
                            {
                                statusEffects.Add(statusEffect.type, new List<StatusEffect>());
                            }
                            statusEffects[statusEffect.type].Add(statusEffect);
                        }
                        break;
                }
            }
            if (Params.VariantFile != null)
            {
                var overrideElement = Params.VariantFile.Root.FromPackage(Params.MainElement.ContentPackage);
                // Only override if the override file contains matching elements
                if (overrideElement.GetChildElement("inventory") != null)
                {
                    inventoryElements.Clear();
                    inventoryCommonness.Clear();
                    foreach (var subElement in overrideElement.GetChildElements("inventory"))
                    {
                        switch (subElement.Name.ToString().ToLowerInvariant())
                        {
                            case "inventory":
                                inventoryElements.Add(subElement);
                                inventoryCommonness.Add(subElement.GetAttributeFloat("commonness", 1.0f));
                                break;
                        }
                    }
                }
                if (overrideElement.GetChildElement("health") != null)
                {
                    healthElements.Clear();
                    healthCommonness.Clear();
                    foreach (var subElement in overrideElement.GetChildElements("health"))
                    {
                        healthElements.Add(subElement);
                        healthCommonness.Add(subElement.GetAttributeFloat("commonness", 1.0f));
                    }
                }
            }

            if (inventoryElements.Count > 0)
            {
                Inventory = new CharacterInventory(
                    inventoryElements.Count == 1 ? inventoryElements[0] : ToolBox.SelectWeightedRandom(inventoryElements, inventoryCommonness, random),
                    this,
                    spawnInitialItems);
            }
            if (healthElements.Count == 0)
            {
                CharacterHealth = new CharacterHealth(this);
            }
            else
            {
                var selectedHealthElement = healthElements.Count == 1 ? healthElements[0] : ToolBox.SelectWeightedRandom(healthElements, healthCommonness, random);
                // If there's no limb elements defined in the override variant, let's use the limb health definitions of the original file.
                var limbHealthElement = selectedHealthElement;
                if (Params.VariantFile != null && limbHealthElement.GetChildElement("limb") == null)
                {
                    limbHealthElement = Params.OriginalElement.GetChildElement("health");
                }
                CharacterHealth = new CharacterHealth(selectedHealthElement, this, limbHealthElement);
            }

            if (Params.Husk && speciesName != "husk" && Prefab.VariantOf != "husk")
            {
                Identifier nonHuskedSpeciesName = Identifier.Empty;
                AfflictionPrefabHusk matchingAffliction = null; 
                foreach (var huskPrefab in AfflictionPrefab.Prefabs.OfType<AfflictionPrefabHusk>())
                {
                    var nonHuskedName = AfflictionHusk.GetNonHuskedSpeciesName(speciesName, huskPrefab);
                    if (huskPrefab.TargetSpecies.Contains(nonHuskedName))
                    {
                        var huskedSpeciesName = AfflictionHusk.GetHuskedSpeciesName(nonHuskedName, huskPrefab);
                        if (huskedSpeciesName.Equals(speciesName))
                        {
                            nonHuskedSpeciesName = nonHuskedName;
                            matchingAffliction = huskPrefab;
                            break;
                        }
                    }                    
                }
                if (matchingAffliction == null || nonHuskedSpeciesName.IsEmpty)
                {
                    DebugConsole.ThrowError($"Cannot find a husk infection that matches {speciesName}! Please make sure that the speciesname is added as 'targets' in the husk affliction prefab definition!\n"
                        + "Note that all the infected speciesnames and files must stick the following pattern: [nonhuskedspeciesname][huskedspeciesname]. E.g. Humanhusk, Crawlerhusk, or Humancustomhusk, or Crawlerzombie. Not \"Customhumanhusk!\" or \"Zombiecrawler\"");
                    // Crashes if we fail to create a ragdoll -> Let's just use some ragdoll so that the user sees the error msg.
                    nonHuskedSpeciesName = IsHumanoid ? CharacterPrefab.HumanSpeciesName : "crawler".ToIdentifier();
                    speciesName = nonHuskedSpeciesName;
                }
                if (ragdollParams == null && prefab.VariantOf == null)
                {
                    Identifier name = Params.UseHuskAppendage ? nonHuskedSpeciesName : speciesName;
                    ragdollParams = IsHumanoid ? RagdollParams.GetDefaultRagdollParams<HumanRagdollParams>(name) : RagdollParams.GetDefaultRagdollParams<FishRagdollParams>(name) as RagdollParams;
                }
                if (Params.HasInfo && info == null)
                {
                    info = new CharacterInfo(nonHuskedSpeciesName);
                }
            }
            else if (Params.HasInfo && info == null)
            {
                info = new CharacterInfo(speciesName);
            }

            if (IsHumanoid)
            {
                AnimController = new HumanoidAnimController(this, seed, ragdollParams as HumanRagdollParams)
                {
                    TargetDir = Direction.Right
                };
            }
            else
            {
                AnimController = new FishAnimController(this, seed, ragdollParams as FishRagdollParams);
                PressureProtection = int.MaxValue;
            }

            AnimController.SetPosition(ConvertUnits.ToSimUnits(position));

            AnimController.FindHull(null);
            if (AnimController.CurrentHull != null) { Submarine = AnimController.CurrentHull.Submarine; }

            CharacterList.Add(this);

            //characters start disabled in the multiplayer mode, and are enabled if/when
            //  - controlled by the player
            //  - client receives a position update from the server
            //  - server receives an input message from the client controlling the character
            //  - if an AICharacter, the server enables it when close enough to any of the players
            Enabled = GameMain.NetworkMember == null;

            if (info != null)
            {
                LoadHeadAttachments();
            }
            ApplyStatusEffects(ActionType.OnSpawn, 1.0f);
        }
        partial void InitProjSpecific(ContentXElement mainElement);

        public void ReloadHead(int? headId = null, int hairIndex = -1, int beardIndex = -1, int moustacheIndex = -1, int faceAttachmentIndex = -1)
        {
            if (Info == null) { return; }
            var head = AnimController.GetLimb(LimbType.Head);
            if (head == null) { return; }
            HashSet<Identifier> tags = Info.Head.Preset.TagSet.ToHashSet();
            if (headId.HasValue)
            {
                tags.RemoveWhere(t => t.StartsWith("variant"));
                tags.Add($"variant{headId.Value}".ToIdentifier());
            }
            var oldHeadInfo = Info.Head;
            Info.RecreateHead(tags.ToImmutableHashSet(), hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            if (hairIndex == -1)
            {
                Info.Head.HairIndex = oldHeadInfo.HairIndex;
            }
            if (beardIndex == -1)
            {
                Info.Head.BeardIndex = oldHeadInfo.BeardIndex;
            }
            if (moustacheIndex == -1)
            {
                Info.Head.MoustacheIndex = oldHeadInfo.MoustacheIndex;
            }
            if (faceAttachmentIndex == -1)
            {
                Info.Head.FaceAttachmentIndex = oldHeadInfo.FaceAttachmentIndex;
            }
            Info.Head.SkinColor = oldHeadInfo.SkinColor;
            Info.Head.HairColor = oldHeadInfo.HairColor;
            Info.Head.FacialHairColor = oldHeadInfo.FacialHairColor;
            Info.CheckColors();
#if CLIENT
            head.RecreateSprites();
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
            if (info.Head.FaceAttachment == null) { info.Head.FaceAttachmentIndex = 0; }
            Info.Head.FaceAttachment?.GetChildElements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.FaceAttachment)));
            if (info.Head.BeardElement == null) { info.Head.BeardIndex = 0; }
            Info.Head.BeardElement?.GetChildElements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Beard)));
            if (info.Head.MoustacheElement == null) { info.Head.MoustacheIndex = 0; }
            Info.Head.MoustacheElement?.GetChildElements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Moustache)));
            if (info.Head.HairElement == null) { info.Head.HairIndex = 0; }
            Info.Head.HairElement?.GetChildElements("sprite").ForEach(s => head.OtherWearables.Add(new WearableSprite(s, WearableType.Hair)));

#if CLIENT
            if (info.Head?.HairWithHatElement?.GetChildElement("sprite") != null)
            {
                head.HairWithHatSprite = new WearableSprite(info.Head.HairWithHatElement.GetChildElement("sprite"), WearableType.Hair);
            }
            head.EnableHuskSprite = Params.Husk;
            head.LoadHerpesSprite();
            head.UpdateWearableTypesToHide();
#endif
        }

        public bool IsKeyHit(InputType inputType)
        {
#if SERVER
            if (GameMain.Server != null && IsRemotePlayer)
            {
                switch (inputType)
                {
                    case InputType.Left:
                        return dequeuedInput.HasFlag(InputNetFlags.Left) && !prevDequeuedInput.HasFlag(InputNetFlags.Left);
                    case InputType.Right:
                        return dequeuedInput.HasFlag(InputNetFlags.Right) && !prevDequeuedInput.HasFlag(InputNetFlags.Right);
                    case InputType.Up:
                        return dequeuedInput.HasFlag(InputNetFlags.Up) && !prevDequeuedInput.HasFlag(InputNetFlags.Up);
                    case InputType.Down:
                        return dequeuedInput.HasFlag(InputNetFlags.Down) && !prevDequeuedInput.HasFlag(InputNetFlags.Down);
                    case InputType.Run:
                        return dequeuedInput.HasFlag(InputNetFlags.Run) && prevDequeuedInput.HasFlag(InputNetFlags.Run);
                    case InputType.Crouch:
                        return dequeuedInput.HasFlag(InputNetFlags.Crouch) && !prevDequeuedInput.HasFlag(InputNetFlags.Crouch);
                    case InputType.Select:
                        return dequeuedInput.HasFlag(InputNetFlags.Select); //TODO: clean up the way this input is registered
                    case InputType.Deselect:
                        return dequeuedInput.HasFlag(InputNetFlags.Deselect);
                    case InputType.Health:
                        return dequeuedInput.HasFlag(InputNetFlags.Health);
                    case InputType.Grab:
                        return dequeuedInput.HasFlag(InputNetFlags.Grab);
                    case InputType.Use:
                        return dequeuedInput.HasFlag(InputNetFlags.Use) && !prevDequeuedInput.HasFlag(InputNetFlags.Use);
                    case InputType.Shoot:
                        return dequeuedInput.HasFlag(InputNetFlags.Shoot) && !prevDequeuedInput.HasFlag(InputNetFlags.Shoot);
                    case InputType.Ragdoll:
                        return dequeuedInput.HasFlag(InputNetFlags.Ragdoll) && !prevDequeuedInput.HasFlag(InputNetFlags.Ragdoll);
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
                var invertControls = CharacterHealth.GetAfflictionOfType("invertcontrols".ToIdentifier());
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
#if DEBUG
            return (info != null && !string.IsNullOrWhiteSpace(info.Name)) ? info.Name : SpeciesName.Value;
#else
            return SpeciesName.Value;
#endif
        }

        public void GiveJobItems(WayPoint spawnPoint = null)
        {
            if (info == null) { return; }
            if (info.HumanPrefabIds != default)
            {
                var humanPrefab = NPCSet.Get(info.HumanPrefabIds.NpcSetIdentifier, info.HumanPrefabIds.NpcIdentifier);
                if (humanPrefab == null)
                {
                    DebugConsole.ThrowError($"Failed to give job items for the character \"{Name}\" - could not find human prefab with the id \"{info.HumanPrefabIds.NpcIdentifier}\" from \"{info.HumanPrefabIds.NpcSetIdentifier}\".");
                }
                else if (humanPrefab.GiveItems(this, spawnPoint?.Submarine ?? Submarine, spawnPoint))
                {
                    return;
                }
            }
            info.Job?.GiveJobItems(this, spawnPoint);
        }

        public void GiveIdCardTags(WayPoint spawnPoint, bool createNetworkEvent = false)
        {
            if (info?.Job == null || spawnPoint == null) { return; }

            foreach (Item item in Inventory.AllItems)
            {
                if (item?.GetComponent<IdCard>() == null) { continue; }
                foreach (string s in spawnPoint.IdCardTags)
                {
                    item.AddTag(s);
                }
                if (createNetworkEvent && GameMain.NetworkMember is { IsServer: true })
                {
                    GameMain.NetworkMember.CreateEntityEvent(item, new Item.ChangePropertyEventData(item.SerializableProperties[nameof(item.Tags).ToIdentifier()], item));
                }
            }
        }

        public float GetSkillLevel(string skillIdentifier) =>
            GetSkillLevel(skillIdentifier.ToIdentifier());

        private static readonly ImmutableDictionary<Identifier, StatTypes> overrideStatTypes = new Dictionary<Identifier, StatTypes>
        {
            { new("helm"), StatTypes.HelmSkillOverride },
            { new("medical"), StatTypes.MedicalSkillOverride },
            { new("weapons"), StatTypes.WeaponsSkillOverride },
            { new("electrical"), StatTypes.ElectricalSkillOverride },
            { new("mechanical"), StatTypes.MechanicalSkillOverride }
        }.ToImmutableDictionary();

        public float GetSkillLevel(Identifier skillIdentifier)
        {
            if (Info?.Job == null) { return 0.0f; }
            float skillLevel = Info.Job.GetSkillLevel(skillIdentifier);

            if (overrideStatTypes.TryGetValue(skillIdentifier, out StatTypes statType))
            {
                float skillOverride = GetStatValue(statType);
                if (skillOverride > skillLevel)
                {
                    skillLevel = skillOverride;
                }
            }

            // apply multipliers first so that multipliers only affect base skill value
            foreach (Affliction affliction in CharacterHealth.GetAllAfflictions())
            {
                skillLevel *= affliction.GetSkillMultiplier();
            }

            if (skillIdentifier != null)
            {
                foreach (Item item in Inventory.AllItems)
                {
                    if (item?.GetComponent<Wearable>() is Wearable wearable &&
                        !Inventory.IsInLimbSlot(item, InvSlotType.Any))
                    {
                        foreach (var allowedSlot in wearable.AllowedSlots)
                        {
                            if (allowedSlot == InvSlotType.Any) { continue; }
                            if (!Inventory.IsInLimbSlot(item, allowedSlot)) { continue; }
                            if (wearable.SkillModifiers.TryGetValue(skillIdentifier, out float skillValue))
                            {
                                skillLevel += skillValue;
                                break;
                            }
                        }

                    }
                }
            }

            skillLevel += GetStatValue(GetSkillStatType(skillIdentifier));
            return Math.Max(skillLevel, 0);
        }

        // TODO: reposition? there's also the overrideTargetMovement variable, but it's not in the same manner
        public Vector2? OverrideMovement { get; set; }
        public bool ForceRun { get; set; }

        public bool IsClimbing => AnimController.IsClimbing;

        public Vector2 GetTargetMovement()
        {
            Vector2 targetMovement = Vector2.Zero;
            if (OverrideMovement.HasValue)
            {
                targetMovement = OverrideMovement.Value;
            }
            else
            {
                if (IsKeyDown(InputType.Left)) { targetMovement.X -= 1.0f; }
                if (IsKeyDown(InputType.Right)) { targetMovement.X += 1.0f; }
                if (IsKeyDown(InputType.Up)) { targetMovement.Y += 1.0f; }
                if (IsKeyDown(InputType.Down)) { targetMovement.Y -= 1.0f; }
            }
            bool run = false;
            if ((IsKeyDown(InputType.Run) && AnimController.ForceSelectAnimationType == AnimationType.NotDefined) || ForceRun)
            {

                run = CanRun;
            }
            return ApplyMovementLimits(targetMovement, AnimController.GetCurrentSpeed(run));
        }

        //can't run if
        //  - dragging someone
        //  - crouching
        //  - moving backwards
        public bool CanRun => (SelectedCharacter == null || !SelectedCharacter.CanBeDragged || HasAbilityFlag(AbilityFlags.MoveNormallyWhileDragging)) &&
                    (!(AnimController is HumanoidAnimController) || !((HumanoidAnimController)AnimController).Crouching) &&
                    !AnimController.IsMovingBackwards && !HasAbilityFlag(AbilityFlags.MustWalk);

        public Vector2 ApplyMovementLimits(Vector2 targetMovement, float currentSpeed)
        {
            //the vertical component is only used for falling through platforms and climbing ladders when not in water,
            //so the movement can't be normalized or the Character would walk slower when pressing down/up
            if (AnimController.InWater)
            {
                float length = targetMovement.Length();
                if (length > 0.0f)
                {
                    targetMovement /= length;
                }
            }
            targetMovement *= currentSpeed;
            float maxSpeed = ApplyTemporarySpeedLimits(currentSpeed);
            targetMovement.X = MathHelper.Clamp(targetMovement.X, -maxSpeed, maxSpeed);
            targetMovement.Y = MathHelper.Clamp(targetMovement.Y, -maxSpeed, maxSpeed);
            SpeedMultiplier = Math.Max(0.0f, greatestPositiveSpeedMultiplier - (1f - greatestNegativeSpeedMultiplier));
            targetMovement *= SpeedMultiplier;
            // Reset, status effects will set the value before the next update
            ResetSpeedMultiplier();
            return targetMovement;
        }

        private float greatestNegativeSpeedMultiplier = 1f;
        private float greatestPositiveSpeedMultiplier = 1f;

        /// <summary>
        /// Can be used to modify the character's speed via StatusEffects
        /// </summary>
        public float SpeedMultiplier { get; private set; } = 1;


        private double propulsionSpeedMultiplierLastSet;
        private float propulsionSpeedMultiplier;
        /// <summary>
        /// Can be used to modify the speed at which Propulsion ItemComponents move the character via StatusEffects (e.g. heavy suit can slow down underwater scooters)
        /// </summary>
        public float PropulsionSpeedMultiplier 
        {
            get { return propulsionSpeedMultiplier; }
            set
            {
                propulsionSpeedMultiplier = value;
                propulsionSpeedMultiplierLastSet = Timing.TotalTime;
            }
        }

        public void StackSpeedMultiplier(float val)
        {
            greatestNegativeSpeedMultiplier = Math.Min(val, greatestNegativeSpeedMultiplier);
            greatestPositiveSpeedMultiplier = Math.Max(val, greatestPositiveSpeedMultiplier);
        }

        public void ResetSpeedMultiplier()
        {
            greatestPositiveSpeedMultiplier = 1f;
            greatestNegativeSpeedMultiplier = 1f;
            if (Timing.TotalTime > propulsionSpeedMultiplierLastSet + 0.1)
            {
                propulsionSpeedMultiplier = 1.0f;
            }
        }

        private float greatestNegativeHealthMultiplier = 1f;
        private float greatestPositiveHealthMultiplier = 1f;

        /// <summary>
        /// Can be used to modify the character's health via StatusEffects
        /// </summary>
        public float HealthMultiplier { get; private set; } = 1;

        public void StackHealthMultiplier(float val)
        {
            greatestNegativeHealthMultiplier = Math.Min(val, greatestNegativeHealthMultiplier);
            greatestPositiveHealthMultiplier = Math.Max(val, greatestPositiveHealthMultiplier);
        }

        private void CalculateHealthMultiplier()
        {
            HealthMultiplier = greatestPositiveHealthMultiplier - (1f - greatestNegativeHealthMultiplier);
            // Reset, status effects should set the values again, if the conditions match
            greatestPositiveHealthMultiplier = 1f;
            greatestNegativeHealthMultiplier = 1f;
        }

        /// <summary>
        /// Health multiplier of the human prefab this character is an instance of (if any)
        /// </summary>
        public float HumanPrefabHealthMultiplier { get; private set; } = 1;

        /// <summary>
        /// Speed reduction from the current limb specific damage. Min 0, max 1.
        /// </summary>
        public float GetTemporarySpeedReduction()
        {
            float reduction = 0;
            reduction = CalculateMovementPenalty(AnimController.GetLimb(LimbType.RightFoot, excludeSevered: false), reduction);
            reduction = CalculateMovementPenalty(AnimController.GetLimb(LimbType.LeftFoot, excludeSevered: false), reduction);
            if (AnimController is HumanoidAnimController)
            {
                if (AnimController.InWater)
                {
                    // Currently only humans use hands for swimming.
                    reduction = CalculateMovementPenalty(AnimController.GetLimb(LimbType.RightHand, excludeSevered: false), reduction);
                    reduction = CalculateMovementPenalty(AnimController.GetLimb(LimbType.LeftHand, excludeSevered: false), reduction);
                }
            }
            else
            {
                int totalTailLimbs = 0;
                int destroyedTailLimbs = 0;
                foreach (var limb in AnimController.Limbs)
                {
                    if (limb.type == LimbType.Tail)
                    {
                        totalTailLimbs++;
                        if (limb.IsSevered)
                        {
                            destroyedTailLimbs++;
                        }
                    }
                }
                if (destroyedTailLimbs > 0)
                {
                    reduction += MathHelper.Lerp(0, AnimController.InWater ? 1f : 0.5f, (float)destroyedTailLimbs / totalTailLimbs);
                }
            }
            return Math.Clamp(reduction, 0, 1f);
        }

        private float CalculateMovementPenalty(Limb limb, float sum, float max = 0.8f)
        {
            if (limb != null)
            {
                sum += MathHelper.Lerp(0, max, CharacterHealth.GetLimbDamage(limb, afflictionType: AfflictionPrefab.DamageType));
            }
            return Math.Clamp(sum, 0, 1f);
        }

        public float GetRightHandPenalty() => CalculateMovementPenalty(AnimController.GetLimb(LimbType.RightHand, excludeSevered: false), 0, max: 1);
        public float GetLeftHandPenalty() => CalculateMovementPenalty(AnimController.GetLimb(LimbType.LeftHand, excludeSevered: false), 0, max: 1);

        public float GetLegPenalty(float startSum = 0)
        {
            float sum = startSum;
            foreach (var limb in AnimController.Limbs)
            {
                switch (limb.type)
                {
                    case LimbType.RightFoot:
                    case LimbType.LeftFoot:
                        sum += CalculateMovementPenalty(limb, sum, max: 0.5f);
                        break;
                }
            }
            return Math.Clamp(sum, 0, 1f);
        }

        public float ApplyTemporarySpeedLimits(float speed)
        {
            float max;
            if (AnimController is HumanoidAnimController)
            {
                max = AnimController.InWater ? 0.5f : 0.8f;
            }
            else
            {
                max = AnimController.InWater ? 0.9f : 0.5f;
            }
            speed *= 1f - MathHelper.Lerp(0, max, GetTemporarySpeedReduction());
            return speed;
        }

        /// <summary>
        /// Values lower than this seem to cause constantious flipping when the mouse is near the player and the player is running, because the root collider moves after flipping.
        /// </summary>
        private const float cursorFollowMargin = 40;

        public void Control(float deltaTime, Camera cam)
        {
            ViewTarget = null;
            if (!AllowInput) { return; }

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

            bool aiControlled = this is AICharacter && Controlled != this && !IsRemotelyControlled;
            if (!aiControlled)
            {
                Vector2 targetMovement = GetTargetMovement();
                AnimController.TargetMovement = targetMovement;
                AnimController.IgnorePlatforms = AnimController.TargetMovement.Y < -0.1f;
            }

            if (AnimController is HumanoidAnimController humanAnimController)
            {
                humanAnimController.Crouching = humanAnimController.ForceSelectAnimationType == AnimationType.Crouch || IsKeyDown(InputType.Crouch);
            }

            if (!aiControlled &&
                !AnimController.IsUsingItem && 
                AnimController.Anim != AnimController.Animation.CPR &&
                (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient || Controlled == this) &&
                (AnimController.OnGround || IsClimbing) && !AnimController.InWater)
            {
                if (dontFollowCursor)
                {
                    AnimController.TargetDir = Direction.Right;
                }
                else
                {
                    if (CursorPosition.X < AnimController.Collider.Position.X - cursorFollowMargin)
                    {
                        AnimController.TargetDir = Direction.Left;
                    }
                    else if (CursorPosition.X > AnimController.Collider.Position.X + cursorFollowMargin)
                    {
                        AnimController.TargetDir = Direction.Right;
                    }
                }
            }

            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetworkMember.IsServer)
                {
                    if (!aiControlled)
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
                }
                else if (GameMain.NetworkMember.IsClient && Controlled != this)
                {
                    if (memState.Count > 0)
                    {
                        AnimController.TargetDir = memState[0].Direction;
                    }
                }
            }

#if DEBUG && CLIENT
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.F))
            {
                AnimController.ReleaseStuckLimbs();
                if (AIController != null && AIController is EnemyAIController enemyAI)
                {
                    enemyAI.LatchOntoAI?.DeattachFromBody(reset: true);
                }
            }
#endif

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Controlled != this && IsKeyDown(InputType.Aim))
            {
                if (currentAttackTarget.AttackLimb?.attack is Attack { Ranged: true } attack && AIController is EnemyAIController enemyAi)
                {
                    enemyAi.AimRangedAttack(attack, currentAttackTarget.DamageTarget as Entity);
                }
            }

            if (attackCoolDown > 0.0f)
            {
                attackCoolDown -= deltaTime;
            }
            else if (IsKeyDown(InputType.Attack))
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Controlled != this)
                {
                    if ((currentAttackTarget.DamageTarget as Entity)?.Removed ?? false)
                    {
                        currentAttackTarget = default;
                    }
                    currentAttackTarget.AttackLimb?.UpdateAttack(deltaTime, currentAttackTarget.AttackPos, currentAttackTarget.DamageTarget, out _);
                }
                else if (IsPlayer)
                {
                    float dist = -1;
                    Vector2 attackPos = SimPosition + ConvertUnits.ToSimUnits(cursorPosition - Position);
                    List<Body> ignoredBodies = AnimController.Limbs.Select(l => l.body.FarseerBody).ToList();
                    ignoredBodies.Add(AnimController.Collider.FarseerBody);

                    var body = Submarine.PickBody(
                        SimPosition,
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
                                SimPosition - ((Submarine)body.UserData).SimPosition,
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
                            if (body.UserData is IDamageable damageable)
                            {
                                attackTarget = damageable;
                            }
                            else if (body.UserData is Limb limb)
                            {
                                attackTarget = limb.character;
                            }
                        }
                    }
                    var currentContexts = GetAttackContexts();
                    var validLimbs = AnimController.Limbs.Where(l =>
                    {
                        if (l.IsSevered || l.IsStuck) { return false; }
                        if (l.Disabled) { return false; }
                        var attack = l.attack;
                        if (attack == null) { return false; }
                        if (attack.CoolDownTimer > 0) { return false; }
                        if (!attack.IsValidContext(currentContexts)) { return false; }
                        if (attackTarget != null)
                        {
                            if (!attack.IsValidTarget(attackTarget as Entity)) { return false; }
                            if (attackTarget is ISerializableEntity se && attackTarget is Character)
                            {
                                if (attack.Conditionals.Any(c => !c.TargetSelf && !c.Matches(se))) { return false; }
                            }
                        }
                        if (attack.Conditionals.Any(c => c.TargetSelf && !c.Matches(this))) { return false; }
                        return true;
                    });
                    var sortedLimbs = validLimbs.OrderBy(l => Vector2.DistanceSquared(ConvertUnits.ToDisplayUnits(l.SimPosition), cursorPosition));
                    // Select closest
                    var attackLimb = sortedLimbs.FirstOrDefault();
                    if (attackLimb != null)
                    {
                        if (attackTarget is Character targetCharacter)
                        {
                            dist = ConvertUnits.ToDisplayUnits(Vector2.Distance(Submarine.LastPickedPosition, attackLimb.SimPosition));
                            foreach (Limb limb in targetCharacter.AnimController.Limbs)
                            {
                                if (limb.IsSevered || limb.Removed) { continue; }
                                float tempDist = ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackLimb.SimPosition));
                                if (tempDist < dist)
                                {
                                    dist = tempDist;
                                }
                            }
                        }
                        attackLimb.UpdateAttack(deltaTime, attackPos, attackTarget, out AttackResult attackResult, dist);
                        if (!attackLimb.attack.IsRunning)
                        {
                            attackCoolDown = 1.0f;
                        }
                    }
                }
            }

            if (Inventory != null)
            {
                bool CanUseItemsWhenSelected(Item item) => item == null || !item.Prefab.DisableItemUsageWhenSelected;
                if (CanUseItemsWhenSelected(SelectedItem) && CanUseItemsWhenSelected(SelectedSecondaryItem))
                {
                    foreach (Item item in HeldItems)
                    {
                        tryUseItem(item, deltaTime);
                    }
                    foreach (Item item in Inventory.AllItems)
                    {
                        if (item.GetComponent<Wearable>() is { AllowUseWhenWorn: true } && HasEquippedItem(item))
                        {
                            tryUseItem(item, deltaTime);
                        }
                    }
                }
            }

            void tryUseItem(Item item, float deltaTime)
            {
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
#if CLIENT
                    else if (item.RequireAimToUse && !IsKeyDown(InputType.Aim))
                    {
                        HintManager.OnShootWithoutAiming(this, item);
                    }
#endif
                }
            }

            if (SelectedItem != null)
            {
                tryUseItem(SelectedItem, deltaTime);
            }

            if (SelectedCharacter != null)
            {
                if (!SelectedCharacter.CanBeSelected ||
                    (Vector2.DistanceSquared(SelectedCharacter.WorldPosition, WorldPosition) > MaxDragDistance * MaxDragDistance &&
                    SelectedCharacter.GetDistanceToClosestLimb(SimPosition) > ConvertUnits.ToSimUnits(MaxDragDistance)))
                {
                    DeselectCharacter();
                }
            }

            if (IsRemotelyControlled && keys != null)
            {
                foreach (Key key in keys)
                {
                    key.ResetHit();
                }
            }
        }

        private struct AttackTargetData
        {
            public Limb AttackLimb { get; set; }
            public IDamageable DamageTarget { get; set; }
            public Vector2 AttackPos { get; set; }
        }

        private AttackTargetData currentAttackTarget;
        public void SetAttackTarget(Limb attackLimb, IDamageable damageTarget, Vector2 attackPos)
        {
            currentAttackTarget = new AttackTargetData()
            {
                AttackLimb = attackLimb,
                DamageTarget = damageTarget,
                AttackPos = attackPos
            };
        }

        public bool CanSeeCharacter(Character target)
        {
            System.Diagnostics.Debug.Assert(target != null);
            if (target == null) { return false; }
            if (target.Removed) { return false; }
            Limb seeingLimb = GetSeeingLimb();
            if (CanSeeTarget(target, seeingLimb)) { return true; }
            if (!target.AnimController.SimplePhysicsEnabled)
            {
                //find the limbs that are furthest from the target's position (from the viewer's point of view)
                Limb leftExtremity = null, rightExtremity = null;
                float leftMostDot = 0.0f, rightMostDot = 0.0f;
                Vector2 dir = target.WorldPosition - WorldPosition;
                Vector2 leftDir = new Vector2(dir.Y, -dir.X);
                Vector2 rightDir = new Vector2(-dir.Y, dir.X);
                foreach (Limb limb in target.AnimController.Limbs)
                {
                    if (limb.IsSevered || limb == target.AnimController.MainLimb) { continue; }
                    if (limb.Hidden) { continue; }
                    Vector2 limbDir = limb.WorldPosition - WorldPosition;
                    float leftDot = Vector2.Dot(limbDir, leftDir);
                    if (leftDot > leftMostDot)
                    {
                        leftMostDot = leftDot;
                        leftExtremity = limb;
                        continue;
                    }
                    float rightDot = Vector2.Dot(limbDir, rightDir);
                    if (rightDot > rightMostDot)
                    {
                        rightMostDot = rightDot;
                        rightExtremity = limb;
                        continue;
                    }
                }
                if (leftExtremity != null && CanSeeTarget(leftExtremity, seeingLimb)) { return true; }
                if (rightExtremity != null && CanSeeTarget(rightExtremity, seeingLimb)) { return true; }
            }
            return false;
        }

        private Limb GetSeeingLimb()
        {
            return AnimController.GetLimb(LimbType.Head) ?? AnimController.GetLimb(LimbType.Torso) ?? AnimController.MainLimb;
        }

        public bool CanSeeTarget(ISpatialEntity target, ISpatialEntity seeingEntity = null)
        {
            System.Diagnostics.Debug.Assert(target != null);
            if (target == null) { return false; }
            seeingEntity ??= AnimController.SimplePhysicsEnabled ? this : GetSeeingLimb() as ISpatialEntity;
            if (seeingEntity == null) { return false; }
            ISpatialEntity sourceEntity = seeingEntity ;
            // TODO: Could we just use the method below? If not, let's refactor it so that we can.
            Vector2 diff = ConvertUnits.ToSimUnits(target.WorldPosition - sourceEntity.WorldPosition);
            Body closestBody;
            //both inside the same sub (or both outside)
            //OR the we're inside, the other character outside
            if (target.Submarine == Submarine || target.Submarine == null)
            {
                closestBody = Submarine.CheckVisibility(sourceEntity.SimPosition, sourceEntity.SimPosition + diff);
            }
            //we're outside, the other character inside
            else if (Submarine == null)
            {
                closestBody = Submarine.CheckVisibility(target.SimPosition, target.SimPosition - diff);
            }
            //both inside different subs
            else
            {
                closestBody = Submarine.CheckVisibility(sourceEntity.SimPosition, sourceEntity.SimPosition + diff);
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
                else if (body.UserData is Item item)
                {
                    return item != target;
                }
                return true;
            }
        }

        /// <summary>
        /// A simple check if the character Dir is towards the target or not. Uses the world coordinates.
        /// </summary>
        public bool IsFacing(Vector2 targetWorldPos) => AnimController.Dir > 0 && targetWorldPos.X > WorldPosition.X || AnimController.Dir < 0 && targetWorldPos.X < WorldPosition.X;

        public bool HasItem(Item item, bool requireEquipped = false, InvSlotType? slotType = null) => requireEquipped ? HasEquippedItem(item, slotType) : item.IsOwnedBy(this);

        public bool HasEquippedItem(Item item, InvSlotType? slotType = null, Func<InvSlotType, bool> predicate = null)
        {
            if (Inventory == null) { return false; }
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                InvSlotType slot = Inventory.SlotTypes[i];
                if (predicate != null)
                {
                    if (!predicate(slot)) { continue; }
                }
                if (slotType.HasValue)
                {
                    if (!slotType.Value.HasFlag(slot)) { continue; }
                }
                else if (slot == InvSlotType.Any)
                {
                    continue;
                }
                if (Inventory.GetItemAt(i) == item) { return true; }
            }
            return false;
        }

        public bool HasEquippedItem(string tagOrIdentifier, bool allowBroken = true, InvSlotType? slotType = null)
            => HasEquippedItem(tagOrIdentifier.ToIdentifier(), allowBroken, slotType);

        public bool HasEquippedItem(Identifier tagOrIdentifier, bool allowBroken = true, InvSlotType? slotType = null)
        {
            if (Inventory == null) { return false; }
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                if (slotType.HasValue)
                {
                    if (!slotType.Value.HasFlag(Inventory.SlotTypes[i])) { continue; }
                }
                else if (Inventory.SlotTypes[i] == InvSlotType.Any) 
                { 
                    continue; 
                }
                var item = Inventory.GetItemAt(i);
                if (item == null) { continue; }
                if (!allowBroken && item.Condition <= 0.0f) { continue; }
                if (item.Prefab.Identifier == tagOrIdentifier || item.HasTag(tagOrIdentifier)) { return true; }
            }
            return false;
        }

        public Item GetEquippedItem(string tagOrIdentifier = null, InvSlotType? slotType = null)
        {
            if (Inventory == null) { return null; }
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                if (slotType.HasValue)
                {
                    if (!slotType.Value.HasFlag(Inventory.SlotTypes[i])) { continue; }
                }
                else if (Inventory.SlotTypes[i] == InvSlotType.Any)
                {
                    continue;
                }
                var item = Inventory.GetItemAt(i);
                if (item == null) { continue; }
                if (tagOrIdentifier == null || item.Prefab.Identifier == tagOrIdentifier || item.HasTag(tagOrIdentifier)) { return item; }
            }
            return null;
        }

        public bool CanAccessInventory(Inventory inventory)
        {
            if (!CanInteract || inventory.Locked) { return false; }

            //the inventory belongs to some other character
            if (inventory.Owner is Character character && inventory.Owner != this)
            {
                var owner = character;
                //can only be accessed if the character is incapacitated and has been selected
                return SelectedCharacter == owner && owner.CanInventoryBeAccessed;
            }

            if (inventory.Owner is Item item)
            {
                if (!CanInteractWith(item) && !item.linkedTo.Any(lt => lt is Item item && item.DisplaySideBySideWhenLinked && CanInteractWith(item))) { return false; }
                ItemContainer container = item.GetComponents<ItemContainer>().FirstOrDefault(ic => ic.Inventory == inventory);
                if (container != null && !container.HasRequiredItems(this, addMessage: false)) { return false; }
            }
            return true;
        }

        private Stopwatch sw;
        private Stopwatch StopWatch => sw ??= new Stopwatch();
        private float _selectedItemPriority;
        private Item _foundItem;
        /// <summary>
        /// Finds the closest item seeking by identifiers or tags from the world.
        /// Ignores items that are outside or in another team's submarine or in a submarine that is not connected to this submarine.
        /// Also ignores non-interactable items and items that are taken by someone else.
        /// The method is run in steps for performance reasons. So you'll have to provide the reference to the itemIndex.
        /// Returns false while running and true when done.
        /// </summary>
        public bool FindItem(ref int itemIndex, out Item targetItem, IEnumerable<Identifier> identifiers = null, bool ignoreBroken = true, 
            IEnumerable<Item> ignoredItems = null, IEnumerable<Identifier> ignoredContainerIdentifiers = null, 
            Func<Item, bool> customPredicate = null, Func<Item, float> customPriorityFunction = null, float maxItemDistance = 10000, ISpatialEntity positionalReference = null)
        {
            if (HumanAIController.DebugAI)
            {
                StopWatch.Restart();
            }
            if (itemIndex == 0)
            {
                _foundItem = null;
                _selectedItemPriority = 0;
            }
            int itemsPerFrame = IsOnPlayerTeam ? 100 : 10;
            int checkedItemCount = 0;
            for (int i = 0; i < itemsPerFrame && itemIndex < Item.ItemList.Count; i++, itemIndex++)
            {
                checkedItemCount++;
                var item = Item.ItemList[itemIndex];
                if (!item.IsInteractable(this)) { continue; }
                if (ignoredItems != null && ignoredItems.Contains(item)) { continue; }
                if (item.Submarine == null) { continue; }
                if (item.Submarine.TeamID != TeamID) { continue; }
                if (item.CurrentHull == null) { continue; }
                if (ignoreBroken && item.Condition <= 0) { continue; }
                if (Submarine != null)
                {
                    if (!Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                }
                if (customPredicate != null && !customPredicate(item)) { continue; }
                if (identifiers != null && identifiers.None(id => item.Prefab.Identifier == id || item.HasTag(id))) { continue; }
                if (ignoredContainerIdentifiers != null && item.Container != null)
                {
                    if (ignoredContainerIdentifiers.Contains(item.ContainerIdentifier)) { continue; }
                }
                if (IsItemTakenBySomeoneElse(item)) { continue; }
                Entity rootInventoryOwner = item.GetRootInventoryOwner();
                if (rootInventoryOwner is Item ownerItem)
                {
                    if (!ownerItem.IsInteractable(this)) { continue; }
                }
                float itemPriority = customPriorityFunction != null ? customPriorityFunction(item) : 1;
                if (itemPriority <= 0) { continue; }
                Vector2 itemPos = (rootInventoryOwner ?? item).WorldPosition;
                Vector2 refPos = positionalReference != null ? positionalReference.WorldPosition : WorldPosition;
                float yDist = Math.Abs(refPos.Y - itemPos.Y);
                yDist = yDist > 100 ? yDist * 5 : 0;
                float dist = Math.Abs(refPos.X - itemPos.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, maxItemDistance, dist));
                itemPriority *= distanceFactor;
                if (itemPriority > _selectedItemPriority)
                {
                    _selectedItemPriority = itemPriority;
                    _foundItem = item;
                }
            }
            targetItem = _foundItem;
            bool completed = itemIndex >= Item.ItemList.Count - 1;
            if (HumanAIController.DebugAI && checkedItemCount > 0 && targetItem != null && StopWatch.ElapsedMilliseconds > 1)
            {
                var msg = $"Went through {checkedItemCount} of total {Item.ItemList.Count} items. Found item {targetItem.Name} in {StopWatch.ElapsedMilliseconds} ms. Completed: {completed}";
                if (StopWatch.ElapsedMilliseconds > 5)
                {
                    DebugConsole.ThrowError(msg);
                }
                else
                {
                    // An occasional warning now and then can be ignored, but multiple at the same time might indicate a performance issue.
                    DebugConsole.AddWarning(msg);
                }
            }
            return completed;
        }

        public bool IsItemTakenBySomeoneElse(Item item) => item.FindParentInventory(i => i.Owner != this && i.Owner is Character owner && !owner.IsDead && !owner.Removed) != null;

        public bool CanInteractWith(Character c, float maxDist = 200.0f, bool checkVisibility = true, bool skipDistanceCheck = false)
        {
            if (c == this || Removed || !c.Enabled || !c.CanBeSelected || c.InvisibleTimer > 0.0f) { return false; }
            if (!c.CharacterHealth.UseHealthWindow && !c.CanBeDragged && (c.onCustomInteract == null || !c.AllowCustomInteract)) { return false; }

            if (!skipDistanceCheck)
            {
                maxDist = Math.Max(ConvertUnits.ToSimUnits(maxDist), c.AnimController.Collider.GetMaxExtent());
                if (Vector2.DistanceSquared(SimPosition, c.SimPosition) > maxDist * maxDist &&
                    Vector2.DistanceSquared(SimPosition, c.AnimController.MainLimb.SimPosition) > maxDist * maxDist) 
                { 
                    return false; 
                }
            }

            return !checkVisibility || CanSeeCharacter(c);
        }

        public bool CanInteractWith(Item item, bool checkLinked = true)
        {
            return CanInteractWith(item, out _, checkLinked);
        }

        public bool CanInteractWith(Item item, out float distanceToItem, bool checkLinked)
        {
            distanceToItem = -1.0f;

            bool hidden = item.HiddenInGame;
#if CLIENT
            if (Screen.Selected == GameMain.SubEditorScreen) { hidden = false; }
#endif
            if (!CanInteract || hidden || !item.IsInteractable(this)) { return false; }

            if (item.ParentInventory != null)
            {
                return CanAccessInventory(item.ParentInventory);
            }

            Wire wire = item.GetComponent<Wire>();
            if (wire != null && item.GetComponent<ConnectionPanel>() == null)
            {
                //locked wires are never interactable
                if (wire.Locked) { return false; }
                if (wire.HiddenInGame && Screen.Selected == GameMain.GameScreen) { return false; }

                //wires are interactable if the character has selected an item the wire is connected to,
                //and it's disconnected from the other end
                if (wire.Connections[0]?.Item != null && SelectedItem == wire.Connections[0].Item)
                {
                    return wire.Connections[1] == null;
                }
                if (wire.Connections[1]?.Item != null && SelectedItem == wire.Connections[1].Item)
                {
                    return wire.Connections[0] == null;
                }
                if (SelectedItem?.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(wire) ?? false)
                {
                    return wire.Connections[0] == null && wire.Connections[1] == null;
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
            if (pickableComponent != null && pickableComponent.Picker != this && pickableComponent.Picker != null && !pickableComponent.Picker.IsDead) { return false; }

            if (SelectedItem?.GetComponent<RemoteController>()?.TargetItem == item) { return true; }
            //optimization: don't use HeldItems because it allocates memory and this method is executed very frequently
            var heldItem1 = Inventory?.GetItemInLimbSlot(InvSlotType.RightHand);
            if (heldItem1?.GetComponent<RemoteController>()?.TargetItem == item) { return true; }
            var heldItem2 = Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand);
            if (heldItem2?.GetComponent<RemoteController>()?.TargetItem == item) { return true; }

            Vector2 characterDirection = Vector2.Transform(Vector2.UnitY, Matrix.CreateRotationZ(AnimController.Collider.Rotation));

            Vector2 upperBodyPosition = Position + (characterDirection * 20.0f);
            Vector2 lowerBodyPosition = Position - (characterDirection * 60.0f);

            if (Submarine != null)
            {
                upperBodyPosition += Submarine.Position;
                lowerBodyPosition += Submarine.Position;
            }

            bool insideTrigger = item.IsInsideTrigger(upperBodyPosition) || item.IsInsideTrigger(lowerBodyPosition);
            if (item.Prefab.Triggers.Length > 0 && !insideTrigger && item.Prefab.RequireBodyInsideTrigger) { return false; }

            Rectangle itemDisplayRect = new Rectangle(item.InteractionRect.X, item.InteractionRect.Y - item.InteractionRect.Height, item.InteractionRect.Width, item.InteractionRect.Height);

            // Get the point along the line between lowerBodyPosition and upperBodyPosition which is closest to the center of itemDisplayRect
            Vector2 playerDistanceCheckPosition =
                lowerBodyPosition.Y < upperBodyPosition.Y ?
                Vector2.Clamp(itemDisplayRect.Center.ToVector2(), lowerBodyPosition, upperBodyPosition) :
                Vector2.Clamp(itemDisplayRect.Center.ToVector2(), upperBodyPosition, lowerBodyPosition);

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

            float interactDistance = item.InteractDistance;
            if ((SelectedSecondaryItem != null || item.IsSecondaryItem) && AnimController is HumanoidAnimController c)
            {
                // Use a distance slightly shorter than the arms length to keep the character in a comfortable pose
                float armLength = 0.75f * ConvertUnits.ToDisplayUnits(c.ArmLength);
                interactDistance = Math.Min(interactDistance, armLength);
            }
            if (distanceToItem > interactDistance && item.InteractDistance > 0.0f) { return false; }

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

            if (SelectedSecondaryItem != null && !item.IsSecondaryItem)
            {
                if (item.GetComponent<Controller>() is { } controller && controller.Direction != 0 && controller.Direction != AnimController.Direction) { return false; }
                float threshold = ConvertUnits.ToSimUnits(cursorFollowMargin);
                if (AnimController.Direction == Direction.Left && SimPosition.X + threshold < itemPosition.X) { return false; }
                if (AnimController.Direction == Direction.Right && SimPosition.X - threshold > itemPosition.X) { return false; }
            }

            if (!item.Prefab.InteractThroughWalls && Screen.Selected != GameMain.SubEditorScreen && !insideTrigger)
            {
                var body = Submarine.CheckVisibility(SimPosition, itemPosition, ignoreLevel: true);
                if (body != null && body.UserData as Item != item && (body.UserData as ItemComponent)?.Item != item && Submarine.LastPickedFixture?.UserData as Item != item) 
                { 
                    return false; 
                }
            }

            return true;
        }

        /// <summary>
        /// Set an action that's invoked when another character interacts with this one.
        /// </summary>
        /// <param name="onCustomInteract">Action invoked when another character interacts with this one. T1 = this character, T2 = the interacting character</param>
        /// <param name="hudText">Displayed on the character when highlighted.</param>
        public void SetCustomInteract(Action<Character, Character> onCustomInteract, LocalizedString hudText)
        {
            this.onCustomInteract = onCustomInteract;
            CustomInteractHUDText = hudText;
        }

        public void SelectCharacter(Character character)
        {
            if (character == null || character == this) { return; }
            SelectedCharacter = character;
        }

        public void DeselectCharacter()
        {
            if (SelectedCharacter == null) { return; }
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

            if (DisableInteract)
            {
                DisableInteract = false;
                return;
            }

            if (!CanInteract)
            {
                SelectedItem = SelectedSecondaryItem = null;
                focusedItem = null;
                if (!AllowInput)
                {
                    FocusedCharacter = null;
                    if (SelectedCharacter != null) { DeselectCharacter(); }
                    return;
                }
            }

#if CLIENT
            if (isLocalPlayer)
            {
                if (!IsMouseOnUI && (ViewTarget == null || ViewTarget == this) && !DisableFocusingOnEntities)
                {
                    if (findFocusedTimer <= 0.0f || Screen.Selected == GameMain.SubEditorScreen)
                    {
                        if (!PlayerInput.PrimaryMouseButtonHeld() || Barotrauma.Inventory.DraggingItemToWorld)
                        {
                            FocusedCharacter = CanInteract || CanEat ? FindCharacterAtPosition(mouseSimPos) : null;
                            if (FocusedCharacter != null && !CanSeeCharacter(FocusedCharacter)) { FocusedCharacter = null; }
                            float aimAssist = GameSettings.CurrentConfig.AimAssistAmount * (AnimController.InWater ? 1.5f : 1.0f);
                            if (HeldItems.Any(it => it?.GetComponent<Wire>()?.IsActive ?? false))
                            {
                                //disable aim assist when rewiring to make it harder to accidentally select items when adding wire nodes
                                aimAssist = 0.0f;
                            }

                            focusedItem = CanInteract ? FindItemAtPosition(mouseSimPos, aimAssist) : null;
                            if (focusedItem != null && focusedItem.CampaignInteractionType != CampaignMode.InteractionType.None)
                            {
                                FocusedCharacter = null;
                            }
                            findFocusedTimer = 0.05f;
                        }
                        else
                        {
                            if (focusedItem != null && !CanInteractWith(focusedItem)) { focusedItem = null; }
                            if (FocusedCharacter != null && !CanInteractWith(FocusedCharacter)) { FocusedCharacter = null; }
                        }
                    }
                }
                else
                {
                    FocusedCharacter = null;
                    focusedItem = null;
                }
                findFocusedTimer -= deltaTime;
                DisableFocusingOnEntities = false;
            }
#endif
            var head = AnimController.GetLimb(LimbType.Head);
            bool headInWater = head == null ? 
                AnimController.InWater : 
                head.InWater;
            //climb ladders automatically when pressing up/down inside their trigger area
            Ladder currentLadder = SelectedSecondaryItem?.GetComponent<Ladder>();
            if ((SelectedSecondaryItem == null || currentLadder != null) &&
                !headInWater && Screen.Selected != GameMain.SubEditorScreen)
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
                            if (isControlled)
                            {
                                ladder.Item.IsHighlighted = true;
                            }
                            break;
                        }
                    }
                }

                if (nearbyLadder != null && climbInput)
                {
                    if (nearbyLadder.Select(this))
                    {
                        SelectedSecondaryItem = nearbyLadder.Item;
                    }
                }
            }

            bool selectInputSameAsDeselect = false;
#if CLIENT
            selectInputSameAsDeselect = GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Select] == GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Deselect];
#endif

            if (SelectedCharacter != null && (IsKeyHit(InputType.Grab) || IsKeyHit(InputType.Health))) //Let people use ladders and buttons and stuff when dragging chars
            {
                DeselectCharacter();
            }
            else if (FocusedCharacter != null && IsKeyHit(InputType.Grab) && FocusedCharacter.CanBeDragged && (CanInteract || FocusedCharacter.IsDead && CanEat))
            {
                SelectCharacter(FocusedCharacter);
            }
            else if (FocusedCharacter != null && !FocusedCharacter.IsIncapacitated && IsKeyHit(InputType.Use) && FocusedCharacter.IsPet && CanInteract)
            {
                (FocusedCharacter.AIController as EnemyAIController).PetBehavior.Play(this);
            }
            else if (FocusedCharacter != null && IsKeyHit(InputType.Health) && FocusedCharacter.CharacterHealth.UseHealthWindow && CanInteract && CanInteractWith(FocusedCharacter, 160f, false))
            {
                if (FocusedCharacter == SelectedCharacter)
                {
                    DeselectCharacter();
#if CLIENT
                    if (Controlled == this)
                    {
                        CharacterHealth.OpenHealthWindow = null;
                    }
#endif
                }
                else
                {
#if CLIENT
                    if (Controlled == this)
                    {
                        HealingCooldown.PutOnCooldown();
                    }
#elif SERVER
                    if (GameMain.Server?.ConnectedClients is { } clients)
                    {
                        foreach (Client c in clients)
                        {
                            if (c.Character != this) { continue; }

                            HealingCooldown.SetCooldown(c);
                            break;
                        }
                    }
#endif
                    SelectCharacter(FocusedCharacter);
#if CLIENT
                    if (Controlled == this)
                    {
                        CharacterHealth.OpenHealthWindow = FocusedCharacter.CharacterHealth;
                    }
#endif
                }
            }
            else if (FocusedCharacter != null && IsKeyHit(InputType.Use) && FocusedCharacter.onCustomInteract != null && FocusedCharacter.AllowCustomInteract)
            {
                FocusedCharacter.onCustomInteract(FocusedCharacter, this);
            }
            else if (IsKeyHit(InputType.Deselect) && SelectedItem != null && 
                (focusedItem == null || focusedItem == SelectedItem || !selectInputSameAsDeselect))
            {
                SelectedItem = null;
#if CLIENT
                CharacterHealth.OpenHealthWindow = null;
#endif
            }
            else if (IsKeyHit(InputType.Deselect) && SelectedSecondaryItem != null && SelectedSecondaryItem.GetComponent<Ladder>() == null &&
                (focusedItem == null || focusedItem == SelectedSecondaryItem || !selectInputSameAsDeselect))
            {
                ReleaseSecondaryItem();
#if CLIENT
                CharacterHealth.OpenHealthWindow = null;
#endif
            }
            else if (IsKeyHit(InputType.Health) && (SelectedItem != null || SelectedSecondaryItem != null))
            {
                SelectedItem = SelectedSecondaryItem = null;
            }
            else if (focusedItem != null)
            {
#if CLIENT
                if (CharacterInventory.DraggingItemToWorld) { return; }
                if (selectInputSameAsDeselect)
                {
                    keys[(int)InputType.Deselect].Reset();
                }
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

                    if (c.IsPlayer || (c.IsBot && !c.IsDead))
                    {
                        c.Enabled = true;
                    }
                    else if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        //disable AI characters that are far away from all clients and the host's character and not controlled by anyone
                        float closestPlayerDist = c.GetDistanceToClosestPlayer();
                        if (closestPlayerDist > c.Params.DisableDistance)
                        {
                            c.Enabled = false;
                            if (c.IsDead && c.AIController is EnemyAIController)
                            {
                                Spawner?.AddEntityToRemoveQueue(c);
                            }
                        }
                        else if (closestPlayerDist < c.Params.DisableDistance * 0.9f)
                        {
                            c.Enabled = true;
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

                        if (distSqr > MathUtils.Pow2(c.Params.DisableDistance))
                        {
                            c.Enabled = false;
                            if (c.IsDead && c.AIController is EnemyAIController)
                            {
                                Entity.Spawner?.AddEntityToRemoveQueue(c);
                            }
                        }
                        else if (distSqr < MathUtils.Pow2(c.Params.DisableDistance * 0.9f))
                        {
                            c.Enabled = true;
                        }
                    }
                }
            }

            for (int i = 0; i < CharacterList.Count; i++)
            {
                var character = CharacterList[i];
                System.Diagnostics.Debug.Assert(character != null && !character.Removed);
                character.Update(deltaTime, cam);
            }
        }

        public virtual void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime, cam);

            if (InvisibleTimer > 0.0f)
            {
                if (Controlled == null || Controlled == this || (Controlled.CharacterHealth.GetAffliction("psychosis")?.Strength ?? 0.0f) <= 0.0f)
                {
                    InvisibleTimer = Math.Min(InvisibleTimer, 1.0f);
                }
                InvisibleTimer -= deltaTime;
            }

            KnockbackCooldownTimer -= deltaTime;

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && this == Controlled && !isSynced) { return; }

            UpdateDespawn(deltaTime);

            if (!Enabled) { return; }

            if (Level.Loaded != null)
            {
                if (WorldPosition.Y < Level.MaxEntityDepth ||
                    (Submarine != null && Submarine.WorldPosition.Y < Level.MaxEntityDepth))
                {
                    Enabled = false;
                    Kill(CauseOfDeathType.Pressure, null);
                    return;
                }
            }

            ApplyStatusEffects(ActionType.Always, deltaTime);

            PreviousHull = CurrentHull;
            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull, useWorldCoordinates: true);

            speechBubbleTimer = Math.Max(0.0f, speechBubbleTimer - deltaTime);

            obstructVisionAmount = Math.Max(obstructVisionAmount - deltaTime, 0.0f);

            if (Inventory != null)
            {
                foreach (Item item in Inventory.AllItems)
                {
                    if (item.body == null || item.body.Enabled) { continue; }
                    item.SetTransform(SimPosition, 0.0f);
                    item.Submarine = Submarine;
                }
            }

            HideFace = false;
            IgnoreMeleeWeapons = false;

            UpdateSightRange(deltaTime);
            UpdateSoundRange(deltaTime);

            UpdateAttackers(deltaTime);

            foreach (var characterTalent in characterTalents)
            {
                characterTalent.UpdateTalent(deltaTime);
            }

            if (IsDead) { return; }

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

            if (NeedsAir)
            {
                //implode if not protected from pressure, and either outside or in a high-pressure hull
                if (!IsProtectedFromPressure && (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f))
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
                            if (IsDead) { return; }
                        }
                    }
                }
                else
                {
                    PressureTimer = 0.0f;
                }
            }
            else if ((GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) && !IsProtectedFromPressure)
            {
                float realWorldDepth = Level.Loaded?.GetRealWorldDepth(WorldPosition.Y) ?? 0.0f;
                if (PressureProtection < realWorldDepth && realWorldDepth > CharacterHealth.CrushDepth)
                {
                    //implode if below crush depth, and either outside or in a high-pressure hull                
                    if (AnimController.CurrentHull == null || AnimController.CurrentHull.LethalPressure >= 80.0f)
                    {
                        Implode();
                        if (IsDead) { return; }
                    }
                }
            }

            ApplyStatusEffects(AnimController.InWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            //wait 0.1 seconds so status effects that continuously set InDetectable to true can keep the character InDetectable
            if (aiTarget != null && Timing.TotalTime > aiTarget.InDetectableSetTime + 0.1f)
            {
                aiTarget.InDetectable = false;
            }

            UpdateControlled(deltaTime, cam);

            //Health effects
            if (NeedsOxygen)
            {
                UpdateOxygen(deltaTime);
            }

            CalculateHealthMultiplier();
            CharacterHealth.Update(deltaTime);

            if (IsIncapacitated)
            {
                Stun = Math.Max(5.0f, Stun);
                AnimController.ResetPullJoints();
                SelectedItem = SelectedSecondaryItem = null;
                return;
            }

            UpdateAIChatMessages(deltaTime);

            if (GameMain.NetworkMember?.ServerSettings?.AllowRagdollButton ?? true)
            {
                bool wasRagdolled = IsRagdolled;
                if (IsForceRagdolled)
                {
                    IsRagdolled = IsForceRagdolled;
                }
                else if (this != Controlled)
                {
                    wasRagdolled = IsRagdolled;
                    IsRagdolled = IsKeyDown(InputType.Ragdoll);
                }
                else
                {
                    bool tooFastToUnragdoll = bodyMovingTooFast(AnimController.Collider) || bodyMovingTooFast(AnimController.MainLimb.body);
                    bool bodyMovingTooFast(PhysicsBody body)
                    {
                        return
                            body.LinearVelocity.LengthSquared() > 8.0f * 8.0f ||
                            //falling down counts as going too fast
                            (!InWater && body.LinearVelocity.Y < -5.0f);
                    }
                    if (ragdollingLockTimer > 0.0f)
                    {
                        ragdollingLockTimer -= deltaTime;
                    }
                    else if (!tooFastToUnragdoll)
                    {
                        IsRagdolled = IsKeyDown(InputType.Ragdoll); //Handle this here instead of Control because we can stop being ragdolled ourselves
                        if (wasRagdolled != IsRagdolled) { ragdollingLockTimer = 0.2f; }
                    }
                    if (IsRagdolled)
                    {
                        SetInput(InputType.Ragdoll, false, true);
                    }
                }
                if (!wasRagdolled && IsRagdolled)
                {
                    CheckTalents(AbilityEffectType.OnRagdoll);
                }
            }

            lowPassMultiplier = MathHelper.Lerp(lowPassMultiplier, 1.0f, 0.1f);

            if (IsRagdolled || !CanMove)
            {
                if (AnimController is HumanoidAnimController humanAnimController) 
                { 
                    humanAnimController.Crouching = false; 
                }
                if (IsRagdolled) { AnimController.IgnorePlatforms = true; }
                AnimController.ResetPullJoints();
                SelectedItem = SelectedSecondaryItem = null;
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

            if (SelectedItem != null && !CanInteractWith(SelectedItem))
            {
                SelectedItem = null;
            }
            if (SelectedSecondaryItem != null && !CanInteractWith(SelectedSecondaryItem))
            {
                ReleaseSecondaryItem();
            }

            if (!IsDead) { LockHands = false; }
        }

        partial void UpdateControlled(float deltaTime, Camera cam);

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        partial void SetOrderProjSpecific(Order order);


        public void AddAttacker(Character character, float damage)
        {
            Attacker attacker = lastAttackers.FirstOrDefault(a => a.Character == character);
            if (attacker != null)
            {
                lastAttackers.Remove(attacker);
            }
            else
            {
                attacker = new Attacker { Character = character };
            }

            if (lastAttackers.Count > maxLastAttackerCount)
            {
                lastAttackers.RemoveRange(0, lastAttackers.Count - maxLastAttackerCount);
            }

            attacker.Damage += damage;
            lastAttackers.Add(attacker);
        }

        public void ForgiveAttacker(Character character)
        {
            int index;
            if ((index = lastAttackers.FindIndex(a => a.Character == character)) >= 0)
            {
                lastAttackers.RemoveAt(index);
            }
        }

        public float GetDamageDoneByAttacker(Character otherCharacter)
        {
            if (otherCharacter == null) { return 0; }
            float dmg = 0;
            Attacker attacker = LastAttackers.LastOrDefault(a => a.Character == otherCharacter);
            if (attacker != null)
            {
                dmg = attacker.Damage;
            }
            return dmg;
        }

        private void UpdateAttackers(float deltaTime)
        {
            //slowly forget about damage done by attackers
            foreach (Attacker enemy in LastAttackers)
            {
                float cumulativeDamage = enemy.Damage;
                if (cumulativeDamage > 0)
                {
                    float reduction = deltaTime;
                    if (cumulativeDamage < 2)
                    {
                        // If the damage is very low, let's not forget so quickly, or we can't cumulate the damage from repair tools (high frequency, low damage)
                        reduction *= 0.5f;
                    }
                    enemy.Damage = Math.Max(0.0f, enemy.Damage - reduction);
                }
            }
        }

        private void UpdateOxygen(float deltaTime)
        {
            if (NeedsAir)
            {
                if (Timing.TotalTime > pressureProtectionLastSet + 0.1)
                {
                    pressureProtection = 0.0f;
                }
            }
            if (NeedsWater)
            {
                float waterAvailable = 100;
                if (!AnimController.InWater && CurrentHull != null)
                {
                    waterAvailable = CurrentHull.WaterPercentage;
                }
                OxygenAvailable += MathHelper.Clamp(waterAvailable - oxygenAvailable, -deltaTime * 50.0f, deltaTime * 50.0f);
            }
            else
            {
                float hullAvailableOxygen = 0.0f;
                if (!AnimController.HeadInWater && AnimController.CurrentHull != null)
                {
                    //don't decrease the amount of oxygen in the hull if the character has more oxygen available than the hull
                    //(i.e. if the character has some external source of oxygen)
                    if (OxygenAvailable * 0.98f < AnimController.CurrentHull.OxygenPercentage && UseHullOxygen)
                    {
                        AnimController.CurrentHull.Oxygen -= Hull.OxygenConsumptionSpeed * deltaTime;
                    }
                    hullAvailableOxygen = AnimController.CurrentHull.OxygenPercentage;

                }
                OxygenAvailable += MathHelper.Clamp(hullAvailableOxygen - oxygenAvailable, -deltaTime * 50.0f, deltaTime * 50.0f);
            }
            UseHullOxygen = true;
        }

        /// <summary>
        /// How far the character is from the closest human player (including spectators)
        /// </summary>
        protected float GetDistanceToClosestPlayer()
        {
            return (float)Math.Sqrt(GetDistanceSqrToClosestPlayer());
        }

        /// <summary>
        /// How far the character is from the closest human player (including spectators)
        /// </summary>
        protected float GetDistanceSqrToClosestPlayer()
        {
            float distSqr = float.MaxValue;
            foreach (Character otherCharacter in CharacterList)
            {
                if (otherCharacter == this || !otherCharacter.IsRemotePlayer) { continue; }
                distSqr = Math.Min(distSqr, Vector2.DistanceSquared(otherCharacter.WorldPosition, WorldPosition));
                if (otherCharacter.ViewTarget != null)
                {
                    distSqr = Math.Min(distSqr, Vector2.DistanceSquared(otherCharacter.ViewTarget.WorldPosition, WorldPosition));
                }
            }
#if SERVER
            for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
            {
                var spectatePos = GameMain.Server.ConnectedClients[i].SpectatePos;
                if (spectatePos != null)
                {
                    distSqr = Math.Min(distSqr, Vector2.DistanceSquared(spectatePos.Value, WorldPosition));
                }
            }
#else
            if (this == Controlled) { return 0.0f; }
            if (controlled != null)
            {
                distSqr = Math.Min(distSqr, Vector2.DistanceSquared(Controlled.WorldPosition, WorldPosition));
            }
            distSqr = Math.Min(distSqr, Vector2.DistanceSquared(GameMain.GameScreen.Cam.Position, WorldPosition));
#endif
            return distSqr;
        }

        public float GetDistanceToClosestLimb(Vector2 simPos)
        {
            float closestDist = float.MaxValue;
            foreach (Limb limb in AnimController.Limbs)
            {
                if (limb.IsSevered) { continue; }
                float dist = Vector2.Distance(simPos, limb.SimPosition);
                dist -= limb.body.GetMaxExtent();
                closestDist = Math.Min(closestDist, dist);
                if (closestDist <= 0.0f) { return 0.0f; }
            }
            return closestDist;
        }

        private float despawnTimer;
        private void UpdateDespawn(float deltaTime, bool ignoreThresholds = false, bool createNetworkEvents = true)
        {
            if (!EnableDespawn) { return; }

            //clients don't despawn characters unless the server says so
            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return; }

            if (!IsDead || (CauseOfDeath?.Type == CauseOfDeathType.Disconnected && GameMain.GameSession?.Campaign != null)) { return; }

            int subCorpseCount = 0;

            if (Submarine != null && !ignoreThresholds)
            {
                subCorpseCount = CharacterList.Count(c => c.IsDead && c.Submarine == Submarine);
                if (subCorpseCount < GameSettings.CurrentConfig.CorpsesPerSubDespawnThreshold) { return; }
            }

            if (SelectedBy != null)
            {
                despawnTimer = 0.0f;
                return;
            }

            float distToClosestPlayer = GetDistanceToClosestPlayer();
            if (distToClosestPlayer > Params.DisableDistance)
            {
                //despawn in 1 minute if very far from all human players
                despawnTimer = Math.Max(despawnTimer, GameSettings.CurrentConfig.CorpseDespawnDelay - 60.0f);
            }

            float despawnPriority = 1.0f;
            if (subCorpseCount > GameSettings.CurrentConfig.CorpsesPerSubDespawnThreshold)
            {
                //despawn faster if there are lots of corpses in the sub (twice as many as the threshold -> despawn twice as fast)
                despawnPriority += (subCorpseCount - GameSettings.CurrentConfig.CorpsesPerSubDespawnThreshold) / (float)GameSettings.CurrentConfig.CorpsesPerSubDespawnThreshold;
            }
            if (AIController is EnemyAIController)
            {
                //enemies despawn faster
                despawnPriority *= 2.0f;
            }
            
            despawnTimer += deltaTime * despawnPriority;
            if (despawnTimer < GameSettings.CurrentConfig.CorpseDespawnDelay) { return; }

            Identifier despawnContainerId =
                IsHuman ?
                "despawncontainer".ToIdentifier() :
                Params.DespawnContainer;
            if (!despawnContainerId.IsEmpty)
            {
                var containerPrefab =
                    MapEntityPrefab.FindByIdentifier(despawnContainerId) as ItemPrefab ??
                    ItemPrefab.Prefabs.Find(me => me?.Tags != null && me.Tags.Contains(despawnContainerId)) ??
                    (MapEntityPrefab.FindByIdentifier("metalcrate".ToIdentifier()) as ItemPrefab);
                if (containerPrefab == null)
                {
                    DebugConsole.NewMessage($"Could not spawn a container for a despawned character's items. No item with the tag \"{despawnContainerId}\" or the identifier \"metalcrate\" found.", Color.Red);
                }
                else
                {
                    Spawner?.AddItemToSpawnQueue(containerPrefab, WorldPosition, onSpawned: onItemContainerSpawned);
                }

                void onItemContainerSpawned(Item item)
                {
                    if (Inventory == null) { return; }

                    item.UpdateTransform();                
                    item.AddTag("name:" + Name);
                    if (info?.Job != null) { item.AddTag($"job:{info.Job.Name}"); }

                    var itemContainer = item?.GetComponent<ItemContainer>();
                    if (itemContainer == null) { return; }
                    List<Item> inventoryItems = new List<Item>(Inventory.AllItemsMod);
                    foreach (Item inventoryItem in inventoryItems)
                    {
                        if (!itemContainer.Inventory.TryPutItem(inventoryItem, user: null, createNetworkEvent: createNetworkEvents))
                        {
                            //if the item couldn't be put inside the despawn container, just drop it
                            inventoryItem.Drop(dropper: this, createNetworkEvent: createNetworkEvents);
                        }
                    }
                    //this needs to happen after the items have been dropped (we can no longer sync dropping the items if the character has been removed)
                    Spawner.AddEntityToRemoveQueue(this);
                }
            }
            else
            {
                Spawner.AddEntityToRemoveQueue(this);
            }
        }

        public void DespawnNow(bool createNetworkEvents = true)
        {
            despawnTimer = GameSettings.CurrentConfig.CorpseDespawnDelay;
            UpdateDespawn(1.0f, ignoreThresholds: true, createNetworkEvents: createNetworkEvents);
            //update twice: first to spawn the duffel bag and move the items into it, then to remove the character
            for (int i = 0; i < 2; i++)
            {
                Spawner.Update(createNetworkEvents);
            }
        }

        public static void RemoveByPrefab(CharacterPrefab prefab)
        {
            if (CharacterList == null) { return; }
            List<Character> list = new List<Character>(CharacterList);
            foreach (Character character in list)
            {
                if (character.Prefab == prefab)
                {
                    character.Remove();
                }
            }
        }

        private readonly float maxAIRange = 20000;
        private readonly float aiTargetChangeSpeed = 5;

        private void UpdateSightRange(float deltaTime)
        {
            if (aiTarget == null) { return; }
            float minRange = Math.Clamp((float)Math.Sqrt(Mass) * Visibility, 250, 1000);
            float massFactor = (float)Math.Sqrt(Mass / 20);
            float targetRange = Math.Min(minRange + massFactor * AnimController.Collider.LinearVelocity.Length() * 2 * Visibility, maxAIRange);
            float newRange = MathHelper.SmoothStep(aiTarget.SightRange, targetRange, deltaTime * aiTargetChangeSpeed);
            if (!float.IsNaN(newRange))
            {
                aiTarget.SightRange = newRange;
            }
        }

        private void UpdateSoundRange(float deltaTime)
        {
            if (aiTarget == null) { return; }
            if (IsDead)
            {
                aiTarget.SoundRange = 0;
            }
            else
            {
                float massFactor = (float)Math.Sqrt(Mass / 10);
                float targetRange = Math.Min(massFactor * AnimController.Collider.LinearVelocity.Length() * 2 * Noise, maxAIRange);
                float newRange = MathHelper.SmoothStep(aiTarget.SoundRange, targetRange, deltaTime * aiTargetChangeSpeed);
                if (!float.IsNaN(newRange))
                {
                    aiTarget.SoundRange = newRange;
                }
            }
        }

        public bool CanHearCharacter(Character speaker)
        {
            if (speaker == null || speaker.SpeechImpediment > 100.0f) { return false; }
            if (speaker == this) { return true; }
            ChatMessageType messageType = ChatMessage.CanUseRadio(speaker) && ChatMessage.CanUseRadio(this) ?
                ChatMessageType.Radio : 
                ChatMessageType.Default;
            return !string.IsNullOrEmpty(ChatMessage.ApplyDistanceEffect("message", messageType, speaker, this));
        }

        /// <param name="force">Force an order to be set for the character, bypassing hearing checks</param>
        public void SetOrder(Order order, bool isNewOrder, bool speak = true, bool force = false)
        {
            var orderGiver = order?.OrderGiver;
            //set the character order only if the character is close enough to hear the message
            if (!force && orderGiver != null && !CanHearCharacter(orderGiver)) { return; }

            if (order != null)
            {
                if (order.AutoDismiss)
                {
                    switch (order.Category)
                    {
                        case OrderCategory.Operate when order.TargetEntity != null:
                            // If there's another character operating the same device, make them dismiss themself
                            foreach (var character in CharacterList)
                            {
                                if (character == this) { continue; }
                                if (character.TeamID != TeamID) { continue; }
                                if (character.AIController is not HumanAIController) { continue; }
                                if (!HumanAIController.IsActive(character)) { continue; }
                                foreach (var currentOrder in character.CurrentOrders)
                                {
                                    if (currentOrder == null) { continue; }
                                    if (currentOrder.Category != OrderCategory.Operate) { continue; }
                                    if (currentOrder.Identifier != order.Identifier) { continue; }
                                    if (currentOrder.TargetEntity != order.TargetEntity) { continue; }
                                    if (!currentOrder.AutoDismiss) { continue; }
                                    character.SetOrder(currentOrder.GetDismissal(), isNewOrder, speak: speak, force: force);
                                    break;
                                }
                            }
                            break;
                        case OrderCategory.Movement:
                            // If there character has another movement order, dismiss that order
                            Order orderToReplace = null;
                            foreach (var currentOrder in CurrentOrders)
                            {
                                if (currentOrder == null) { continue; }
                                if (currentOrder.Category != OrderCategory.Movement) { continue; }
                                orderToReplace = currentOrder;
                                break;
                            }
                            if (orderToReplace is { AutoDismiss: true })
                            {
                                SetOrder(orderToReplace.GetDismissal(), isNewOrder, speak: speak, force: force);
                            }
                            break;
                    }
                }
            }

            // Prevent adding duplicate orders
            RemoveDuplicateOrders(order);
            AddCurrentOrder(order);

            if (orderGiver != null && order.Identifier != "dismissed" && isNewOrder)
            {
                var abilityOrderedCharacter = new AbilityOrderedCharacter(this);
                orderGiver.CheckTalents(AbilityEffectType.OnGiveOrder, abilityOrderedCharacter);

                if (order.OrderGiver.LastOrderedCharacter != this)
                {
                    order.OrderGiver.SecondLastOrderedCharacter = order.OrderGiver.LastOrderedCharacter;
                    order.OrderGiver.LastOrderedCharacter = this;
                }
            }

            if (AIController is HumanAIController humanAI)
            {
                humanAI.SetOrder(order, speak);
            }
            SetOrderProjSpecific(order);
        }

        private void AddCurrentOrder(Order newOrder)
        {
            if (newOrder == null || newOrder.Identifier == "dismissed")
            {
                if (newOrder.Option != Identifier.Empty)
                {
                    if (CurrentOrders.Any(o => o.MatchesDismissedOrder(newOrder.Option)))
                    {
                        var dismissedOrderInfo = CurrentOrders.First(o => o.MatchesDismissedOrder(newOrder.Option));
                        int dismissedOrderPriority = dismissedOrderInfo.ManualPriority;
                        CurrentOrders.Remove(dismissedOrderInfo);
                        for (int i = 0; i < CurrentOrders.Count; i++)
                        {
                            var orderInfo = CurrentOrders[i];
                            if (orderInfo.ManualPriority < dismissedOrderPriority)
                            {
                                CurrentOrders[i] = orderInfo.WithManualPriority(orderInfo.ManualPriority + 1);
                            }
                        }
                    }
                }
                else
                {
                    CurrentOrders.Clear();
                }
            }
            else
            {
                for (int i = 0; i < CurrentOrders.Count; i++)
                {
                    var orderInfo = CurrentOrders[i];
                    if (orderInfo.ManualPriority <= newOrder.ManualPriority)
                    {
                        CurrentOrders[i] = orderInfo.WithManualPriority(orderInfo.ManualPriority - 1);
                    }
                }
                CurrentOrders.RemoveAll(order => order.ManualPriority <= 0);
                CurrentOrders.Add(newOrder);
                // Sort the current orders so the one with the highest priority comes first
                CurrentOrders.Sort((x, y) => y.ManualPriority.CompareTo(x.ManualPriority));
            }
        }

        private bool RemoveDuplicateOrders(Order order)
        {
            bool removed = false;
            int? priorityOfRemoved = null;
            for (int i = CurrentOrders.Count - 1; i >= 0; i--)
            {
                var orderInfo = CurrentOrders[i];
                if (order.Identifier == orderInfo.Identifier)
                {
                    priorityOfRemoved = orderInfo.ManualPriority;
                    CurrentOrders.RemoveAt(i);
                    removed = true;
                    break;
                }
            }

            if (!priorityOfRemoved.HasValue) { return removed; }

            for (int i = 0; i < CurrentOrders.Count; i++)
            {
                var orderInfo = CurrentOrders[i];
                if (orderInfo.ManualPriority < priorityOfRemoved.Value)
                {
                    CurrentOrders[i] = orderInfo.WithManualPriority(orderInfo.ManualPriority + 1);
                }
            }

            CurrentOrders.RemoveAll(order => order.ManualPriority <= 0);
            // Sort the current orders so the one with the highest priority comes first
            CurrentOrders.Sort((x, y) => y.ManualPriority.CompareTo(x.ManualPriority));

            return removed;
        }

        public Order GetCurrentOrderWithTopPriority()
        {
            return GetCurrentOrder(orderInfo =>
            {
                if (orderInfo == null) { return false; }
                if (orderInfo.Identifier == "dismissed") { return false; }
                if (orderInfo.ManualPriority < 1) { return false; }
                return true;
            });
        }

        public Order GetCurrentOrder(Order order)
        {
            return GetCurrentOrder(orderInfo =>
            {
                return orderInfo.MatchesOrder(order);
            });
        }

        private Order GetCurrentOrder(Func<Order, bool> predicate)
        {
            if (CurrentOrders != null && CurrentOrders.Any(predicate))
            {
                return CurrentOrders.First(predicate);
            }
            else
            {
                return null;
            }
        }

        private readonly List<AIChatMessage> aiChatMessageQueue = new List<AIChatMessage>();

        //key = identifier, value = time the message was sent
        private readonly Dictionary<Identifier, float> prevAiChatMessages = new Dictionary<Identifier, float>();

        public void DisableLine(Identifier identifier)
        {
            if (identifier != Identifier.Empty)
            {
                prevAiChatMessages[identifier] = (float)Timing.TotalTime;
            }
        }

        public void DisableLine(string identifier)
        {
            DisableLine(identifier.ToIdentifier());
        }

        public void Speak(string message, ChatMessageType? messageType = null, float delay = 0.0f, Identifier identifier = default, float minDurationBetweenSimilar = 0.0f)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(message)) { return; }

            if (SpeechImpediment >= 100.0f) { return; }

            if (prevAiChatMessages.ContainsKey(identifier) && 
                prevAiChatMessages[identifier] < Timing.TotalTime - minDurationBetweenSimilar) 
            { 
                prevAiChatMessages.Remove(identifier);                 
            }

            //already sent a similar message a moment ago
            if (identifier != Identifier.Empty && minDurationBetweenSimilar > 0.0f &&
                (aiChatMessageQueue.Any(m => m.Identifier == identifier) || prevAiChatMessages.ContainsKey(identifier)))
            {
                return;
            }
            aiChatMessageQueue.Add(new AIChatMessage(message, messageType, identifier, delay));
        }

        private void UpdateAIChatMessages(float deltaTime)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            List<AIChatMessage> sentMessages = new List<AIChatMessage>();
            foreach (AIChatMessage message in aiChatMessageQueue)
            {
                message.SendDelay -= deltaTime;
                if (message.SendDelay > 0.0f) { continue; }

                bool canUseRadio = ChatMessage.CanUseRadio(this, out WifiComponent radio);
                if (message.MessageType == null)
                {
                    message.MessageType = canUseRadio ? ChatMessageType.Radio : ChatMessageType.Default;
                }
#if CLIENT
                if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.IsSinglePlayer)
                {
                    string modifiedMessage = ChatMessage.ApplyDistanceEffect(message.Message, message.MessageType.Value, this, Controlled);
                    if (!string.IsNullOrEmpty(modifiedMessage))
                    {
                        GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(Name, modifiedMessage, message.MessageType.Value, this);
                    }
                    if (canUseRadio)
                    {
                        Signal s = new Signal(modifiedMessage, sender: this, source: radio.Item);
                        radio.TransmitSignal(s, sentFromChat: true);
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
                if (sent.Identifier != Identifier.Empty)
                {
                    prevAiChatMessages[sent.Identifier] = (float)sent.SendTime;
                }
            }

            if (prevAiChatMessages.Count > 100)
            {
                HashSet<Identifier> toRemove = new HashSet<Identifier>();
                foreach (KeyValuePair<Identifier,float> prevMessage in prevAiChatMessages)
                {
                    if (prevMessage.Value < Timing.TotalTime - 60.0f)
                    {
                        toRemove.Add(prevMessage.Key);
                    }
                }
                foreach (Identifier identifier in toRemove)
                {
                    prevAiChatMessages.Remove(identifier);
                }
            }
        }


        public void ShowSpeechBubble(float duration, Color color)
        {
            speechBubbleTimer = Math.Max(speechBubbleTimer, duration);
            speechBubbleColor = color;
        }

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
        public AttackResult ApplyAttack(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false, Limb targetLimb = null, float penetration = 0f)
        {
            if (Removed)
            {
                string errorMsg = "Tried to apply an attack to a removed character ([name]).\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg.Replace("[name]", Name));
                GameAnalyticsManager.AddErrorEventOnce("Character.ApplyAttack:RemovedCharacter", GameAnalyticsManager.ErrorSeverity.Error, errorMsg.Replace("[name]", SpeciesName.Value));
                return new AttackResult();
            }

            Limb limbHit = targetLimb;

            float attackImpulse = attack.TargetImpulse + attack.TargetForce  * attack.ImpactMultiplier * deltaTime;

            AbilityAttackData attackData = new AbilityAttackData(attack, this, attacker);
            IEnumerable<Affliction> attackAfflictions;
            if (attackData.Afflictions != null)
            {
                attackAfflictions = attackData.Afflictions.Union(attack.Afflictions.Keys);
            }
            else
            {
                attackAfflictions = attack.Afflictions.Keys;
            }

            var attackResult = targetLimb == null ?
                AddDamage(worldPosition, attackAfflictions, attack.Stun, playSound, attackImpulse, out limbHit, attacker, attack.DamageMultiplier * attackData.DamageMultiplier) :
                DamageLimb(worldPosition, targetLimb, attackAfflictions, attack.Stun, playSound, attackImpulse, attacker, attack.DamageMultiplier * attackData.DamageMultiplier, penetration: penetration + attackData.AddedPenetration, shouldImplode: attackData.ShouldImplode);

            if (attacker != null)
            {
                var abilityAttackResult = new AbilityAttackResult(attackResult);
                attacker.CheckTalents(AbilityEffectType.OnAttackResult, abilityAttackResult);
                CheckTalents(AbilityEffectType.OnAttackedResult, abilityAttackResult);
            }

            if (limbHit == null) { return new AttackResult(); }
            Vector2 forceWorld = attack.TargetImpulseWorld + attack.TargetForceWorld * attack.ImpactMultiplier;
            if (attacker != null)
            {
                forceWorld.X *= attacker.AnimController.Dir;
            }
            limbHit.body?.ApplyLinearImpulse(forceWorld * deltaTime, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            var mainLimb = limbHit.character.AnimController.MainLimb;
            if (limbHit != mainLimb)
            {
                // Always add force to mainlimb
                mainLimb.body?.ApplyLinearImpulse(forceWorld * deltaTime, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }
#if SERVER
            if (attacker is Character attackingCharacter && attackingCharacter.AIController == null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(GameServer.CharacterLogName(this) + " attacked by " + GameServer.CharacterLogName(attackingCharacter) + ".");
                if (attackResult.Afflictions != null)
                {
                    foreach (Affliction affliction in attackResult.Afflictions)
                    {
                        if (Math.Abs(affliction.Strength) <= 0.1f) { continue;}
                        sb.Append($" {affliction.Prefab.Name}: {affliction.Strength.ToString("0.0")}");
                    }
                }
                GameServer.Log(sb.ToString(), ServerLog.MessageType.Attack);            
            }
#endif
            // Don't allow beheading for monster attacks, because it happens too frequently (crawlers/tigerthreshers etc attacking each other -> they will most often target to the head)
            TrySeverLimbJoints(limbHit, attack.SeverLimbsProbability, attackResult.Damage, allowBeheading: attacker == null || attacker.IsHuman || attacker.IsPlayer, attacker: attacker);

            return attackResult;
        }

        public void TrySeverLimbJoints(Limb targetLimb, float severLimbsProbability, float damage, bool allowBeheading, bool ignoreSeveranceProbabilityModifier = false, Character attacker = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
#if DEBUG
            if (targetLimb.character != this)
            {
                DebugConsole.ThrowError($"{Name} is attempting to sever joints of {targetLimb.character.Name}!");
                return;
            }
#endif
            if (damage > 0 && damage < targetLimb.Params.MinSeveranceDamage) { return; }
            if (!IsDead)
            {
                if (!allowBeheading && targetLimb.type == LimbType.Head) { return; }
                if (!targetLimb.CanBeSeveredAlive) { return; }
            }
            bool wasSevered = false;
            float random = Rand.Value();
            foreach (LimbJoint joint in AnimController.LimbJoints)
            {
                if (!joint.CanBeSevered) { continue; }
                // Limb A is where we start creating the joint and LimbB is where the joint ends.
                // Normally the joints have been created starting from the body, in which case we'd want to use LimbB e.g. to severe a hand when it's hit.
                // But heads are a different case, because many characters have been created so that the head is first and then comes the rest of the body.
                // If this is the case, we'll have to use LimbA to decapitate the creature when it's hit on the head. Otherwise decapitation could happen only when we hit the body, not the head.
                var referenceLimb = targetLimb.type == LimbType.Head && targetLimb.Params.ID == 0 ? joint.LimbA : joint.LimbB;
                if (referenceLimb != targetLimb) { continue; }
                float probability = severLimbsProbability;
                if (!IsDead && !ignoreSeveranceProbabilityModifier)
                {
                    probability *= joint.Params.SeveranceProbabilityModifier;
                }
                if (probability <= 0) { continue; }
                if (random > probability) { continue; }
                bool severed = AnimController.SeverLimbJoint(joint);
                if (!wasSevered)
                {
                    wasSevered = severed;
                }
                if (severed)
                {       
                    Limb otherLimb = joint.LimbA == targetLimb ? joint.LimbB : joint.LimbA;
                    otherLimb.body.ApplyLinearImpulse(targetLimb.LinearVelocity * targetLimb.Mass, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.5f);
                    if (attacker != null)
                    {
                        if (statusEffects.TryGetValue(ActionType.OnSevered, out var statusEffectList))
                        {
                            foreach (var statusEffect in statusEffectList)
                            {
                                statusEffect.SetUser(attacker);
                            }
                        }
                        if (targetLimb.StatusEffects.TryGetValue(ActionType.OnSevered, out var limbStatusEffectList))
                        {
                            foreach (var statusEffect in limbStatusEffectList)
                            {
                                statusEffect.SetUser(attacker);
                            }
                        }
                    }
                    ApplyStatusEffects(ActionType.OnSevered, 1.0f);
                    targetLimb.ApplyStatusEffects(ActionType.OnSevered, 1.0f); 
                }
            }
            if (wasSevered && targetLimb.character.AIController is EnemyAIController enemyAI)
            {
                enemyAI.ReevaluateAttacks();
            }            
        }

        public AttackResult AddDamage(Vector2 worldPosition, IEnumerable<Affliction> afflictions, float stun, bool playSound, float attackImpulse = 0.0f, Character attacker = null, float damageMultiplier = 1f)
        {
            return AddDamage(worldPosition, afflictions, stun, playSound, attackImpulse, out _, attacker, damageMultiplier: damageMultiplier);
        }

        public AttackResult AddDamage(Vector2 worldPosition, IEnumerable<Affliction> afflictions, float stun, bool playSound, float attackImpulse, out Limb hitLimb, Character attacker = null, float damageMultiplier = 1)
        {
            hitLimb = null;

            if (Removed) { return new AttackResult(); }

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

            return DamageLimb(worldPosition, hitLimb, afflictions, stun, playSound, attackImpulse, attacker, damageMultiplier);
        }

        public void RecordKill(Character target)
        {
            var abilityCharacterKill = new AbilityCharacterKill(target, this);
            foreach (Character attackerCrewmember in GetFriendlyCrew(this))
            {
                attackerCrewmember.CheckTalents(AbilityEffectType.OnCrewKillCharacter, abilityCharacterKill);
            }
            CheckTalents(AbilityEffectType.OnKillCharacter, abilityCharacterKill);

            if (!IsOnPlayerTeam) { return; }
            CreatureMetrics.RecordKill(target.SpeciesName);
        }

        public AttackResult DamageLimb(Vector2 worldPosition, Limb hitLimb, IEnumerable<Affliction> afflictions, float stun, bool playSound, float attackImpulse, Character attacker = null, float damageMultiplier = 1, bool allowStacking = true, float penetration = 0f, bool shouldImplode = false)
        {
            if (Removed) { return new AttackResult(); }

            //character inside the sub received damage from a monster outside the sub
            //can happen during normal gameplay if someone for example fires a ranged weapon from outside, 
            //the intention of this error message is to diagnose an issue with monsters being able to damage characters from outside

            // Disabled, because this happens every now and then when the monsters can get in and out of the sub.

//            if (attacker?.AIController is EnemyAIController && Submarine != null && attacker.Submarine == null)
//            {
//                string errorMsg = $"Character {Name} received damage from outside the sub while inside (attacker: {attacker.Name})";
//                GameAnalyticsManager.AddErrorEventOnce("Character.DamageLimb:DamageFromOutside" + Name + attacker.Name,
//                    GameAnalyticsManager.ErrorSeverity.Warning,
//                    errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
//#if DEBUG
//                DebugConsole.ThrowError(errorMsg);
//#endif
//            }

            SetStun(stun);

            if (attacker != null && attacker != this && GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowFriendlyFire)
            {
                if (attacker.TeamID == TeamID) 
                {
                    afflictions = afflictions.Where(a => a.Prefab.IsBuff);
                    if (!afflictions.Any()) { return new AttackResult(); }                   
                }
            }

            Vector2 dir = hitLimb.WorldPosition - worldPosition;
            if (Math.Abs(attackImpulse) > 0.0f)
            {
                Vector2 diff = dir;
                if (diff == Vector2.Zero) { diff = Rand.Vector(1.0f); }
                Vector2 impulse = Vector2.Normalize(diff) * attackImpulse;
                Vector2 hitPos = hitLimb.SimPosition + ConvertUnits.ToSimUnits(diff);
                hitLimb.body.ApplyLinearImpulse(impulse, hitPos, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.5f);
                var mainLimb = hitLimb.character.AnimController.MainLimb;
                if (hitLimb != mainLimb)
                {
                    // Always add force to mainlimb
                    mainLimb.body.ApplyLinearImpulse(impulse, hitPos, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                }
            }
            bool wasDead = IsDead;
            Vector2 simPos = hitLimb.SimPosition + ConvertUnits.ToSimUnits(dir);
            float prevVitality = CharacterHealth.Vitality;
            AttackResult attackResult = hitLimb.AddDamage(simPos, afflictions, playSound, damageMultiplier: damageMultiplier, penetration: penetration, attacker: attacker);
            CharacterHealth.ApplyDamage(hitLimb, attackResult, allowStacking);
            if (shouldImplode)
            {
                // Only used by assistant's True Potential talent. Has to run here in order to properly give kill credit when it activates.
                Implode();
            }

            if (attacker != this)
            {
                OnAttacked?.Invoke(attacker, attackResult);
                OnAttackedProjSpecific(attacker, attackResult, stun);
                if (!wasDead)
                {
                    TryAdjustAttackerSkill(attacker, attackResult);
                }
            }
            if (attackResult.Damage > 0)
            {
                LastDamage = attackResult;
                if (attacker != null && attacker != this && !attacker.Removed)
                {
                    AddAttacker(attacker, attackResult.Damage);
                    if (IsOnPlayerTeam)
                    {
                        CreatureMetrics.AddEncounter(attacker.SpeciesName);
                    }
                    if (attacker.IsOnPlayerTeam)
                    {
                        CreatureMetrics.AddEncounter(SpeciesName);
                    }
                }
                ApplyStatusEffects(ActionType.OnDamaged, 1.0f);
                hitLimb.ApplyStatusEffects(ActionType.OnDamaged, 1.0f);
            }
#if CLIENT
            if (Params.UseBossHealthBar && Controlled != null && Controlled.teamID == attacker?.teamID)
            {
                CharacterHUD.ShowBossHealthBar(this, attackResult.Damage);
            }
#endif
            return attackResult;
        }

        partial void OnAttackedProjSpecific(Character attacker, AttackResult attackResult, float stun);

        public void TryAdjustAttackerSkill(Character attacker, AttackResult attackResult)
        {
            if (attacker == null) { return; }
            if (!attacker.IsOnPlayerTeam) { return; }
            bool isEnemy = AIController is EnemyAIController || TeamID != attacker.TeamID;
            if (!isEnemy) { return; }
            float weaponDamage = 0;
            float medicalDamage = 0;
            foreach (var affliction in attackResult.Afflictions)
            {
                if (affliction.Prefab.IsBuff) { continue; }
                if (Params.IsMachine && !affliction.Prefab.AffectMachines) { continue; }
                if (affliction.Prefab.AfflictionType == AfflictionPrefab.PoisonType || 
                    affliction.Prefab.AfflictionType == AfflictionPrefab.ParalysisType)
                {
                    if (!Params.Health.PoisonImmunity)
                    {
                        float relativeVitality = MaxVitality / 100f;
                        // Undo the applied modifiers to get the base value. Poison damage is multiplied by max vitality when it's applied.
                        float dmg = affliction.Strength;
                        if (relativeVitality > 0)
                        {
                            dmg /= relativeVitality;
                        }
                        if (PoisonVulnerability > 0)
                        {
                            dmg /= PoisonVulnerability;
                        }
                        float strength = MaxVitality;
                        if (Params.AI != null)
                        {
                            strength = Params.AI.CombatStrength;
                        }
                        // Adjust the skill gain by the strength of the target. Combat strength >= 1000 gives 2x bonus, combat strength < 333 less than 1x.
                        float vitalityFactor = MathHelper.Lerp(0.5f, 2f, MathUtils.InverseLerp(0, 1000, strength));
                        dmg *= vitalityFactor;
                        medicalDamage += dmg * affliction.Prefab.MedicalSkillGain;
                    }
                }
                else
                {
                    medicalDamage += affliction.GetVitalityDecrease(null) * affliction.Prefab.MedicalSkillGain;
                }
                weaponDamage += affliction.GetVitalityDecrease(null) * affliction.Prefab.WeaponsSkillGain;
            }
            if (medicalDamage > 0)
            {
                IncreaseSkillLevel("medical".ToIdentifier(), medicalDamage);
            }
            if (weaponDamage > 0)
            {
                IncreaseSkillLevel("weapons".ToIdentifier(), weaponDamage);
            }

            void IncreaseSkillLevel(Identifier skill, float damage)
            {
                float attackerSkillLevel = attacker.GetSkillLevel(skill);
                // The formula is too generous on low skill levels, hence the minimum divider.
                float minSkillDivider = 15f;
                attacker.Info?.IncreaseSkillLevel(skill, damage * SkillSettings.Current.SkillIncreasePerHostileDamage / Math.Max(attackerSkillLevel, minSkillDivider));
            }
        }

        public void TryAdjustHealerSkill(Character healer, float healthChange = 0, Affliction affliction = null)
        {
            if (healer == null) { return; }
            bool isEnemy = AIController is EnemyAIController || TeamID != healer.TeamID;
            if (isEnemy) { return; }
            float medicalGain = healthChange;
            if (affliction?.Prefab is { IsBuff: true } && (!Params.IsMachine || affliction.Prefab.AffectMachines))
            {
                medicalGain += affliction.Strength * affliction.Prefab.MedicalSkillGain;
            }
            if (medicalGain <= 0) { return; }
            Identifier skill = new Identifier("medical");
            float attackerSkillLevel = healer.GetSkillLevel(skill);
            // The formula is too generous on low skill levels, hence the minimum divider.
            float minSkillDivider = 15f;
            healer.Info?.IncreaseSkillLevel(skill, medicalGain * SkillSettings.Current.SkillIncreasePerFriendlyHealed / Math.Max(attackerSkillLevel, minSkillDivider));
        }

        /// <summary>
        /// Is the character knocked down regardless whether the technical state is dead, unconcious, paralyzed, or stunned. 
        /// With stunning, the parameter uses an one second delay before the character is treated as knocked down. The purpose of this is to ignore minor stunning. If you don't want to to ignore any stun, use the Stun property.
        /// </summary>
        public bool IsKnockedDown => IsRagdolled || CharacterHealth.StunTimer > 1.0f || IsIncapacitated;

        public void SetStun(float newStun, bool allowStunDecrease = false, bool isNetworkMessage = false)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && !isNetworkMessage) { return; }
            if (Screen.Selected != GameMain.GameScreen) { return; }
            if (newStun > 0 && Params.Health.StunImmunity)
            {
                if (EmpVulnerability <= 0 || CharacterHealth.GetAfflictionStrength(AfflictionPrefab.EMPType, allowLimbAfflictions: false) <= 0)
                {
                    return;
                }
            }
            if ((newStun <= Stun && !allowStunDecrease) || !MathUtils.IsValid(newStun)) { return; }
            if (Math.Sign(newStun) != Math.Sign(Stun))
            {
                AnimController.ResetPullJoints();
            }
            CharacterHealth.Stun = newStun;
            if (newStun > 0.0f)
            {
                SelectedItem = SelectedSecondaryItem = null;
                if (SelectedCharacter != null) { DeselectCharacter(); }
            }
            HealthUpdateInterval = 0.0f;
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();
        public void ApplyStatusEffects(ActionType actionType, float deltaTime)
        {
            if (actionType == ActionType.OnEating)
            {
                float eatingRegen = Params.Health.HealthRegenerationWhenEating;
                if (eatingRegen > 0)
                {
                    CharacterHealth.ReduceAfflictionOnAllLimbs(AfflictionPrefab.DamageType, eatingRegen * deltaTime);
                }
            }
            if (statusEffects.TryGetValue(actionType, out var statusEffectList))
            {
                foreach (StatusEffect statusEffect in statusEffectList)
                {
                    if (statusEffect.type == ActionType.OnDamaged)
                    {
                        if (!statusEffect.HasRequiredAfflictions(LastDamage)) { continue; }
                        if (statusEffect.OnlyWhenDamagedByPlayer)
                        {
                            if (LastAttacker == null || !LastAttacker.IsPlayer)
                            {
                                continue;
                            }
                        }
                    }
                    if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                        statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                    {
                        targets.Clear();
                        statusEffect.AddNearbyTargets(WorldPosition, targets);
                        statusEffect.Apply(actionType, deltaTime, this, targets);
                    }
                    else if (statusEffect.targetLimbs != null)
                    {
                        foreach (var limbType in statusEffect.targetLimbs)
                        {
                            if (statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                            {
                                // Target all matching limbs
                                foreach (var limb in AnimController.Limbs)
                                {
                                    if (limb.IsSevered) { continue; }
                                    if (limb.type == limbType)
                                    {
                                        statusEffect.sourceBody = limb.body;
                                        statusEffect.Apply(actionType, deltaTime, this, limb);
                                    }
                                }
                            }
                            else if (statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
                            {
                                // Target just the first matching limb
                                Limb limb = AnimController.GetLimb(limbType);
                                if (limb != null)
                                {
                                    statusEffect.sourceBody = limb.body;
                                    statusEffect.Apply(actionType, deltaTime, this, limb);
                                }
                            }
                            else if (statusEffect.HasTargetType(StatusEffect.TargetType.LastLimb))
                            {
                                // Target just the last matching limb
                                Limb limb = AnimController.Limbs.LastOrDefault(l => l.type == limbType && !l.IsSevered && !l.Hidden);
                                if (limb != null)
                                {
                                    statusEffect.sourceBody = limb.body;
                                    statusEffect.Apply(actionType, deltaTime, this, limb);
                                }
                            }
                        }
                    }
                    if (statusEffect.HasTargetType(StatusEffect.TargetType.This) || statusEffect.HasTargetType(StatusEffect.TargetType.Character))
                    {
                        statusEffect.Apply(actionType, deltaTime, this, this);
                    }
                    if (statusEffect.HasTargetType(StatusEffect.TargetType.Hull) && CurrentHull != null)
                    {
                        statusEffect.Apply(actionType, deltaTime, this, CurrentHull);
                    }
                }
                if (actionType != ActionType.OnDamaged && actionType != ActionType.OnSevered)
                {
                    // OnDamaged is called only for the limb that is hit.
                    foreach (Limb limb in AnimController.Limbs)
                    {
                        limb.ApplyStatusEffects(actionType, deltaTime);
                    }
                }
            }
            //OnActive effects are handled by the afflictions themselves
            if (actionType != ActionType.OnActive)
            {
                CharacterHealth.ApplyAfflictionStatusEffects(actionType);
            }
        }

        private void Implode(bool isNetworkMessage = false)
        {
            if (CharacterHealth.Unkillable || GodMode || IsDead) { return; }

            if (!isNetworkMessage)
            {
                if (GameMain.NetworkMember is { IsClient: true }) { return; }
            }

            CharacterHealth.ApplyAffliction(null, new Affliction(AfflictionPrefab.Pressure, AfflictionPrefab.Pressure.MaxStrength));
            if (GameMain.NetworkMember is not { IsClient: true } || isNetworkMessage)
            {
                Kill(CauseOfDeathType.Pressure, null, isNetworkMessage: true);
            }
            if (IsDead)
            {
                BreakJoints();
            }
        }

        public void BreakJoints()
        {
            Vector2 centerOfMass = AnimController.GetCenterOfMass();
            foreach (Limb limb in AnimController.Limbs)
            {
                if (limb.IsSevered) { continue; }
                limb.AddDamage(limb.SimPosition, 500.0f, 0.0f, 0.0f, false);

                Vector2 diff = centerOfMass - limb.SimPosition;

                if (!MathUtils.IsValid(diff))
                {
                    string errorMsg = "Attempted to apply an invalid impulse to a limb in Character.BreakJoints (" + diff + "). Limb position: " + limb.SimPosition + ", center of mass: " + centerOfMass + ".";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Ragdoll.GetCenterOfMass", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    return;
                }

                if (diff == Vector2.Zero) { continue; }
                limb.body.ApplyLinearImpulse(diff * 50.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }

            ImplodeFX();

            foreach (var joint in AnimController.LimbJoints)
            {
                if (joint.LimbA.type == LimbType.Head || joint.LimbB.type == LimbType.Head) { continue; }
                if (joint.revoluteJoint != null)
                {
                    joint.revoluteJoint.LimitEnabled = false;
                }
            }
        }

        partial void ImplodeFX();

        public void Kill(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction, bool isNetworkMessage = false, bool log = true)
        {
            if (IsDead || CharacterHealth.Unkillable || GodMode) { return; }

            HealthUpdateInterval = 0.0f;

            //clients aren't allowed to kill characters unless they receive a network message
            if (!isNetworkMessage && GameMain.NetworkMember is { IsClient: true })
            {
                return;
            }

#if SERVER
            if (GameMain.NetworkMember is { IsServer: true })
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new CharacterStatusEventData(forceAfflictionData: true));
            }
#endif

            isDead = true;

            ApplyStatusEffects(ActionType.OnDeath, 1.0f);

            AnimController.Frozen = false;

            CauseOfDeath = new CauseOfDeath(
                causeOfDeath, causeOfDeathAffliction?.Prefab,
                causeOfDeathAffliction?.Source, LastDamageSource);

            if (GameAnalyticsManager.SendUserStatistics)
            {
                string causeOfDeathStr = causeOfDeathAffliction == null ?
                    causeOfDeath.ToString() : causeOfDeathAffliction.Prefab.Identifier.Value.Replace(" ", "");

                string characterType = GetCharacterType(this);
                GameAnalyticsManager.AddDesignEvent("Kill:" + characterType + ":" + causeOfDeathStr);
                if (CauseOfDeath.Killer != null)
                {
                    GameAnalyticsManager.AddDesignEvent("Kill:" + characterType + ":Killer:" + GetCharacterType(CauseOfDeath.Killer));
                }
                if (CauseOfDeath.DamageSource != null)
                {
                    string damageSourceStr = CauseOfDeath.DamageSource.ToString();
                    if (CauseOfDeath.DamageSource is Item damageSourceItem) { damageSourceStr = damageSourceItem.ToString(); }
                    GameAnalyticsManager.AddDesignEvent("Kill:" + characterType + ":DamageSource:" + damageSourceStr);
                }

                static string GetCharacterType(Character character)
                {
                    if (character.IsPlayer)
                        return "Player";
                    else if (character.AIController is EnemyAIController)
                        return "Enemy" + character.SpeciesName;
                    else if (character.AIController is HumanAIController && character.TeamID == CharacterTeamType.Team2)
                        return "EnemyHuman";
                    else if (character.Info != null && character.TeamID == CharacterTeamType.Team1)
                        return "AICrew";
                    else if (character.Info != null && character.TeamID == CharacterTeamType.FriendlyNPC)
                        return "FriendlyNPC";
                    return "Unknown";
                }
            }

            OnDeath?.Invoke(this, CauseOfDeath);

            if (CauseOfDeath.Type != CauseOfDeathType.Disconnected)
            {
                var abilityCharacterKiller = new AbilityCharacterKiller(CauseOfDeath.Killer);
                CheckTalents(AbilityEffectType.OnDieToCharacter, abilityCharacterKiller);
                CauseOfDeath.Killer?.RecordKill(this);
            }

            if (GameMain.GameSession != null && Screen.Selected == GameMain.GameScreen)
            {
                SteamAchievementManager.OnCharacterKilled(this, CauseOfDeath);
            }

            KillProjSpecific(causeOfDeath, causeOfDeathAffliction, log);

            if (info != null)
            {
                info.CauseOfDeath = CauseOfDeath;
                info.MissionsCompletedSinceDeath = 0;
            }
            AnimController.movement = Vector2.Zero;
            AnimController.TargetMovement = Vector2.Zero;

            if (!LockHands)
            {
                foreach (Item heldItem in HeldItems.ToList())
                {
                    //if the item is both wearable and holdable, and currently worn, don't drop the item
                    var wearable = heldItem.GetComponent<Wearable>();
                    if (wearable is { IsActive: true }) { continue; }
                    heldItem.Drop(this);
                }
            }

            SelectedItem = SelectedSecondaryItem = null;
            SelectedCharacter = null;
            
            AnimController.ResetPullJoints();

            foreach (var joint in AnimController.LimbJoints)
            {
                if (joint.revoluteJoint != null)
                {
                    joint.revoluteJoint.MotorEnabled = false;
                }
            }

            GameMain.GameSession?.KillCharacter(this);
        }
        partial void KillProjSpecific(CauseOfDeathType causeOfDeath, Affliction causeOfDeathAffliction, bool log);

        public void Revive(bool removeAllAfflictions = true)
        {
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to revive an already removed character\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            aiTarget?.Remove();

            aiTarget = new AITarget(this);
            if (removeAllAfflictions)
            {
                CharacterHealth.RemoveAllAfflictions();
            }
            else
            {
                CharacterHealth.RemoveNegativeAfflictions();
            }
            SetAllDamage(0.0f, 0.0f, 0.0f);
            Oxygen = 100.0f;
            Bloodloss = 0.0f;
            SetStun(0.0f, true);
            isDead = false;

            foreach (LimbJoint joint in AnimController.LimbJoints)
            {
                var revoluteJoint = joint.revoluteJoint;
                if (revoluteJoint != null)
                {
                    revoluteJoint.MotorEnabled = true;
                }
                joint.Enabled = true;
                joint.IsSevered = false;
            }

            foreach (Limb limb in AnimController.Limbs)
            {
#if CLIENT
                if (limb.LightSource != null)
                {
                    limb.LightSource.Color = limb.InitialLightSourceColor;
                }
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
                DebugConsole.ThrowError("Attempting to remove an already removed character\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            DebugConsole.Log("Removing character " + Name + " (ID: " + ID + ")");

#if CLIENT
            //ensure we apply any pending inventory updates to drop any items that need to be dropped when the character despawns
            if (GameMain.Client?.ClientPeer is { IsActive: true })
            {
                Inventory?.ApplyReceivedState();
            }
#endif

            base.Remove();

            foreach (Item heldItem in HeldItems.ToList())
            {
                heldItem.Drop(this);
            }

            info?.Remove();

#if CLIENT
            GameMain.GameSession?.CrewManager?.KillCharacter(this, resetCrewListIndex: false);

            if (Controlled == this) { Controlled = null; }
#endif

            CharacterList.Remove(this);

            foreach (var attachedProjectile in AttachedProjectiles.ToList())
            {
                attachedProjectile.Unstick();
            }
            Latchers.ForEachMod(l => l?.DeattachFromBody(reset: true));
            Latchers.Clear();

            if (Inventory != null)
            {
                foreach (Item item in Inventory.AllItems)
                {
                    Spawner?.AddItemToRemoveQueue(item);
                }
            }

            itemSelectedDurations.Clear();

            DisposeProjSpecific();

            aiTarget?.Remove();
            AnimController?.Remove();
            CharacterHealth?.Remove();

            foreach (Character c in CharacterList)
            {
                if (c.FocusedCharacter == this) { c.FocusedCharacter = null; }
                if (c.SelectedCharacter == this) { c.SelectedCharacter = null; }
            }
        }
        partial void DisposeProjSpecific();

        public void TeleportTo(Vector2 worldPos)
        {
            CurrentHull = null;
            AnimController.CurrentHull = null;
            Submarine = null;
            AnimController.SetPosition(ConvertUnits.ToSimUnits(worldPos), lerp: false);
            AnimController.FindHull(worldPos, setSubmarine: true);
            CurrentHull = AnimController.CurrentHull;
            if (AIController is HumanAIController humanAI)
            {
                humanAI.PathSteering?.ResetPath();
            }
        }

        public static void SaveInventory(Inventory inventory, XElement parentElement)
        {
            if (inventory == null || parentElement == null) { return; }
            var items = inventory.AllItems.Distinct();
            foreach (Item item in items)
            {
                item.Submarine = inventory.Owner.Submarine;
                var itemElement = item.Save(parentElement);

                List<int> slotIndices = inventory.FindIndices(item);
                itemElement.Add(new XAttribute("i", string.Join(",", slotIndices)));

                foreach (ItemContainer container in item.GetComponents<ItemContainer>())
                {
                    XElement childInvElement = new XElement("inventory");
                    itemElement.Add(childInvElement);
                    SaveInventory(container.Inventory, childInvElement);
                }
            }
        }

        /// <summary>
        /// Calls <see cref="SaveInventory(Barotrauma.Inventory, XElement)"/> using 'Inventory' and 'Info.InventoryData'
        /// </summary>
        public void SaveInventory()
        {
            SaveInventory(Inventory, Info?.InventoryData);
        }

        public void SpawnInventoryItems(Inventory inventory, ContentXElement itemData)
        {
            SpawnInventoryItemsRecursive(inventory, itemData, new List<Item>());
        }
        
        private void SpawnInventoryItemsRecursive(Inventory inventory, ContentXElement element, List<Item> extraDuffelBags)
        {
            foreach (var itemElement in element.Elements())
            {
                var newItem = Item.Load(itemElement, inventory.Owner.Submarine, createNetworkEvent: true, idRemap: IdRemap.DiscardId);
                if (newItem == null) { continue; }

                if (!MathUtils.NearlyEqual(newItem.Condition, newItem.MaxCondition) &&
                    GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    newItem.CreateStatusEvent(loadingRound: true);
                }
#if SERVER
                newItem.GetComponent<Terminal>()?.SyncHistory();
                if (newItem.GetComponent<WifiComponent>() is WifiComponent wifiComponent) { newItem.CreateServerEvent(wifiComponent); }
                if (newItem.GetComponent<GeneticMaterial>() is GeneticMaterial geneticMaterial) { newItem.CreateServerEvent(geneticMaterial); }
#endif
                int[] slotIndices = itemElement.GetAttributeIntArray("i", new int[] { 0 });
                if (!slotIndices.Any())
                {
                    DebugConsole.ThrowError("Invalid inventory data in character \"" + Name + "\" - no slot indices found");
                    continue;
                }

                //make sure there's no other item in the slot
                //this should not happen normally, but can occur if the character is accidentally given new job items while also loading previous items in the campaign
                for (int i = 0; i < inventory.Capacity; i++)
                {
                    if (slotIndices.Contains(i))
                    {
                        var existingItem = inventory.GetItemAt(i);
                        if (existingItem != null && existingItem != newItem && (((MapEntity)existingItem).Prefab != ((MapEntity)newItem).Prefab || existingItem.Prefab.MaxStackSize == 1))
                        {
                            DebugConsole.ThrowError($"Error while loading character inventory data. The slot {i} was already occupied by the item \"{existingItem.Name} ({existingItem.ID})\" when loading the item \"{newItem.Name} ({newItem.ID})\"");
                            existingItem.Drop(null, createNetworkEvent: false);
                        }
                    }
                }

                bool canBePutInOriginalInventory = true;
                if (slotIndices[0] >= inventory.Capacity)
                {
                    canBePutInOriginalInventory = false;
                    //legacy support: before item stacking was implemented, revolver for example had a separate slot for each bullet
                    //now there's just one, try to put the extra items where they fit (= stack them)
                    for (int i = 0; i < inventory.Capacity; i++)
                    {
                        if (inventory.CanBePutInSlot(newItem, i))
                        {
                            slotIndices[0] = i;
                            canBePutInOriginalInventory = true;
                            break;
                        }
                    }
                }
                else
                {
                    canBePutInOriginalInventory = inventory.CanBePutInSlot(newItem, slotIndices[0], ignoreCondition: true);
                }

                if (canBePutInOriginalInventory)
                {
                    inventory.TryPutItem(newItem, slotIndices[0], false, false, null);
                    newItem.ParentInventory = inventory;

                    //force the item to the correct slots
                    //  e.g. putting the item in a hand slot will also put it in the first available Any-slot, 
                    //  which may not be where it actually was
                    for (int i = 0; i < inventory.Capacity; i++)
                    {
                        if (slotIndices.Contains(i))
                        {
                            if (!inventory.GetItemsAt(i).Contains(newItem)) { inventory.ForceToSlot(newItem, i); }
                        }
                        else if (inventory.FindIndices(newItem).Contains(i))
                        {
                            inventory.ForceRemoveFromSlot(newItem, i);
                        }
                    }
                }
                else
                {
                    // In case the inventory capacity is smaller than it was when saving:
                    // 1) Spawn a new duffel bag if none yet spawned or if the existing ones aren't enough
                    if (extraDuffelBags.None(i => i.OwnInventory.CanBePut(newItem)) && ItemPrefab.FindByIdentifier("duffelbag".ToIdentifier()) is ItemPrefab duffelBagPrefab)
                    {
                        var hull = Hull.FindHull(WorldPosition, guess: CurrentHull);
                        var mainSub = Submarine.MainSubs.FirstOrDefault(s => s.TeamID == TeamID);
                        if ((hull == null || hull.Submarine != mainSub) && mainSub != null)
                        {
                            var wp = WayPoint.GetRandom(spawnType: SpawnType.Cargo, sub: mainSub) ?? WayPoint.GetRandom(sub: mainSub);
                            if (wp != null)
                            {
                                hull = Hull.FindHull(wp.WorldPosition);
                            }
                        }
                        var newDuffelBag = new Item(duffelBagPrefab,
                            hull != null ? CargoManager.GetCargoPos(hull, duffelBagPrefab) : Position,
                            hull?.Submarine ?? Submarine);
                        extraDuffelBags.Add(newDuffelBag);
#if SERVER
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(newDuffelBag));
#endif
                    }

                    // 2) Find a slot for the new item
                    for (int i = 0; i < extraDuffelBags.Count; i++)
                    {
                        var duffelBag = extraDuffelBags[i];
                        for (int j = 0; j < duffelBag.OwnInventory.Capacity; j++)
                        {
                            if (duffelBag.OwnInventory.TryPutItem(newItem, j, false, false, null))
                            {
                                newItem.ParentInventory = duffelBag.OwnInventory;
                                break;
                            }
                        }
                    }
                }

                int itemContainerIndex = 0;
                var itemContainers = newItem.GetComponents<ItemContainer>().ToList();
                foreach (var childInvElement in itemElement.Elements())
                {
                    if (itemContainerIndex >= itemContainers.Count) break;
                    if (!childInvElement.Name.ToString().Equals("inventory", StringComparison.OrdinalIgnoreCase)) { continue; }
                    SpawnInventoryItemsRecursive(itemContainers[itemContainerIndex].Inventory, childInvElement, extraDuffelBags);
                    itemContainerIndex++;
                }
            }
        }

        private readonly HashSet<AttackContext> currentContexts = new HashSet<AttackContext>();

        public IEnumerable<AttackContext> GetAttackContexts()
        {
            currentContexts.Clear();
            if (AnimController.InWater)
            {
                currentContexts.Add(AttackContext.Water);
            }
            else
            {
                currentContexts.Add(AttackContext.Ground);
            }
            if (CurrentHull == null)
            {
                currentContexts.Add(AttackContext.Outside);
            }
            else
            {
                currentContexts.Add(AttackContext.Inside);
            }
            return currentContexts;
        }

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
                visibleHulls.AddRange(CurrentHull.GetLinkedEntities(tempList, filter: h =>
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

        public Vector2 GetRelativeSimPosition(ISpatialEntity target, Vector2? worldPos = null) => GetRelativeSimPosition(this, target, worldPos);

        public static Vector2 GetRelativeSimPosition(ISpatialEntity from, ISpatialEntity to, Vector2? worldPos = null)
        {
            Vector2 targetPos = to.SimPosition;
            if (worldPos.HasValue)
            {
                Vector2 wp = worldPos.Value;
                if (to.Submarine != null)
                {
                    wp -= to.Submarine.Position;
                }
                targetPos = ConvertUnits.ToSimUnits(wp);
            }
            if (from.Submarine == null && to.Submarine != null)
            {
                // outside and targeting inside
                targetPos += to.Submarine.SimPosition;
            }
            else if (from.Submarine != null && to.Submarine == null)
            {
                // inside and targeting outside
                targetPos -= from.Submarine.SimPosition;
            }
            else if (from.Submarine != to.Submarine)
            {
                if (from.Submarine != null && to.Submarine != null)
                {
                    // both inside, but in different subs
                    Vector2 diff = from.Submarine.SimPosition - to.Submarine.SimPosition;
                    targetPos -= diff;
                }
            }
            return targetPos;
        }

        public bool IsCaptain => HasJob("captain");
        public bool IsEngineer => HasJob("engineer");
        public bool IsMechanic => HasJob("mechanic");
        public bool IsMedic => HasJob("medicaldoctor");
        public bool IsSecurity => HasJob("securityofficer") || HasJob("vipsecurityofficer");
        public bool IsAssistant => HasJob("assistant");
        public bool IsWatchman => HasJob("watchman");
        public bool IsVip => HasJob("prisoner");
        public bool IsPrisoner => HasJob("prisoner");
        public Color? UniqueNameColor { get; set; } = null;

        public bool HasJob(string identifier) => Info?.Job?.Prefab.Identifier == identifier;

        public bool HasJob(Identifier identifier) => Info?.Job?.Prefab.Identifier == identifier;

        /// <summary>
        /// Is the character currently protected from the pressure by immunity/ability or a status effect (e.g. from a diving suit).
        /// </summary>
        public bool IsProtectedFromPressure => IsImmuneToPressure || PressureProtection >= (Level.Loaded?.GetRealWorldDepth(WorldPosition.Y) ?? 1.0f);

        public bool IsImmuneToPressure => !NeedsAir || HasAbilityFlag(AbilityFlags.ImmuneToPressure);

        #region Talents
        private readonly List<CharacterTalent> characterTalents = new List<CharacterTalent>();

        public IReadOnlyCollection<CharacterTalent> CharacterTalents => characterTalents;

        public void LoadTalents()
        {
            List<Identifier> toBeRemoved = null;
            foreach (Identifier talent in info.UnlockedTalents)
            {
                if (!GiveTalent(talent, addingFirstTime: false))
                {
                    DebugConsole.AddWarning(Name + " had talent that did not exist! Removing talent from CharacterInfo.");
                    toBeRemoved ??= new List<Identifier>();
                    toBeRemoved.Add(talent);
                }
            }

            if (toBeRemoved != null)
            {
                foreach (Identifier removeTalent in toBeRemoved)
                {
                    Info.UnlockedTalents.Remove(removeTalent);
                }
            }
        }

        public bool GiveTalent(Identifier talentIdentifier, bool addingFirstTime = true)
        {
            TalentPrefab talentPrefab = TalentPrefab.TalentPrefabs.Find(c => c.Identifier == talentIdentifier);
            if (talentPrefab == null)
            {
                DebugConsole.AddWarning($"Tried to add talent by identifier {talentIdentifier} to character {Name}, but no such talent exists.");
                return false;
            }
            return GiveTalent(talentPrefab, addingFirstTime);
        }

        public bool GiveTalent(UInt32 talentIdentifier, bool addingFirstTime = true)
        {
            TalentPrefab talentPrefab = TalentPrefab.TalentPrefabs.Find(c => c.UintIdentifier == talentIdentifier);
            if (talentPrefab == null)
            {
                DebugConsole.AddWarning($"Tried to add talent by identifier {talentIdentifier} to character {Name}, but no such talent exists.");
                return false;
            }
            return GiveTalent(talentPrefab, addingFirstTime);
        }

        public bool GiveTalent(TalentPrefab talentPrefab, bool addingFirstTime = true)
        {
            if (info == null) { return false; }
            info.UnlockedTalents.Add(talentPrefab.Identifier);
            if (characterTalents.Any(t => t.Prefab == talentPrefab)) { return false; }
#if SERVER
            GameMain.NetworkMember.CreateEntityEvent(this, new UpdateTalentsEventData());
#endif
            CharacterTalent characterTalent = new CharacterTalent(talentPrefab, this);
            characterTalents.Add(characterTalent);
            characterTalent.ActivateTalent(addingFirstTime);
            characterTalent.AddedThisRound = addingFirstTime;

            if (addingFirstTime)
            {
                OnTalentGiven(talentPrefab);
                GameAnalyticsManager.AddDesignEvent("TalentUnlocked:" + (info.Job?.Prefab.Identifier ?? "None".ToIdentifier()) + ":" + talentPrefab.Identifier,
                    GameMain.GameSession?.Campaign?.TotalPlayTime ?? 0.0);
            }
            return true;
        }

        public bool HasTalent(Identifier identifier)
        {
            return info.UnlockedTalents.Contains(identifier);
        }

        public bool HasUnlockedAllTalents()
        {
            if (TalentTree.JobTalentTrees.TryGet(Info.Job.Prefab.Identifier, out TalentTree talentTree))
            {
                foreach (TalentSubTree talentSubTree in talentTree.TalentSubTrees)
                {
                    foreach (TalentOption talentOption in talentSubTree.TalentOptionStages)
                    {
                        if (!talentOption.HasMaxTalents(info.UnlockedTalents))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public bool HasTalents()
        {
            return characterTalents.Any();
        }

        public void CheckTalents(AbilityEffectType abilityEffectType, AbilityObject abilityObject)
        {
            foreach (CharacterTalent characterTalent in CharacterTalents)
            {
                characterTalent.CheckTalent(abilityEffectType, abilityObject);
            }
        }

        public void CheckTalents(AbilityEffectType abilityEffectType)
        {
            foreach (var characterTalent in characterTalents)
            {
                characterTalent.CheckTalent(abilityEffectType, null);
            }
        }

        partial void OnTalentGiven(TalentPrefab talentPrefab);

        #endregion

        private readonly HashSet<Hull> sameRoomHulls = new();

        /// <summary>
        /// Check if the character is in the same room
        /// Room and hull differ in that a room can consist of multiple linked hulls
        /// </summary>
        public bool IsInSameRoomAs(Character character)
        {
            if (character == this) { return true; }

            if (character.CurrentHull is null || CurrentHull is null)
            {
                // Outside doesn't count as a room
                return false;
            }

            if (character.Submarine != Submarine) { return false; }
            if (character.CurrentHull == CurrentHull) { return true; }

            sameRoomHulls.Clear();
            CurrentHull.GetLinkedEntities(sameRoomHulls);
            sameRoomHulls.Add(CurrentHull);

            return sameRoomHulls.Contains(character.CurrentHull);
        }

        public static IEnumerable<Character> GetFriendlyCrew(Character character)
        {
            if (character is null)
            {
                return Enumerable.Empty<Character>();
            }
            return CharacterList.Where(c => HumanAIController.IsFriendly(character, c, onlySameTeam: true) && !c.IsDead);
        }

        public bool HasRecipeForItem(Identifier recipeIdentifier)
        {
            return characterTalents.Any(t => t.UnlockedRecipes.Contains(recipeIdentifier));
        }

        public bool HasStoreAccessForItem(ItemPrefab prefab)
        {
            foreach (CharacterTalent talent in characterTalents)
            {
                foreach (Identifier unlockedItem in talent.UnlockedStoreItems)
                {
                    if (prefab.Tags.Contains(unlockedItem)) { return true; }
                }
            }

            return false;
        }

        /// <summary>
        /// Shows visual notification of money gained by the specific player. Useful for mid-mission monetary gains.
        /// </summary>
        public void GiveMoney(int amount)
        {
            if (!(GameMain.GameSession?.Campaign is { } campaign)) { return; }
            if (amount <= 0) { return; }

            Wallet wallet;
#if SERVER
            if (!(campaign is MultiPlayerCampaign mpCampaign)) { throw new InvalidOperationException("Campaign on a server is not a multiplayer campaign"); }
            Client targetClient = null;

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character == this)
                {
                    targetClient = client;
                    break;
                }
            }

            wallet = targetClient is null ? mpCampaign.Bank : mpCampaign.GetWallet(targetClient);
#else
            wallet = campaign.Wallet;
#endif

            int prevAmount = wallet.Balance;
            wallet.Give(amount);
            OnMoneyChanged(prevAmount, wallet.Balance);
        }

#if CLIENT
        public void SetMoney(int amount)
        {
            if (!(GameMain.GameSession?.Campaign is { } campaign)) { return; }
            if (amount == campaign.Wallet.Balance) { return; }

            int prevAmount = campaign.Wallet.Balance;
            campaign.Wallet.Balance = amount;
            OnMoneyChanged(prevAmount, campaign.Wallet.Balance);
        }
#endif

        partial void OnMoneyChanged(int prevAmount, int newAmount);

        /// <summary>
        /// This dictionary is used for stats that are required very frequently. Not very performant, but easier to develop with for now.
        /// If necessary, the approach of using a dictionary could be replaced by an encapsulated class that contains the stats as attributes.
        /// </summary>
        private readonly Dictionary<StatTypes, float> statValues = new Dictionary<StatTypes, float>();

        /// <summary>
        /// A dictionary with temporary values, updated when the character equips/unequips wearables. Used to reduce unnecessary inventory checking.
        /// </summary>
        private readonly Dictionary<StatTypes, float> wearableStatValues = new Dictionary<StatTypes, float>();

        public float GetStatValue(StatTypes statType, bool includeSaved = true)
        {
            if (!IsHuman) { return 0f; }

            float statValue = 0f;
            if (statValues.TryGetValue(statType, out float value))
            {
                statValue += value;
            }
            if (CharacterHealth != null)
            {
                statValue += CharacterHealth.GetStatValue(statType);
            }
            if (Info != null && includeSaved)
            {
                // could be optimized by instead updating the Character.cs statvalues dictionary whenever the CharacterInfo.cs values change
                statValue += Info.GetSavedStatValue(statType);
            }
            if (wearableStatValues.TryGetValue(statType, out float wearableValue))
            {
                statValue += wearableValue;
            }

            return statValue;
        }

        public void OnWearablesChanged()
        {
            wearableStatValues.Clear();
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                if (Inventory.SlotTypes[i] != InvSlotType.Any && Inventory.SlotTypes[i] != InvSlotType.LeftHand && Inventory.SlotTypes[i] != InvSlotType.RightHand
                    && Inventory.GetItemAt(i)?.GetComponent<Wearable>() is Wearable wearable)
                {
                    foreach (var statValuePair in wearable.WearableStatValues)
                    {
                        if (wearableStatValues.ContainsKey(statValuePair.Key))
                        {
                            wearableStatValues[statValuePair.Key] += statValuePair.Value;
                        }
                        else
                        {
                            wearableStatValues.Add(statValuePair.Key, statValuePair.Value);
                        }
                    }
                }
            }
        }

        public void ChangeStat(StatTypes statType, float value)
        {
            if (statValues.ContainsKey(statType))
            {
                statValues[statType] += value;
            }
            else
            {
                statValues.Add(statType, value);
            }
        }
        
        private static StatTypes GetSkillStatType(Identifier skillIdentifier)
        {
            // Using this method to translate between skill identifiers and stat types. Feel free to replace it if there's a better way
            switch (skillIdentifier.Value.ToLowerInvariant())
            {
                case "electrical":
                    return StatTypes.ElectricalSkillBonus;
                case "helm":
                    return StatTypes.HelmSkillBonus;
                case "mechanical":
                    return StatTypes.MechanicalSkillBonus;
                case "medical":
                    return StatTypes.MedicalSkillBonus;
                case "weapons":
                    return StatTypes.WeaponsSkillBonus;
                default:
                    return StatTypes.None;
            }
        }

        private AbilityFlags abilityFlags;

        public void AddAbilityFlag(AbilityFlags abilityFlag)
        {
            abilityFlags |= abilityFlag;
        }

        public void RemoveAbilityFlag(AbilityFlags abilityFlag)
        {
            abilityFlags &= ~abilityFlag;
        }

        public bool HasAbilityFlag(AbilityFlags abilityFlag)
        {
            return abilityFlags.HasFlag(abilityFlag) || CharacterHealth.HasFlag(abilityFlag);
        }

        private readonly Dictionary<TalentResistanceIdentifier, float> abilityResistances = new();

        public float GetAbilityResistance(AfflictionPrefab affliction)
        {
            float resistance = 0f;
            bool hadResistance = false;

            foreach (var (key, value) in abilityResistances)
            {
                if (key.ResistanceIdentifier == affliction.AfflictionType ||
                    key.ResistanceIdentifier == affliction.Identifier)
                {
                    resistance += value;
                    hadResistance = true;
                }
            }

            return hadResistance ? resistance : 1f;
        }

        public void ChangeAbilityResistance(TalentResistanceIdentifier identifier, float value)
        {
            if (!MathUtils.IsValid(value))
            {
#if DEBUG
                DebugConsole.ThrowError($"Attempted to set ability resistance to an invalid value ({value})\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                return;
            }

            if (abilityResistances.ContainsKey(identifier))
            {
                abilityResistances[identifier] *= value;
            }
            else
            {
                abilityResistances.Add(identifier, value);
            }
        }

        public void RemoveAbilityResistance(TalentResistanceIdentifier identifier) => abilityResistances.Remove(identifier);

        public bool IsFriendly(Character other) => IsFriendly(this, other);

        public static bool IsFriendly(Character me, Character other) => IsOnFriendlyTeam(me, other) && IsSameSpeciesOrGroup(me, other);

        public static bool IsOnFriendlyTeam(CharacterTeamType myTeam, CharacterTeamType otherTeam)
        {
            if (myTeam == otherTeam) { return true; }
            return myTeam switch
            {
                // NPCs are friendly to the same team and the friendly NPCs
                CharacterTeamType.None or CharacterTeamType.Team1 or CharacterTeamType.Team2 => otherTeam == CharacterTeamType.FriendlyNPC,
                // Friendly NPCs are friendly to both player teams
                CharacterTeamType.FriendlyNPC => otherTeam == CharacterTeamType.Team1 || otherTeam == CharacterTeamType.Team2,
                _ => true
            };
        }

        public static bool IsOnFriendlyTeam(Character me, Character other) => IsOnFriendlyTeam(me.TeamID, other.TeamID);
        public bool IsOnFriendlyTeam(Character other) => IsOnFriendlyTeam(TeamID, other.TeamID);
        public bool IsOnFriendlyTeam(CharacterTeamType otherTeam) => IsOnFriendlyTeam(TeamID, otherTeam);

        public bool IsSameSpeciesOrGroup(Character other) => IsSameSpeciesOrGroup(this, other);

        public static bool IsSameSpeciesOrGroup(Character me, Character other) => other.SpeciesName == me.SpeciesName || CharacterParams.CompareGroup(me.Group, other.Group);

        public void StopClimbing()
        {
            AnimController.StopClimbing();
            ReleaseSecondaryItem();
        }
    }

    class ActiveTeamChange
    {
        public CharacterTeamType DesiredTeamId { get; }
        public enum TeamChangePriorities
        {
            Base, // given to characters when generated or when their base team is set
            Willful, // cognitive, willful team changes, such as prisoners escaping 
            Absolute // possession, insanity, the like
        }
        public TeamChangePriorities TeamChangePriority { get; }
        public bool AggressiveBehavior { get; }

        public ActiveTeamChange(CharacterTeamType desiredTeamId, TeamChangePriorities teamChangePriority, bool aggressiveBehavior = false)
        {
            DesiredTeamId = desiredTeamId;
            TeamChangePriority = teamChangePriority;
            AggressiveBehavior = aggressiveBehavior;
        }
    }

    internal sealed class AbilityCharacterLoot : AbilityObject, IAbilityCharacter
    {
        public Character Character { get; set; }

        public AbilityCharacterLoot(Character character)
        {
            Character = character;
        }
    }

    class AbilityCharacterKill : AbilityObject, IAbilityCharacter
    {
        public AbilityCharacterKill(Character character, Character killer)
        {
            Character = character;
            Killer = killer;
        }
        public Character Character { get; set; }
        public Character Killer { get; set; }
    }

    class AbilityAttackData : AbilityObject, IAbilityCharacter
    {
        public float DamageMultiplier { get; set; } = 1f;
        public float AddedPenetration { get; set; } = 0f;
        public List<Affliction> Afflictions { get; set; }
        public bool ShouldImplode { get; set; } = false;
        public Attack SourceAttack { get; }
        public Character Character { get; set; }
        public Character Attacker { get; set; }

        public AbilityAttackData(Attack sourceAttack, Character target, Character attacker)
        {
            SourceAttack = sourceAttack;
            Character = target;
            if (attacker != null)
            {
                Attacker = attacker;
                attacker.CheckTalents(AbilityEffectType.OnAttack, this);
                target.CheckTalents(AbilityEffectType.OnAttacked, this);
                DamageMultiplier *= 1 + attacker.GetStatValue(StatTypes.AttackMultiplier);
                if (attacker.TeamID == target.TeamID)
                {
                    DamageMultiplier *= 1 + attacker.GetStatValue(StatTypes.TeamAttackMultiplier);
                }
            }
        }
    }

    class AbilityAttackResult : AbilityObject, IAbilityAttackResult
    {
        public AttackResult AttackResult { get; set; }

        public AbilityAttackResult(AttackResult attackResult)
        {
            AttackResult = attackResult;
        }
    }

    class AbilityCharacterKiller : AbilityObject, IAbilityCharacter
    {
        public AbilityCharacterKiller(Character character)
        {
            Character = character;
        }
        public Character Character { get; set; }
    }

    class AbilityOrderedCharacter : AbilityObject, IAbilityCharacter
    {
        public AbilityOrderedCharacter(Character character)
        {
            Character = character;
        }
        public Character Character { get; set; }
    }

}
