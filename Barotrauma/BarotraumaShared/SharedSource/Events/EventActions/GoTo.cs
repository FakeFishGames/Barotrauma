using System.Xml.Linq;

namespace Barotrauma
{
    class GoTo : EventAction
    {
        [Serialize("", true)]
        public string Name { get; set; }

        public GoTo(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goTo)
        {
            goTo = Name;
            return true;
        }

        public override string ToDebugString()
        {
            return $"[-] Go to label \"{Name}\"";
        }

        public override void Reset() { }
    }
}