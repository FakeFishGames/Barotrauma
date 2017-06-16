using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Barotrauma.Networking;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GameMain
    {
        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static World World;
        public static GameSettings Config;

        public static GameServer Server;
        public const GameClient Client = null;
        public static NetworkMember NetworkMember
        {
            get { return Server as NetworkMember; }
        }

        public static GameSession GameSession;

        public static GameMain Instance
        {
            get;
            private set;
        }

        //only screens the server implements
        public static GameScreen GameScreen;
        public static NetLobbyScreen NetLobbyScreen;

        //null screens because they are not implemented by the server,
        //but they're checked for all over the place
        //TODO: maybe clean up instead of having these constants
        public const Screen MainMenuScreen = null;
        public const Screen LobbyScreen = null;

        public const Screen ServerListScreen = null;

        public const Screen EditMapScreen = null;
        public const Screen EditCharacterScreen = null;

        //

        public static ContentPackage SelectedPackage
        {
            get { return Config.SelectedContentPackage; }
        }

        public GameMain()
        {
            Instance = this;

            Config = new GameSettings("serverconfig.xml");
            if (Config.WasGameUpdated)
            {
                UpdaterUtil.CleanOldFiles();
                Config.WasGameUpdated = false;
                Config.Save("serverconfig.xml");
            }
        }

        public void Run()
        {
            //TODO: implement
        }

        public CoroutineHandle ShowLoading(IEnumerable<object> loader, bool waitKeyHit = true)
        {
            return CoroutineManager.StartCoroutine(loader);
        }
    }
}
