namespace Barotrauma
{
    class GoTo : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public string Name { get; set; }

        public GoTo(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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