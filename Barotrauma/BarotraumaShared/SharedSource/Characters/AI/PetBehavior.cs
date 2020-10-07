using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class PetBehavior
    {
        public float Hunger { get; set; } = 50.0f;
        public float Happiness { get; set; } = 50.0f;

        public float MaxHappiness { get; set; }
        public float MaxHunger { get; set; }

        public float HappinessDecreaseRate { get; set; }
        public float HungerIncreaseRate { get; set; }

        public float PlayForce { get; set; }

        public float PlayTimer { get; set; }

        public EnemyAIController AiController { get; private set; } = null;

        public Character Owner { get; set; }

        private class ItemProduction
        {
            public struct Item
            {
                public ItemPrefab Prefab;
                public float Commonness;
            }
            public List<Item> Items;
            public Vector2 HungerRange;
            public Vector2 HappinessRange;
            public float Rate;
            public float HungerRate;
            public float InvHungerRate;
            public float HappinessRate;
            public float InvHappinessRate;

            private readonly float totalCommonness;
            private float timer;

            public ItemProduction(XElement element)
            {
                Items = new List<Item>();

                HungerRate = element.GetAttributeFloat("hungerrate", 0.0f);
                InvHungerRate = element.GetAttributeFloat("invhungerrate", 0.0f);
                HappinessRate = element.GetAttributeFloat("happinessrate", 0.0f);
                InvHappinessRate = element.GetAttributeFloat("invhappinessrate", 0.0f);

                string[] requiredHappinessStr = element.GetAttributeString("requiredhappiness", "0-100").Split('-');
                string[] requiredHungerStr = element.GetAttributeString("requiredhunger", "0-100").Split('-');
                HappinessRange = new Vector2(0, 100);
                HungerRange = new Vector2(0, 100);
                float tempF;
                if (requiredHappinessStr.Length >= 2)
                {
                    if (float.TryParse(requiredHappinessStr[0], NumberStyles.Any, CultureInfo.InvariantCulture, out tempF)) { HappinessRange.X = tempF; }
                    if (float.TryParse(requiredHappinessStr[1], NumberStyles.Any, CultureInfo.InvariantCulture, out tempF)) { HappinessRange.Y = tempF; }
                }
                if (requiredHungerStr.Length >= 2)
                {
                    if (float.TryParse(requiredHungerStr[0], NumberStyles.Any, CultureInfo.InvariantCulture, out tempF)) { HungerRange.X = tempF; }
                    if (float.TryParse(requiredHungerStr[1], NumberStyles.Any, CultureInfo.InvariantCulture, out tempF)) { HungerRange.Y = tempF; }
                }
                Rate = element.GetAttributeFloat("rate", 0.016f);
                totalCommonness = 0.0f;
                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.LocalName.ToLowerInvariant())
                    {
                        case "item":
                            Item newItemToProduce = new Item
                            {
                                Prefab = ItemPrefab.Find("", subElement.GetAttributeString("identifier", "")),
                                Commonness = subElement.GetAttributeFloat("commonness", 0.0f)
                            };
                            totalCommonness += newItemToProduce.Commonness;
                            Items.Add(newItemToProduce);
                            break;
                    }
                }

                timer = 1.0f;
            }

            public void Update(PetBehavior pet, float deltaTime)
            {
                if (pet.Happiness < HappinessRange.X || pet.Happiness > HappinessRange.Y) { return; }
                if (pet.Hunger < HungerRange.X || pet.Hunger > HungerRange.Y) { return; }

                float currentRate = Rate;
                currentRate += HappinessRate * (pet.Happiness - HappinessRange.X) / (HappinessRange.Y - HappinessRange.X);
                currentRate += InvHappinessRate * (1.0f - ((pet.Happiness - HappinessRange.X) / (HappinessRange.Y - HappinessRange.X)));
                currentRate += HungerRate * (pet.Hunger - HungerRange.X) / (HungerRange.Y - HungerRange.X);
                currentRate += InvHungerRate * (1.0f - ((pet.Hunger - HungerRange.X) / (HungerRange.Y - HungerRange.X)));
                timer -= currentRate * deltaTime;
                if (timer <= 0.0f)
                {
                    timer = 1.0f;
                    float r = Rand.Range(0.0f, totalCommonness);
                    float aggregate = 0.0f;
                    for (int i = 0; i < Items.Count; i++)
                    {
                        aggregate += Items[i].Commonness;
                        if (aggregate >= r)
                        {
                            Entity.Spawner.AddToSpawnQueue(Items[i].Prefab, pet.AiController.Character.WorldPosition);
                            break;
                        }
                    }
                }
            }
        }

        private class Food
        {
            public string Tag;
            public Vector2 HungerRange;
            public float Hunger;
            public float Happiness;
            public float Priority;

            public CharacterParams.TargetParams TargetParams = null;
        }

        private readonly List<ItemProduction> itemsToProduce = new List<ItemProduction>();
        private readonly List<Food> foods = new List<Food>();

        public PetBehavior(XElement element, EnemyAIController aiController)
        {
            AiController = aiController;
            AiController.Character.CanBeDragged = true;

            MaxHappiness = element.GetAttributeFloat("maxhappiness", 100.0f);
            MaxHunger = element.GetAttributeFloat("maxhunger", 100.0f);

            Happiness = MaxHappiness * 0.5f;
            Hunger = MaxHunger * 0.5f;

            HappinessDecreaseRate = element.GetAttributeFloat("happinessdecreaserate", 0.1f);
            HungerIncreaseRate = element.GetAttributeFloat("hungerincreaserate", 0.25f);

            PlayForce = element.GetAttributeFloat("playforce", 15.0f);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "itemproduction":
                        itemsToProduce.Add(new ItemProduction(subElement));
                        break;
                    case "eat":
                        Food food = new Food
                        {
                            Tag = subElement.GetAttributeString("tag", "")
                        };
                        string[] requiredHungerStr = subElement.GetAttributeString("requiredhunger", "0-100").Split('-');
                        food.HungerRange = new Vector2(0, 100);
                        if (requiredHungerStr.Length >= 2)
                        {
                            if (float.TryParse(requiredHungerStr[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float tempF)) { food.HungerRange.X = tempF; }
                            if (float.TryParse(requiredHungerStr[1], NumberStyles.Any, CultureInfo.InvariantCulture, out tempF)) { food.HungerRange.Y = tempF; }
                        }
                        food.Hunger = subElement.GetAttributeFloat("hunger", -1);
                        food.Happiness = subElement.GetAttributeFloat("happiness", 1);
                        food.Priority = subElement.GetAttributeFloat("priority", 100);
                        foods.Add(food);
                        break;
                }
            }
        }

        public void OnEat(IEnumerable<string> tags, float amount)
        {
            for (int i = 0; i < foods.Count; i++)
            {
                if (tags.Any(t => t.Equals(foods[i].Tag, System.StringComparison.OrdinalIgnoreCase)))
                {
                    Hunger += foods[i].Hunger * amount;
                    Happiness += foods[i].Happiness * amount;
                    break;
                }
            }
        }

        public void OnEat(string tag, float amount)
        {
            for (int i = 0; i < foods.Count; i++)
            {
                if (tag.Equals(foods[i].Tag, System.StringComparison.OrdinalIgnoreCase))
                {
                    Hunger += foods[i].Hunger * amount;
                    Happiness += foods[i].Happiness * amount;
                    break;
                }
            }
        }

        public void Play()
        {
            if (PlayTimer > 0.0f) { return; }
            PlayTimer = 5.0f;
            AiController.Character.Stun = 1.0f;
            Happiness += 10.0f;
            if (Happiness > MaxHappiness) { Happiness = MaxHappiness; }
            AiController.Character.AnimController.MainLimb.body.LinearVelocity += new Vector2(0, PlayForce);
        }

        public string GetName()
        {
            if (AiController.Character.Inventory != null)
            {
                var items = AiController.Character.Inventory.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null) { continue; }
                    var tag = item.GetComponent<NameTag>();
                    if (tag != null && !string.IsNullOrWhiteSpace(tag.WrittenName))
                    {
                        return tag.WrittenName;
                    }
                }
            }

            return AiController.Character.Name;
        }

        public void Update(float deltaTime)
        {
            var character = AiController.Character;
            if (character?.Removed ?? true || character.IsDead) { return; }
            if (GameMain.NetworkMember?.IsClient ?? false) { return; } //TODO: syncing

            Hunger += HungerIncreaseRate * deltaTime;

            Happiness -= HappinessDecreaseRate * deltaTime;

            PlayTimer -= deltaTime;

            for (int i = 0; i < foods.Count; i++)
            {
                if (Hunger >= foods[i].HungerRange.X && Hunger <= foods[i].HungerRange.Y)
                {
                    if (foods[i].TargetParams == null &&
                        AiController.AIParams.TryAddNewTarget(foods[i].Tag, AIState.Eat, foods[i].Priority, out CharacterParams.TargetParams targetParams))
                    {
                        foods[i].TargetParams = targetParams;
                    }
                }
                else if (foods[i].TargetParams != null)
                {
                    AiController.AIParams.RemoveTarget(foods[i].TargetParams);
                    foods[i].TargetParams = null;
                }
            }

            if (Hunger < 0.0f) { Hunger = 0.0f; }
            if (Hunger > MaxHunger) { Hunger = MaxHunger; }
            if (Happiness < 0.0f) { Happiness = 0.0f; }
            if (Happiness > MaxHappiness) { Happiness = MaxHappiness; }
            if (PlayTimer < 0.0f) { PlayTimer = 0.0f; }

            if (Hunger >= MaxHunger * 0.99f)
            {
                character.CharacterHealth.ApplyAffliction(character.AnimController.MainLimb, new Affliction(AfflictionPrefab.InternalDamage, 8.0f * deltaTime));
            }
            else if (Hunger < MaxHunger * 0.1f)
            {
                character.CharacterHealth.ReduceAffliction(null, null, 8.0f * deltaTime);
            }

            if (character.SelectedBy != null)
            {
                character.Stun = 1.0f;
            }

            for (int i = 0; i < itemsToProduce.Count; i++)
            {
                itemsToProduce[i].Update(this, deltaTime);
            }
        }
    }
}
