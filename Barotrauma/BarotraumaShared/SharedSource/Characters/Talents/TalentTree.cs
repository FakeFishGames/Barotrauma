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

        public readonly ImmutableArray<TalentSubTree> TalentSubTrees;

        /// <summary>
        /// Talent identifiers of all the talents in this tree
        /// </summary>
        public readonly ImmutableHashSet<Identifier> AllTalentIdentifiers;

        public ContentXElement ConfigElement
        {
            get;
            private set;
        }

        public TalentTree(ContentXElement element, TalentTreesFile file) : base(file, element)
        {
            ConfigElement = element;

            if (Identifier.IsEmpty)
            {
                DebugConsole.ThrowError($"No job defined for talent tree in \"{file.Path}\"!");
                return;
            }
            
            List<TalentSubTree> subTrees = new List<TalentSubTree>();
            foreach (var subTreeElement in element.GetChildElements("subtree"))
            {
                subTrees.Add(new TalentSubTree(subTreeElement));
            }
            TalentSubTrees = subTrees.ToImmutableArray();
            AllTalentIdentifiers = TalentSubTrees.SelectMany(t => t.AllTalentIdentifiers).ToImmutableHashSet();
        }

		protected override Identifier DetermineIdentifier(XElement element)
		{
            return element.GetAttributeIdentifier("jobIdentifier", "");
		}

		public bool TalentIsInTree(Identifier talentIdentifier)
        {
            return AllTalentIdentifiers.Contains(talentIdentifier);
        }

        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier)
        {
            return IsViableTalentForCharacter(character, talentIdentifier, character?.Info?.UnlockedTalents ?? (ICollection<Identifier>)Array.Empty<Identifier>());
        }

        // i hate this function - markus
        // me too - joonas
        public static TalentTreeStageState GetTalentOptionStageState(Character character, Identifier subTreeIdentifier, int index, List<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab is null) { return TalentTreeStageState.Invalid; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return TalentTreeStageState.Invalid; }

            TalentSubTree subTree = talentTree.TalentSubTrees.FirstOrDefault(tst => tst.Identifier == subTreeIdentifier);

            if (subTree == null) { return TalentTreeStageState.Invalid; }

            TalentOption targetTalentOption = subTree.TalentOptionStages[index];

            if (targetTalentOption.TalentIdentifiers.Any(t => character.HasTalent(t)))
            {
                return TalentTreeStageState.Unlocked;
            }

            if (targetTalentOption.TalentIdentifiers.Any(t => selectedTalents.Contains(t)))
            {
                return TalentTreeStageState.Highlighted;
            }

            bool hasTalentInLastTier = true;
            bool isLastTalentPurchased = true;

            int lastindex = index - 1;
            if (lastindex >= 0)
            {
                TalentOption lastLatentOption = subTree.TalentOptionStages[lastindex];
                hasTalentInLastTier = lastLatentOption.TalentIdentifiers.Any(HasTalent);
                isLastTalentPurchased = lastLatentOption.TalentIdentifiers.Any(t => character.HasTalent(t));
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

            bool HasTalent(Identifier talentId)
            {
                return selectedTalents.Contains(talentId);
            }
        }


        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier, ICollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab == null) { return false; }
            if (character.Info.GetTotalTalentPoints() - selectedTalents.Count() <= 0) { return false; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return false; }

            foreach (var subTree in talentTree.TalentSubTrees)
            {
                if (subTree.ForceUnlock && subTree.TalentOptionStages.Any(option => option.TalentIdentifiers.Contains(talentIdentifier))) { return true; }

                foreach (var talentOptionStage in subTree.TalentOptionStages)
                {
                    bool hasTalentInThisTier = talentOptionStage.TalentIdentifiers.Any(t => selectedTalents.Contains(t));
                    if (!hasTalentInThisTier)
                    {
                        if (talentOptionStage.TalentIdentifiers.Contains(talentIdentifier))
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

        public readonly ImmutableArray<TalentOption> TalentOptionStages;

        public readonly ImmutableHashSet<Identifier> AllTalentIdentifiers;

        public TalentSubTree(ContentXElement subTreeElement)
        {
            Identifier = subTreeElement.GetAttributeIdentifier("identifier", "");
            DisplayName = TextManager.Get("talenttree." + Identifier).Fallback(Identifier.Value);
            List<TalentOption> talentOptionStages = new List<TalentOption>();
            foreach (var talentOptionsElement in subTreeElement.GetChildElements("talentoptions"))
            {
                talentOptionStages.Add(new TalentOption(talentOptionsElement, Identifier));
            }
            TalentOptionStages = talentOptionStages.ToImmutableArray();
            AllTalentIdentifiers = TalentOptionStages.SelectMany(t => t.TalentIdentifiers).ToImmutableHashSet();
        }

    }

    class TalentOption
    {
        private readonly ImmutableHashSet<Identifier> talentIdentifiers;

        public IEnumerable<Identifier> TalentIdentifiers => talentIdentifiers;

        public bool HasTalent(Identifier talentIdentifier)
        {
            return talentIdentifiers.Contains(talentIdentifier);
        }

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
