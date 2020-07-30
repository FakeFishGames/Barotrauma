using System.Xml.Linq;

namespace Barotrauma
{
    class Label : EventAction
    {
        [Serialize("", true)]
        public string Name { get; set; }

        public Label(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goTo)
        {
            return true;
        }

        public override bool SetGoToTarget(string goTo)
        {
            return goTo.Equals(Name, System.StringComparison.InvariantCultureIgnoreCase);
        }

        public override string ToDebugString()
        {
            return $"[-] Label \"{Name}\"";
        }

        public override void Reset() { }
    }
}