#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma.PerkBehaviors
{
    internal enum PerkSimulation
    {
        /// <summary>
        /// Perk is only run on the server,
        /// other parts of the game handle the client-side effects.
        /// Like serializable properties and affliction syncing.
        /// </summary>
        ServerOnly,
        /// <summary>
        /// Both the server and clients run the perk.
        /// </summary>
        ServerAndClients
    }

    internal abstract class PerkBase : ISerializableEntity
    {
        public string Name { get; }
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public virtual PerkSimulation Simulation => PerkSimulation.ServerOnly;

        public readonly DisembarkPerkPrefab Prefab;

        protected PerkBase(ContentXElement element, DisembarkPerkPrefab prefab)
        {
            Name = element.Name.ToString();
            Prefab = prefab;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public virtual bool CanApply(SubmarineInfo submarine)
        {
            return true;
        }

        /// <summary>
        /// You might notice that this function is not virtual.
        /// It was at first, but there was a misunderstanding in design,
        /// so I turned it into a kill switch for all perks for now.
        /// If we ever want to add perks that do work when a submarine is present,
        /// this function can be made virtual again and set to true in the appropriate perks.
        /// </summary>
        public bool CanApplyWithoutSubmarine()
            => false;

        public abstract void ApplyOnRoundStart(IReadOnlyCollection<Character> teamCharacters, Submarine? teamSubmarine);

        public static bool TryLoadFromXml(ContentXElement element, DisembarkPerkPrefab prefab, [NotNullWhen(true)] out PerkBase? perk)
        {
            Type? type = ReflectionUtils.GetTypeWithBackwardsCompatibility(ToolBox.BarotraumaAssembly, "Barotrauma.PerkBehaviors", element.Name.ToString(), throwOnError: false, ignoreCase: true);
            if (type is null)
            {
                DebugConsole.ThrowError($"Could not find a perk behavior of the type \"{element.Name}\".", contentPackage: element.ContentPackage);
                perk = null;
                return false;
            }

            try
            {
                object? instance = Activator.CreateInstance(type, element, prefab);
                if (instance is PerkBase perkInstance)
                {
                    perk = perkInstance;
                    return true;
                }

                throw new InvalidCastException($"Could not cast the instance of type \"{type}\" to a {nameof(PerkBase)}.");
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(e.InnerException != null ? e.InnerException.ToString() : e.ToString(), contentPackage: element.ContentPackage);
                perk = null;
                return false;
            }
        }
    }
}