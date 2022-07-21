using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Server;

namespace Barotrauma
{
    partial class Traitor
    {
        public abstract class Goal
        {
            public HashSet<Traitor> Traitors { get; } = new HashSet<Traitor>();
            public TraitorMission Mission { get; internal set; }

            public virtual string StatusTextId { get; set; } = "TraitorGoalStatusTextFormat";

            public virtual string InfoTextId { get; set; } = null;

            public virtual string CompletedTextId { get; set; } = null;

            public virtual string StatusValueTextId => IsCompleted ? "complete" : "inprogress";

            public virtual IEnumerable<string> StatusTextKeys => new [] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues(Traitor traitor) => new [] { InfoText(traitor), TextManager.FormatServerMessage(StatusValueTextId) };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues(Traitor traitor) => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues(Traitor traitor) => new string[] { };

            protected virtual string FormatText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values)
                => TextManager.FormatServerMessageWithPronouns(traitor.Character.Info, textId, keys.Zip(values, (k,v) => (k,v)).ToArray());

            protected internal virtual string GetStatusText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);
            protected internal virtual string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);
            protected internal virtual string GetCompletedText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);

            public virtual string StatusText(Traitor traitor) => GetStatusText(traitor, StatusTextId, StatusTextKeys, StatusTextValues(traitor));
            public virtual string InfoText(Traitor traitor) => GetInfoText(traitor, InfoTextId, InfoTextKeys, InfoTextValues(traitor));

            public virtual string CompletedText(Traitor traitor) => CompletedTextId != null ? GetCompletedText(traitor, CompletedTextId, CompletedTextKeys, CompletedTextValues(traitor)) : StatusText(traitor);

            public abstract bool IsCompleted { get; }
            public virtual bool IsStarted(Traitor traitor) => Traitors.Contains(traitor);
            public virtual bool CanBeCompleted(ICollection<Traitor> traitors) => !Traitors.Any(traitor => traitor.Character?.IsDead ?? true);
            public virtual bool IsEnemy(Character character) => false;
            public virtual bool IsAllowedToDamage(Structure structure) => false;
            public virtual bool Start(Traitor traitor)
            {
                Traitors.Add(traitor);
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
