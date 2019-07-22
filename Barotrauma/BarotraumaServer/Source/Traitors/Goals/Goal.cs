using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public abstract class Goal
        {
            public Traitor Traitor { get; private set; }
            public TraitorMission Mission { get; internal set; }

            private string statusTextId = "TraitorGoalStatusTextFormat";
            public virtual string StatusTextId { get => statusTextId; set { statusTextId = value; } }

            private string infoTextId = null;
            public virtual string InfoTextId { get => infoTextId; set { infoTextId = value; } }

            private string completedTextId = null;
            public virtual string CompletedTextId { get => completedTextId; set { completedTextId = value; } }

            public virtual IEnumerable<string> StatusTextKeys => new string[] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues => new string[] { InfoText, TextManager.Get(IsCompleted ? "done" : "pending") };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues => new string[] { };

            public virtual string StatusText => TextManager.GetWithVariables(StatusTextId, StatusTextKeys.ToArray(), StatusTextValues.ToArray());
            public virtual string InfoText => TextManager.GetWithVariables(InfoTextId, InfoTextKeys.ToArray(), InfoTextValues.ToArray());
            public virtual string CompletedText => CompletedTextId != null ? TextManager.GetWithVariables(CompletedTextId, CompletedTextKeys.ToArray(), CompletedTextValues.ToArray()) : StatusText;

            public abstract bool IsCompleted { get; }
            public virtual bool IsStarted => Traitor != null;

            public virtual bool Start(GameServer server, Traitor traitor)
            {
                Traitor = traitor;
                return true;
            }

            public virtual void Update(float deltaTime)
            {
            }

            protected Goal()
            {
            }
        }
    }
}
