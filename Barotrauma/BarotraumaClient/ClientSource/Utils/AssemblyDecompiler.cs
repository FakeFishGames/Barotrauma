#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ICSharpCode.Decompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class AssemblyDecompiler
    {
        private readonly record struct TypeInfo(
            string TypeName,
            string NameSpace,
            string FullTypeName,
            string TopMostFullTypeName)
        {
            public readonly bool IsTopMost = string.Equals(FullTypeName, TopMostFullTypeName, StringComparison.Ordinal);

            public static TypeInfo CreateHardcoded(string name)
                => new TypeInfo(
                    TypeName: name,
                    NameSpace: string.Empty,
                    FullTypeName: name,
                    TopMostFullTypeName: name);
        }

        private readonly record struct TypeAndLazySource(TypeInfo TypeInfo, Lazy<string> LazySource);

        private static readonly DecompilerSettings Settings = new()
        {
            FileScopedNamespaces = true,
            UsingDeclarations = true,
            ShowXmlDocumentation = true,
            RemoveDeadCode = true,
            AlwaysUseBraces = true,
            ThrowOnAssemblyResolveErrors = false,
            CSharpFormattingOptions = CSharpFormattingOptions
        };

        private static CSharpFormattingOptions CSharpFormattingOptions
        {
            get
            {
                // if we care enough, we could easily allow these to be changed by the user
                CSharpFormattingOptions options = FormattingOptionsFactory.CreateAllman();
                // 4 spaces, tab character doesn't render in GUITextBlock
                options.IndentationString = new string(' ', count: 4);
                return options;
            }
        }

        private static ImmutableArray<RichTextData> SyntaxHighlight(string csharpCode)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(csharpCode, CSharpParseOptions.Default);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            List<RichTextData> builder = [];

            ImmutableArray<(Range<int> Range, int Offset)> lineOffsets = BuildLineOffsets(csharpCode);

            foreach (SyntaxTrivia trivia in root.DescendantTrivia())
            {
                SyntaxKind kind = trivia.Kind();

                switch (kind)
                {
                    case SyntaxKind.SingleLineCommentTrivia:
                    case SyntaxKind.MultiLineCommentTrivia:
                    case SyntaxKind.DocumentationCommentExteriorTrivia:
                    case SyntaxKind.SingleLineDocumentationCommentTrivia:
                    case SyntaxKind.MultiLineDocumentationCommentTrivia:
                        builder.Add(CreateRichTextData(GetTokenPosition(trivia.SpanStart, trivia.Span.End), GUIStyle.Green));
                        break;
                }
            }

            foreach (SyntaxToken token in root.DescendantTokens())
            {
                SyntaxKind kind = token.Kind();
                switch (kind)
                {
                    case SyntaxKind.StringLiteralToken:
                    case SyntaxKind.CharacterLiteralToken:
                    case SyntaxKind.InterpolatedStringStartToken:
                    case SyntaxKind.InterpolatedStringEndToken:
                    case SyntaxKind.InterpolatedVerbatimStringStartToken:
                    case SyntaxKind.InterpolatedStringTextToken:
                        builder.Add(CreateRichTextData(GetTokenPosition(token.SpanStart, token.Span.End), GUIStyle.Yellow));
                        break;
                    case SyntaxKind.NumericLiteralToken:
                        builder.Add(CreateRichTextData(GetTokenPosition(token.SpanStart, token.Span.End), GUIStyle.Red));
                        break;
                }

                // keywords
                if (kind is >= SyntaxKind.BoolKeyword and <= SyntaxKind.ImplicitKeyword
                            or >= SyntaxKind.YieldKeyword and <= SyntaxKind.NotKeyword
                            or >= SyntaxKind.WithKeyword and <= SyntaxKind.ExtensionKeyword
                            or >= SyntaxKind.ElifKeyword and <= SyntaxKind.ReferenceKeyword
                            or >= SyntaxKind.LoadKeyword and <= SyntaxKind.VarKeyword
                            // Highlight operators too, I think makes them easier to read
                            or >= SyntaxKind.BarBarToken and <= SyntaxKind.GreaterThanEqualsToken
                            // Defined individually because there are tokens like OpenParenToken '(' and CloseParenToken ')'
                            // in between that we don't want to highlight
                            or SyntaxKind.PercentToken
                            or SyntaxKind.CaretToken
                            or SyntaxKind.AmpersandToken
                            or SyntaxKind.AsteriskToken
                            or SyntaxKind.MinusToken
                            or SyntaxKind.SlashToken
                            or SyntaxKind.PlusToken
                            or SyntaxKind.BarToken
                    // for some reason 'var' is classified as an IdentifierToken in a lot of places?
                    || (kind is SyntaxKind.IdentifierToken && token.ValueText is "var"))
                {
                    builder.Add(CreateRichTextData(GetTokenPosition(token.SpanStart, token.Span.End), GUIStyle.Blue));
                }
            }

            return [..builder.OrderBy(static r => r.StartIndex)];

            Range<int> GetTokenPosition(int spanStart, int spanEnd)
            {
                int start = spanStart;
                int end = spanEnd - 1;

                // see the comment in BuildLineOffsets() for why this is needed
                int newLines = GetNewLinesSince(start);
                start -= newLines;
                end -= newLines;

                return new Range<int>(start, end);
            }

            int GetNewLinesSince(int position)
            {
                foreach (var (lineRange, offset) in lineOffsets)
                {
                    if (lineRange.Contains(position))
                    {
                        return offset;
                    }
                }

                return 0;
            }

            static RichTextData CreateRichTextData(Range<int> range, Color color)
                => new RichTextData
                {
                    StartIndex = range.Start,
                    EndIndex = range.End,
                    Color = color
                };
        }

        /// <summary>
        /// Builds a list of how many new line characters there are before a given character range.
        /// </summary>
        /// <remarks>
        /// SyntaxToken.SpanStart includes new line characters, but RichTextData is made so it skips over line breaks.
        /// So we need to offset the indices by the number of new lines before the token, else the highlighting will be off
        /// by +1 for each new line.
        /// </remarks>
        private static ImmutableArray<(Range<int> Range, int Offset)> BuildLineOffsets(string text)
        {
            var builder = ImmutableArray.CreateBuilder<(Range<int> Range, int Offset)>();

            for (int charIndex = 0, lineOffset = 0, lineStart = 0; charIndex < text.Length; charIndex++)
            {
                if (text[charIndex] == '\n')
                {
                    builder.Add((Range: new Range<int>(lineStart, charIndex), Offset: lineOffset));
                    lineOffset++;
                    lineStart = charIndex + 1;
                }
            }

            return builder.ToImmutable();
        }

        private static bool TryDecompile(string assemblyPath, [NotNullWhen(true)] out Dictionary<TypeInfo, Lazy<string>>? typesAndSource)
        {
            Dictionary<TypeInfo, Lazy<string>> result = [];

            if (!File.Exists(assemblyPath))
            {
                typesAndSource = result;
                return false;
            }

            CSharpDecompiler decompiler = new CSharpDecompiler(assemblyPath, Settings);

            // Hardcode the assembly attribute info, because it's not an actual type in the assembly and could be useful to see
            result[TypeInfo.CreateHardcoded("<Assembly Info>")]
                = new Lazy<string>(decompiler.DecompileModuleAndAssemblyAttributesToString());

            foreach (ITypeDefinition type in decompiler.TypeSystem.MainModule.TypeDefinitions)
            {
                if (type.IsCompilerGeneratedOrIsInCompilerGeneratedClass()) { continue; }

                TypeInfo typeInfo = new(TypeName: type.Name,
                                        NameSpace: type.Namespace,
                                        FullTypeName: type.FullName,
                                        TopMostFullTypeName: GetTopMostType(type).FullName);

                result[typeInfo] = new Lazy<string>(() =>
                {
                    try
                    {
                        string code = decompiler.DecompileTypeAsString(type.FullTypeName);
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            return "/* Decompilation resulted in empty source */";
                        }

                        return code;
                    }
                    catch (Exception e)
                    {
                        return $"/* Decompilation resulted in exception:\n{e}\n*/";
                    }
                });
            }

            typesAndSource = result;
            return true;

            static ITypeDefinition GetTopMostType(ITypeDefinition type)
            {
                while (type.DeclaringTypeDefinition is not null)
                {
                    type = type.DeclaringTypeDefinition;
                }

                return type;
            }
        }

        public static void OpenDecompilePopup(string assemblyPath)
        {
            GUIMessageBox msgBox = new GUIMessageBox(
                headerText: TextManager.Get("codemodwarning.decompile"),
                text: string.Empty,
                buttons: [],
                relativeSize: (0.75f, 0.75f))
            {
                Header =
                {
                    Font = GUIStyle.Font,
                    AutoScaleHorizontal = true,
                    Wrap = false
                }
            };

            GUIMessageBox.MessageBoxes.Remove(msgBox);
            GUIMessageBox.MessageBoxes.Insert(0, msgBox);

            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform((1f, 1f), msgBox.Content.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            GUILayoutGroup decompileLayout = new GUILayoutGroup(new RectTransform((1.0f, 0.9f), mainLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.005f
            };
            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform((1.0f, 0.1f), mainLayout.RectTransform), childAnchor: Anchor.Center);

            if (!TryDecompile(assemblyPath, out Dictionary<TypeInfo, Lazy<string>>? typesAndSource))
            {
                GUIFrame errorFrame = new GUIFrame(new RectTransform((1f, 1f), decompileLayout.RectTransform), style: "InnerFrame");
                new GUITextBlock(new RectTransform((1f, 1f), errorFrame.RectTransform), TextManager.Get("codemodwarning.decompilefailed"), wrap: true, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
                CreateCloseButton();
                return;
            }

            GUILayoutGroup typeListLayout = new GUILayoutGroup(new RectTransform((0.3f, 1.0f), decompileLayout.RectTransform))
            {
                Stretch = true
            };
            GUITextBox searchBox = GUI.CreateTextBoxWithPlaceholder(new RectTransform((1f, 0.05f), typeListLayout.RectTransform), string.Empty, TextManager.Get("search"));
            GUIListBox typeList = new GUIListBox(new RectTransform((1f, 0.95f), typeListLayout.RectTransform));

            GUIDualScrollArea codeScrollArea = new GUIDualScrollArea(new RectTransform((0.7f, 1f), decompileLayout.RectTransform));
            GUIFrame codeTextParent = new GUIFrame(new RectTransform(Vector2.One, codeScrollArea.Content.RectTransform), style: "GUITextBoxNoBorder");

            GUITextBox codeTextBox = new GUITextBox(new RectTransform(Vector2.One, codeScrollArea.Content.RectTransform, anchor: Anchor.TopLeft), text: string.Empty, font: GUIStyle.MonospacedFont, style: "GUITextBoxNoBorderNoHover", textAlignment: Alignment.TopLeft)
            {
                Readonly = true
            };

            codeTextBox.OnTextChanged += (textBox, text) =>
            {
                Vector2 textSize = textBox.Font.MeasureString(codeTextBox.Text);
                textBox.RectTransform.NonScaledSize = new Point(Math.Max(codeTextParent.Rect.Width, (int)textSize.X + 10), Math.Max(codeTextParent.Rect.Height, (int)textSize.Y + 10));
                codeScrollArea.Scroll(horizontal: 0f, vertical: 0f);
                codeScrollArea.Recalculate();
                return true;
            };

            List<string> shownNamespaces = [];
            string? currentNamespace = null;
            foreach (var (typeInfo, lazySource) in typesAndSource.OrderBy(static kvp => kvp.Key.NameSpace))
            {
                string nameSpace = string.IsNullOrWhiteSpace(typeInfo.NameSpace) ? "<Root Namespace>" : typeInfo.NameSpace;
                if (currentNamespace != nameSpace)
                {
                    currentNamespace = nameSpace;
                    new GUIButton(new RectTransform((1f, 0.05f), typeList.Content.RectTransform), text: nameSpace, style: "GUIButtonSmallFreeScale")
                    {
                        OnClicked = (button, o) =>
                        {
                            if (!shownNamespaces.Remove(typeInfo.NameSpace))
                            {
                                shownNamespaces.Add(typeInfo.NameSpace);
                            }

                            foreach (GUIComponent child in typeList.Content.Children)
                            {
                                if (child.UserData is not TypeAndLazySource userData) { continue; }

                                child.Visible = shownNamespaces.Contains(userData.TypeInfo.NameSpace) && userData.TypeInfo.IsTopMost;
                            }

                            return true;
                        }
                    };
                }

                GUIFrame typeFrame = new GUIFrame(new RectTransform((1f, 0.05f), typeList.Content.RectTransform), style: "ListBoxElement")
                {
                    Visible = false,
                    ToolTip = typeInfo.FullTypeName,
                    UserData = new TypeAndLazySource(typeInfo, lazySource)
                };

                // checkbox for when the source has been viewed once
                new GUITickBox(new RectTransform(Vector2.One, typeFrame.RectTransform, Anchor.CenterLeft), typeInfo.TypeName, style: "ViewedTickBox")
                {
                    Selected = false,
                    CanBeFocused = false
                };
            }

            typeList.OnSelected += (element, userdata) =>
            {
                if (userdata is not TypeAndLazySource typeAndSource) { return false; }

                element.GetChild<GUITickBox>().Selected = true;

                foreach (GUIComponent c in typeList.Content.Children)
                {
                    if (c.UserData is not TypeAndLazySource otherTypeAndSource) { continue; }

                    if (string.Equals(otherTypeAndSource.TypeInfo.TopMostFullTypeName, typeAndSource.TypeInfo.TopMostFullTypeName, StringComparison.Ordinal))
                    {
                        c.GetChild<GUITickBox>().Selected = true;
                    }
                }

                string text = typeAndSource.LazySource.Value
                                           .Replace("\r", string.Empty); // GUITextBlock doesn't handle \r\n line endings properly, so strip \r

                ImmutableArray<RichTextData> richText = SyntaxHighlight(text);
                codeTextBox.TextBlock.Text = text;
                codeTextBox.TextBlock.OverrideRichTextData = richText;
                codeTextBox.InvokeOnTextChanged();
                msgBox.Header.Text = typeAndSource.TypeInfo.FullTypeName;
                return true;
            };

            searchBox.OnTextChanged += (box, text) =>
            {
                // reset visibility
                if (string.IsNullOrWhiteSpace(text))
                {
                    foreach (GUIComponent child in typeList.Content.Children)
                    {
                        if (child.UserData is TypeAndLazySource typeAndLazySource)
                        {
                            child.Visible = shownNamespaces.Contains(typeAndLazySource.TypeInfo.NameSpace) && typeAndLazySource.TypeInfo.IsTopMost;
                        }
                        else
                        {
                            child.Visible = true;
                        }
                    }

                    return true;
                }

                foreach (GUIComponent child in typeList.Content.Children)
                {
                    if (child.UserData is not TypeAndLazySource userData)
                    {
                        child.Visible = false;
                        continue;
                    }

                    child.Visible = userData.TypeInfo.FullTypeName.Contains(text, StringComparison.OrdinalIgnoreCase);
                }

                return true;
            };

            CreateCloseButton();
            return;

            void CreateCloseButton()
            {
                new GUIButton(new RectTransform((0.5f, 1f), buttonLayout.RectTransform, Anchor.Center), TextManager.Get("Close"))
                {
                    OnClicked = (btn, userdata) =>
                    {
                        msgBox.Close();
                        return true;
                    }
                };
            }
        }
    }
}