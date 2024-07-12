using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveDeconstructItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "deconstruct item".ToIdentifier();
        protected override bool AllowWhileHandcuffed => false;

        protected override bool AllowInFriendlySubs => true;

        public readonly Item Item;

        private Deconstructor deconstructor;

        private AIObjectiveDecontainItem decontainObjective;

        public AIObjectiveDeconstructItem(Item item, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            Item = item;
        }

        protected override void Act(float deltaTime)
        {
            if (subObjectives.Any()) { return; }

            if (deconstructor == null)
            {
                deconstructor = FindDeconstructor();
                if (deconstructor == null)
                {
                    Abandon = true;
                    return;
                }
            }

            TryAddSubObjective(ref decontainObjective,
                constructor: () => new AIObjectiveDecontainItem(character, Item, objectiveManager,
                    sourceContainer: Item.Container?.GetComponent<ItemContainer>(), targetContainer: deconstructor.InputContainer, priorityModifier: PriorityModifier)
                {
                    Equip = true,
                    RemoveExistingWhenNecessary = true
                },
                onCompleted: () =>
                {
                    StartDeconstructor();
                    //make sure the item gets moved to the main sub if the crew leaves while a bot is deconstructing something in the outpost
                    if (deconstructor.Item.Submarine is { Info.IsOutpost: true })
                    {
                        HumanAIController.HandleRelocation(Item);
                        deconstructor.RelocateOutputToMainSub = true;
                    }
                    IsCompleted = true; 
                    RemoveSubObjective(ref decontainObjective);
                },
                onAbandon: () =>
                {
                    Abandon = true;
                });
        }

        private Deconstructor FindDeconstructor()
        {
            Deconstructor closestDeconstructor = null;
            float bestDistFactor = 0;
            foreach (var otherItem in Item.ItemList)
            {
                var potentialDeconstructor = otherItem.GetComponent<Deconstructor>();
                if (potentialDeconstructor?.InputContainer == null) { continue; }
                if (!potentialDeconstructor.InputContainer.Inventory.CanBePut(Item)) { continue; }
                if (!potentialDeconstructor.Item.HasAccess(character)) { continue; }
                float distFactor = GetDistanceFactor(Item.WorldPosition, potentialDeconstructor.Item.WorldPosition, factorAtMaxDistance: 0.2f);
                if (distFactor > bestDistFactor)
                {
                    closestDeconstructor = potentialDeconstructor;
                    bestDistFactor = distFactor;
                }
            }
            return closestDeconstructor;
        }

        private void StartDeconstructor()
        {
            deconstructor.SetActive(active: true, user: character, createNetworkEvent: true);
        }

        protected override bool CheckObjectiveSpecific()
        {
            if (Item.IgnoreByAI(character))
            {
                Abandon = true;
            }
            else if (deconstructor != null && deconstructor.Item.IgnoreByAI(character))
            {
                Abandon = true;
            }
            return !Abandon && IsCompleted;
        }

        public override void Reset()
        {
            base.Reset();
            decontainObjective = null;
        }

        public void DropTarget()
        {
            if (Item != null && character.HasItem(Item))
            {
                Item.Drop(character);
            }
        }
    }
}
