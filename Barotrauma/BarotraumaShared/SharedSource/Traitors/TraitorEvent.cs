#nullable enable
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class TraitorEvent : ScriptedEvent
    {
        public enum State
        {
            Incomplete,
            Completed,
            Failed
        }

        public Action? OnStateChanged;

        private new readonly TraitorEventPrefab prefab;

        public new TraitorEventPrefab Prefab => prefab;

        private LocalizedString codeWord;

        private State currentState;
        public State CurrentState 
        {
            get { return currentState; }
            set
            {
                if (currentState == value) { return; }
                currentState = value;
                OnStateChanged?.Invoke();
            }
        }

        private Client? traitor;

        public Client? Traitor => traitor;

        private readonly HashSet<Client> secondaryTraitors = new HashSet<Client>();

        public IEnumerable<Client> SecondaryTraitors => secondaryTraitors;

        public override string ToString()
        {
            return $"{nameof(TraitorEvent)} ({prefab.Identifier})";
        }

        private readonly static HashSet<Identifier> nonActionChildElementNames = new HashSet<Identifier>()
        {
            "icon".ToIdentifier(),
            "reputationrequirement".ToIdentifier(),
            "missionrequirement".ToIdentifier(),
            "levelrequirement".ToIdentifier()
        };
        protected override IEnumerable<Identifier> NonActionChildElementNames => nonActionChildElementNames;

        public TraitorEvent(TraitorEventPrefab prefab, int seed) : base(prefab, seed)
        {
            this.prefab = prefab;
            codeWord = string.Empty;
        }

        protected override void InitEventSpecific(EventSet? parentSet = null)
        {
            if (traitor == null)
            {
                DebugConsole.ThrowError($"Error when initializing event \"{prefab.Identifier}\": traitor not set.\n" + Environment.StackTrace);
            }
        }

        public override LocalizedString ReplaceVariablesInEventText(LocalizedString str)
        {
            if (codeWord.IsNullOrEmpty())
            {
                //store the code word so the same random word is used in all the actions in the event
                codeWord = TextManager.Get("traitor.codeword");
            }

            return str
                .Replace("[traitor]", traitor?.Name ?? "none")
                .Replace("[target]", (GetTargets("target".ToIdentifier()).FirstOrDefault() as Character)?.DisplayName ?? "none")
                .Replace("[codeword]", codeWord.Value);
        }

        public void SetTraitor(Client traitor)
        {
            if (traitor.Character == null)
            {
                throw new InvalidOperationException($"Tried to set a client who's not controlling a character (\"{traitor.Name}\") as the traitor.");
            }
            this.traitor = traitor;
            traitor.Character.IsTraitor = true;
            AddTarget(Tags.Traitor, traitor.Character);
            AddTarget(Tags.AnyTraitor, traitor.Character);
            AddTargetPredicate(Tags.NonTraitor, TargetPredicate.EntityType.Character, e => e is Character c && (c.IsPlayer || c.IsBot) && !c.IsTraitor && c.TeamID == traitor.TeamID && !c.IsIncapacitated);
            AddTargetPredicate(Tags.NonTraitorPlayer, TargetPredicate.EntityType.Character, e => e is Character c && c.IsPlayer && !c.IsTraitor && c.IsOnPlayerTeam && !c.IsIncapacitated);
        }

        public void SetSecondaryTraitors(IEnumerable<Client> traitors)
        {
            int index = 0;
            foreach (var traitor in traitors)
            {
                if (traitor.Character == null)
                {
                    throw new InvalidOperationException($"Tried to set a client who's not controlling a character (\"{traitor.Name}\") as a secondary traitor.");
                }
                if (this.traitor == traitor)
                {
                    DebugConsole.ThrowError($"Tried to assign the main traitor {traitor.Name} as a secondary traitor.");
                    continue;
                }
                secondaryTraitors.Add(traitor);
                traitor.Character.IsTraitor = true;
                AddTarget(Tags.SecondaryTraitor, traitor.Character);
                AddTarget((Tags.SecondaryTraitor.ToString() + index).ToIdentifier(), traitor.Character);
                AddTarget(Tags.AnyTraitor, traitor.Character);
                index++;
            }
        }
    }
}
