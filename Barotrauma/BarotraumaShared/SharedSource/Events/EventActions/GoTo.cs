namespace Barotrauma
{
    /// <summary>
    /// Makes the event jump to a <see cref="Label"/> somewhere else in the event.
    /// </summary>
    class GoTo : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Name of the label to jump to.")]
        public string Name { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "How many times can this GoTo action be repeated? Can be used to make some parts of an event repeat a limited number of times. If negative or zero, there's no limit.")]
        public int MaxTimes { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "By default, jumping to another part in the event closes the active conversation prompt. Use this if if you want to keep it open instead.")]
        public bool EndConversation { get; set; }

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