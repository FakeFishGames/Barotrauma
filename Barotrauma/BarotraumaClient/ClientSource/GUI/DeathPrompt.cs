#nullable enable
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

internal class DeathPrompt
{
    private static CoroutineHandle? createPromptCoroutine;

    private GUIComponent? skillPanel;
    private GUIComponent? newCharacterPanel;
    private GUIComponent? takeOverBotPanel;

    private GUIComponent? content;
    
    public static GUIComponent? takeOverBotPanelFrame;

    /// <summary>
    /// Private constructor, because these should only be created using the Show method
    /// </summary>
    private DeathPrompt() { }

    public static void Create(float delay)
    {
        if (!RespawnManager.UseDeathPrompt) { return; }
        if (GameMain.GameSession.DeathPrompt != null)
        {
            return;
        }

        if (createPromptCoroutine != null && CoroutineManager.IsCoroutineRunning(createPromptCoroutine)) { return; }
        if ((GameMain.GameSession is not { IsRunning: true })) { return; }

        createPromptCoroutine = CoroutineManager.Invoke(() =>
        {
            if (GameMain.GameSession != null)
            {
                GameMain.GameSession.DeathPrompt = new DeathPrompt();
                GameMain.GameSession.DeathPrompt.CreatePrompt();
                SoundPlayer.OverrideMusicType = "crewdead".ToIdentifier();
                SoundPlayer.OverrideMusicDuration = 25.0f;
            }
        }, delay);
    }

    public void AddToGUIUpdateList()
    {
        content?.AddToGUIUpdateList();
    }

    private void CreatePrompt()
    {
        const float FadeInInterval = 1.0f;
        const float FadeInDuration = 1.0f;

        bool permadeath = GameMain.NetworkMember is { ServerSettings.RespawnMode: RespawnMode.Permadeath };
        bool ironman = GameMain.NetworkMember is { ServerSettings: { RespawnMode: RespawnMode.Permadeath, IronmanMode: true } };

        var background = new GUICustomComponent(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), onDraw: DrawBackground)
        {
            UserData = this
        };
        background.FadeIn(wait: 0, duration: 5.0f);

        var foreground = new GUIImage(new RectTransform(new Vector2(1.0f, GUI.RelativeHorizontalAspectRatio), background.RectTransform, Anchor.BottomCenter) { AbsoluteOffset = new Point(0, GUI.IntScale(-20)) }, "DeathScreenForeground")
        {
            Color = Color.White
        };
        foreground.FadeIn(wait: 0, duration: 5.0f);
        foreground.Pulsate(startScale: Vector2.One, Vector2.One * 0.8f, duration: 25.0f);

