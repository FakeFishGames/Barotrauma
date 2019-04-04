using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override IEnumerable<object> UpdateState()
        {
            yield return new WaitForSeconds(4.0f);

            infoBox = CreateInfoFrame("Use WASD to move and the mouse to look around");

            yield return new WaitForSeconds(5.0f);

            infoBox = null;
        }
    }
}
