using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class MultiPlayerCampaign
    {
        public static void StartCampaignSetup()
        {
            DebugConsole.NewMessage("********* CAMPAIGN SETUP *********", Color.White);
            DebugConsole.ShowQuestionPrompt("Do you want to start a new campaign? Y/N", (string arg) =>
            {
                if (arg.ToLowerInvariant() == "y" || arg.ToLowerInvariant() == "yes")
                {
                    DebugConsole.ShowQuestionPrompt("Enter a save name for the campaign:", (string saveName) =>
                    {
                        if (string.IsNullOrWhiteSpace(saveName)) return;

                        string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                        GameMain.GameSession = new GameSession(new Submarine(GameMain.NetLobbyScreen.SelectedSub.FilePath, ""), savePath, 
                            GameModePreset.List.Find(g => g.Identifier == "multiplayercampaign"));
                        var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
                        campaign.GenerateMap(GameMain.NetLobbyScreen.LevelSeed);
                        campaign.SetDelegates();

                        GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                        GameMain.GameSession.Map.SelectRandomLocation(true);
                        SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                        campaign.LastSaveID++;

                        DebugConsole.NewMessage("Campaign started!", Color.Cyan);
                    });
                }
                else
                {
                    string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer);
                    DebugConsole.NewMessage("Saved campaigns:", Color.White);
                    for (int i = 0; i < saveFiles.Length; i++)
                    {
                        DebugConsole.NewMessage("   " + i + ". " + saveFiles[i], Color.White);
                    }
                    DebugConsole.ShowQuestionPrompt("Select a save file to load (0 - " + (saveFiles.Length - 1) + "):", (string selectedSave) =>
                    {
                        int saveIndex = -1;
                        if (!int.TryParse(selectedSave, out saveIndex)) return;

                        SaveUtil.LoadGame(saveFiles[saveIndex]);
                        ((MultiPlayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
                        GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                        GameMain.GameSession.Map.SelectRandomLocation(true);

                        DebugConsole.NewMessage("Campaign loaded!", Color.Cyan);
                    });
                }
            });
        }
    }
}