        var frame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.3f), background.RectTransform, Anchor.Center))
        {
            UserData = this
        };
        frame.FadeIn(wait: 0, duration: FadeInDuration);

        new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.1f), background.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.2f) }, string.Empty, font: GUIStyle.LargeFont, textAlignment: Alignment.TopCenter)
        {
            TextGetter = () => 
            {
                return GameMain.Client.EndRoundTimeRemaining > 0.0f ?
                    TextManager.GetWithVariable("endinground", "[time]", ToolBox.SecondsToReadableTime(GameMain.Client.EndRoundTimeRemaining))
                            .Fallback(ToolBox.SecondsToReadableTime(GameMain.Client.EndRoundTimeRemaining), useDefaultLanguageIfFound: false) :
                    string.Empty;
            }
        };

        var content = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.8f), frame.RectTransform, Anchor.Center))
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };

        //"you have died" header
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.Get("deathprompt.header"), font: GUIStyle.LargeFont, textAlignment: Alignment.Center)
            .FadeIn(wait: 0, duration: FadeInDuration);

        var causeOfDeath = GameMain.Client?.Character?.CauseOfDeath;
        if (causeOfDeath != null && causeOfDeath.Type != CauseOfDeathType.Unknown)
        {
            var causeOfDeathDescription = causeOfDeath.Affliction != null ?
                  causeOfDeath.Affliction.SelfCauseOfDeathDescription :
                  TextManager.Get("Self_CauseOfDeathDescription." + causeOfDeath.Type.ToString(), "Self_CauseOfDeathDescription.Damage");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), causeOfDeathDescription)
                .FadeIn(wait: FadeInInterval * 2, duration: FadeInDuration);
        }
        
        if (permadeath)
        {
            if (ironman)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
                        TextManager.Get("deathprompt.permadeathnotification") + "\n\n" + TextManager.Get("deathprompt.ironmanexplanation"), wrap: true)
                    .FadeIn(wait: FadeInInterval * 3, duration: FadeInDuration);
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
                        TextManager.Get("deathprompt.permadeathnotification") + '\n' + TextManager.Get("deathprompt.takeoverbotexplanation"), wrap: true)
                    .FadeIn(wait: FadeInInterval * 3, duration: FadeInDuration);
            }
        }
        else if (RespawnManager.SkillLossPercentageOnDeath > 0)
        {
            string skillLossAmount = ((int)RespawnManager.SkillLossPercentageOnDeath).ToString();
            string skillLossText = $"‖color: { XMLExtensions.ToStringHex(GUIStyle.Red)}‖{skillLossAmount}‖end‖";
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
               RichString.Rich(TextManager.GetWithVariable("respawnskillpenalty", "[percentage]", skillLossText)))
                .FadeIn(wait: FadeInInterval * 3, duration: FadeInDuration);
        };

        //"what do you want to do" buttons in the middle 
        //-------------------------------------------------------------------------------------------------------

        var decisionButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), content.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };

        if (ironman)
        {
            // The only option is to spectate
            var buttonContainerMiddle = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), decisionButtonContainer.RectTransform), childAnchor: Anchor.Center);
            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainerMiddle.RectTransform), TextManager.Get("spectatebutton"))
            {
                OnClicked = (btn, userdata) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: true);
                    Close();
                    return true;
                }
            }.FadeIn(wait: FadeInInterval * 4, duration: FadeInDuration, alsoChildren: true);
        }
        else
        {
            var buttonContainerLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), decisionButtonContainer.RectTransform));
            var buttonContainerRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), decisionButtonContainer.RectTransform));

            // The default "I'll wait" button
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), buttonContainerLeft.RectTransform), TextManager.Get("respawnquestionpromptwait"))
            {
                OnClicked = (btn, userdata) =>
                {
                    GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: true);
                    Close();
                    return true;
                }
            }.FadeIn(wait: FadeInInterval * 4, duration: FadeInDuration, alsoChildren: true);

            if (permadeath)
            {
                if (GameMain.Client != null && GameMain.Client.ServerSettings.AllowBotTakeoverOnPermadeath)
                {
                    new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), buttonContainerRight.RectTransform), TextManager.Get("deathprompt.takeoverbot"))
                    {
                        Enabled = false,
                        OnAddedToGUIUpdateList = (component) =>
                        {
                            component.Enabled = GetAvailableBots().Any();
                        },
                        OnClicked = (btn, userdata) =>
                        {
                            if (takeOverBotPanel == null)
                            {
                                CreateTakeOverBotPanel(frame, this);
                            }
                            else
                            {
                                takeOverBotPanel.Parent?.RemoveChild(takeOverBotPanel);
                                takeOverBotPanel = null;
                            }
                            return true;
                        }
                    }.FadeIn(wait: FadeInInterval * 4, duration: FadeInDuration, alsoChildren: true);
                }
            }
            else
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), buttonContainerRight.RectTransform), TextManager.Get("deathprompt.respawnnow"))
                {
                    OnClicked = (btn, userdata) =>
                    {
                        GameMain.Client?.SendRespawnPromptResponse(waitForNextRoundRespawn: false);
                        Close();
                        return true;
                    },
                    Enabled = GameMain.NetworkMember is { ServerSettings.RespawnMode: RespawnMode.MidRound }
                }.FadeIn(wait: FadeInInterval * 4, duration: FadeInDuration, alsoChildren: true);
            }

            //"info buttons" at the bottom
            //-------------------------------------------------------------------------------------------------------

            var infoButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform), childAnchor: Anchor.TopRight)
            {
                Stretch = true,
                RelativeSpacing = 0.025f
            };
            if (permadeath)
            {
                if (Level.IsLoadedFriendlyOutpost)
                {
                    new GUIButton(new RectTransform(new Vector2(0.6f, 1.0f), infoButtonContainer.RectTransform), TextManager.Get("npctitle.hrmanager"), style: "GUIButtonSmall")
                    {
                        OnClicked = (btn, userdata) =>
                        {
                            if (GameMain.GameSession?.Campaign is { } campaign)
                            {
                                campaign.ShowCampaignUI = true;
                                campaign.CampaignUI?.SelectTab(CampaignMode.InteractionType.Crew);
                            }
                            Close();
                            return true;
                        }
                    }.FadeIn(wait: FadeInInterval * 5, duration: FadeInDuration, alsoChildren: true);
                }
            }
            else
            {
                new GUIButton(new RectTransform(new Vector2(0.6f, 1.0f), infoButtonContainer.RectTransform), TextManager.Get("deathprompt.showskills"), style: "GUIButtonSmall")
                {
                    OnClicked = (btn, userdata) =>
                    {
                        if (skillPanel == null)
                        {
                            CreateSkillPanel(frame, GameMain.Client?.Character?.Info ?? GameMain.Client?.CharacterInfo);
                        }
                        else
                        {
                            skillPanel.Parent?.RemoveChild(skillPanel);
                            skillPanel = null;
                        }
                        return true;
                    }
                }.FadeIn(wait: FadeInInterval * 5, duration: FadeInDuration, alsoChildren: true);

                new GUIButton(new RectTransform(new Vector2(0.6f, 1.0f), infoButtonContainer.RectTransform), TextManager.Get("deathprompt.newcharacter"), style: "GUIButtonSmall")
                {
                    OnClicked = (btn, userdata) =>
                    {
                        if (newCharacterPanel == null)
                        {
                            CreateNewCharacterPanel(frame);
                        }
                        else
                        {
                            newCharacterPanel.Parent?.RemoveChild(newCharacterPanel);
                            newCharacterPanel = null;
                        }
                        return true;
                    }
                }.FadeIn(wait: FadeInInterval * 5, duration: FadeInDuration, alsoChildren: true);
            }
        }

        //TODO
        /*new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), infoButtonContainer.RectTransform), "Respawn settings", style: "GUIButtonSmall")
        {
            OnClicked = (btn, userdata) =>
            {
                return true;
            }
        }.FadeIn(wait: FadeInInterval * 5, duration: FadeInDuration, alsoChildren: true);*/

        this.content = background;
    }

    private void CreateSkillPanel(GUIComponent parent, CharacterInfo? characterInfo)
    {
        if (characterInfo == null) { return; }
        var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent.RectTransform, Anchor.CenterRight, Pivot.CenterLeft));

        var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), frame.RectTransform, Anchor.Center), isHorizontal: true)
        {
            Stretch = true
        };

        var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), content.RectTransform))
        {
            RelativeSpacing = 0.05f
        };
        var middleColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), content.RectTransform))
        {
            RelativeSpacing = 0.05f
        };
        var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), content.RectTransform))
        {
            RelativeSpacing = 0.05f
        };

        var leftHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform), TextManager.Get("Skills"), font: GUIStyle.SubHeadingFont, textColor: GUIStyle.TextColorBright);
        var middleHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), middleColumn.RectTransform), TextManager.Get("deathprompt.SkillsLostHeader"), font: GUIStyle.SubHeadingFont, textColor: GUIStyle.TextColorBright);
        var rightHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform), TextManager.Get("deathprompt.respawnnow"), font: GUIStyle.SubHeadingFont, textColor: GUIStyle.TextColorBright);

        GUITextBlock.AutoScaleAndNormalize(leftHeader, middleHeader, rightHeader);

        foreach (var skill in characterInfo.Job.GetSkills().OrderByDescending(s => s.Level))
        {
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform), skill.DisplayName);

            int previousSkill = (int)skill.HighestLevelDuringRound;
            int reducedSkill = (int)RespawnManager.GetReducedSkill(characterInfo, skill, RespawnManager.SkillLossPercentageOnDeath);
            int reducedSkillOnImmediateRespawn = (int)RespawnManager.GetReducedSkill(characterInfo, skill, RespawnManager.SkillLossPercentageOnImmediateRespawn, currentSkillLevel: reducedSkill);
             
            int skillLoss = reducedSkill - previousSkill;
            int skillLossOnImmediateRespawn = reducedSkillOnImmediateRespawn - previousSkill;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), middleColumn.RectTransform), 
               RichString.Rich($"{reducedSkill} (‖color:{XMLExtensions.ToStringHex(GUIStyle.Red)}‖{skillLoss}‖end‖)"));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform),
                RichString.Rich($"{reducedSkillOnImmediateRespawn} (‖color:{XMLExtensions.ToStringHex(GUIStyle.Red)}‖{skillLossOnImmediateRespawn}‖end‖)"));
        }

        new GUIButton(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform, Anchor.BottomLeft), TextManager.Get("Close"), style: "GUIButtonSmall")
        {
            IgnoreLayoutGroups = true,
            OnClicked = (btn, userdata) =>
            {
                frame.Parent?.RemoveChild(frame);
                skillPanel = null;
                return true;
            }
        };

        skillPanel = frame;
    }

    private void CreateNewCharacterPanel(GUIComponent parent)
    {
        var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.5f), parent.RectTransform, Anchor.CenterRight, Pivot.CenterLeft));

        var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: false)
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };
        GameMain.NetLobbyScreen.CreatePlayerFrame(content, alwaysAllowEditing: true, createPendingText: false);

        var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.15f), content.RectTransform), isHorizontal: true)
        {
            RelativeSpacing = 0.05f,
            Stretch = true
        };

        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform, Anchor.BottomLeft), TextManager.Get("Cancel"), style: "GUIButtonSmall")
        {
            OnClicked = (btn, userdata) =>
            {
                frame.Parent?.RemoveChild(frame);
                newCharacterPanel = null;
                return true;
            }
        };
        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform, Anchor.BottomLeft), TextManager.Get("ApplySettingsYes"), style: "GUIButtonSmall")
        {
            OnClicked = (btn, userdata) =>
            {
                GameMain.NetLobbyScreen.TryDiscardCampaignCharacter(onYes: () => 
                { 
                    frame.Parent?.RemoveChild(frame);
                    newCharacterPanel = null;
                });
                return true;
            }
        };

        newCharacterPanel = frame;
    }

    public static void CreateTakeOverBotPanel()
    {
        var panelHolder = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.3f), GUI.Canvas, Anchor.Center));
        var takeOverBotPanel = CreateTakeOverBotPanel(panelHolder, deathPrompt: null);
        if (takeOverBotPanel != null)
        {
            takeOverBotPanel.RectTransform.SetPosition(Anchor.Center);
            GUIMessageBox.MessageBoxes.Add(panelHolder);
        }
    }

    /// <summary>
    /// Static because the "take over bot" panel can be accessed outside the death prompt too
    /// </summary>
    private static GUIComponent? CreateTakeOverBotPanel(GUIComponent parent, DeathPrompt? deathPrompt)
    {
        if (GameMain.GameSession?.CrewManager == null) { return null; }
        if (GameMain.GameSession?.Campaign is not MultiPlayerCampaign campaign) { return null; }

        if (campaign.CampaignUI == null) { campaign.InitCampaignUI(); }

        var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent.RectTransform, Anchor.CenterRight, Pivot.CenterLeft));
        takeOverBotPanelFrame = frame;

        var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: false)
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };

        var botList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.9f), content.RectTransform));
        foreach (CharacterInfo c in GetAvailableBots())
        {
            var characterFrame = campaign.CampaignUI?.HRManagerUI.CreateCharacterFrame(c, botList, hideSalary: true);
            if (characterFrame != null)
            {
                characterFrame.UserData = c;
            }
        }
        botList.UpdateScrollBarSize();

        var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.15f), content.RectTransform), isHorizontal: true)
        {
            RelativeSpacing = 0.05f,
            Stretch = true
        };

        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform, Anchor.BottomLeft), TextManager.Get("Cancel"), style: "GUIButtonSmall")
        {
            OnClicked = (btn, userdata) =>
            {
                GUIMessageBox.MessageBoxes.Remove(frame.Parent);
                frame.Parent?.RemoveChild(frame);
                if (deathPrompt != null)
                {
                    deathPrompt.takeOverBotPanel = null;
                }
                return true;
            }
        };
        new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform, Anchor.BottomLeft), TextManager.Get("inputtype.select"), style: "GUIButtonSmall")
        {
            Enabled = false,
            OnAddedToGUIUpdateList = (component) =>
            {
                component.Enabled = botList.SelectedData is CharacterInfo;
            },
            OnClicked = (btn, userdata) =>
            {
                if (botList.SelectedData is CharacterInfo selectedCharacter && GameMain.Client is GameClient client)
                {
                    client.SendTakeOverBotRequest(selectedCharacter);
                    GUIMessageBox.MessageBoxes.Remove(frame.Parent);
                    deathPrompt?.Close();
                    return true;
                }
                else
                {
                    DebugConsole.ThrowError($"Conditions for sending bot takeover request not met");
                    return false;
                }
            }
        };
        if (deathPrompt != null)
        {
            deathPrompt.takeOverBotPanel = frame;
        }
        return frame;
    }

    private static IEnumerable<CharacterInfo> GetAvailableBots()
    {
        if (GameMain.GameSession?.CrewManager is { } crewManager)
        {
            return crewManager.GetCharacterInfos().Where(c => 
                /*either an alive bot */
                c is { Character.IsBot: true, Character.IsDead: false } || 
                /* or a newly hired bot that hasn't spawned yet */
                (c.IsNewHire && c.Character == null));
        }
        else
        {
            return Enumerable.Empty<CharacterInfo>();
        }
    }

    private void DrawBackground(SpriteBatch spriteBatch, GUICustomComponent guiCustomComponent)
    {
        var background = GUIStyle.GetComponentStyle("DeathScreenBackground");
        if (background != null)
        {
            GUI.DrawBackgroundSprite(spriteBatch, background.GetDefaultSprite(), Color.White * (guiCustomComponent.Color.A / 255.0f));
        }
    }

    public void Close()
    {
        if (GameMain.GameSession != null)
        {
            GameMain.GameSession.DeathPrompt = null;
        }
    }
    
    public static void CloseBotPanel()
    {
        if (takeOverBotPanelFrame is GUIComponent frame)
        {
            GUIMessageBox.MessageBoxes.Remove(frame.Parent);
            frame.Parent?.RemoveChild(frame);
        }
        takeOverBotPanelFrame = null;
    }
}
