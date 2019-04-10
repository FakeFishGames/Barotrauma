using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class EngineerTutorial : ScenarioTutorial
    {
        public EngineerTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();
        }

        public override IEnumerable<object> UpdateState()
        {
            yield return null;
        }
    }
}
