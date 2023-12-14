using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;
using System.Collections.Immutable;

namespace Barotrauma
{
    abstract partial class AIObjective
    {
        public virtual float Devotion => AIObjectiveManager.baseDevotion;

        public abstract Identifier Identifier { get; set; }
        public virtual string DebugTag => Identifier.Value;
        public virtual bool ForceRun => false;
        public virtual bool IgnoreUnsafeHulls => false;
        public virtual bool AbandonWhenCannotCompleteSubjectives => true;
        public virtual bool AllowSubObjectiveSorting => false;
        public virtual bool PrioritizeIfSubObjectivesActive => false;

        /// <summary>
        /// Can there be multiple objective instaces of the same type?
        /// </summary>
        public virtual bool AllowMultipleInstances => false;

        /// <summary>
        /// Run the main objective with all subobjectives concurrently?
        /// If false, the main objective will continue only when all the subobjectives have been removed (done).
        /// </summary>
        public virtual bool ConcurrentObjectives => false;

        public virtual bool KeepDivingGearOn => false;
        public virtual bool KeepDivingGearOnAlsoWhenInactive => false;

        /// <summary>
        /// There's a separate property for diving suit and mask: KeepDivingGearOn.
        /// </summary>
        public virtual bool AllowAutomaticItemUnequipping => false;
        public virtual bool AllowOutsideSubmarine => false;
        public virtual bool AllowInFriendlySubs => false;
        public virtual bool AllowInAnySub => false;
        public virtual bool AllowWhileHandcuffed => true;

        protected readonly List<AIObjective> subObjectives = new List<AIObjective>();
        private float _cumulatedDevotion;
        protected float CumulatedDevotion
        {
            get { return _cumulatedDevotion; }
            set { _cumulatedDevotion = MathHelper.Clamp(value, 0, MaxDevotion); }
        }

        protected virtual float MaxDevotion => 10;

        /// <summary>
        /// Final priority value after all calculations.
        /// </summary>
        public float Priority { get; set; }
        public float BasePriority { get; set; }
        public float PriorityModifier { get; private set; } = 1;

        private float resetPriorityTimer;
        private readonly float resetPriorityTime = 1;
        private bool _forceHighestPriority;
        // For forcing the highest priority temporarily. Will reset automatically after one second, unless kept alive by something.
        public bool ForceHighestPriority
        {
            get { return _forceHighestPriority; }
            set
            {
                if (_forceHighestPriority == value) { return; }
                _forceHighestPriority = value;
                if (_forceHighestPriority)
                {
                    resetPriorityTimer = resetPriorityTime;
                }
            }
        }

        // For temporarily forcing walking. Will reset after each priority calculation, so it will need to be kept alive by something.
        // The intention of this boolean to allow walking even when the priority is higher than AIObjectiveManager.RunPriority.
        public bool ForceWalk { get; set; }

        public bool IgnoreAtOutpost { get; set; }

        public readonly Character character;
        public readonly AIObjectiveManager objectiveManager;
        public readonly Identifier Option;

        private bool _abandon;
        public bool Abandon
        {
            get { return _abandon; }
            set
            {
                _abandon = value;
                if (_abandon)
                {
                    OnAbandon();
                }
            }
        }

        public virtual bool CanBeCompleted => !Abandon;

        /// <summary>
        /// When true, the objective is never completed, unless CanBeCompleted returns false.
        /// </summary>
        public virtual bool IsLoop { get; set; }
        public IEnumerable<AIObjective> SubObjectives => subObjectives;
        public AIObjective CurrentSubObjective => subObjectives.FirstOrDefault();

        private readonly List<AIObjective> all = new List<AIObjective>();
        public IEnumerable<AIObjective> GetSubObjectivesRecursive(bool includingSelf = false)
        {
            all.Clear();
            if (includingSelf)
            {
                all.Add(this);
            }
            foreach (var subObjective in subObjectives)
            {
                all.AddRange(subObjective.GetSubObjectivesRecursive(true));
            }
            return all;
        }

#pragma warning disable CS0649
        /// <summary>
        /// Aborts the objective when this condition is true.
        /// </summary>
        public Func<AIObjective, bool> AbortCondition;
#pragma warning restore CS0649

