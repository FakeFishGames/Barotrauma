using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor.Resources
{
    /// <summary>
    /// A readonly resource that gets its value from expression-based setters.
    /// </summary>
    internal class VariableResource : ReadonlyResource
    {
        class TemporaryEffect
        {
            public readonly int Duration;
            public int Timer;

            public readonly float Value;
            public readonly ModifyResourceAction.OperationType Operator;

            /// <param name="timer">How many world steps the effect has already been active (used when loading a save)</param>
            public TemporaryEffect(float value, ModifyResourceAction.OperationType op, int duration, int timer = 0)
            {
                Value = value;
                Operator = op;
                Duration = duration;
                Timer = timer;
            }

            public float CalculateModifiedValue(float currentAmount)
            {
                return Operator switch
                {
                    ModifyResourceAction.OperationType.Add => currentAmount + Value,
                    ModifyResourceAction.OperationType.Subtract => currentAmount - Value,
                    ModifyResourceAction.OperationType.Multiply => currentAmount * Value,
                    ModifyResourceAction.OperationType.Set => Value,
                    _ => currentAmount
                };
            }
        }

        private readonly List<TemporaryEffect> temporaryEffects = [];

        private readonly ImmutableArray<PassiveGenerationParams> setters;

        private readonly List<EconomyLogEntry> logEntriesFromActiveSetters = [];

        /// <summary>
        /// Log entries created by any of the currently active setters (= the things that affect the value of the resource).
        /// Can be used to e.g. display logs in the resources UI when some condition that reduces happiness is active.
        /// </summary>
        public IEnumerable<EconomyLogEntry> LogEntriesFromActiveSetters => logEntriesFromActiveSetters;

        public VariableResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
        {
            var builder = ImmutableArray.CreateBuilder<PassiveGenerationParams>();
            foreach (ContentXElement subElement in prefab.OriginalElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "Setter")
                {
                    builder.Add(new PassiveGenerationParams(subElement, manager));
                }
            }
            if (element != null)
            {
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.NameAsIdentifier() == nameof(TemporaryEffect))
                    {
                        float value = subElement.GetAttributeFloat("value", 0f);
                        int duration = subElement.GetAttributeInt("duration", 0);
                        int timer = subElement.GetAttributeInt("timer", 0);
                        ModifyResourceAction.OperationType op = subElement.GetAttributeEnum("op", ModifyResourceAction.OperationType.Add);
                        temporaryEffects.Add(new TemporaryEffect(value, op, duration, timer));
                    }
                }
            }

            if (builder.Count == 0)
            {
                DebugConsole.ThrowError($"VariableResource \"{prefab.Identifier}\" has no setters defined in its prefab, the resource will always be 0.");
            }

            setters = builder.ToImmutable();
        }

        /// <summary>
        /// Adds a temporary modified to the value of the resource.
        /// </summary>
        public void AddTemporaryEffect(float value, ModifyResourceAction.OperationType op, int duration)
        {
            float prevAmount = Amount;

            temporaryEffects.Add(new TemporaryEffect(value, op, duration));

            UpdateResourceStatusIndicators();
#if CLIENT
            Manager.CreateResourceChangeNotification(this, prevAmount);
#endif
        }

        protected override float GetAmountResourceSpecific()
        {
            float amount = 0f;
            logEntriesFromActiveSetters.Clear();
            foreach (PassiveGenerationParams setter in setters)
            {
                bool matchesRequirements = Requirement.RequirementsMatch(Manager, setter.Requirements, setter.ConditionalComparison, debugLogName: $"Resource {Identifier}");
                if (!matchesRequirements) { continue; }
                float prevAmount = amount;
                amount = setter.CalculateGeneration(amount);
                if (!setter.LogTextTag.IsEmpty)
                {
                    logEntriesFromActiveSetters.Add(EconomyLogEntrySpecial.Create(setter.LogTextTag, this, prevAmount, amount));
                }
            }
            foreach (var temporaryEffect in temporaryEffects)
            {
                amount = temporaryEffect.CalculateModifiedValue(amount);
            }
            return MathHelper.Clamp(amount, MinAmount, MaxAmount);
        }

        public override void Tick(int currentWorldStep)
        {
            base.Tick(currentWorldStep);
            foreach (var temporaryEffect in temporaryEffects)
            {
                temporaryEffect.Timer++;
            }
            temporaryEffects.RemoveAll(t => t.Timer >= t.Duration);
        }

        public override XElement Save()
       {
            XElement saveElement = base.Save();
            foreach (var temporaryEffect in temporaryEffects)
            {
                XElement itemElement = new XElement(nameof(TemporaryEffect),
                                                    new XAttribute("value", temporaryEffect.Value),
                                                    new XAttribute("timer", temporaryEffect.Timer),
                                                    new XAttribute("duration", temporaryEffect.Duration),
                                                    new XAttribute("op", temporaryEffect.Operator));
                saveElement.Add(itemElement);
            }
            return saveElement;
        }

        /// <summary>
        /// Lists all resources that are taken into equation for this resource.
        /// </summary>
        public IEnumerable<Resource> GetResourcesAffectingThisResource()
        {
            foreach (PassiveGenerationParams setter in setters)
            {
                foreach (var token in setter.Tokens)
                {
                    if (token.Value.TryGet(out Identifier identifier))
                    {
                        if (Manager.GetResource(identifier) is { } resource)
                        {
                            yield return resource;
                        }
                    }
                }
            }
        }
    }
}