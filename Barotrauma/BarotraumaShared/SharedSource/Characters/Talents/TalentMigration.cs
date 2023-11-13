#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    internal abstract class TalentMigration
    {
        private readonly Version version;

        private delegate TalentMigration TalentMigrationCtor(Version version, ContentXElement element);

        private static readonly Dictionary<Identifier, TalentMigrationCtor> migrationTemplates =
            new()
            {
                [new Identifier("AddStat")] =
                    static (version, element) => new TalentMigrationAddStat(version, element),

                [new Identifier("UpdateStatIdentifier")] =
                    static (version, element) => new TalentMigrationUpdateStatIdentifier(version, element)
            };

        public bool TryApply(Version savedVersion, CharacterInfo info)
        {
            if (version <= savedVersion) { return false; }
            Apply(info);
            return true;
        }

        protected abstract void Apply(CharacterInfo info);

        protected TalentMigration(Version targetVersion)
        {
            version = targetVersion;
        }

        public static TalentMigration FromXML(ContentXElement element)
        {
            Version? version = XMLExtensions.GetAttributeVersion(element, "version", null);

            if (version is null)
            {
                throw new Exception("Talent migration version not defined.");
            }

            Identifier name = element.Name.ToString().ToIdentifier();

            if (!migrationTemplates.TryGetValue(name, out TalentMigrationCtor? ctor))
            {
                throw new Exception($"Unknown talent migration type: {name}.");
            }

            return ctor(version, element);
        }
    }

    /// <summary>
    /// Migration that adds a missing permanent stat to the character.
    /// </summary>
    internal sealed class TalentMigrationAddStat : TalentMigration
    {
        [Serialize(StatTypes.None, IsPropertySaveable.Yes)]
        public StatTypes StatType { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier StatIdentifier { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float Value { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool RemoveOnDeath { get; set; }

        public TalentMigrationAddStat(Version targetVersion, ContentXElement element) : base(targetVersion)
            => SerializableProperty.DeserializeProperties(this, element);

        protected override void Apply(CharacterInfo info)
        {
            info.ChangeSavedStatValue(StatType, Value, StatIdentifier, RemoveOnDeath);
        }
    }

    /// <summary>
    /// Migration that updates permanent stat identifiers.
    /// </summary>
    internal class TalentMigrationUpdateStatIdentifier : TalentMigration
    {
        [Serialize("", IsPropertySaveable.Yes, "The old identifier to update.")]
        public Identifier Old { get; set; }

        [Serialize("", IsPropertySaveable.Yes, "What to change the old identifier to.")]
        public Identifier New { get; set; }

        public TalentMigrationUpdateStatIdentifier(Version targetVersion, ContentXElement element) : base(targetVersion)
            => SerializableProperty.DeserializeProperties(this, element);

        protected override void Apply(CharacterInfo info)
        {
            foreach (SavedStatValue statValue in info.SavedStatValues.Values.SelectMany(static s => s))
            {
                if (statValue.StatIdentifier != Old) { continue; }

                statValue.StatIdentifier = New;
            }
        }
    }
}