        /// <summary>
        /// A single shot event. Automatically cleared after launching. Use OnCompleted method for implementing (internal) persistent behavior.
        /// </summary>
        public event Action Completed;
        /// <summary>
        /// A single shot event. Automatically cleared after launching. Use OnAbandoned method for implementing (internal) persistent behavior.
        /// </summary>
        public event Action Abandoned;
        /// <summary>
        /// A single shot event. Automatically cleared after launching. Use OnSelected method for implementing (internal) persistent behavior.
        /// </summary>
        public event Action Selected;
        /// <summary>
        /// A single shot event. Automatically cleared after launching. Use OnDeselected method for implementing (internal) persistent behavior.
        /// </summary>
        public event Action Deselected;

        protected HumanAIController HumanAIController => character.AIController as HumanAIController;
        protected IndoorsSteeringManager PathSteering => HumanAIController.PathSteering;
        protected SteeringManager SteeringManager => HumanAIController.SteeringManager;

        public AIObjective GetActiveObjective()
        {
            var subObjective = CurrentSubObjective;
            return subObjective == null ? this : subObjective.GetActiveObjective();
        }

        public AIObjective(Character character, AIObjectiveManager objectiveManager, float priorityModifier, Identifier option = default)
        {
            this.objectiveManager = objectiveManager;
            this.character = character;
            Option = option;
            PriorityModifier = priorityModifier;
        }

        /// <summary>
        /// Makes the character act according to the objective, or according to any subobjectives that need to be completed before this one
        /// </summary>
        public void TryComplete(float deltaTime)
        {
            if (isCompleted) { return; }
            if (CheckState()) { return; }
            // Not ready -> act (can't do foreach because it's possible that the collection is modified in event callbacks.
            for (int i = 0; i < subObjectives.Count; i++)
            {
                subObjectives[i].TryComplete(deltaTime);
                if (!ConcurrentObjectives) { return; }
            }
            Act(deltaTime);
        }

        public void AddSubObjective(AIObjective objective, bool addFirst = false)
        {
            var type = objective.GetType();
            subObjectives.RemoveAll(o => o.GetType() == type);
            if (addFirst)
            {
                subObjectives.Insert(0, objective);
            }
            else
            {
                subObjectives.Add(objective);
            }
        }

        public void RemoveSubObjective<T>(ref T objective) where T : AIObjective
        {
            if (objective != null)
            {
                if (subObjectives.Contains(objective))
                {
                    subObjectives.Remove(objective);
                }
                objective = null;
            }
        }

        public void SortSubObjectives()
        {
            if (!AllowSubObjectiveSorting) { return; }
            if (subObjectives.None()) { return; }
            var previousSubObjective = subObjectives.First();
            subObjectives.ForEach(so => so.GetPriority());
            subObjectives.Sort((x, y) => y.Priority.CompareTo(x.Priority));
            if (ConcurrentObjectives)
            {
                subObjectives.ForEach(so => so.SortSubObjectives());
            }
            else
            {
                var currentSubObjective = subObjectives.First();
                if (previousSubObjective != currentSubObjective)
                {
                    previousSubObjective.OnDeselected();
                    currentSubObjective.OnSelected();
                }
                currentSubObjective.SortSubObjectives();
            }
        }

        public bool IsAllowed
        {
            get 
            {
                if (!AllowWhileHandcuffed && character.LockHands) { return false; }
                if (!AllowOutsideSubmarine && character.Submarine == null) { return false; }
                // Evaluate ignored at outpost first, because it has higher priority than AllowInAnySub or AllowInFriendlySubs.
                if (IsIgnoredAtOutpost()) { return false; }
                if (AllowInAnySub) { return true; }
                if ((AllowInFriendlySubs && character.Submarine.TeamID == CharacterTeamType.FriendlyNPC) || character.IsEscorted) { return true; }
                return character.Submarine.TeamID == character.TeamID || character.Submarine.TeamID == character.OriginalTeamID;
            }
        }

