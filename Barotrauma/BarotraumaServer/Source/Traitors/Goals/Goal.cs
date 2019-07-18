using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class Goal
        {
            public Traitor Traitor { get; private set; }
            public TraitorMission Mission { get; internal set; }

            public virtual string StatusTextId { get; set; } = "TraitorGoalStatusTextFormat";
            public virtual string InfoTextId { get; set; } = null;
            public virtual string CompletedTextId { get; set; } = null;

            public virtual IEnumerable<string> StatusTextKeys => new string[] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues => new string[] {
               InfoText,
               TextManager.Get(IsCompleted ? "done" : "pending")
            };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues => new string[] { };

            public virtual string StatusText => TextManager.GetWithVariables(StatusTextId, StatusTextKeys.ToArray(), StatusTextValues.ToArray());
            public virtual string InfoText => TextManager.GetWithVariables(InfoTextId, InfoTextKeys.ToArray(), InfoTextValues.ToArray());
            public virtual string CompletedText => CompletedTextId != null ? TextManager.GetWithVariables(CompletedTextId, CompletedTextKeys.ToArray(), CompletedTextValues.ToArray()) : StatusText;

            public virtual bool IsCompleted => false;

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
