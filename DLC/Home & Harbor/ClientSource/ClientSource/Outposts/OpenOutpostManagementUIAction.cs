namespace HomeAndHarbor;

internal partial class OpenOutpostManagementUIAction
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, OpenOutpostManagementUIData>(
            Plugin.NetworkHeaders.OpenOutpostManagementUI,
            ClientRead);
    }

    private static void ClientRead(OpenOutpostManagementUIData data)
    {
        // Open the outpost management UI
        if (OutpostManagementUI.Instance != null)
        {
            OutpostManagementUI.Instance.Open(OutpostManagementUI.Tab.Building);
        }
    }
}
