using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{

    /// <summary>
    /// Triggers a "conversation popup" with text and support for different branching options.
    /// </summary>
    public partial class ConversationAction : EventAction
    {
        public class OptionActionGroup : SubactionGroup
        {
            [Serialize("", IsPropertySaveable.Yes, description: "The text to display in the option.")]
            public string Text { get; set; }

            [Serialize(false, IsPropertySaveable.Yes, description: "Should this option end the conversation (closing the conversation prompt?). " +
                "By default, options that don't have any actions inside them, or that only have a GoTo action, end the conversation. " +
                "But if there are other actions inside the option, the game assumes there may be some kind of a follow-up coming to the conversation, " +
                "and by default leaves it open.")]
            public bool EndConversation { get; set; }

            [Serialize(false, IsPropertySaveable.Yes, description: $"If enabled, the player will send the {nameof(Text)} in chat when selecting the option, or if {nameof(ForceSayText)} is not empty, will send that instead.")]
            public bool ForceSay { get; set; }

            [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, the message sent in chat will be sent in radio chat instead.")]
            public bool ForceSayInRadio { get; set; }

            [Serialize("", IsPropertySaveable.Yes, description: $"Message sent in chat, if empty, {nameof(Text)} is used instead.")]
            public string ForceSayText { get; set; }

            [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message be stripped of any quotation mark characters?")]
            public bool ForceSayRemoveQuotes { get; set; }

            [Serialize(false, IsPropertySaveable.Yes, description: $"If enabled, the character will say the line even if it can't normally speak.")]
            public bool ForceSayEvenIfCantSpeak { get; set; }

            [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message be stripped of all text between square brackets (including the brackets themselves)?")]
            public bool ForceSayRemoveSquareBrackets { get; set; }

            [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message only be sent to other players (in singleplayer this would mean force say is disabled, in multiplayer everyone except the person interacting with the NPC would be able to see the messages)")]
            public bool ForceSayOnlyShowToOthers { get; set; }

            public ForceSayOptions ForceSayOptions { get; set; }

            public OptionActionGroup(ScriptedEvent scriptedEvent, ContentXElement element) : base(scriptedEvent, element)
            {
                if (ForceSayRemoveQuotes) { ForceSayOptions |= ForceSayOptions.RemoveQuotes; }
                if (ForceSayRemoveSquareBrackets) { ForceSayOptions |= ForceSayOptions.RemoveSquaresBrackets; }
            }
        }

        public enum DialogTypes
        {
            Regular,
            Small,
            Mission
        }
        
        const float InterruptDistance = 300.0f;

        /// <summary>
        /// Other events can't trigger conversations if some other event has triggered one within this time.
        /// Intended to prevent multiple events from triggering conversations at the same time.
        /// </summary>
        const float BlockOtherConversationsDuration = 5.0f;

        [Serialize("", IsPropertySaveable.Yes, description: "The text to display in the prompt. Can be the text as-is, or a tag referring to a line in a text file.")]
        public string Text { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: $"If enabled, the speaker will send the {nameof(Text)} in chat, or if {nameof(ForceSayText)} is not empty, will send that instead. Note: requires a valid SpeakerTag to be defined.")]
        public bool ForceSay { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, the message sent in chat by the speaker will be sent in radio chat instead.")]
        public bool ForceSayInRadio { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: $"Message sent in chat by the speaker, if empty, {nameof(Text)} is used instead.")]
        public string ForceSayText { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message be stripped of any quotation mark characters?")]
        public bool ForceSayRemoveQuotes { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: $"If enabled, the character will say the line even if it can't normally speak.")]
        public bool ForceSayEvenIfCantSpeak { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message be stripped of all text between square brackets (including the brackets themselves)?")]
        public bool ForceSayRemoveSquareBrackets { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the chat message only be sent to other players (in singleplayer this would mean force say is disabled, in multiplayer everyone except the person interacting with the NPC would be able to see the messages)")]
        public bool ForceSayOnlyShowToOthers { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character who's speaking. Makes a speech bubble icon appear above the character to indicate you can speak with them, and stops the character in place when the conversation triggers. Also allows the conversation to be interrupted if the speaker dies or becomes incapacitated mid-conversation.")]
        public Identifier SpeakerTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the player the conversation is shown to. If empty, the conversation is shown to everyone. If SpeakerTag is defined, the conversation is always only shown to the player who interacts with the speaker, and if TargetTag is then empty, that player is also the one checked for interruptions (death, incapacitation, distance etc).")]
        public Identifier TargetTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, "Should someone interact with the speaker for the conversation to trigger?")]
        public bool WaitForInteraction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, "Tag to assign to whoever invokes the conversation.")]
        public Identifier InvokerTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the screen fade to black when the conversation is active?")]
        public bool FadeToBlack { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, "Should the event end if the conversations is interrupted (e.g. if the speaker dies or falls unconscious mid-conversation). Defaults to true.")]
        public bool EndEventIfInterrupted { get; set; }

        [Serialize("_end", IsPropertySaveable.Yes, $"If {nameof(EndEventIfInterrupted)} is true, the label that the action jumps into when the event is interrupted. Jumps to the end of the event by default.")]
        public string InterruptedGotoLabel { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of an event sprite to display in the corner of the conversation prompt.")]
        public string EventSprite { get; set; }
        
        [Serialize(DialogTypes.Regular, IsPropertySaveable.Yes, description: "Type of the dialog prompt.")]
        public DialogTypes DialogType { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Does this conversation continue after this ConversationAction? If you have multiple successive ConversationActions, perhaps with some actions happening in between, you can enable this to prevent the dialog prompt from closing between the actions. Not necessary if the ConversationActions are nested inside each other: those are always considered parts of the same conversation, and shown in the same prompt.")]
        public bool ContinueConversation { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, the event will not stop to wait for the conversation to be dismissed.")]
        public bool ContinueAutomatically { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If SpeakerTag is defined, the conversation is interrupted by default if the speaker and the target end up too far from each other. This can be used to disable that behavior, keeping the dialog prompt open regardless of the distance.")]
        public bool IgnoreInterruptDistance { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "Priority of this conversation. If the speaker is assigned to a conversation with a lower priority that hasn't been opened yet, this conversation can take over (equal or higher priority ones, or already-open dialogs, will keep the current conversation). Defaults to 1, while outpost service conversations are explicitly set to 0, so other events will have priority over them by default.")]
        public int Priority { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Only relevant for conversations without any options, disables the close button when there's no option to prevent the player from being able to close the conversation.")]
        public bool DisableCloseButton { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Whether the conversation should only be considered interrupted after the dialogue has started (ignoring if the speaker dies before the conversation begins).")]
        public bool OnlyInterruptInDialogue { get; set; }

        public ForceSayOptions ForceSayOptions { get; set; }

        public override Mission ParentMission
        {
            get => base.ParentMission;
            set
            {
                foreach (var option in Options)
                {
                    option.Actions.ForEach(subAction => subAction.ParentMission = value);
                }
                base.ParentMission = value;
            }
        }

        public Character Speaker
        {
            get;
            private set;
        }

        /// <summary>
        /// A fallback speaking entity resolved from the <see cref="SpeakerTag"/> when no matching characters are found.
        /// </summary>
        /// <remarks>
        /// Ideally, this would not need to be stored separately. However, <see cref="Speaker"/> is used extensively throughout this class, 
        /// and refactoring it to support generic speaking entities would be unnecessarily complex.
        /// </remarks>
        private ISpeakable forceSaySpeakingEntity;

        private AIObjective prevIdleObjective, prevGotoObjective;
        private AIObjective npcFollowObjective;

        public List<OptionActionGroup> Options { get; private set; }

        public SubactionGroup Interrupted { get; private set; }

        private static UInt16 actionCount;

        //an identifier the server uses to identify which ConversationAction a client is responding to
        public readonly UInt16 Identifier;

        private float startDelay;

        private int selectedOption = -1;
        private bool dialogOpened = false;
        private bool openingFromSubConversation = false;

        private double lastActiveTime;

        private bool interrupt;

        /// <summary>
        /// The character who started the conversation with the speaker, also passed down to nested conversations.
        /// Used as the target when checking for interruptions if the conversation has no <see cref="TargetTag"/>.
        /// </summary>
        private Character invokingCharacter;

        private readonly XElement textElement;

        public ConversationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            actionCount++;
            Identifier = actionCount;
            Options = new List<OptionActionGroup>();
            foreach (var elem in element.Elements())
            {
                if (elem.Name.LocalName.Equals("option", StringComparison.OrdinalIgnoreCase))
                {
                    Options.Add(new OptionActionGroup(ParentEvent, elem));
                }
                else if (elem.Name.LocalName.Equals("interrupt", StringComparison.OrdinalIgnoreCase))
                {
                    Interrupted = new SubactionGroup(ParentEvent, elem);
                }
                else if (elem.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    Text = elem.GetAttributeString("tag", string.Empty);
                    textElement = elem;
                }
                else
                {
                    string thisName = nameof(ConversationAction);
                    DebugConsole.ThrowError(
                        $"Error in {thisName} in the event \"{parentEvent.Prefab.Identifier}\"" +
                        $" - unrecognized child element \"{elem.Name}\". If it's an action intended to execute after the {thisName}, " +
                        $"it should be after the {thisName}, not inside it.",
                        contentPackage: element.ContentPackage);
                }
            }

            if (ForceSayRemoveQuotes) { ForceSayOptions |= ForceSayOptions.RemoveQuotes; }
            if (ForceSayRemoveSquareBrackets) { ForceSayOptions |= ForceSayOptions.RemoveSquaresBrackets; }
        }

        public LocalizedString GetDisplayText()
        {
            LocalizedString text = string.Empty;

            if (textElement != null)
            {
                TextManager.ConstructDescription(ref text, textElement, ParentEvent.GetTextForReplacementElement);
            }
            else
            {
                text = TextManager.Get(Text).Fallback(Text);
                if (text.Value.IsNullOrEmpty())
                {
                    text = text.Fallback(Text);
                }
            }
            return ParentEvent.ReplaceVariablesInEventText(text);
        }

        public override IEnumerable<EventAction> GetSubActions()
        {
            return Options.SelectMany(group => group.Actions);
        }

        public override bool IsFinished(ref string goTo)
        {
            if (interrupt)
            {
                if (dialogOpened)
                {
#if CLIENT
                    dialogBox?.Close();
                    GUIMessageBox.MessageBoxes.ForEachMod(mb => 
                    { 
                        if (mb.UserData as string == "ConversationAction")
                        {
                            (mb as GUIMessageBox)?.Close();
                        }
                    });
#else
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        if (c.InGame && c.Character != null)
                        {
                            DebugConsole.Log($"Conversation {ParentEvent.Prefab.Identifier} finished, communicating to clients...");
                            ServerWrite(Speaker, c, interrupt);
                            // If the client is looking at a conversation prompt created by another action belonging to this same event, interrupt that too.
                            // Similar to how we interrupt whichever prompt is open in the #if CLIENT block above.
                            // Otherwise if the parent of the currently open conversation interrupts instead of this one, the prompt would get stuck.
                            if (lastActiveAction.TryGetValue(c, out ConversationAction activeAction) &&
                                activeAction != this && activeAction.ParentEvent != null && activeAction.ParentEvent == ParentEvent)
                            {
                                activeAction.ServerWrite(Speaker, c, interrupt);
                            }
                        }
                    }
#endif
                    ResetSpeaker();
                    dialogOpened = false;
                }

                if (Interrupted == null)
                {
                    if (EndEventIfInterrupted) { goTo = InterruptedGotoLabel; }
                    return true;
                }
                else
                {
                    return Interrupted.IsFinished(ref goTo);
                }
            }

            if (ContinueAutomatically && Options.None())
            {
                return dialogOpened;
            }

            if (selectedOption >= 0)
            {
                if (Options.None() || Options[selectedOption].IsFinished(ref goTo))
                {
                    ResetSpeaker();
                    return true;
                }
            }
            return false;
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            Options.ForEach(a => a.Reset(shouldEndConversations));
            ResetSpeaker();
            selectedOption = -1;
            interrupt = false;
            dialogOpened = false;
            Speaker = null;
            // Keep the invoker if the dialog stays open, otherwise we'll have no way of getting it back
            if (shouldEndConversations) { invokingCharacter = null; }
#if CLIENT
            if (shouldEndConversations)
            {
                dialogBox?.Close();
                dialogBox = null;
            }
#endif
        }

        /// <summary>
        /// Retriggers the conversation after the specified delay.
        /// </summary>
        public void RetriggerAfter(float delay)
        {
            startDelay = delay;
            dialogOpened = false;
            selectedOption = -1;
            invokingCharacter = null;
        }

        public override bool SetGoToTarget(string goTo)
        {
            selectedOption = -1;
            for (int i = 0; i < Options.Count; i++)
            {
                if (Options[i].SetGoToTarget(goTo))
                {
                    selectedOption = i;
                    interrupt = false;
                    dialogOpened = true;
                    return true;
                }
            }
            return false;
        }

        private void ResetSpeaker()
        {
            forceSaySpeakingEntity = null;
            if (Speaker == null) { return; }
            ClearTalkInteraction(Speaker);
            if (Speaker.AIController is HumanAIController humanAI && !Speaker.IsDead && !Speaker.Removed)
            {
                humanAI.ClearForcedOrder();
                if (prevIdleObjective != null) { humanAI.ObjectiveManager.AddObjective(prevIdleObjective); }
                if (prevGotoObjective != null && !prevGotoObjective.Abandon) { humanAI.ObjectiveManager.AddObjective(prevGotoObjective); }
                humanAI.ObjectiveManager.SortObjectives();
            }
        }

        private static void ClearTalkInteraction(Character speaker)
        {
            speaker.CampaignInteractionType = CampaignMode.InteractionType.None;
            speaker.ActiveConversation = null;
            speaker.SetCustomInteract(null, null);
#if SERVER
            if (!speaker.Removed)
            {
                GameMain.NetworkMember.CreateEntityEvent(speaker, new Character.AssignCampaignInteractionEventData());
            }
#endif
        }

        public int[] GetEndingOptions()
        {
            List<int> endings = Options
                .Where(group =>  
                    group.EndConversation || 
                    //no actions = safe to assume this must end the conversation
                    !group.Actions.Any() ||
                    //no follow-up conversation and a goto makes the event jump somewhere else
                    //we cannot easily determine whether that goto will lead to a follow-up conversation,
                    //so it's safest to close this conversation to prevent it from getting stuck (the potential follow-up will open a new one)
                    (group.Actions.None(a => a is ConversationAction) && group.Actions.Any(a => a is GoTo { EndConversation: true })))
                .Select(group => Options.IndexOf(group))
                .ToList();
            if (!ContinueConversation) { endings.Add(-1); }
            return endings.ToArray();
        }

        public override void Update(float deltaTime)
        {
            startDelay -= deltaTime;
            if (startDelay > 0) { return; }

            if (interrupt)
            {
                Interrupted?.Update(deltaTime);
            }
            else if (selectedOption < 0)
            {
                if ((dialogOpened || !OnlyInterruptInDialogue) && ShouldInterrupt(requireTarget: true))
                {
                    ResetSpeaker();
                    interrupt = true;
                    return;
                }

                if (dialogOpened)
                {
                    lastActiveTime = Timing.TotalTime;

#if CLIENT
                    if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "ConversationAction"))
                    {
                        Character.DisableControls = true;
                    }
                    else
                    {
                        Reset();
                    }
#endif
                    return;
                }

                if (!SpeakerTag.IsEmpty)
                {
                    if (npcFollowObjective != null)
                    {
                        npcFollowObjective.ForceHighestPriority = true;
                    }
                    if (Speaker != null && !Speaker.Removed && Speaker.CampaignInteractionType == CampaignMode.InteractionType.Talk && Speaker.ActiveConversation?.ParentEvent != this.ParentEvent)
                    {
                        //another conversation already active, lets see if it should block this one
                        if (SpeakerHasBlockingConversationActive(Speaker)) { return; }
                    }
                    Character newSpeaker = ParentEvent.GetTargets(SpeakerTag).FirstOrDefault(e => e is Character) as Character;
                    if (Speaker != null && Speaker != newSpeaker && Speaker.ActiveConversation == this)
                    {
                        // The speaker tag moved to someone else, so take the talk interaction off the previous speaker
                        ClearTalkInteraction(Speaker);
                    }
                    Speaker = newSpeaker;
                    if (Speaker == null || Speaker.Removed)
                    {
                        forceSaySpeakingEntity = ParentEvent.GetTargets(SpeakerTag).FirstOrDefault(e => e is ISpeakable) as ISpeakable;
                        if (forceSaySpeakingEntity != null)
                        {
                            TryStartConversation(Speaker);
                        }
                        return;
                    }
                    bool setupTalkInteraction = false; // Do we need to register this conversation as the speaker's active talk interaction?    
                    if (Speaker.CampaignInteractionType == CampaignMode.InteractionType.Talk && Speaker.ActiveConversation?.ParentEvent != this.ParentEvent)
                    {
                        //some conversation already assigned to the speaker, wait for it to be removed (unless this conversation has higher priority and the dialog hasn't been opened yet, in which case it can take over)
                        if (SpeakerHasBlockingConversationActive(Speaker)) { return; }
                        setupTalkInteraction = true;
                    }
                    else if (!WaitForInteraction || openingFromSubConversation)
                    {
                        TryStartConversation(Speaker);
                    }
                    else
                    {
                        setupTalkInteraction = true;
                    }
                    if (setupTalkInteraction && Speaker.ActiveConversation != this)
                    {
                        Speaker.CampaignInteractionType = CampaignMode.InteractionType.Talk;
                        Speaker.ActiveConversation = this;
#if CLIENT
                        Speaker.SetCustomInteract(
                            TryStartConversation,
                            TextManager.GetWithVariable("CampaignInteraction.Talk", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)));
#else
                        Speaker.SetCustomInteract(
                            TryStartConversation,
                            TextManager.Get("CampaignInteraction.Talk"));
                        GameMain.NetworkMember.CreateEntityEvent(Speaker, new Character.AssignCampaignInteractionEventData());
#endif
                    }
                    return;
                }
                else
                {
                    TryStartConversation(Speaker);
                }
            }
            else
            {
                //after the conversation has been finished and the target character assigned,
                //we no longer care if we still have a target
                if (ShouldInterrupt(requireTarget: false))
                {
                    ResetSpeaker();
                    interrupt = true;
                }
                else if (Options.Any())
                {
                    Options[selectedOption].Update(deltaTime);
                }
            }
        }

        /// <summary>
        /// Is the dialog of this action open and waiting for the target(s) to choose an option?
        /// </summary>
        private bool IsWaitingForResponse => dialogOpened && selectedOption < 0 && !interrupt && !ParentEvent.IsFinished;

        /// <summary>
        /// Does the character currently have a conversation prompt open? Only applies to player-controlled characters.
        /// </summary>
        public static bool IsConversationOpenFor(Character character)
        {
            if (character == null || character.Removed) { return false; }
#if CLIENT
            // Only the local player's prompts are known client-side (AI runs on the client only in SP)
            return character == Character.Controlled && IsDialogOpen;
#elif SERVER
            if (!character.IsRemotePlayer) { return false; }
            Client client = GameMain.Server?.ConnectedClients.Find(c => c.Character == character);
            if (client == null) { return false; }
            return lastActiveAction.TryGetValue(client, out ConversationAction action) && action.IsWaitingForResponse;
#else
            return false;
#endif
        }

        private bool SpeakerHasBlockingConversationActive(Character speaker)
        {
            //another conversation already active, lets see if it should block this one
            if (speaker.ActiveConversation?.dialogOpened == true) { return true; }
            //active conversation has a higher/same prio, we might need to block this one...
            if (Priority <= (speaker.ActiveConversation?.Priority ?? 0))
            {
                //...except if this one doesn't wait for interaction, i.e. it may be the continuation of some other conversation or something triggered based on distance
                //in that case, lets allow this one to take priority.
                if (WaitForInteraction)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ShouldInterrupt(bool requireTarget)
        {
            IEnumerable<Entity> targets = Enumerable.Empty<Entity>();
            if (requireTarget)
            {
                if (!TargetTag.IsEmpty)
                {
                    targets = ParentEvent.GetTargets(TargetTag).Where(e => IsValidTarget(e, requireTarget));
                    if (!targets.Any()) { return true; }
                }
                // Without a TargetTag, the dialog is only shown to the character who interacted with the speaker, so they're the de facto target
                else if (invokingCharacter != null)
                {
                    if (!IsValidTarget(invokingCharacter, requireTarget)) { return true; }
                    targets = invokingCharacter.ToEnumerable();
                }
            }

            if (Speaker != null)
            {
                if (targets.Any() && !IgnoreInterruptDistance)
                {
                    if (targets.All(t => Vector2.DistanceSquared(t.WorldPosition, Speaker.WorldPosition) > InterruptDistance * InterruptDistance)) { return true; }
                }
                if (Speaker.AIController is HumanAIController humanAI && !humanAI.AllowCampaignInteraction())
                {
                    return true;                    
                }
                return 
                    Speaker.Removed || 
                    Speaker.IsDead ||
                    // Don't interrupt yet if the speaker just falls unconscious before the conversation even starts, since they can still
                    // be revived and have the conversation be started with them
                    (dialogOpened && Speaker.IsIncapacitated);
            }

            return false;
        }

        private bool IsValidTarget(Entity e, bool requirePlayerControlled = true)
        {
            bool isValid = 
                e is Character character && !character.Removed && !character.IsDead && !character.IsIncapacitated &&
                (character == Character.Controlled || character.IsRemotePlayer || !requirePlayerControlled);
#if SERVER
            if (!dialogOpened)
            {
                UpdateIgnoredClients();
                isValid &= !ignoredClients.Keys.Any(c => c.Character == e);
            }
#elif CLIENT
            bool block = IsBlockedByMenu && !dialogOpened;
            isValid &= (e != Character.Controlled || !block);
#endif
            return isValid;
        }

        private void TryStartConversation(Character speaker, Character targetCharacter = null)
        {
            IEnumerable<Entity> targets = Enumerable.Empty<Entity>();
            if (!TargetTag.IsEmpty)
            {
                targets = ParentEvent.GetTargets(TargetTag).Where(e => IsValidTarget(e));
                if (!targets.Any() || IsBlockedByAnotherConversation(targets, BlockOtherConversationsDuration)) { return; }
                //some specific character tried to start the convo, but not included in the targets for this conversation -> disallow
                if (targetCharacter != null && !targets.Contains(targetCharacter)) { return; }
            }
            else 
            {
#if SERVER
                if (GameMain.NetworkMember != null)
                {
                    //conversation targeted to everyone, but no-one present yet who could potentially hear it -> don't start yet
                    UpdateIgnoredClients();
                    if (GameMain.NetworkMember.ConnectedClients.None(c => CanClientReceive(c))) { return; }
                }
#endif
                if (IsBlockedByAnotherConversation(targetCharacter?.ToEnumerable(), BlockOtherConversationsDuration)) { return; }
            }


            if (speaker?.AIController is HumanAIController humanAI)
            {
                prevIdleObjective = humanAI.ObjectiveManager.GetObjective<AIObjectiveIdle>();
                prevGotoObjective = humanAI.ObjectiveManager.GetObjective<AIObjectiveGoTo>();
                if (targets.Any() || targetCharacter != null) 
                {
                    Entity closestTarget = targetCharacter;
                    float closestDist = float.MaxValue;
                    foreach (Entity entity in targets)
                    {
                        float dist = Vector2.DistanceSquared(entity.WorldPosition, speaker.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestTarget = entity;
                            closestDist = dist;
                        }
                    }
                    if (closestTarget != null)
                    {
                        if (closestTarget is Character character)
                        {
                            npcFollowObjective = humanAI.SetForcedOrder(new Order(OrderPrefab.Prefabs["follow"], Barotrauma.Identifier.Empty, null, orderGiver: character));
                            npcFollowObjective.ForceHighestPriority = true;
                        }

                        humanAI.FaceTarget(closestTarget);
                    }
                }
            }

            // Nested conversations aren't started by interaction, so keep the invoker passed down from the parent
            if (targetCharacter != null) { invokingCharacter = targetCharacter; }
            if (targetCharacter != null && !InvokerTag.IsEmpty)
            {
                ParentEvent.RemoveTag(InvokerTag);
                ParentEvent.AddTarget(InvokerTag, targetCharacter);
            }

            if (ForceSay
#if CLIENT
                && !(ForceSayOnlyShowToOthers && !GameMain.DebugDraw)
#endif
                )
            {
                Client invokerClient = ForceSayOnlyShowToOthers ?
                    GameMain.NetworkMember?.ConnectedClients.FirstOrDefault(c => c.Character != null && (c.Character == invokingCharacter || targets.Contains(c.Character))) :
                    null;

                ISpeakable forceSaySpeaker = Speaker ?? forceSaySpeakingEntity;

                forceSaySpeaker?.ForceSay(
                    ForceSayText.IsNullOrEmpty() ? TextManager.Get(Text).Fallback(Text) : TextManager.Get(ForceSayText).Fallback(ForceSayText), 
                    sayInRadio: ForceSayInRadio,
                    ForceSayOptions,
                    ignoreSpeechImpediments: ForceSayEvenIfCantSpeak,
                    // Small delay so the speaking character doesn't talk at the same time as the player
                    delay: 0.7f,
                    ignoredClient: invokerClient);
            }

            ShowDialog(Speaker, targetCharacter);

            // Set the Speaker of the child conversation actions so they know which character is speaking
            Options.SelectMany(static op => op.Actions).OfType<ConversationAction>().ForEach(action =>
            {
                if (Speaker != null) { action.Speaker = speaker; }
                if (forceSaySpeakingEntity != null) { action.forceSaySpeakingEntity = forceSaySpeakingEntity; }
                action.invokingCharacter = invokingCharacter;

                // Prevents conversations inside options from waiting for interaction (which is not possible, since the conversation box is already open)
                action.openingFromSubConversation = true;
            });

            dialogOpened = true;
            if (Speaker != null)
            {
                Speaker = speaker;

                speaker.CampaignInteractionType = CampaignMode.InteractionType.None;
                speaker.SetCustomInteract(null, null);
#if SERVER
                GameMain.NetworkMember.CreateEntityEvent(speaker, new Character.AssignCampaignInteractionEventData());
#endif
            }
        }

        partial void ShowDialog(Character speaker, Character targetCharacter);

        public override string ToDebugString()
        {
            if (!interrupt)
            {
                return $"{ToolBox.GetDebugSymbol(selectedOption > -1, selectedOption < 0 && dialogOpened)} {nameof(ConversationAction)} -> (Selected option: {selectedOption.ColorizeObject()})";
            }
            else
            {
                return $"{ToolBox.GetDebugSymbol(true, selectedOption < 0 && dialogOpened)} {nameof(ConversationAction)} -> (Interrupted)";
            }
        }
    }
}