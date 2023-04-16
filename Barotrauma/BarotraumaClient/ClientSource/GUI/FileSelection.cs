#nullable enable
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public static class FileSelection
    {
        private static bool open;
        public static bool Open
        {
            get
            {
                return open;
            }
            set
            {
                if (value) { InitIfNecessary(); }
                if (!value)
                {
                    fileSystemWatcher?.Dispose();
                    fileSystemWatcher = null;
                }
                open = value;
            }
        }

        private static GUIFrame? backgroundFrame;
        private static GUIFrame? window;
        private static GUIListBox? sidebar;
        private static GUIListBox? fileList;
        private static GUITextBox? directoryBox;
        private static GUITextBox? filterBox;
        private static GUITextBox? fileBox;
        private static GUIDropDown? fileTypeDropdown;
        private static GUIButton? openButton;

        private static System.IO.FileSystemWatcher? fileSystemWatcher;

        private enum ItemIsDirectory
        {
            Yes, No
        }

        private static string? currentFileTypePattern;

        private static readonly string[] ignoredDrivePrefixes =
        {
            "/sys/", "/snap/"
        };

        private static string currentDirectory = "";
        public static string CurrentDirectory
        {
            get
            {
                return currentDirectory;
            }
            set
            {
                string[] dirSplit = value.Replace('\\', '/').Split('/');
                List<string> dirs = new List<string>();
                for (int i = 0; i < dirSplit.Length; i++)
                {
                    if (dirSplit[i].Trim() == "..")
                    {
                        if (dirs.Count > 1)
                        {
                            dirs.RemoveAt(dirs.Count - 1);
                        }
                    }
                    else if (dirSplit[i].Trim() != ".")
                    {
                        dirs.Add(dirSplit[i]);
                    }
                }
                currentDirectory = string.Join("/", dirs);
                if (!currentDirectory.EndsWith("/"))
                {
                    currentDirectory += "/";
                }
                fileSystemWatcher?.Dispose();
                fileSystemWatcher = new System.IO.FileSystemWatcher(currentDirectory)
                {
                    Filter = "*",
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName
                };
                fileSystemWatcher.Created += OnFileSystemChanges;
                fileSystemWatcher.Deleted += OnFileSystemChanges;
                fileSystemWatcher.Renamed += OnFileSystemChanges;
                fileSystemWatcher.EnableRaisingEvents = true;
                RefreshFileList();
            }
        }

        public static Action<string>? OnFileSelected
        {
            get;
            set;
        }

        private static void OnFileSystemChanges(object sender, System.IO.FileSystemEventArgs e)
        {
            if (fileList is null) { return; }
            switch (e.ChangeType)
            {
                case System.IO.WatcherChangeTypes.Created:
                    {
                        var itemFrame = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), fileList.Content.RectTransform), e.Name ?? string.Empty)
                        {
                            UserData = Directory.Exists(e.FullPath) ? ItemIsDirectory.Yes : ItemIsDirectory.No
                        };
                        if (itemFrame.UserData is ItemIsDirectory.Yes)
                        {
                            itemFrame.Text += "/";
                        }
                        fileList.Content.RectTransform.SortChildren(SortFiles);
                    }
                    break;
                case System.IO.WatcherChangeTypes.Deleted:
                    {
                        var itemFrame = fileList.Content.FindChild(c => (c is GUITextBlock tb) && (tb.Text == e.Name || tb.Text == e.Name + "/"));
                        if (itemFrame != null) { fileList.RemoveChild(itemFrame); }
                    }
                    break;
                case System.IO.WatcherChangeTypes.Renamed:
                    {
                        System.IO.RenamedEventArgs renameArgs = e as System.IO.RenamedEventArgs ?? throw new InvalidCastException($"Unable to cast {nameof(System.IO.FileSystemEventArgs)} to {nameof(System.IO.RenamedEventArgs)}.");
                        var itemFrame =
                            fileList.Content.FindChild(c => (c is GUITextBlock tb) && (tb.Text == renameArgs.OldName || tb.Text == renameArgs.OldName + "/")) as GUITextBlock
                            ?? throw new Exception($"Could not find file list item with name \"{renameArgs.OldName}\"");
                        itemFrame.UserData = Directory.Exists(e.FullPath) ? ItemIsDirectory.Yes : ItemIsDirectory.No;
                        itemFrame.Text = renameArgs.Name ?? string.Empty;
                        if (itemFrame.UserData is ItemIsDirectory.Yes)
                        {
                            itemFrame.Text += "/";
                        }
                        fileList.Content.RectTransform.SortChildren(SortFiles);
                    }
                    break;
            }
        }

        private static int SortFiles(RectTransform r1, RectTransform r2)
        {
            string file1 = (r1.GUIComponent as GUITextBlock)?.Text?.SanitizedValue ?? "";
            string file2 = (r2.GUIComponent as GUITextBlock)?.Text?.SanitizedValue ?? "";
            bool dir1 = r1.GUIComponent.UserData is ItemIsDirectory.Yes;
            bool dir2 = r2.GUIComponent.UserData is ItemIsDirectory.Yes;
            if (dir1 && !dir2)
            {
                return -1;
            }
            else if (!dir1 && dir2)
            {
                return 1;
            }

            return string.Compare(file1, file2);
        }

        private static void InitIfNecessary()
        {
            if (backgroundFrame == null) { Init(); }
        }
        
        public static void Init()
        {
            backgroundFrame = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas), style: null)
            {
                Color = Color.Black * 0.5f,
                HoverColor = Color.Black * 0.5f,
                SelectedColor = Color.Black * 0.5f,
                PressedColor = Color.Black * 0.5f,
            };

            window = new GUIFrame(new RectTransform(Vector2.One * 0.8f, backgroundFrame.RectTransform, Anchor.Center));

            var horizontalLayout = new GUILayoutGroup(new RectTransform(Vector2.One * 0.9f, window.RectTransform, Anchor.Center), true);
            sidebar = new GUIListBox(new RectTransform(new Vector2(0.29f, 1.0f), horizontalLayout.RectTransform))
            {
                PlaySoundOnSelect = true
            };

            var drives = System.IO.DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.DriveType == System.IO.DriveType.Ram) { continue; }
                if (ignoredDrivePrefixes.Any(p => drive.Name.StartsWith(p))) { continue; }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), sidebar.Content.RectTransform), drive.Name.Replace('\\','/'));
            }

            sidebar.OnSelected = (child, userdata) =>
            {
                CurrentDirectory = (child as GUITextBlock)?.Text.SanitizedValue ?? throw new Exception("Sidebar selection is invalid");

                return false;
            };

            //spacing between sidebar and fileListLayout
            new GUIFrame(new RectTransform(new Vector2(0.01f, 1.0f), horizontalLayout.RectTransform), style: null);

            var fileListLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1.0f), horizontalLayout.RectTransform));
            var firstRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), fileListLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), firstRow.RectTransform), "^")
            {
                OnClicked = MoveToParentDirectory
            };
            directoryBox = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), firstRow.RectTransform))
            {
                OverflowClip = true,
                OnEnterPressed = (tb, txt) =>
                {
                    if (Directory.Exists(txt))
                    {
                        CurrentDirectory = txt;
                        return true;
                    }
                    else
                    {
                        tb.Text = CurrentDirectory;
                        return false;
                    }
                }
            };
            filterBox = new GUITextBox(new RectTransform(new Vector2(0.25f, 1.0f), firstRow.RectTransform))
            {
                OverflowClip = true
            };
            firstRow.RectTransform.MinSize = new Point(0, firstRow.RectTransform.Children.Max(c => c.MinSize.Y));

            filterBox.OnTextChanged += (txtbox, txt) =>
            {
                RefreshFileList();
                return true;
            };
            //spacing between rows
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), fileListLayout.RectTransform), style: null);

            fileList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.85f), fileListLayout.RectTransform))
            {
                PlaySoundOnSelect = true,
                OnSelected = (child, userdata) =>
                {
                    if (userdata is null) { return false; }
                    if (fileBox is null) { return false; }

                    var fileName = (child as GUITextBlock)!.Text.SanitizedValue;
                    fileBox.Text = fileName;
                    if (PlayerInput.DoubleClicked())
                    {
                        bool isDir = userdata is ItemIsDirectory.Yes;
                        if (isDir)
                        {
                            CurrentDirectory += fileName;
                        }
                        else
                        {
                            OnFileSelected?.Invoke(CurrentDirectory + fileName);
                            Open = false;
                        }
                    }

                    return true;
                }
            };

            //spacing between rows
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), fileListLayout.RectTransform), style: null);

            var thirdRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), fileListLayout.RectTransform), true);
            fileBox = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), thirdRow.RectTransform))
            {
                OnEnterPressed = (tb, txt) => openButton?.OnClicked?.Invoke(openButton, null) ?? false
            };

            fileTypeDropdown = new GUIDropDown(new RectTransform(new Vector2(0.3f, 1.0f), thirdRow.RectTransform), dropAbove: true)
            {
                OnSelected = (child, userdata) =>
                {
                    currentFileTypePattern = (child as GUITextBlock)!.UserData as string;
                    RefreshFileList();

                    return true;
                }
            };

            fileTypeDropdown.Select(4);

            //spacing between rows
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), fileListLayout.RectTransform), style: null);
            var fourthRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), fileListLayout.RectTransform), true);

            //padding for open/cancel buttons
            new GUIFrame(new RectTransform(new Vector2(0.7f, 1.0f), fourthRow.RectTransform), style: null);

            openButton = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), fourthRow.RectTransform), TextManager.Get("opensubbutton"))
            {
                OnClicked = (btn, obj) =>
                {
                    if (Directory.Exists(Path.Combine(CurrentDirectory, fileBox.Text)))
                    {
                        CurrentDirectory += fileBox.Text;
                    }
                    if (!File.Exists(CurrentDirectory + fileBox.Text)) { return false; }
                    OnFileSelected?.Invoke(CurrentDirectory + fileBox.Text);
                    Open = false;
                    return false;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), fourthRow.RectTransform), TextManager.Get("cancel"))
            {
                OnClicked = (btn, obj) =>
                {
                    Open = false;
                    return false;
                }
            };

            CurrentDirectory = Directory.GetCurrentDirectory();
        }

        public static void ClearFileTypeFilters()
        {
            InitIfNecessary();
            fileTypeDropdown!.ClearChildren();
        }

        public static void AddFileTypeFilter(string name, string pattern)
        {
            InitIfNecessary();
            fileTypeDropdown!.AddItem(name + " (" + pattern + ")", pattern);
        }

        public static void SelectFileTypeFilter(string pattern)
        {
            InitIfNecessary();
            fileTypeDropdown!.SelectItem(pattern);
        }

        public static void RefreshFileList()
        {
            InitIfNecessary();
            fileList!.Content.ClearChildren();
            fileList.BarScroll = 0.0f;

            try
            {
                var directories = Directory.EnumerateDirectories(currentDirectory, "*" + filterBox!.Text + "*");
                foreach (var directory in directories)
                {
                    string txt = directory;
                    if (txt.StartsWith(currentDirectory)) { txt = txt.Substring(currentDirectory.Length); }
                    if (!txt.EndsWith("/")) { txt += "/"; }
                    //get directory info
                    DirectoryInfo dirInfo = new DirectoryInfo(directory);
                    try
                    {
                        //this will throw an exception if the directory can't be opened
                        Directory.GetDirectories(directory);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    var itemFrame = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), fileList.Content.RectTransform), txt)
                    {
                        UserData = ItemIsDirectory.Yes
                    };
                    var folderIcon = new GUIImage(new RectTransform(new Point((int)(itemFrame.Rect.Height * 0.8f)), itemFrame.RectTransform, Anchor.CenterLeft)
                    {
                        AbsoluteOffset = new Point((int)(itemFrame.Rect.Height * 0.25f), 0)
                    }, style: "OpenButton", scaleToFit: true);
                    itemFrame.Padding = new Vector4(folderIcon.Rect.Width * 1.5f, itemFrame.Padding.Y, itemFrame.Padding.Z, itemFrame.Padding.W);
                }

                IEnumerable<string> files = Enumerable.Empty<string>();
                if (currentFileTypePattern.IsNullOrEmpty())
                {
                    files = Directory.GetFiles(currentDirectory);
                }
                else
                {
                    foreach (string pattern in currentFileTypePattern!.Split(','))
                    {
                        string patternTrimmed = pattern.Trim();
                        patternTrimmed = "*" + filterBox.Text + "*" + patternTrimmed;
                        if (files.None())
                        {
                            files = Directory.EnumerateFiles(currentDirectory, patternTrimmed);
                        }
                        else
                        {
                            files = files.Concat(Directory.EnumerateFiles(currentDirectory, patternTrimmed));
                        }
                    }
                }

                foreach (var file in files)
                {
                    string txt = file;
                    if (txt.StartsWith(currentDirectory)) { txt = txt.Substring(currentDirectory.Length); }
                    var itemFrame = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), fileList.Content.RectTransform), txt)
                    {
                        UserData = ItemIsDirectory.No
                    };
                }
            }
            catch (Exception e)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), fileList.Content.RectTransform), "Could not list items in directory: " + e.Message)
                {
                    CanBeFocused = false
                };
            }

            directoryBox!.Text = currentDirectory;
            fileBox!.Text = "";
            fileList.Deselect();
        }

        public static bool MoveToParentDirectory(GUIButton button, object userdata)
        {
            string dir = CurrentDirectory;
            if (dir.EndsWith("/")) { dir = dir.Substring(0, dir.Length - 1); }
            int index = dir.LastIndexOf("/");
            if (index < 0) { return false; }
            CurrentDirectory = CurrentDirectory.Substring(0, index + 1);

            return true;
        }

        public static void AddToGUIUpdateList()
        {
            if (!Open) { return; }
            backgroundFrame?.AddToGUIUpdateList();
        }
    }
}
