using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Xml.Linq;
namespace HomeAndHarbor;

public sealed partial class LaunchButton : ItemComponent, IClientSerializable, IServerSerializable
{
    [Serialize("", IsPropertySaveable.No)]
    public string ButtonText { get; private set; } = "";

    private bool lidOpen;

    public LaunchButton(Item item, ContentXElement element) : base(item, element)
    {
        IsActive = false;

        InitProjectSpecific(element);
#if CLIENT
        CreateGUI();
#endif
    }

    partial void InitProjectSpecific(ContentXElement element);

    public void Launch(Character character)
    {
        character.SelectedItem = null;
        lidOpen = true;
        IsActive = true;
#if SERVER
        Item.CreateServerEvent(this);
#endif
    }

#if SERVER
    public void ServerEventRead(IReadMessage msg, Client c)
    {
        bool launch = msg.ReadBoolean();
        if (c.Character != null && c.Character.CanInteractWith(Item))
        {
            if (launch)
            {
                Launch(c.Character);
            }
            else if (!lidOpen)
            {
                lidOpen = true;
                Item.CreateServerEvent(this);
            }
        }
    }

    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(lidOpen);
        msg.WriteBoolean(IsActive);
    }
#endif
}
