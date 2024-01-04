namespace Barotrauma
{
    class GoTo : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public string Name { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxTimes { get; set; }

        private int counter;

        public GoTo(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goTo)
        {
            if (counter < MaxTimes || MaxTimes <= 0)
            {
                goTo = Name;
                counter++;
            }
            return true;
        }

        public override string ToDebugString()
        {
            string msg = $"[-] Go to label \"{Name}\"";
            if (MaxTimes > 0)
            {
                msg += $" ({counter}/{MaxTimes})";
            }
            return msg;
        }

        public override void Reset() { }
    }
}