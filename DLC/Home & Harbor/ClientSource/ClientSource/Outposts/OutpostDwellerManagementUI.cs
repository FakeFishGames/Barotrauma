#nullable enable

using System.Collections.Immutable;
using Barotrauma;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

internal class OutpostDwellerManagementUI
{
    private readonly PersistentOutpostData outpostData;
    private readonly Submarine outpost;
    private readonly GUIComponent parent;
    private readonly GUIComponent popupParent;

    private GUIListBox? dwellersListBox;
    private GUIListBox? crewListBox;
    private GUIFrame? characterPreviewFrame;
    internal static GUIMessageBox? workstationAssignmentPopup;
    
    private Character? workstationAssignmentCharacter;
    private GUIListBox? workstationAssignmentListBox;
    private ImmutableDictionary<MapEntity, MiniMapGUIComponent>? workstationMinimapComponents;
    private GUITextBlock? workstationPopupNameBlock;
    private GUITextBlock? workstationPopupJobBlock;
    private GUILayoutGroup? workstationPopupSkillColumn;
    private GUIFrame? minimapContainer;
    private WorkstationComponent? hoveredWorkstation;
    private float hoverPulseTimer;

    /// <summary>
    /// Buttonns used to move crew members to outpost dwellers. 
    /// We keep track of these because they need to be disabled when there's only one crew member left, and re-enabled when it's above 1 again.
    /// </summary>
    private readonly Dictionary<Character, GUIButton> moveToOutpostButtons = [];

    public OutpostDwellerManagementUI(PersistentOutpostData outpostData, Submarine outpost, GUIComponent parent, GUIComponent popupParent)
    {
        this.outpostData = outpostData;
        this.outpost = outpost;
        this.parent = parent;
        this.popupParent = popupParent;

        Identifier eventIdentifier = nameof(OutpostDwellerManagementUI).ToIdentifier();

        outpostData.DwellerManager.OnDwellerWorkstationChanged.RegisterOverwriteExisting(eventIdentifier, e => 
        {
            UpdateDwellerInList(e.Character);
            // Always update the workstation list when any assignment changes, the change might be relevant for other characters' assignment choices
            UpdateWorkstationPopup();
        });

        outpostData.DwellerManager.OnDwellerAdded.RegisterOverwriteExisting(eventIdentifier, AddDwellerToList);
        outpostData.DwellerManager.OnDwellerRemoved.RegisterOverwriteExisting(eventIdentifier, RemoveDwellerFromList);
        outpostData.DwellerManager.OnCharacterAddedToCrew.RegisterOverwriteExisting(eventIdentifier, AddCrewMemberToList);
        outpostData.DwellerManager.OnCharacterRemovedFromCrew.RegisterOverwriteExisting(eventIdentifier, RemoveCrewMemberFromList);

        CreateUI();
    }

    private void UpdateWorkstationPopup()
    {
        // Update workstation popup if it's currently open (regardless of which character it's for)
        if (workstationAssignmentCharacter == null || workstationAssignmentListBox == null) { return; }

        // Update the character info section at the top left corner
        UpdateWorkstationPopupCharacterInfo();

        // Update the workstation list
        foreach (var rowFrame in workstationAssignmentListBox.GetAllChildren<GUIFrame>())
        {
            if (rowFrame.UserData is WorkstationRowData rowData)
            {
                UpdateWorkstationRowData(rowFrame, rowData.Workstation);
            }
        }

        // The minimap markers need to be recolored to match the new utilization levels
        ResetMinimapMarkers();
    }

    private void UpdateWorkstationPopupCharacterInfo()
    {
        if (workstationAssignmentCharacter == null) { return; }
        if (workstationPopupNameBlock == null || workstationPopupJobBlock == null || workstationPopupSkillColumn == null) { return; }

        // Update character name and job display
        UpdateCharacterNameAndJobBlocks(workstationAssignmentCharacter, workstationPopupNameBlock, workstationPopupJobBlock);

        // Find and recreate the skill column to update the icon and color
        if (workstationPopupSkillColumn.Children.LastOrDefault() is GUIComponent skillComponent)
        {
            workstationPopupSkillColumn.RemoveChild(skillComponent);
            CreateSkillColumn(workstationAssignmentCharacter, workstationPopupSkillColumn, width: 0.15f, height: 0.35f);
            workstationPopupSkillColumn.Recalculate();
        }
    }
    
    public void RefreshAll()
    {
        RefreshDwellersList();
        RefreshCrewList();
        
        // Refresh workstation popup if it's open
        if (workstationAssignmentPopup != null && workstationAssignmentCharacter != null)
        {
            RefreshWorkstationList();
        }
    }

    private bool CanManageDwellers()
    {
        if (!outpostData.IsPlayerOwned) { return false; }
        if (!GameMain.IsMultiplayer) { return true; }
        
        return OutpostBuilder.AllowedToManageOutpost();
    }

