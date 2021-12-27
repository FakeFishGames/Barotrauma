using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class EditorTutorial : Tutorial
    {
        public EditorTutorial(XElement element)
            : base (element)
        {
        }

        public override IEnumerable<CoroutineStatus> UpdateState()
        {
            /*infoBox = CreateInfoFrame("Use the mouse wheel to zoom in and out, and WASD to move the camera around.", true);

            while (infoBox != null)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Press \"Structure\" at the left side of the screen to start placing some walls.");

            while (GameMain.SubEditorScreen.SelectedTab != (int)MapEntityCategory.Structure)
            {
                yield return CoroutineStatus.Running;
            }
            
            infoBox = CreateInfoFrame("Select \"topwall\" from the list.", true);

            while (MapEntityPrefab.Selected == null || MapEntityPrefab.Selected.Name != "topwall")
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("You can now create a horizontal wall by clicking and dragging. When you're done, right click to stop creating walls.");*/




            yield return CoroutineStatus.Success;
        }
    }
}
