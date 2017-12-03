namespace Barotrauma.Networking
{
    static class NetConfig
    {
        public const int DefaultPort = 14242;

        //UpdateEntity networkevents aren't sent to clients if they're further than this from the entity
        public const float UpdateEntityDistance = 2500.0f;

        public const int MaxPlayers = 16;

        public static string MasterServerUrl = GameMain.Config.MasterServerUrl;

        //if a Character is further than this from the sub, the server will ignore it
        //(in display units)
        public static float CharacterIgnoreDistance = 20000.0f;
        public static float CharacterIgnoreDistanceSqr = CharacterIgnoreDistance * CharacterIgnoreDistance;

        //how much the physics body of an item has to move until the server 
        //send a position update to clients (in sim units)
        public static float ItemPosUpdateDistance = 2.0f;
        
        public const float LargeCharacterUpdateInterval = 5.0f;

        public const float DeleteDisconnectedTime = 10.0f;

        public const float IdSendInterval = 0.2f;
        public const float RerequestInterval = 0.2f;

        public const int ReliableMessageBufferSize = 500;
        public const int ResendAttempts = 10;
    }
}
