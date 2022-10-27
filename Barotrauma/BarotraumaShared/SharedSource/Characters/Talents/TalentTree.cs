using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal sealed class TalentTree : Prefab
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

        public TalentTree(ContentXElement element, TalentTreesFile file) : base(file, element.GetAttributeIdentifier("jobIdentifier", ""))
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

        public bool TalentIsInTree(Identifier talentIdentifier)
        {
            return AllTalentIdentifiers.Contains(talentIdentifier);
        }

        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier)
        {
            return IsViableTalentForCharacter(character, talentIdentifier, character?.Info?.UnlockedTalents ?? (IReadOnlyCollection<Identifier>)Array.Empty<Identifier>());
        }

        public static bool TalentTreeMeetsRequirements(TalentTree tree, TalentSubTree targetTree, IReadOnlyCollection<Identifier> selectedTalents)
        {
            IEnumerable<TalentSubTree> blockingSubTrees = tree.TalentSubTrees.Where(tst => tst.BlockedTrees.Contains(targetTree.Identifier)),
                                       requiredSubTrees = tree.TalentSubTrees.Where(tst => targetTree.RequiredTrees.Contains(tst.Identifier));

            return requiredSubTrees.All(tst => tst.IsCompleted(selectedTalents)) &&  // check if we meet requirements
                   !blockingSubTrees.Any(tst => tst.HasAnyTalent(selectedTalents)); // check if any other talent trees are blocking this one
        }

        // i hate this function - markus
        // me too - joonas
        public static TalentTreeStageState GetTalentOptionStageState(Character character, Identifier subTreeIdentifier, int index, IReadOnlyCollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab is null) { return TalentTreeStageState.Invalid; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return TalentTreeStageState.Invalid; }

            TalentSubTree subTree = talentTree!.TalentSubTrees.FirstOrDefault(tst => tst.Identifier == subTreeIdentifier);
            if (subTree is null) { return TalentTreeStageState.Invalid; }

            if (!TalentTreeMeetsRequirements(talentTree, subTree, selectedTalents))
            {
                return TalentTreeStageState.Locked;
            }

            TalentOption targetTalentOption = subTree.TalentOptionStages[index];

            if (targetTalentOption.HasEnoughTalents(character.Info))
            {
                return TalentTreeStageState.Unlocked;
            }

            if (targetTalentOption.HasSelectedTalent(selectedTalents))
            {
                return TalentTreeStageState.Highlighted;
            }

            bool hasTalentInLastTier = true;
            bool isLastTalentPurchased = true;

            int lastindex = index - 1;
            if (lastindex >= 0)
            {
                TalentOption lastLatentOption = subTree.TalentOptionStages[lastindex];
                hasTalentInLastTier = lastLatentOption.HasEnoughTalents(selectedTalents);
                isLastTalentPurchased = lastLatentOption.HasEnoughTalents(character.Info);
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
        }


        public static bool IsViableTalentForCharacter(Character character, Identifier talentIdentifier, IReadOnlyCollection<Identifier> selectedTalents)
        {
            if (character?.Info?.Job.Prefab == null) { return false; }

            if (character.Info.GetTotalTalentPoints() - selectedTalents.Count <= 0) { return false; }

            if (!JobTalentTrees.TryGet(character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return false; }

            foreach (var subTree in talentTree!.TalentSubTrees)
            {
                foreach (var talentOptionStage in subTree.TalentOptionStages)
                {
                    bool hasTalentInThisTier = talentOptionStage.HasEnoughTalents(selectedTalents);
                    if (!hasTalentInThisTier)
                    {
                        if (talentOptionStage.TalentIdentifiers.Contains(talentIdentifier))
                        {
                            return TalentTreeMeetsRequirements(talentTree, subTree, selectedTalents);
                        }
                        break;
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

    internal enum TalentTreeType
    {
        Specialization,
        Primary
    }

    internal sealed class TalentSubTree
    {
        public Identifier Identifier { get; }

        public LocalizedString DisplayName { get; }

        public readonly ImmutableArray<TalentOption> TalentOptionStages;

        public readonly ImmutableHashSet<Identifier> AllTalentIdentifiers;

        public readonly TalentTreeType Type;
        public readonly ImmutableHashSet<Identifier> RequiredTrees;
        public readonly ImmutableHashSet<Identifier> BlockedTrees;

        public bool IsCompleted(IReadOnlyCollection<Identifier> talents) => TalentOptionStages.All(option => option.HasEnoughTalents(talents));
        public bool HasAnyTalent(IReadOnlyCollection<Identifier> talents) => TalentOptionStages.Any(option => option.HasSelectedTalent(talents));

        public TalentSubTree(ContentXElement subTreeElement)
        {
            Identifier = subTreeElement.GetAttributeIdentifier("identifier", "");
            string nameIdentifier = subTreeElement.GetAttributeString("nameidentifier", string.Empty);
            if (string.IsNullOrWhiteSpace(nameIdentifier))
            {
                nameIdentifier = $"talenttree.{Identifier}";
            }
            DisplayName = TextManager.Get($"talenttree.{nameIdentifier}").Fallback(Identifier.Value);
            Type = subTreeElement.GetAttributeEnum("type", TalentTreeType.Specialization);
            RequiredTrees = subTreeElement.GetAttributeIdentifierImmutableHashSet("requires", ImmutableHashSet<Identifier>.Empty);
            BlockedTrees = subTreeElement.GetAttributeIdentifierImmutableHashSet("blocks", ImmutableHashSet<Identifier>.Empty);
            List<TalentOption> talentOptionStages = new List<TalentOption>();
            foreach (var talentOptionsElement in subTreeElement.GetChildElements("talentoptions"))
            {
                talentOptionStages.Add(new TalentOption(talentOptionsElement, Identifier));
            }

            TalentOptionStages = talentOptionStages.ToImmutableArray();
            AllTalentIdentifiers = TalentOptionStages.SelectMany(t => t.TalentIdentifiers).ToImmutableHashSet();
        }
    }

    internal readonly struct TalentOption
    {
        private readonly ImmutableHashSet<Identifier> talentIdentifiers;

        public IEnumerable<Identifier> TalentIdentifiers => talentIdentifiers;

        public readonly int MaxChosenTalents;

        /// <summary>
        /// When specified the talent option will show talent with this identifier
        /// and clicking on it will expand the talent option to show the talents
        /// </summary>
        public readonly Option<Identifier> ShowcaseTalent;

        public bool HasEnoughTalents(CharacterInfo character) => CountMatchingTalents(character.UnlockedTalents) >= MaxChosenTalents;
        public bool HasEnoughTalents(IReadOnlyCollection<Identifier> selectedTalents) => CountMatchingTalents(selectedTalents) >= MaxChosenTalents;

        // No LINQ
        public bool HasSelectedTalent(IReadOnlyCollection<Identifier> selectedTalents)
        {
            foreach (Identifier talent in selectedTalents)
            {
                if (talentIdentifiers.Contains(talent))
                {
                    return true;
                }
            }
            return false;
        }

        public int CountMatchingTalents(IReadOnlyCollection<Identifier> talents)
        {
            int i = 0;
            foreach (Identifier talent in talents)
            {
                if (talentIdentifiers.Contains(talent))
                {
                    i++;
                }
            }
            return i;
        }

        public TalentOption(ContentXElement talentOptionsElement, Identifier debugIdentifier)
        {
            MaxChosenTalents = talentOptionsElement.GetAttributeInt("maxchosentalents", 1);

            Identifier showcaseTalent = talentOptionsElement.GetAttributeIdentifier("showcasetalent", Identifier.Empty);
            ShowcaseTalent = !showcaseTalent.IsEmpty
                ? Option<Identifier>.Some(showcaseTalent)
                : Option<Identifier>.None();

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