        /// <summary>
        /// Returns true only when at a friendly outpost and when the order is set to be ignored there.
        /// Note that even if this returns false, the objective can be disallowed, because AllowInFriendlySubs is false.
        /// </summary>
        public bool IsIgnoredAtOutpost()
        {
            if (!IgnoreAtOutpost) { return false; }
            if (!Level.IsLoadedFriendlyOutpost) { return false; }
            if (!character.IsOnPlayerTeam || character.IsFriendlyNPCTurnedHostile) { return false; }
            if (character.Submarine?.Info == null) { return false; }
            return character.Submarine.Info.IsOutpost && character.Submarine.TeamID == CharacterTeamType.FriendlyNPC;
        }

        protected void HandleNonAllowed()
        {
            Priority = 0;
            Abandon = !IsIgnoredAtOutpost();
        }

        protected virtual float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleNonAllowed();
                return Priority;
            }
            if (objectiveManager.IsOrder(this))
            {
                Priority = objectiveManager.GetOrderPriority(this);
            }
            else
            {
                Priority = BasePriority + CumulatedDevotion;
            }
            return Priority;
        }

        /// <summary>
        /// Call this only when the priority needs to be recalculated. Use the cached Priority property when you don't need to recalculate.
        /// </summary>
        public float CalculatePriority()
        {
            ForceWalk = false;
            Priority = GetPriority();
            ForceHighestPriority = false;
            return Priority;
        }

        private void UpdateDevotion(float deltaTime)
        {
            var currentObjective = objectiveManager.CurrentObjective;
            if (currentObjective != null && (currentObjective == this || currentObjective.subObjectives.FirstOrDefault() == this))
            {
                CumulatedDevotion += Devotion * deltaTime;
            }
        }

        public virtual bool IsDuplicate<T>(T otherObjective) where T : AIObjective => otherObjective.Option == Option;

        public virtual void Update(float deltaTime)
        {
            if (resetPriorityTimer > 0)
            {
                resetPriorityTimer -= deltaTime;
            }
            else
            {
                ForceHighestPriority = false;
            }
            if (!objectiveManager.IsOrder(this) && objectiveManager.WaitTimer <= 0)
            {
                UpdateDevotion(deltaTime);
            }
            subObjectives.ForEach(so => so.Update(deltaTime));
        }

        /// <summary>
        /// Checks if the subobjectives in the given collection are removed from the subobjectives. And if so, removes it also from the dictionary.
        /// </summary>
        protected void SyncRemovedObjectives<T1, T2>(Dictionary<T1, T2> dictionary, IEnumerable<T1> collection) where T2 : AIObjective
        {
            foreach (T1 key in collection)
            {
                if (dictionary.TryGetValue(key, out T2 objective))
                {
                    if (!subObjectives.Contains(objective))
                    {
                        dictionary.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the objective already is created and added in subobjectives. If not, creates it.
        /// Handles objectives that cannot be completed. If the objective has been removed form the subobjectives, a null value is assigned to the reference.
        /// Returns true if the objective was created and successfully added.
        /// </summary>
        protected bool TryAddSubObjective<T>(ref T objective, Func<T> constructor, Action onCompleted = null, Action onAbandon = null) where T : AIObjective
        {
            if (objective != null)
            {
                // Sub objective already found, no need to do anything if it remains in the subobjectives
                // If the sub objective is removed -> it's either completed or impossible to complete.
                if (!subObjectives.Contains(objective))
                {
                    objective = null;
                }
                return false;
            }
            else
            {
                objective = constructor();
                if (!subObjectives.Contains(objective))
                {
                    if (objective.AllowMultipleInstances)
                    {
                        subObjectives.Add(objective);
                    }
                    else
                    {
                        AddSubObjective(objective);
                    }
                    if (onCompleted != null)
                    {
                        objective.Completed += onCompleted;
                    }
                    if (onAbandon != null)
                    {
                        objective.Abandoned += onAbandon;
                    }
                    return true;
                }
#if DEBUG
                DebugConsole.ThrowError("Attempted to add a duplicate subobjective!\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                return false;
            }
        }

        public virtual void OnSelected()
        {
            Reset();
            Selected?.Invoke();
            Selected = null;
        }

        public virtual void OnDeselected()
        {
            CumulatedDevotion = 0;
            Deselected?.Invoke();
            Deselected = null;
        }

        protected virtual void OnCompleted()
        {
            Completed?.Invoke();
            Completed = null;
        }

        protected virtual void OnAbandon()
        {
            Abandoned?.Invoke();
            Abandoned = null;
        }

        public virtual void Reset()
        {
            subObjectives.Clear();
            isCompleted = false;
            hasBeenChecked = false;
            _abandon = false;
            CumulatedDevotion = 0;
        }

        protected abstract void Act(float deltaTime);

        private bool isCompleted;
        private bool hasBeenChecked;

        public bool IsCompleted
        {
            get
            {
                if (!hasBeenChecked)
                {
                    CheckState();
                }
                return isCompleted;
            }
            protected set
            {
                if (isCompleted == value) { return; }
                isCompleted = value;
                if (isCompleted)
                {
                    OnCompleted();
                }
            }
        }

        private bool Check()
        {
            if (AbortCondition != null && AbortCondition(this))
            {
                Abandon = true;
                return false;
            }
            return CheckObjectiveSpecific();
        }

        protected abstract bool CheckObjectiveSpecific();

        private bool CheckState()
        {
            hasBeenChecked = true;
            CheckSubObjectives();
            if (subObjectives.None() || ConcurrentObjectives && subObjectives.All(so => so is AIObjectiveGoTo))
            {
                if (Check())
                {
                    IsCompleted = true;
                }
            }
            return isCompleted;
        }

        private void CheckSubObjectives()
        {
            for (int i = 0; i < subObjectives.Count; i++)
            {
                var subObjective = subObjectives[i];
                subObjective.CheckState();
                if (subObjective.IsCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing SUBobjective {subObjective.DebugTag} of {DebugTag}, because it is completed.", Color.LightGreen);
#endif
                    subObjectives.Remove(subObjective);
                }
                else if (!subObjective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing SUBobjective {subObjective.DebugTag} of {DebugTag}, because it cannot be completed.", Color.Red);
#endif
                    subObjectives.Remove(subObjective);
                    if (AbandonWhenCannotCompleteSubjectives)
                    {
                        if (objectiveManager.IsOrder(this))
                        {
                            Reset();
                        }
                        else
                        {
                            Abandon = true;
                        }
                    }
                }
            }
        }

        public virtual void SpeakAfterOrderReceived() { }

        protected static bool CanEquip(Character character, Item item, bool allowWearing)
        {
            if (item == null) { return false; }
            bool canEquip = false;
            if (item.AllowedSlots.Contains(InvSlotType.Any))
            {
                if (character.Inventory.IsAnySlotAvailable(item))
                {
                    canEquip = true;
                }
            }
            if (!canEquip)
            {
                var inv = character.Inventory;
                foreach (var allowedSlot in item.AllowedSlots)
                {
                    if (!allowWearing)
                    {
                        if (!allowedSlot.HasFlag(InvSlotType.RightHand) && !allowedSlot.HasFlag(InvSlotType.LeftHand))
                        {
                            continue;
                        }
                    }
                    foreach (var slotType in inv.SlotTypes)
                    {
                        if (!allowedSlot.HasFlag(slotType)) { continue; }
                        for (int i = 0; i < inv.Capacity; i++)
                        {
                            canEquip = true;
                            if (allowedSlot.HasFlag(inv.SlotTypes[i]) && inv.GetItemAt(i) != null)
                            {
                                canEquip = false;
                                break;
                            }
                        }
                    }
                }
            }
            return canEquip && character.Inventory.CanBePut(item);
        }

        protected bool CanEquip(Item item, bool allowWearing) => CanEquip(character, item, allowWearing);
    }
}
