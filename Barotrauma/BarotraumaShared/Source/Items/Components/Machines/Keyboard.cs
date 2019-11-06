using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent
    {
        List<string> history;
        int maxHistoryCount = 10;

        public Keyboard(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            history = new List<string>();
            for (int i = 0; i < maxHistoryCount; i++) { history.Add(null); }
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, history[0], "signal_out", null);
        }
    }
}