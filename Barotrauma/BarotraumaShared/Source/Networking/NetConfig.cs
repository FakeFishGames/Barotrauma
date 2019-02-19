namespace Barotrauma.Networking
{
    static class NetConfig
    {
        public const int DefaultPort = 27015;
        public const int DefaultQueryPort = 27016;

        public const int MaxPlayers = 16;

        public static string MasterServerUrl = GameMain.Config.MasterServerUrl;

        //if a Character is further than this from the sub and the players, the server will disable it
        //(in display units)
        public const float DisableCharacterDist = 22000.0f;
        public const float DisableCharacterDistSqr = DisableCharacterDist * DisableCharacterDist;

        //the character needs to get this close to be re-enabled
        public const float EnableCharacterDist = 20000.0f;
        public const float EnableCharacterDistSqr = EnableCharacterDist * EnableCharacterDist;

        public const float MaxPhysicsBodyVelocity = 64.0f;
        public const float MaxPhysicsBodyAngularVelocity = 16.0f;

        //how much the physics body of an item has to move until the server 
        //send a position update to clients (in sim units)
        public const float ItemPosUpdateDistance = 2.0f;
        
        public const float DeleteDisconnectedTime = 10.0f;        
    }
}
