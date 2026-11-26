using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Xml.Linq;
namespace HomeAndHarbor;

public sealed partial class OutpostManagementComponent : ItemComponent
{
    public OutpostManagementComponent(Item item, ContentXElement element) : base(item, element)
    {
    }

    public override bool Select(Character character)
    {
        if (character == Character.Controlled)
        {
#if CLIENT
            OutpostManagementUI.Instance?.Open(OutpostManagementUI.Tab.Building);
#endif
            return true;
        }

        return false;
    }
}
