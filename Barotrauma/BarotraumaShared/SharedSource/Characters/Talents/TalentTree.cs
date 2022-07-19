using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class TalentTree : Prefab
    {
        public enum TalentTreeStageState
        {
            Invalid,
            Locked,
            Unlocked,
            Available,
            Highlighted
        }

        public static readonly PrefabCollection<TalentTree> JobTalentTrees = new PrefabCollection<TalentTree>();

        public readonly List<TalentSubTree> TalentSubTrees = new List<TalentSubTree>();

        public ContentXElement ConfigElement
        {
            get;
            private set;
        }

        public TalentTree(ContentXElement element, TalentTreesFile file) : base(file, element.GetAttributeIdentifier("jobIdentifier", ""))
        {
            ConfigElement = element;

            if (Identifier.IsEmpty)
            {
                DebugConsole.ThrowError($"No job defined for talent tree in \"{file.Path}\"!");
                return;
            }

            foreach (var subTreeElement in element.GetChildElements("subtree"))
            {
                TalentSubTrees.Add(new TalentSubTree(subTreeElement));
            }
        }
        
        public bool TalentIsInTree(Identifier talentIdentifier)
        {
            return TalentSubTrees.SelectMany(s => s.TalentOptionStages.SelectMany(o => o.Talents.Select(t => t.Identifier))).Any(c => c == talentIdentifier);
        }

        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier)
        {
            return IsViableTalentForCharacter(character, talentIdentifier, character?.Info?.UnlockedTalents ?? (ICollection<Identifier>)Array.Empty<Identifier>());
        }

        // i hate this function - markus
        public static TalentTreeStageState GetTalentOptionStageState(Character character, Identifier subTreeIdentifier, int index, List<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab is null) { return TalentTreeStageState.Invalid; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return TalentTreeStageState.Invalid; }

            TalentSubTree subTree = talentTree.TalentSubTrees.FirstOrDefault(tst => tst.Identifier == subTreeIdentifier);

            if (subTree == null) { return TalentTreeStageState.Invalid; }

            TalentOption targetTalentOption = subTree.TalentOptionStages[index];

            if (targetTalentOption.Talents.Any(t => character.HasTalent(t.Identifier)))
            {
                return TalentTreeStageState.Unlocked;
            }

            if (targetTalentOption.Talents.Any(t => selectedTalents.Contains(t.Identifier)))
            {
                return TalentTreeStageState.Highlighted;
            }

            bool hasTalentInLastTier = true;
            bool isLastTalentPurchased = true;

            int lastindex = index - 1;
            if (lastindex >= 0)
            {
                TalentOption lastLatentOption = subTree.TalentOptionStages[lastindex];
                hasTalentInLastTier = lastLatentOption.Talents.Any(HasTalent);
                isLastTalentPurchased = lastLatentOption.Talents.Any(t => character.HasTalent(t.Identifier));
            }

            if (!hasTalentInLastTier)
            {
                return TalentTreeStageState.Locked;
            }

            bool hasPointsForNewTalent = character.Info.GetTotalTalentPoints() - selectedTalents.Count > 0;

            if (hasPointsForNewTalent)
            {
                return isLastTalentPurchased ? TalentTreeStageState.Highlighted : TalentTreeStageState.Available;
            }

            return TalentTreeStageState.Locked;

            bool HasTalent(TalentPrefab t)
            {
                return selectedTalents.Contains(t.Identifier);
            }
        }


        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier, ICollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab == null) { return false; }
            if (character.Info.GetTotalTalentPoints() - selectedTalents.Count() <= 0) { return false; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return false; }

            foreach (var subTree in talentTree.TalentSubTrees)
            {
                if (subTree.ForceUnlock && subTree.TalentOptionStages.Any(option => option.Talents.Any(t => t.Identifier == talentIdentifier))) { return true; }

                foreach (var talentOptionStage in subTree.TalentOptionStages)
                {
                    bool hasTalentInThisTier = talentOptionStage.Talents.Any(t => selectedTalents.Contains(t.Identifier));
                    if (!hasTalentInThisTier)
                    {
                        if (talentOptionStage.Talents.Any(t => t.Identifier == talentIdentifier))
                        {
                            return true;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            return false;
        }

        public static List<Identifier> CheckTalentSelection(Character controlledCharacter, IEnumerable<Identifier> selectedTalents)
        {
            List<Identifier> viableTalents = new List<Identifier>();
            bool canStillUnlock = true;
            // keep trying to unlock talents until none of the talents are unlockable
            while (canStillUnlock && selectedTalents.Any())
            {
                canStillUnlock = false;
                foreach (Identifier talent in selectedTalents)
                {
                    if (!viableTalents.Contains(talent) && IsViableTalentForCharacter(controlledCharacter, talent, viableTalents))
                    {
                        viableTalents.Add(talent);
                        canStillUnlock = true;
                    }
                }
            }
            return viableTalents;
        }

        public override void Dispose() { }
    }

    class TalentSubTree
    {
        public Identifier Identifier { get; }

        public LocalizedString DisplayName { get; }

        public bool ForceUnlock;

        public readonly List<TalentOption> TalentOptionStages = new List<TalentOption>();

        public TalentSubTree(ContentXElement subTreeElement)
        {
            Identifier = subTreeElement.GetAttributeIdentifier("identifier", "");

            DisplayName = TextManager.Get("talenttree." + Identifier).Fallback(Identifier.Value);

            foreach (var talentOptionsElement in subTreeElement.GetChildElements("talentoptions"))
            {
                TalentOptionStages.Add(new TalentOption(talentOptionsElement, Identifier));
            }
        }

    }

    class TalentOption
    {
        private readonly ImmutableHashSet<Identifier> talentIdentifiers;

        public IEnumerable<TalentPrefab> Talents
            => talentIdentifiers.Select(id => TalentPrefab.TalentPrefabs[id]);

        public TalentOption(ContentXElement talentOptionsElement, Identifier debugIdentifier)
        {
            var talentIdentifiers = new HashSet<Identifier>();
            foreach (var talentOptionElement in talentOptionsElement.GetChildElements("talentoption"))
            {
                Identifier identifier = talentOptionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                talentIdentifiers.Add(identifier);
            }
            this.talentIdentifiers = talentIdentifiers.ToImmutableHashSet();
        }
    }

}
