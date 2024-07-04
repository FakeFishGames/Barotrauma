using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Tags a specific entity. Tags are used by other actions to refer to specific entities. The tags are event-specific, i.e. you cannot use a tag that was added by another event to refer to an entity.
    /// </summary>
    class TagAction : EventAction
    {
        public enum SubType { Any = 0, Player = 1, Outpost = 2, Wreck = 4, BeaconStation = 8 }

        [Serialize("", IsPropertySaveable.Yes, description: "What criteria to use to select the entities to target. Valid values are players, player, traitor, nontraitor, nontraitorplayer, bot, crew, humanprefabidentifier:[id], jobidentifier:[id], structureidentifier:[id], structurespecialtag:[tag], itemidentifier:[id], itemtag:[tag], hull, hullname:[name], submarine:[type], eventtag:[tag].")]
        public string Criteria { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The tag to apply to the target.")]
        public Identifier Tag { get; set; }

        [Serialize(SubType.Any, IsPropertySaveable.Yes, description: "The type of submarine the target needs to be in.")]
        public SubType SubmarineType { get; set; }

        [Serialize("", IsPropertySaveable.Yes, "If set, the target must be in an outpost module that has this tag.")]
        public Identifier RequiredModuleTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should incapacitated (e.g. dead, paralyzed, unconscious) characters be ignored, i.e. not considered valid targets?")]
        public bool IgnoreIncapacitatedCharacters { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Can items that have been set to be hidden in-game be tagged?")]
        public bool AllowHiddenItems { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If there are multiple matching targets, should all of them be tagged or one chosen randomly?")]
        public bool ChooseRandom { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the event continue if the TagAction can't find any valid targets?")]
        public bool ContinueIfNoTargetsFound { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "If larger than 0, the specified percentage of the matching targets are tagged. Between 0-100.")]
        public float ChoosePercentage { get; set; }

        private bool isFinished = false;

        /// <summary>
        /// If the action tags some entities directly (not trying to find targets on the fly), 
        /// we may be able to determine that targets can not be found even if we'd recheck
        /// </summary>
        private bool cantFindTargets = false;

        /// <summary>
        /// If the TagAction adds a target predicate (a criteria that keeps finding targets on the fly),
        /// we must keep checking if targets have been found to determine if the action can continue or not
        /// </summary>
        private bool mustRecheckTargets = false;

        private bool taggingDone = false;

        public TagAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            Taggers = new (string k, Action<Identifier> v)[]
            {
                ("players", v => TagPlayers()),
                ("player", v => TagPlayers()),
                ("traitor", v => TagTraitors()),
                ("nontraitor", v => TagNonTraitors()),
                ("nontraitorplayer", v => TagNonTraitorPlayers()),
                ("bot", v => TagBots(playerCrewOnly: false)),
                ("crew", v => TagCrew()),
                ("humanprefabidentifier", TagHumansByIdentifier),
                ("humanprefabtag", TagHumansByTag),
                ("jobidentifier", TagHumansByJobIdentifier),
                ("structureidentifier", TagStructuresByIdentifier),
                ("structurespecialtag", TagStructuresBySpecialTag),
                ("itemidentifier", TagItemsByIdentifier),
                ("itemtag", TagItemsByTag),
                ("hull", v => TagHulls()),
                ("hullname", TagHullsByName),
                ("submarine", TagSubmarinesByType),
                ("eventtag", TagByEventTag),
            }.Select(t => (t.k.ToIdentifier(), t.v)).ToImmutableDictionary();
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        private void TagByEventTag(Identifier eventTag)
        {
            AddTarget(Tag, ParentEvent.GetTargets(eventTag).Where(t => MatchesRequirements(t)));
        }

        private void TagPlayers()
        {
            AddTargetPredicate(
                Tag, 
                ScriptedEvent.TargetPredicate.EntityType.Character, 
                e => e is Character c && c.IsPlayer && (!c.IsIncapacitated || !IgnoreIncapacitatedCharacters));
        }

        private void TagTraitors()
        {
            AddTargetPredicate(
                Tags.Traitor,
                ScriptedEvent.TargetPredicate.EntityType.Character, 
                e => e is Character c && (c.IsPlayer || c.IsBot) && c.IsTraitor && !c.IsIncapacitated);
        }

        private void TagNonTraitors()
        {
            AddTargetPredicate(
                Tags.NonTraitor,
                ScriptedEvent.TargetPredicate.EntityType.Character,
                e => e is Character c && (c.IsPlayer || c.IsBot) && !c.IsTraitor && c.IsOnPlayerTeam && !c.IsIncapacitated);
        }

        private void TagNonTraitorPlayers()
        {
            AddTargetPredicate(
                Tags.NonTraitorPlayer,
                ScriptedEvent.TargetPredicate.EntityType.Character,
                e => e is Character c && c.IsPlayer && !c.IsTraitor && c.IsOnPlayerTeam && !c.IsIncapacitated);
        }

        private void TagBots(bool playerCrewOnly)
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Character,
                e => 
                    e is Character c &&
                    c.IsBot && 
                    (!c.IsIncapacitated || !IgnoreIncapacitatedCharacters) && 
                    (!playerCrewOnly || c.TeamID == CharacterTeamType.Team1));
        }

        private void TagCrew()
        {
#if CLIENT
            AddTarget(Tag, GameMain.GameSession.CrewManager.GetCharacters());
#else
            TagPlayers(); 
            TagBots(playerCrewOnly: true);
#endif
        }

        private void TagHumansByIdentifier(Identifier identifier)
        {
            AddTarget(Tag, Character.CharacterList.Where(c => c.HumanPrefab?.Identifier == identifier));
        }

        private void TagHumansByTag(Identifier tag)
        {
            AddTarget(Tag, Character.CharacterList.Where(c => c.HumanPrefab != null && c.HumanPrefab.GetTags().Contains(tag)));
        }

        private void TagHumansByJobIdentifier(Identifier jobIdentifier)
        {
            AddTarget(Tag, Character.CharacterList.Where(c => c.HasJob(jobIdentifier)));
        }

        private void TagStructuresByIdentifier(Identifier identifier)
        {
            AddTargetPredicate(
                Tag, 
                ScriptedEvent.TargetPredicate.EntityType.Structure,
                e => e is Structure s && MatchesRequirements(s) && s.Prefab.Identifier == identifier);
        }

        private void TagStructuresBySpecialTag(Identifier tag)
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Structure,
                e => e is Structure s && MatchesRequirements(s) && s.SpecialTag.ToIdentifier() == tag);
        }

        private void TagItemsByIdentifier(Identifier identifier)
        {
            AddTargetPredicate(
                Tag, 
                ScriptedEvent.TargetPredicate.EntityType.Item,
                e => e is Item it && it.Prefab.Identifier == identifier && IsValidItem(it));
        }

        private void TagItemsByTag(Identifier tag)
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Item,
                e => e is Item it && it.HasTag(tag) && IsValidItem(it));
        }

        private void TagHulls()
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Hull,
                 e => e is Hull h && MatchesRequirements(h));
        }

        private void TagHullsByName(Identifier name)
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Hull,
                e => e is Hull h && MatchesRequirements(h) && h.RoomName.Contains(name.Value, StringComparison.OrdinalIgnoreCase));
        }

        private void TagSubmarinesByType(Identifier type)
        {
            AddTargetPredicate(
                Tag,
                ScriptedEvent.TargetPredicate.EntityType.Submarine,
                e => e is Submarine s && MatchesRequirements(s) && (type.IsEmpty || type == s.Info?.Type.ToIdentifier()));
        }

        private bool IsValidItem(Item it)
        {
            return 
                !it.IsLayerHidden && /*items in hidden layers are treated as if they didn't exist, regardless if hidden items should be allowed*/
                (!it.HiddenInGame || AllowHiddenItems) && 
                ModuleTagMatches(it) && 
                //if the item has just spawned, it may be in a hull but not moved into the coordinate space of the hull yet
                //= it.Submarine still null
                SubmarineTypeMatches(it.Submarine ?? it.CurrentHull?.Submarine ?? it.ParentInventory?.Owner?.Submarine);
        }

        private bool MatchesRequirements(Entity e)
        {
            return ModuleTagMatches(e) && SubmarineTypeMatches(e.Submarine);
        }

        private bool ModuleTagMatches(Entity e)
        {
            if (RequiredModuleTag.IsEmpty) { return true; }
            if (e?.Submarine == null) { return false; }

            Hull hull;
            if (e is Character character)
            {
                hull = character.CurrentHull;
            }
            else if (e is Item item)
            {
                hull = item.CurrentHull;
            }
            else if (e is WayPoint wp)
            {
                hull = wp.CurrentHull;
            }
            else if (e is Hull h)
            {
                hull = h;
            }
            else
            {
                DebugConsole.AddWarning($"Potential error in event \"{ParentEvent.Prefab.Identifier}\": {nameof(TagAction)} cannot check the module tags of an entity of the type {e.GetType()}.");
                return false;
            }

            return hull != null && hull.OutpostModuleTags.Contains(RequiredModuleTag);
        }


        private bool SubmarineTypeMatches(Submarine sub)
        {
            return SubmarineTypeMatches(sub, SubmarineType);
        }

        public static bool SubmarineTypeMatches(Submarine sub, SubType submarineType)
        {
            if (submarineType == SubType.Any) { return true; }
            if (sub == null) { return false; }
            switch (sub.Info.Type)
            {
                case Barotrauma.SubmarineType.Player:
                    return submarineType.HasFlag(SubType.Player) && sub != GameMain.NetworkMember?.RespawnManager?.RespawnShuttle;
                case Barotrauma.SubmarineType.Outpost:
                case Barotrauma.SubmarineType.OutpostModule:
                    return submarineType.HasFlag(SubType.Outpost);
                case Barotrauma.SubmarineType.Wreck:
                    return submarineType.HasFlag(SubType.Wreck);
                case Barotrauma.SubmarineType.BeaconStation:
                    return submarineType.HasFlag(SubType.BeaconStation);
                default:
                    return false;
            }
        }

        private void AddTargetPredicate(Identifier tag, ScriptedEvent.TargetPredicate.EntityType entityType, Predicate<Entity> predicate)
        {
            if (ChoosePercentage > 0.0f)
            {
                TagPercentage(tag, Entity.GetEntities().Where(e => predicate(e)));
            }
            else if (ChooseRandom)
            {
                TagRandom(tag, Entity.GetEntities().Where(e => predicate(e)));
            }
            else
            {
                ParentEvent.AddTargetPredicate(tag, entityType, predicate);
                mustRecheckTargets = true;
            }
        }

        private void AddTarget(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None())
            {
                cantFindTargets = true;
                return;
            }
            if (ChoosePercentage > 0.0f)
            {
                TagPercentage(tag, entities);
            }
            else if (ChooseRandom)
            {
                TagRandom(tag, entities);
            }
            else
            {
                foreach (var entity in entities)
                {
                    ParentEvent.AddTarget(tag, entity);
                }
            }
        }

        private List<Entity> tempEntities;
        private void TagPercentage(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None())
            {
                cantFindTargets = true;
                return;
            }

            int amountToChoose = (int)Math.Ceiling(entities.Count() * (ChoosePercentage / 100.0f));

            tempEntities ??= new List<Entity>();
            tempEntities.Clear();
            for (int i = 0; i < amountToChoose; i++)
            {
                var entity = entities.GetRandomUnsynced();
                tempEntities.Remove(entity);
                ParentEvent.AddTarget(tag, entity);
            }
        }

        private void TagRandom(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None()) 
            {
                cantFindTargets = true;
                return; 
            }
            ParentEvent.AddTarget(tag, entities.GetRandomUnsynced());
        }

        private readonly ImmutableDictionary<Identifier, Action<Identifier>> Taggers;
        
        public override void Update(float deltaTime)
        {
            if (isFinished || cantFindTargets) { return; }

            if (!taggingDone)
            {
                cantFindTargets = false;
                string[] criteriaSplit = Criteria.Split(';');
                foreach (string entry in criteriaSplit)
                {
                    string[] kvp = entry.Split(':');
                    Identifier key = kvp[0].Trim().ToIdentifier();
                    Identifier value = kvp.Length > 1 ? kvp[1].Trim().ToIdentifier() : Identifier.Empty;
                    if (Taggers.TryGetValue(key, out Action<Identifier> tagger))
                    {
                        tagger(value);
                    }
                    else
                    {
                        string errorMessage = $"Error in TagAction (event \"{ParentEvent.Prefab.Identifier}\") - unrecognized target criteria \"{key}\".";
                        DebugConsole.ThrowError(errorMessage, 
                            contentPackage: ParentEvent.Prefab?.ContentPackage);
                        GameAnalyticsManager.AddErrorEventOnce($"TagAction.Update:InvalidCriteria_{ParentEvent.Prefab.Identifier}_{key}", GameAnalyticsManager.ErrorSeverity.Error, errorMessage);
                    }
                }
                taggingDone = true;
            }

            if (ContinueIfNoTargetsFound)
            {
                isFinished = true;
            }
            else
            {
                if (mustRecheckTargets)
                {
                    isFinished = ParentEvent.GetTargets(Tag).Any();
                }
                else
                {
                    isFinished = !cantFindTargets;
                }
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(TagAction)} -> (Criteria: {Criteria.ColorizeObject()}, Tag: {Tag.ColorizeObject()}, Sub: {SubmarineType.ColorizeObject()})";
        }
    }
}