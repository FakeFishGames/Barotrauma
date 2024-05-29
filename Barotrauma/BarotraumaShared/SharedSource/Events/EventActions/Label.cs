namespace Barotrauma
{
    /// <summary>
    /// Defines a point in the event that <see cref="GoTo"/> actions can jump to.
    /// </summary>
    class Label : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public string Name { get; set; }

        public Label(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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