    private void CreateUI() // copied more or less from HRManagerUI
    {
        int panelMaxWidth = (int)(GUI.xScale * (GUI.HorizontalAspectRatio < 1.4f ? 650 : 560));
        
        // Left panel (Dwellers)
        var dwellersContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1f), parent.RectTransform)
            {
                // Wider than the right side panel
                MaxSize = new Point((int)(panelMaxWidth * 1.5f), parent.Rect.Height)
            })
        {
            Stretch = true,
            RelativeSpacing = 0.01f
        };

        var dwellersGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.97f), anchor: Anchor.Center,
                parent: new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), dwellersContainer.RectTransform)).RectTransform))
        {
            RelativeSpacing = 0.015f,
            Stretch = true
        };

        dwellersListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), dwellersGroup.RectTransform), style: "GUIListBoxNoBorder")
        {
            CurrentSelectMode = GUIListBox.SelectMode.None,
            AutoHideScrollBar = false,
            PlaySoundOnSelect = false,
            Spacing = 1,
            OnHoverEnded = CreateCharacterHoverEndHandler()
        };
        dwellersListBox.OnHoverStarted = CreateCharacterHoverHandler(dwellersListBox);

        // Right panel (Crew)
        var crewMainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.29f, 1f), parent.RectTransform, anchor: Anchor.TopRight)
            {
                MaxSize = new Point((int)(panelMaxWidth * 1.2f), parent.Rect.Height)
            })
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };

        var crewPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), crewMainGroup.RectTransform)
        {
            MaxSize = new Point((int)(panelMaxWidth * 1.2f), (int)(parent.Rect.Height * 0.9f))
        });

        var crewGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.97f), anchor: Anchor.Center,
            parent: crewPanel.RectTransform));

        float height = 0.05f;
        var crewHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, height), crewGroup.RectTransform),
            TextManager.Get("campaignmenucrew"),
            font: GUIStyle.SubHeadingFont);

        new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), crewHeader.RectTransform, Anchor.CenterRight),
            text: string.Empty,
            textAlignment: Alignment.CenterRight)
        {
            TextGetter = () => 
            {
                int crewSize = GameSession.GetSessionCrewCharacters(CharacterType.Both).Count;
                return TextManager.GetWithVariables("crewsize",
                    ("[current]", crewSize.ToString()),
                    ("[max]", CrewManager.MaxCrewSize.ToString()));
            }
        };
        
        crewListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 16 * height), crewGroup.RectTransform), style: "GUIListBoxNoBorder")
        {
            CurrentSelectMode = GUIListBox.SelectMode.None,
            PlaySoundOnSelect = false,
            Spacing = 1,
            OnHoverEnded = CreateCharacterHoverEndHandler()
        };
        crewListBox.OnHoverStarted = CreateCharacterHoverHandler(crewListBox);

        RefreshDwellersList();
        RefreshCrewList();
    }

    public void RefreshCrewList()
    {
        if (crewListBox == null) { return; }

        moveToOutpostButtons.Clear();

        crewListBox.Content.ClearChildren();

        foreach (Character crewMember in GetSortedCrewCharacters())
        {
            if (crewMember.Info == null)
            {
                continue;
            }

            CreateCrewMemberRow(crewMember, crewListBox);
        }
    }

    private GUIListBox.OnHoverStartedHandler CreateCharacterHoverHandler(GUIListBox listBox)
    {
        return (_, userdata) =>
        {
            // Handle both dweller list (DwellerRowData) and crew list (CharacterInfo)
            CharacterInfo? characterInfo;
            if (userdata is DwellerRowData rowData)
            {
                characterInfo = rowData.CharacterInfo;
            }
            else
            {
                characterInfo = userdata as CharacterInfo;
            }

            if (characterInfo != null)
            {
                SelectCharacter(listBox, GUI.MouseOn as GUIFrame, characterInfo);
            }
            return true;
        };
    }

    private GUIListBox.OnHoverEndedHandler CreateCharacterHoverEndHandler()
    {
        return () =>
        {
            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }
            return true;
        };
    }

    private struct CharacterRowComponents
    {
        public GUITextBlock NameBlock;
        public GUITextBlock JobBlock;
    }

    private readonly struct DwellerRowData(
        CharacterInfo characterInfo,
        GUITextBlock nameBlock,
        GUITextBlock jobBlock,
        GUIFrame workstationPanelOutline,
        GUIFrame workstationPanelTitleFrame,
        GUITextBlock workstationPanelTitleText,
        GUIFrame workstationButtonArea,
        GUIButton moveToCrewButton)
    {
        public readonly CharacterInfo CharacterInfo = characterInfo;
        public readonly GUITextBlock NameBlock = nameBlock;
        public readonly GUITextBlock JobBlock = jobBlock;
        public readonly GUIFrame WorkstationPanelOutline = workstationPanelOutline;
        public readonly GUIFrame WorkstationPanelTitleFrame = workstationPanelTitleFrame;
        public readonly GUITextBlock WorkstationPanelTitleText = workstationPanelTitleText;
        public readonly GUIFrame WorkstationButtonArea = workstationButtonArea;
        public readonly GUIButton MoveToCrewButton = moveToCrewButton;
    }

    private readonly struct WorkstationRowData(
        WorkstationComponent workstation,
        GUIFrame checkmarkColumn,
        GUITextBlock occupancyText,
        GUIButton assignButton)
    {
        public readonly WorkstationComponent Workstation = workstation;
        public readonly GUIFrame CheckmarkColumn = checkmarkColumn;
        public readonly GUITextBlock OccupancyText = occupancyText;
        public readonly GUIButton AssignButton = assignButton;
    }

    private GUICustomComponent CreatePortraitColumn(Character character, GUILayoutGroup rowGroup, float width)
    {
        return new GUICustomComponent(new RectTransform(new Vector2(width, 0.8f), rowGroup.RectTransform),
            onDraw: (sb, component) => character.Info?.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
        {
            CanBeFocused = false
        };
    }

    private static LocalizedString GetJobNameOrTitle(CharacterInfo? characterInfo)
    {
        if (characterInfo?.Job is { } job && !string.IsNullOrEmpty(job.Name.Value))
        {
            return job.Name;
        }
        else if (!string.IsNullOrEmpty(characterInfo?.Title?.Value))
        {
            return characterInfo.Title;
        }
        else
        {
            return TextManager.Get("dwellerui.nojob");
        }
    }

    private static void UpdateCharacterNameAndJobBlocks(Character character, GUITextBlock nameBlock, GUITextBlock jobBlock)
    {
        // Update name color to reflect current job (including job override)
        nameBlock.TextColor = character.Info?.Job?.Prefab.UIColor ?? Color.White;

        // Update job text to reflect current job/title
        jobBlock.Text = GetJobNameOrTitle(character.Info);
        jobBlock.Text = ToolBox.LimitString(jobBlock.Text, jobBlock.Font, jobBlock.Rect.Width);
    }

    private CharacterRowComponents CreateNameAndJobColumn(Character character, GUILayoutGroup rowGroup, float width)
    {
        var nameAndJobGroup = new GUILayoutGroup(new RectTransform(new Vector2(width, 0.8f), rowGroup.RectTransform));
        var nameGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft);
        var nameBlock = new GUITextBlock(new RectTransform(Vector2.One, nameGroup.RectTransform),
            character.Info?.DisplayName ?? "UNKNOWN",
            textColor: character.Info?.Job?.Prefab.UIColor ?? Color.White,
            textAlignment: Alignment.BottomLeft)
        {
            CanBeFocused = false
        };

        var jobBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndJobGroup.RectTransform),
            GetJobNameOrTitle(character.Info),
            textColor: Color.White,
            font: GUIStyle.SmallFont,
            textAlignment: Alignment.TopLeft)
        {
            CanBeFocused = false
        };

        return new CharacterRowComponents
        {
            NameBlock = nameBlock,
            JobBlock = jobBlock
        };
    }

    private GUIComponent CreateSkillColumn(Character? character, GUILayoutGroup rowGroup, float width = 0.15f, float height = 0.6f)
    {
        Job? job = character?.Info?.Job;
        Job? overridingJob = character?.Info?.JobOverride;

        // Show the overriding dweller job's primary skill (the "featured" skill) if there is one, otherwise fall back to the best skill the character actually has (if any)
        if ((overridingJob?.PrimarySkill ?? job?.GetSkills()?.MaxBy(s => s.Level)) is Skill primarySkill)
        {
            var skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(width, height), rowGroup.RectTransform),
                isHorizontal: true);
            float iconWidth = (float)skillGroup.Rect.Height / skillGroup.Rect.Width;
            new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 1.0f), skillGroup.RectTransform),
                ((int)primarySkill.Level).ToString(),
                textAlignment: Alignment.CenterRight)
            {
                Padding = Vector4.Zero,
                CanBeFocused = false
            };
            var skillIcon = new GUIImage(new RectTransform(Vector2.One, skillGroup.RectTransform, scaleBasis: ScaleBasis.Smallest),
                primarySkill.Icon,
                scaleToFit: true)
            {
                CanBeFocused = false
            };
            skillIcon.Color = GetSkillIconColor(primarySkill, overridingJob?.Prefab);
            return skillGroup;
        }
        else
        {
            // Empty placeholder to maintain alignment
            return new GUIFrame(new RectTransform(new Vector2(width, height), rowGroup.RectTransform), style: null) { CanBeFocused = false };
        }
    }

    private static Color GetSkillIconColor(Skill skill, JobPrefab? overridingJobPrefab)
    {
        if (skill.IconColor is Color skillColor)
        {
            //skills with their own icon style use a fixed color
            return skillColor;
        }
        else if (overridingJobPrefab?.UIColor is Color jobColor)
        {
            return jobColor;
        }
        else
        {
            //color the skill icon according to the job whose primary skill it is
            return
                JobPrefab.Prefabs
                .Where(job => !job.HiddenJob)
                .FirstOrDefault(job => job.PrimarySkill?.Identifier == skill.Identifier)?.UIColor ?? Color.White;
        }
    }

    private void CreateCrewMemberRow(Character character, GUIListBox targetListBox)
    {
        if (character.Info == null) { return; }

        var rowFrame = new GUIFrame(new RectTransform(new Point(targetListBox.Content.Rect.Width, GUI.IntScale(80)), targetListBox.Content.RectTransform),
            style: "ListBoxElement")
        {
            UserData = character.Info
        };
        
        var rowGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), rowFrame.RectTransform, anchor: Anchor.Center),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft)
        {
            AbsoluteSpacing = 1,
            Stretch = true
        };
        
        bool canManage = CanManageDwellers();

        // Move to outpost button (leftmost, before character info, to align with the "direction" of the action, and be symmetrical with the move to crew button in the dweller list)
        var moveToOutpostColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.05f, 1.0f), rowGroup.RectTransform),
            childAnchor: Anchor.CenterLeft)
        {
            IsHorizontal = true
        };
        var moveToOutpostButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.95f), moveToOutpostColumn.RectTransform),
            style: "DwellerUIMoveToOutpostButton")
        {
            ToolTip = canManage ? TextManager.Get("dwellerui.movetooutpost") : TextManager.Get("dwellerui.nopermission"),
            UserData = character, 
            // Don't show for player characters, or if there's only one crew member remaining
            Enabled = canManage,
            OnClicked = (_, userdata) => 
            {
                if (userdata is Character c)
                {
                    AskMoveCrewToOutpost(c);
                }
                return true;
            }
        };
        moveToOutpostButtons[character] = moveToOutpostButton;
        RefreshMoveToOutpostButton(character);

        // Add character columns
        float portraitWidth = (0.8f * rowGroup.Rect.Height) / rowGroup.Rect.Width;
        CreatePortraitColumn(character, rowGroup, portraitWidth);
        var nameAndJobColumn = CreateNameAndJobColumn(character, rowGroup, 0.44f - portraitWidth);
        CreateSkillColumn(character, rowGroup);

        rowGroup.Recalculate();
        // Truncate text if too long after recalculation
        nameAndJobColumn.NameBlock.Text = ToolBox.LimitString(nameAndJobColumn.NameBlock.Text, nameAndJobColumn.NameBlock.Font, nameAndJobColumn.NameBlock.Rect.Width);
        nameAndJobColumn.JobBlock.Text = ToolBox.LimitString(nameAndJobColumn.JobBlock.Text, nameAndJobColumn.JobBlock.Font, nameAndJobColumn.JobBlock.Rect.Width);
    }

    public void OnOpened()
    {
        RefreshAll();
    }

    private void RefreshDwellersList()
    {
        if (dwellersListBox == null) { return; }

        dwellersListBox.Content.ClearChildren();
        
        foreach (var character in GetSortedDwellerCharacters())
        {
            CreateDwellerRow(character, dwellersListBox);
        }
    }

    private LocalizedString GetWorkstationPanelText(Character character, WorkstationComponent? assignedWorkstation)
    {
        if (outpostData.DwellerManager.FindDwellerInfo(character) is { IsUnavailableForWork: true } dwellerInfo)
        {
            // The specific reason for unavailability for work is optional, if it's not set just show the default
            return TextManager.Get(dwellerInfo.UnavailabilityReason, "dwellerui.unavailable".ToIdentifier());
        }

        return assignedWorkstation?.Item?.Name ?? TextManager.Get("dwellerui.noworkstation");
    }

    private void CreateDwellerRow(Character character, GUIListBox targetListBox)
    {
        if (outpostData.DwellerManager.FindDwellerInfo(character) is null) { return; }
        if (character.Info == null) { return; }

        var rowFrame = new GUIFrame(new RectTransform(new Point(targetListBox.Content.Rect.Width, GUI.IntScale(80)), targetListBox.Content.RectTransform),
            style: "ListBoxElement");

        var rowGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), rowFrame.RectTransform, anchor: Anchor.Center),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft)
        {
            AbsoluteSpacing = 1,
            Stretch = true
        };
        
        // Start with the same basic info as in the other listings (left-aligned), then add the stuff specific to this list to the end of the row
        float portraitWidth = (0.8f * rowGroup.Rect.Height) / rowGroup.Rect.Width;
        CreatePortraitColumn(character, rowGroup, portraitWidth);
        var nameAndJobColumn = CreateNameAndJobColumn(character, rowGroup, 0.45f - portraitWidth);
        CreateSkillColumn(character, rowGroup, width: 0.1f);
        
        // Spacer
        new GUIFrame(new RectTransform(new Vector2(0.025f, 1.0f), rowGroup.RectTransform),
            style: null)
        {
            CanBeFocused = false
        };

        // Dweller's workstation (if any) determines the workspace mini-panel's colors (and row order)
        WorkstationComponent? assignedWorkstation = outpostData.DwellerManager.GetAssignedWorkstation(character);
        LocalizedString workstationText = GetWorkstationPanelText(character, assignedWorkstation);
        Color indicatorColor = assignedWorkstation?.IndicatorColor ?? GUIStyle.TextColorDim * 0.5f; // default to dim grey for no workstation
        Color panelForeground = indicatorColor * 0.66f;
        Color panelBackground = indicatorColor * 0.33f;
        
        var workstationPanelOutlineFrame = new GUIFrame(new RectTransform(new Vector2(0.35f, 1.0f), rowGroup.RectTransform),
            style: null)
        {
            CanBeFocused = false,
            Color = panelBackground,
            OutlineColor = panelForeground,
            OutlineThickness = 1
        };
        var workstationPanelGroup = new GUILayoutGroup(new RectTransform(Vector2.One, workstationPanelOutlineFrame.RectTransform))
        {
            CanBeFocused = false,
            Stretch = true,
            RelativeSpacing = 0
        };
        var workstationPanelTitleFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.33f), workstationPanelGroup.RectTransform),
            style: null)
        {
            CanBeFocused = false,
            Color = panelForeground
        };
        var workstationPanelTitleText = new GUITextBlock(new RectTransform(Vector2.One, workstationPanelTitleFrame.RectTransform),
            workstationText,
            textColor: Color.White,
            textAlignment: Alignment.Center,
            font: GUIStyle.SmallFont)
        {
            CanBeFocused = false
        };
        
        // Column with a mini-panel for workstation info and buttons
        var buttonAreaFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.67f), workstationPanelGroup.RectTransform), style: null)
        {
            CanBeFocused = false
        };

        // Move to Crew button column
        var moveToCrewColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.07f, 1.0f), rowGroup.RectTransform),
            childAnchor: Anchor.CenterRight)
        {
            IsHorizontal = true
        };
        // Note: the enabled state and the tooltip are set in UpdateDwellerRowData, they can change while the row exists
        var moveToCrewButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.95f), moveToCrewColumn.RectTransform),
            style: "DwellerUIMoveToCrewButton")
        {
            UserData = character,
            Visible = !character.IsPlayer,
            OnClicked = (btn, userdata) =>
            {
                if (userdata is Character c)
                {
                    MoveToCurrentCrew(c);
                }
                return true;
            }
        };

        // Store references to components that will need updating
        var rowData = new DwellerRowData(
            character.Info,
            nameAndJobColumn.NameBlock,
            nameAndJobColumn.JobBlock,
            workstationPanelOutlineFrame,
            workstationPanelTitleFrame,
            workstationPanelTitleText,
            buttonAreaFrame,
            moveToCrewButton);
        rowFrame.UserData = rowData;

        // Create/populate workstation assignment buttons and panel data (stuff that might change, hence it's in its own method)
        UpdateDwellerRowData(rowFrame, character);

        rowGroup.Recalculate();
        // Truncate text if too long after recalculation
        nameAndJobColumn.NameBlock.Text = ToolBox.LimitString(nameAndJobColumn.NameBlock.Text, nameAndJobColumn.NameBlock.Font, nameAndJobColumn.NameBlock.Rect.Width);
        nameAndJobColumn.JobBlock.Text = ToolBox.LimitString(nameAndJobColumn.JobBlock.Text, nameAndJobColumn.JobBlock.Font, nameAndJobColumn.JobBlock.Rect.Width);
    }

    private void UpdateDwellerRowData(GUIFrame rowFrame, Character character)
    {
        if (character.Info == null) { return; }
        if (outpostData.DwellerManager.FindDwellerInfo(character) is null) { return; }
        if (rowFrame.UserData is not DwellerRowData rowData) { return; }

        // Update character name and job display
        UpdateCharacterNameAndJobBlocks(character, rowData.NameBlock, rowData.JobBlock);

        bool canManage = CanManageDwellers();
        bool isUnavailableForWork = outpostData.DwellerManager.FindDwellerInfo(character) is { IsUnavailableForWork: true };

        // Update workstation info and colors
        WorkstationComponent? assignedWorkstation = outpostData.DwellerManager.GetAssignedWorkstation(character);
        LocalizedString workstationText = GetWorkstationPanelText(character, assignedWorkstation);
        Color indicatorColor = assignedWorkstation?.IndicatorColor ?? GUIStyle.TextColorDim * 0.5f;
        Color panelForeground = indicatorColor * 0.66f;
        Color panelBackground = indicatorColor * 0.33f;

        // Update panel colors
        rowData.WorkstationPanelOutline.Color = panelBackground;
        rowData.WorkstationPanelOutline.OutlineColor = panelForeground;
        rowData.WorkstationPanelTitleFrame.Color = panelForeground;

        // Update workstation text
        rowData.WorkstationPanelTitleText.Text = workstationText;
        rowData.WorkstationPanelTitleText.Text = ToolBox.LimitString(rowData.WorkstationPanelTitleText.Text, rowData.WorkstationPanelTitleText.Font, rowData.WorkstationPanelTitleText.Rect.Width);

        rowData.WorkstationPanelTitleText.TextColor =
            isUnavailableForWork ? GUIStyle.Red :
            assignedWorkstation == null ? GUIStyle.TextColorDim :
            GUIStyle.TextColorNormal;

        rowData.MoveToCrewButton.Enabled = canManage && !isUnavailableForWork;
        rowData.MoveToCrewButton.ToolTip =
            !canManage ? TextManager.Get("dwellerui.nopermission") :
            isUnavailableForWork ? TextManager.Get("dwellerui.cannotassignunavailable") :
            TextManager.Get("dwellerui.movetocrew");

        // Update workstation assignment buttons
        Point buttonSize = new Point(GUI.IntScale(56), GUI.IntScale(36));
        int buttonSpacing = GUI.IntScale(6);

        // Clear existing buttons
        rowData.WorkstationButtonArea.ClearChildren();

        if (assignedWorkstation == null) // Not assigned to a workstation
        {
            LocalizedString assignToolTip =
                !canManage ? TextManager.Get("dwellerui.nopermission") :
                isUnavailableForWork ? TextManager.Get("dwellerui.cannotassignunavailable") :
                TextManager.Get("dwellerui.assignworkstation");

            new GUIButton(new RectTransform(buttonSize, rowData.WorkstationButtonArea.RectTransform, Anchor.Center),
                style: "DwellerUIWorkstationAssignButton")
            {
                UserData = character,
                Enabled = canManage && !isUnavailableForWork,
                ToolTip = assignToolTip,
                OnClicked = (_, userdata) =>
                {
                    if (userdata is Character c)
                    {
                        ShowWorkstationSelectionPopup(c);
                    }
                    return true;
                }
            };
        }
        else // Assigned to a workstation
        {
            var twoButtonContainer = new GUIFrame(new RectTransform(new Point(buttonSize.X * 2 + buttonSpacing, buttonSize.Y), rowData.WorkstationButtonArea.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };
            
            new GUIButton(new RectTransform(buttonSize, twoButtonContainer.RectTransform, Anchor.CenterLeft),
                style: "DwellerUIWorkstationChangeAssignmentButton")
            {
                UserData = character,
                Enabled = canManage,
                ToolTip = canManage ? TextManager.Get("dwellerui.changeworkstationassignment") : TextManager.Get("dwellerui.nopermission"),
                OnClicked = (_, userdata) =>
                {
                    if (userdata is Character c)
                    {
                        ShowWorkstationSelectionPopup(c);
                    }
                    return true;
                }
            };
            
            new GUIButton(new RectTransform(buttonSize, twoButtonContainer.RectTransform, Anchor.CenterRight),
                style: "DwellerUIWorkstationUnassignButton")
            {
                UserData = character,
                Enabled = canManage,
                ToolTip = canManage ? TextManager.Get("dwellerui.removeworkstationassignment") : TextManager.Get("dwellerui.nopermission"),
                OnClicked = (_, userdata) =>
                {
                    if (userdata is Character c)
                    {
                        AssignCharacterToWorkstation(c, 0);
                    }
                    return true;
                }
            };
        }
    }

    private void AssignCharacterToWorkstation(Character character, int newWorkstationId, bool confirm = true)
    {
        if (newWorkstationId == 0)
        {
            // This means unassigning, so there's no need to check workstation capacity
            confirm = false;
        }
        
        if (!confirm) // either confirmation isn't needed/wanted, or it has already been received
        {
            outpostData.DwellerManager.AssignCharacterToWorkstation(character, newWorkstationId);
            
            // UI will be updated via events in both SP and MP
            
            return;
        }
        
        if (WorkstationComponent.FindWorkstationWithID(newWorkstationId) is WorkstationComponent workstation &&
            workstation.AssignedNPCCount >= workstation.MaxAssignedNPCs)
        {
            // Close workstation popup temporarily to avoid layering issues
            bool hadWorkstationPopupOpen = workstationAssignmentPopup != null;
            if (hadWorkstationPopupOpen)
            {
                workstationAssignmentPopup?.Close();
                workstationAssignmentPopup = null;
            }
            
            var confirmation = new GUIMessageBox(
                TextManager.Get("dwellerui.assignworkstation"),
                TextManager.GetWithVariables("dwellerui.workstationfullconfirm",
                    ("[character]", character.Info?.DisplayName ?? ""),
                    ("[workstationname]", workstation.Item?.Name ?? "")),
                buttons: [TextManager.Get("yes"), TextManager.Get("no")])
            {
                UserData = "verificationprompt" // handles esc automatically
            };
            
            confirmation.Buttons[0].OnClicked = (_, _) => 
            {
                AssignCharacterToWorkstation(character, newWorkstationId, confirm: false);
                confirmation.Close();
                
                // Reopen workstation popup if it was open before
                if (hadWorkstationPopupOpen)
                {
                    ShowWorkstationSelectionPopup(character);
                }
                return true;
            };
            
            confirmation.Buttons[1].OnClicked = (_, _) => 
            {
                confirmation.Close();
                
                // Reopen workstation popup if it was open before
                if (hadWorkstationPopupOpen)
                {
                    ShowWorkstationSelectionPopup(character);
                }
                return true;
            };
        }
        else
        {
            AssignCharacterToWorkstation(character, newWorkstationId, confirm: false);
        }
    }

    private void ShowWorkstationSelectionPopup(Character character)
    {
        workstationAssignmentCharacter = character;
        
        workstationAssignmentPopup = new GUIMessageBox(
            headerText: "",
            text: "",
            buttons: [ TextManager.Get("close") ],
            minSize: new Point(GUI.IntScale(1150), GUI.IntScale(700)));
        workstationAssignmentPopup.Buttons[0].OnClicked = (_, _) =>
        {
            workstationAssignmentPopup.Close();
            workstationAssignmentCharacter = null;
            workstationAssignmentListBox = null;
            workstationMinimapComponents = null;
            workstationPopupNameBlock = null;
            workstationPopupJobBlock = null;
            workstationPopupSkillColumn = null;
            minimapContainer = null;
            hoveredWorkstation = null;
            hoverPulseTimer = 0f;
            return true;
        };

        var mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.85f), workstationAssignmentPopup.Content.RectTransform))
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        
        var horizontalContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), mainLayout.RectTransform),
            isHorizontal: true)
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        
        // Left side of the panel
        var leftSideContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.426f, 1.0f), horizontalContainer.RectTransform))
        {
            Stretch = true,
            RelativeSpacing = 0.02f
        };
        
        // Character info row
        var characterInfoFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), leftSideContainer.RectTransform),
            style: "InnerFrameDark",
            color: Color.Black);
        var characterInfoGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.69f, 0.69f), characterInfoFrame.RectTransform, anchor: Anchor.Center),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft)
        {
            AbsoluteSpacing = GUI.IntScale(1),
            Stretch = true
        };
        float portraitWidth = (0.8f * characterInfoGroup.Rect.Height) / characterInfoGroup.Rect.Width;
        CreatePortraitColumn(character, characterInfoGroup, portraitWidth);
        var nameAndJobColumn = CreateNameAndJobColumn(character, characterInfoGroup, 0.65f - portraitWidth);
        var skillColumn = CreateSkillColumn(character, characterInfoGroup, width: 0.15f, height: 0.35f);

        // Store references for later updates
        workstationPopupNameBlock = nameAndJobColumn.NameBlock;
        workstationPopupJobBlock = nameAndJobColumn.JobBlock;
        workstationPopupSkillColumn = characterInfoGroup;

        // Recalculate sizes and truncate text if needed
        characterInfoGroup.Recalculate();
        nameAndJobColumn.NameBlock.Text = ToolBox.LimitString(nameAndJobColumn.NameBlock.Text, nameAndJobColumn.NameBlock.Font, nameAndJobColumn.NameBlock.Rect.Width);
        nameAndJobColumn.JobBlock.Text = ToolBox.LimitString(nameAndJobColumn.JobBlock.Text, nameAndJobColumn.JobBlock.Font, nameAndJobColumn.JobBlock.Rect.Width);
        
        minimapContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.83f), leftSideContainer.RectTransform, scaleBasis: ScaleBasis.Smallest),
            style: "InnerFrameDark",
            color: Color.Black);

        var listBoxContainer = new GUIFrame(new RectTransform(new Vector2(0.565f, 1.0f), horizontalContainer.RectTransform),
            style: "InnerFrameDark",
            color: Color.Black);

        // Right side of the panel
        workstationAssignmentListBox = new GUIListBox(new RectTransform(new Vector2(0.98f,0.98f), listBoxContainer.RectTransform, Anchor.Center), style: "GUIListBoxNoBorder")
        {
            CurrentSelectMode = GUIListBox.SelectMode.None
        };
        
        // Populate with data
        RefreshWorkstationList();
        RefreshWorkstationMinimap(minimapContainer);
    }
    
    private void RefreshWorkstationList()
    {
        if (workstationAssignmentCharacter == null || workstationAssignmentListBox == null) { return; }
        
        var availableWorkstations = WorkstationComponent.WorkstationComponents
            .Where(w => w.Item?.Submarine == outpost)
            .OrderBy(w => w.MenuOrder)
            .ThenBy(w => w.Item.Prefab.Name)
            .ToList();

        workstationAssignmentListBox.Content.ClearChildren();

        if (!availableWorkstations.Any())
        {
            new GUITextBlock(new RectTransform(Vector2.One, workstationAssignmentListBox.Content.RectTransform),
                TextManager.Get("dwellerui.noworkstations"),
                textAlignment: Alignment.Center,
                textColor: Color.Gray);
            return;
        }

        foreach (WorkstationComponent workstation in availableWorkstations)
        {
            CreateWorkstationRow(workstation, workstationAssignmentListBox);
        }
    }
    
    private void RefreshWorkstationMinimap(GUIFrame container)
    {
        if (workstationAssignmentCharacter == null || workstationAssignmentListBox == null) { return; }

        var availableWorkstations = WorkstationComponent.WorkstationComponents
            .Where(w => w.Item?.Submarine == outpost)
            .OrderBy(w => w.Item?.Name ?? "")
            .ThenByDescending(w => w.Item?.Position.Y ?? 0f)
            .ThenBy(w => w.Item?.Position.X ?? 0f)
            .ToList();

        var workstationItems = availableWorkstations.Select(w => w.Item).Cast<MapEntity>();
        var minimapSettings = new MiniMapSettings(createHullElements: true, elementColor: Color.Gray * 0.3f);
        MiniMap.CreateMiniMap(outpost, container, minimapSettings, workstationItems,
            out var minimapComponents);
        workstationMinimapComponents = minimapComponents;

        // Color the workstation markers based on utilization
        ResetMinimapMarkers();

        // Set up hover handlers for the workstation list to highlight on minimap
        // Note: We don't use OnHoverEnded to clear state because it fires when moving between
        // child components within a row, causing flickering. Instead, we detect in onUpdate.
        workstationAssignmentListBox.OnHoverStarted = (_, userdata) =>
        {
            if (userdata is WorkstationRowData rowData && rowData.Workstation?.Item is not null)
            {
                // Only reset timer when switching to a different workstation
                if (hoveredWorkstation != rowData.Workstation)
                {
                    hoveredWorkstation = rowData.Workstation;
                    hoverPulseTimer = 0f;
                }
            }
            return true;
        };

        if (minimapContainer == null)
        {
            DebugConsole.ThrowError("The minimap container is missing!");
            return;
        }

        // Draw pulsing effect on the hovered workstation to highlight it
        new GUICustomComponent(new RectTransform(Vector2.One, minimapContainer.RectTransform),
            onUpdate: (deltaTime, component) =>
            {
                // Check if mouse is over any workstation row by walking up the parent hierarchy
                bool mouseOverWorkstationRow = false;
                GUIComponent? mouseOn = GUI.MouseOn;
                while (mouseOn != null)
                {
                    if (mouseOn.UserData is WorkstationRowData)
                    {
                        mouseOverWorkstationRow = true;
                        break;
                    }
                    mouseOn = mouseOn.Parent;
                }

                if (!mouseOverWorkstationRow && hoveredWorkstation != null)
                {
                    // Mouse has left all workstation rows, clear the highlight
                    hoveredWorkstation = null;
                    hoverPulseTimer = 0f;
                }
                else if (hoveredWorkstation != null)
                {
                    hoverPulseTimer += deltaTime;
                }
            },
            onDraw: (spriteBatch, component) =>
            {
                if (hoveredWorkstation?.Item != null &&
                    workstationMinimapComponents != null &&
                    workstationMinimapComponents.TryGetValue(hoveredWorkstation.Item, out var minimapComponent))
                {
                    // Complete cycle every 0.67 seconds
                    float sineWave = (float)Math.Sin(hoverPulseTimer * MathHelper.TwoPi * 0.67f);

                    Rectangle baseRect = minimapComponent.RectComponent.Rect;
                    
                    // Calculate max scale based on minimap size, so the highlighting is the same regardless of the relative size of the markers
                    float targetMaxSize = Math.Min(minimapContainer.Rect.Width, minimapContainer.Rect.Height) * 0.06f; // 6% of minimap size
                    float currentSize = Math.Max(baseRect.Width, baseRect.Height);
                    float maxScale = Math.Max(1.1f, targetMaxSize / currentSize); // The highlight should always be a bit bigger than the marker

                    // Pulse between 100% and calculated max scale
                    float scale = MathHelper.Lerp(1.0f, maxScale, (sineWave + 1f) / 2f);

                    // Pulse between 75% and 100% brightness
                    float brightness = MathHelper.Lerp(0.75f, 1.0f, (sineWave + 1f) / 2f);
                    Color pulseColor = GetWorkstationUtilizationColor(hoveredWorkstation) * brightness;

                    int pulseWidth = (int)(baseRect.Width * scale);
                    int pulseHeight = (int)(baseRect.Height * scale);

                    Rectangle pulseRect = new Rectangle(
                        baseRect.Center.X - pulseWidth / 2,
                        baseRect.Center.Y - pulseHeight / 2,
                        pulseWidth,
                        pulseHeight);

                    GUI.DrawRectangle(spriteBatch, pulseRect, pulseColor, isFilled: true);
                }
            }
        )
        {
            CanBeFocused = false
        };
    }
    
    private void CreateWorkstationRow(WorkstationComponent workstation, GUIListBox targetListBox)
    {
        var workstationRow = new GUIFrame(new RectTransform(new Point(targetListBox.Content.Rect.Width, GUI.IntScale(40)), targetListBox.Content.RectTransform),
            style: "ListBoxElement");
        var workstationRowGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), workstationRow.RectTransform, anchor: Anchor.Center),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft)
        {
            AbsoluteSpacing = GUI.IntScale(5),
            Stretch = true
        };
        
        // Checkmark column
        var checkmarkColumn = new GUIFrame(new RectTransform(new Vector2(0.08f, 1.0f), workstationRowGroup.RectTransform),
            style: null)
        {
            CanBeFocused = false
        };
        
        // Skill column: the workstation's skill and the selected character's level in it
        CreateWorkstationSkillColumn(workstation, workstationRowGroup, width: 0.12f);

        // Workstation name
        var name = new GUITextBlock(new RectTransform(new Vector2(0.38f, 1.0f), workstationRowGroup.RectTransform),
            workstation.Item.Name,
            textColor: workstation.IndicatorColor,
            textAlignment: Alignment.CenterLeft);
        if (name.TextSize.X > name.Rect.Width * 0.95f)
        {
            name.Text = ToolBox.LimitString(workstation.Item.Name, name.Font, (int)(name.Rect.Width * 0.95f));
            name.ToolTip = workstation.Item.Name;
        }

        // Occupancy text
        var occupancyText = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), workstationRowGroup.RectTransform),
            text: "",
            textAlignment: Alignment.Center);

        // Button
        var buttonGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 0.9f), workstationRowGroup.RectTransform), childAnchor: Anchor.CenterRight);
        var assignButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.9f), buttonGroup.RectTransform),
            text: "")
        {
            OnClicked = (btn, userdata) =>
            {
                if (workstationAssignmentCharacter == null || workstation.UniqueId == 0) { return false; }

                // Get current assignment state at click time
                int currentWorkstationId = outpostData.DwellerManager.FindDwellerInfo(workstationAssignmentCharacter)?.AssignedWorkstationId ?? 0;
                bool isCurrentlyAssigned = currentWorkstationId == workstation.UniqueId;
                AssignCharacterToWorkstation(workstationAssignmentCharacter, isCurrentlyAssigned ? 0 : workstation.UniqueId);
                return true;
            }
        };

        // Store references to components that will need updating
        var rowData = new WorkstationRowData(
            workstation,
            checkmarkColumn,
            occupancyText,
            assignButton);
        workstationRow.UserData = rowData;

        // Populate the row with actual data
        UpdateWorkstationRowData(workstationRow, workstation);
    }

    private void CreateWorkstationSkillColumn(WorkstationComponent workstation, GUILayoutGroup rowGroup, float width)
    {
        if (workstation.SkillIdentifier.IsEmpty)
        {
            // Empty placeholder to maintain alignment
            new GUIFrame(new RectTransform(new Vector2(width, 1.0f), rowGroup.RectTransform), style: null) { CanBeFocused = false };
            return;
        }

        float skillLevel = workstationAssignmentCharacter?.GetSkillLevel(workstation.SkillIdentifier) ?? 0f;
        Skill skill = new Skill(workstation.SkillIdentifier, skillLevel);

        LocalizedString tooltip =
            TextManager.Get("fabricatorrequiredskills") + ": " + RichString.ColorizeText(TextManager.Get($"SkillName.{workstation.SkillIdentifier}").Fallback(workstation.SkillIdentifier.Value), GUIStyle.Orange);
        if (workstationAssignmentCharacter != null)
        {
            tooltip = $"{tooltip}\n{workstationAssignmentCharacter.DisplayName}: {(int)(skill.Level)}";
        }

        var skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(width, 0.6f), rowGroup.RectTransform),
            isHorizontal: true,
            childAnchor: Anchor.CenterLeft)
        {
            // Layout groups aren't focusable by default, but the tooltip needs it
            CanBeFocused = true,
            ToolTip = RichString.Rich(tooltip )           
        };
        var skillIcon = new GUIImage(new RectTransform(Vector2.One, skillGroup.RectTransform, scaleBasis: ScaleBasis.Smallest),
            skill.Icon,
            scaleToFit: true)
        {
            CanBeFocused = false
        };
        JobPrefab.Prefabs.TryGet(workstation.OverridingJobIdentifier, out JobPrefab? overridingJobPrefab);
        skillIcon.Color = GetSkillIconColor(skill, overridingJobPrefab);

        float iconWidth = (float)skillGroup.Rect.Height / skillGroup.Rect.Width;
        new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 1.0f), skillGroup.RectTransform),
            ((int)skillLevel).ToString(),
            textAlignment: Alignment.CenterLeft)
        {
            Padding = new Vector4(GUI.IntScale(4), 0, 0, 0),
            CanBeFocused = false
        };
    }
    
    
    private void UpdateWorkstationRowData(GUIFrame rowFrame, WorkstationComponent workstation)
    {
        if (workstationAssignmentCharacter == null) { return; }
        if (rowFrame.UserData is not WorkstationRowData rowData) { return; }

        int currentWorkstationId = outpostData.DwellerManager.FindDwellerInfo(workstationAssignmentCharacter)?.AssignedWorkstationId ?? 0;
        bool assignedToThis = workstation.UniqueId != 0 && currentWorkstationId == workstation.UniqueId;
        bool canManage = CanManageDwellers();
        
        // Update the checkmark
        rowData.CheckmarkColumn.ClearChildren();

        if (assignedToThis)
        {
            new GUIImage(new RectTransform(new Vector2(0.6f, 0.6f), rowData.CheckmarkColumn.RectTransform, anchor: Anchor.Center),
                style: "MissionCompletedIcon",
                scaleToFit: true)
            {
                CanBeFocused = false
            };
        }
        
        // Update the occupancy text
        rowData.OccupancyText.Text = TextManager.GetWithVariables("dwellerui.workstationoccupancy",
            ("[current]", workstation.AssignedNPCCount.ToString()),
            ("[max]", workstation.MaxAssignedNPCs.ToString()));
        rowData.OccupancyText.TextColor = GetWorkstationUtilizationColor(workstation);

        // Update the assign button
        GUIStyle.Apply(rowData.AssignButton.Frame, assignedToThis ? "DwellerUIWorkstationUnassignButton" : "DwellerUIWorkstationAssignButton");
        rowData.AssignButton.ToolTip = canManage ?
            (assignedToThis ? TextManager.Get("dwellerui.removeworkstationassignment") : TextManager.Get("dwellerui.assignworkstation")) :
            TextManager.Get("dwellerui.nopermission");
        rowData.AssignButton.Enabled = canManage;
    }

    private void AskMoveCrewToOutpost(Character character)
    {
        var confirmation = new GUIMessageBox(TextManager.Get("dwellerui.movetooutpost"), 
            text: TextManager.GetWithVariable("dwellerui.movetooutpostconfirm", "[character]", character.Info.DisplayName),
            buttons: [TextManager.Get("yes"), TextManager.Get("no")])
        {
            UserData = "verificationprompt" // handles esc automatically
        };

        confirmation.Buttons[0].OnClicked = (_, _) => 
        {
            outpostData.DwellerManager.MoveCrewMemberToDweller(character);
            
            // UI will be updated via events in both SP and MP
            
            confirmation.Close();
            return true;
        };

        confirmation.Buttons[1].OnClicked = (_, _) => 
        {
            confirmation.Close();
            return true;
        };
    }

    private static Color GetWorkstationUtilizationColor(WorkstationComponent workstation)
    {
        return ((float)workstation.AssignedNPCCount / workstation.MaxAssignedNPCs) switch
        {
            0f => GUIStyle.Red,      // Empty
            1f => GUIStyle.Green,    // Full
            _ => GUIStyle.Yellow     // Partial utilization
        };
    }

    private static void SetWorkstationMinimapColor(MiniMapGUIComponent component, Color baseColor, bool isHighlighted = false)
    {
        component.RectComponent.Color = baseColor * (isHighlighted ? 1.0f : 0.5f);
        component.RectComponent.OutlineColor = baseColor * (isHighlighted ? 1.0f : 0.5f);
        component.RectComponent.OutlineThickness = isHighlighted ? 4 : 2;
    }

    private void ResetMinimapMarkers()
    {
        if (workstationMinimapComponents == null) { return; }
        
        var workstations = WorkstationComponent.WorkstationComponents
            .Where(w => w.Item?.Submarine == outpost);
            
        foreach (var workstation in workstations)
        {
            if (workstation.Item != null && workstationMinimapComponents.TryGetValue(workstation.Item, out var minimapComponent))
            {
                SetWorkstationMinimapColor(minimapComponent, GetWorkstationUtilizationColor(workstation));
            }
        }
    }

    private void MoveToCurrentCrew(Character character)
    {
        // Check if there's room in the crew first
        int currentCrewSize = GameSession.GetSessionCrewCharacters(CharacterType.Both).Count;
        if (currentCrewSize >= CrewManager.MaxCrewSize)
        {
            var errorDialog = new GUIMessageBox(TextManager.Get("dwellerui.movetocrew"),
                TextManager.Get("dwellerui.crewisfull"),
                buttons: [TextManager.Get("ok")]);
            errorDialog.Buttons[0].OnClicked = (_, _) => 
            {
                errorDialog.Close();
                return true;
            };
            return;
        }

        var confirmation = new GUIMessageBox(TextManager.Get("dwellerui.movetocrew"), 
            text: TextManager.GetWithVariable("dwellerui.movetocrewconfirm", "[character]", character.Info.DisplayName),
            buttons: [TextManager.Get("yes"), TextManager.Get("no")])
        {
            UserData = "verificationprompt" // handles esc automatically
        };

        confirmation.Buttons[0].OnClicked = (_, _) => 
        {
            outpostData.DwellerManager.MoveDwellerToCrewMember(character);

            // UI will be updated via events in both SP and MP

            confirmation.Close();
            return true;
        };

        confirmation.Buttons[1].OnClicked = (_, _) => 
        {
            confirmation.Close();
            return true;
        };

        RefreshAllMoveToOutpostButtons();
    }

    private void CreateCharacterPreviewFrame(GUIListBox sourceListBox, GUIFrame characterFrame, CharacterInfo characterInfo)
    {
        var frameSize = new Point(GUI.IntScale(300), GUI.IntScale(350));
        
        // Determine offsets based on which list this box is opening from, the left or the right side one
        var pivot = sourceListBox == dwellersListBox ? Pivot.TopLeft : Pivot.TopRight;
        var absoluteOffset = new Point(
            pivot == Pivot.TopLeft 
                ? sourceListBox.Parent.Parent.Rect.Right + GUI.IntScale(1) 
                : sourceListBox.Parent.Parent.Rect.Left - GUI.IntScale(28), // For TopRight pivot, this positions the right edge of popup
            characterFrame.Rect.Top);
        if (GameMain.GraphicsHeight - (absoluteOffset.Y + frameSize.Y) < 0)
        {
            pivot = sourceListBox == dwellersListBox ? Pivot.BottomLeft : Pivot.BottomRight;
            absoluteOffset.Y = characterFrame.Rect.Bottom;
        }
        
        characterPreviewFrame = new GUIFrame(new RectTransform(frameSize, parent: popupParent.RectTransform, pivot: pivot)
            {
                AbsoluteOffset = absoluteOffset
            },
            style: "InnerFrame")
        {
            UserData = characterInfo
        };
        
        var mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), characterPreviewFrame.RectTransform, anchor: Anchor.Center))
        {
            RelativeSpacing = 0.01f,
            Stretch = true
        };

        // Character info
        var infoGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform),
            isHorizontal: true);
        var infoLabelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), infoGroup.RectTransform))
        {
            Stretch = true
        };
        var infoValueGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), infoGroup.RectTransform))
        {
            Stretch = true
        };
        float blockHeight = 1.0f / 4;
        new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform),
            TextManager.Get("name"),
            textColor: GUIStyle.TextColorBright);
        var nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform),
            text: "");
        string name = characterInfo.DisplayName;
        nameBlock.Text = ToolBox.LimitString(name, nameBlock.Font, nameBlock.Rect.Width);

        if (characterInfo.HasSpecifierTags)
        {
            var menuCategoryVar = characterInfo.Prefab.MenuCategoryVar;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform),
                TextManager.Get(menuCategoryVar),
                textColor: GUIStyle.TextColorBright);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform),
                TextManager.Get(characterInfo.ReplaceVars($"[{menuCategoryVar}]")));
        }

        if (characterInfo.PersonalityTrait is NPCPersonalityTrait trait)
        {
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoLabelGroup.RectTransform),
                TextManager.Get("PersonalityTrait"),
                textColor: GUIStyle.TextColorBright);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), infoValueGroup.RectTransform),
                trait.DisplayName);
        }
        infoLabelGroup.Recalculate();
        infoValueGroup.Recalculate();

        new GUIImage(new RectTransform(new Vector2(1.0f, 0.05f), mainGroup.RectTransform), "HorizontalLine");

        // Skills
        var skillGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.475f), mainGroup.RectTransform),
            isHorizontal: true);
        var skillNameGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), skillGroup.RectTransform));
        var skillLevelGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1.0f), skillGroup.RectTransform));
        if (characterInfo.Job?.GetSkills() is IEnumerable<Skill> characterSkills && characterSkills.Any())
        {
            blockHeight = 1.0f / characterSkills.Count();
            foreach (var skill in characterSkills.OrderByDescending(skill => skill.Level))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillNameGroup.RectTransform),
                    TextManager.Get("SkillName." + skill.Identifier),
                    font: GUIStyle.SmallFont)
                {
                    ToolTip = skill.Description
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, blockHeight), skillLevelGroup.RectTransform),
                    ((int)skill.Level).ToString(),
                    textAlignment: Alignment.Right);
            }
        }
        else
        {
            new GUITextBlock(new RectTransform(Vector2.One, skillNameGroup.RectTransform),
                TextManager.Get("dwellerui.noskills"),
                font: GUIStyle.SmallFont,
                textColor: Color.Gray);
        }

        mainGroup.Recalculate();
        characterPreviewFrame.RectTransform.MinSize = new Point(
            0,
            (int)(mainGroup.Children.Sum(c => c.Rect.Height + mainGroup.Rect.Height * mainGroup.RelativeSpacing) / mainGroup.RectTransform.RelativeSize.Y));
    }

    private void SelectCharacter(GUIListBox? listBox, GUIFrame? characterFrame, CharacterInfo? characterInfo)
    {
        if (characterPreviewFrame != null && characterPreviewFrame.UserData != characterInfo)
        {
            characterPreviewFrame.Parent?.RemoveChild(characterPreviewFrame);
            characterPreviewFrame = null;
        }

        if (listBox == null || characterFrame == null || characterInfo == null) { return; }

        if (characterPreviewFrame == null)
        {
            CreateCharacterPreviewFrame(listBox, characterFrame, characterInfo);
        }
    }

    private delegate void RowCreationDelegate(Character character, GUIListBox listBox);
    
    private GUIFrame? FindCharacterRow(GUIListBox listBox, Character character)
    {
        if (character.Info == null) { return null; }
        
        return listBox.Content.Children
            .OfType<GUIFrame>()
            .FirstOrDefault(frame =>
            {
                // Handle both dweller list (DwellerRowData) and crew list (CharacterInfo)
                if (frame.UserData is DwellerRowData rowData)
                {
                    return rowData.CharacterInfo == character.Info;
                }
                return frame.UserData == character.Info;
            });
    }

    private void AddCharacterToList(GUIListBox? targetListBox, Character character, RowCreationDelegate createRow, 
                                   IEnumerable<Character>? sortOrder = null)
    {
        if (targetListBox == null || FindCharacterRow(targetListBox, character) != null) { return; }

        int targetIndex = -1;
        if (sortOrder != null)
        {
            var sortedList = sortOrder.ToList();
            targetIndex = sortedList.IndexOf(character);
        }
        
        // If we need to insert at a specific position, we need to temporarily move existing items
        if (targetIndex >= 0 && targetIndex < targetListBox.Content.CountChildren)
        {
            // Store references to items that should come after the new item
            var itemsToMove = new List<GUIComponent>();
            for (int i = targetIndex; i < targetListBox.Content.CountChildren; i++)
            {
                itemsToMove.Add(targetListBox.Content.GetChild(i));
            }
            
            // Remove items that need to be repositioned
            foreach (var item in itemsToMove)
            {
                targetListBox.Content.RemoveChild(item);
            }
            
            // Create the new row at the correct position (which is now at the end)
            createRow(character, targetListBox);
            
            // Re-add the moved items after the new one
            foreach (var item in itemsToMove)
            {
                item.RectTransform.Parent = targetListBox.Content.RectTransform;
            }
        }
        else
        {
            // The simplest case, just add to the end
            createRow(character, targetListBox);
        }
        
        targetListBox.Content.RectTransform.RecalculateChildren(resize: true);
    }

    private void RemoveCharacterFromList(GUIListBox? targetListBox, Character character)
    {
        if (targetListBox == null) { return; }
        
        var existingRow = FindCharacterRow(targetListBox, character);
        if (existingRow != null)
        {
            targetListBox.Content.RemoveChild(existingRow);
            targetListBox.Content.RectTransform.RecalculateChildren(resize: true);
        }

        RefreshAllMoveToOutpostButtons();
    }

    private void UpdateCharacterInList(GUIListBox? targetListBox, Character character, RowCreationDelegate createRow,
                                      IEnumerable<Character>? sortOrder = null)
    {
        RemoveCharacterFromList(targetListBox, character);
        AddCharacterToList(targetListBox, character, createRow, sortOrder);
    }

    public void AddDwellerToList(Character character)
        => AddCharacterToList(dwellersListBox, character, CreateDwellerRow, GetSortedDwellerCharacters());

    public void RemoveDwellerFromList(Character character)
    {
        RemoveCharacterFromList(dwellersListBox, character);
        if (character == workstationAssignmentCharacter)
        {
            workstationAssignmentPopup?.Close();
            workstationAssignmentPopup = null;
        }
    }

    public void UpdateDwellerInList(Character character)
    {
        if (dwellersListBox == null) { return; }
        
        var existingRow = FindCharacterRow(dwellersListBox, character);
        if (existingRow != null)
        {
            // Just update existing row data instead of recreating the entire row
            UpdateDwellerRowData(existingRow, character);
        }
        else
        {
            // Row doesn't exist, create it
            UpdateCharacterInList(dwellersListBox, character, CreateDwellerRow, GetSortedDwellerCharacters());
        }
    }

    public void AddCrewMemberToList(Character character)
        => AddCharacterToList(crewListBox, character, CreateCrewMemberRow, GetSortedCrewCharacters());

    public void RemoveCrewMemberFromList(Character character)
    {
        RemoveCharacterFromList(crewListBox, character);
        moveToOutpostButtons.Remove(character);
    }

    public void UpdateCrewMemberInList(Character character)
    {
        if (crewListBox == null) { return; }

        var existingRow = FindCharacterRow(crewListBox, character);
        if (existingRow != null)
        {
            // Update character name color to reflect current job (including job override)
            if (character.Info != null)
            {

                foreach (var nameBlock in existingRow.GetAllChildren<GUITextBlock>().Where(tb =>
                             tb.Text == character.Info.DisplayName))
                {
                    nameBlock.TextColor = character.Info?.Job?.Prefab.UIColor ?? Color.White;
                }
            }
        }
        else
        {
            // Row doesn't exist, create it
            UpdateCharacterInList(crewListBox, character, CreateCrewMemberRow, GetSortedCrewCharacters());
        }
    }

    private void RefreshAllMoveToOutpostButtons()
    {
        foreach (var character in moveToOutpostButtons.Keys)
        {
            RefreshMoveToOutpostButton(character);
        }
    }

    /// <summary>
    /// Updates the state of the button that moves the character from the crew to the crew outpost. The button is hidden if the character is a player or if there's only one crew member remaining.
    /// </summary>
    private void RefreshMoveToOutpostButton(Character character)
    {
        if (moveToOutpostButtons.TryGetValue(character, out GUIButton? button) && button != null)
        {
            button.Visible = !character.IsPlayer && GameSession.GetSessionCrewCharacters(CharacterType.Both).Count > 1;
        }
    }
    
    public void UpdateWorkstationInPopup(WorkstationComponent? workstation)
    {
        if (workstationAssignmentListBox != null && workstation != null)
        {
            // Update the specific workstation row
            var rowFrame = workstationAssignmentListBox.Content.Children
                .OfType<GUIFrame>()
                .FirstOrDefault(frame => frame.UserData is WorkstationRowData rowData && rowData.Workstation == workstation);

            if (rowFrame != null)
            {
                UpdateWorkstationRowData(rowFrame, workstation);
            }
        }
    }

    private IEnumerable<Character> GetSortedDwellerCharacters()
    {
        return SortManageableCharacters(outpostData.DwellerManager.SpawnedCharacters);
    }

    private IEnumerable<Character> GetSortedCrewCharacters()
    {
        return SortManageableCharacters(GameSession.GetSessionCrewCharacters(CharacterType.Both));
    }

    private static IEnumerable<Character> SortManageableCharacters(IEnumerable<Character> source)
    {
        return source
            .Where(character => character is { IsDead: false, Removed: false })
            .OrderBy(character => character.Info?.DisplayName ?? "");
    }
}