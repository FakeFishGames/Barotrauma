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

            public virtual string StatusValueTextId => IsCompleted ? "complete" : "inprogress";

            public virtual IEnumerable<string> StatusTextKeys => new string[] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues => new string[] { InfoText, TextManager.FormatServerMessage(StatusValueTextId) };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues => new string[] { };

            public virtual string StatusText => TextManager.FormatServerMessageWithGenderPronouns(Traitor.Character.Info.Gender, StatusTextId, StatusTextKeys, StatusTextValues);
            public virtual string InfoText => TextManager.FormatServerMessageWithGenderPronouns(Traitor.Character.Info.Gender, InfoTextId, InfoTextKeys, InfoTextValues);
            public virtual string CompletedText => CompletedTextId != null ? TextManager.FormatServerMessageWithGenderPronouns(Traitor.Character.Info.Gender, CompletedTextId, CompletedTextKeys, CompletedTextValues) : StatusText;

            public abstract bool IsCompleted { get; }
            public virtual bool IsStarted => Traitor != null;
            public virtual bool CanBeCompleted => !(Traitor?.Character?.IsDead ?? true);

            public virtual bool IsEnemy(Character character) => false;

            public virtual bool Start(Traitor traitor)
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
