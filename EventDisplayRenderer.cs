using System.Collections.Immutable;
using System.Drawing;
using System.Globalization;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer.TerminalGui;
using TAttribute = Terminal.Gui.Drawing.Attribute;
using TColor = Terminal.Gui.Drawing.Color;

namespace EventResponseAnalyzer;

internal static class EventDisplayRenderer
{
    internal sealed record DisplaySnapshot(ImmutableArray<Event> Events);
    internal sealed record Event(string TriggerName, string Name, int StoryId, ImmutableArray<Option> Options);
    internal sealed record Option(string Text, ImmutableArray<Effect> Effects);
    internal sealed record Effect(StyledText Text, EffectKind Kind);
    internal sealed record StyledText(string Value, ImmutableArray<SkillLevelSpan> SkillLevels)
    {
        internal static StyledText Plain(string value) => new(value, []);

        internal StyledText Prepend(string prefix)
            => new(
                prefix + Value,
                [.. SkillLevels.Select(x => x with { Start = x.Start + prefix.Length })]);
    }

    internal readonly record struct SkillLevelSpan(int Start, int Length, int Level);

    internal enum EffectKind
    {
        Normal,
        Success,
        Failure,
        Unknown
    }

    internal static WorkspaceContent Render(DisplaySnapshot snapshot)
        => new(() => new EventTreeView(snapshot));

    sealed class EventTreeView : View
    {
        readonly TreeNode[] roots;
        RenderLine[] renderedLines = [];

        public EventTreeView(DisplaySnapshot snapshot)
        {
            Id = "event-response-root";
            Width = Dim.Fill();
            Height = Dim.Fill();
            CanFocus = true;
            TabStop = TabBehavior.TabGroup;
            ViewportSettings = ViewportSettingsFlags.HasScrollBars;

            roots = [.. snapshot.Events.Select(item =>
                new TreeNode(
                    [new(StyledText.Plain(item.TriggerName), EffectKind.Normal)],
                    [
                        new(
                            [new(StyledText.Plain($"{item.Name}({item.StoryId})"), EffectKind.Normal)],
                            [.. item.Options.Select(option =>
                                new TreeNode(
                                    [new(StyledText.Plain(option.Text), EffectKind.Normal)],
                                    [
                                        new(
                                            [.. option.Effects.Select(effect =>
                                                new StyledLine(effect.Text, effect.Kind))],
                                            [])
                                    ]))]
                        )
                    ]))];
        }

        protected override void OnSubViewLayout(LayoutEventArgs args)
        {
            var visibleWidth = Math.Max(1, Viewport.Width);
            var visibleHeight = Math.Max(1, Viewport.Height);
            renderedLines = LayoutTree(visibleWidth);

            var contentSize = new Size(visibleWidth, Math.Max(visibleHeight, renderedLines.Length));
            if (GetContentSize() != contentSize)
                SetContentSize(contentSize);

            var maxY = Math.Max(0, contentSize.Height - visibleHeight);
            if (Viewport.X != 0 || Viewport.Y > maxY)
            {
                Viewport = new Rectangle(
                    0,
                    Math.Min(Viewport.Y, maxY),
                    Viewport.Width,
                    Viewport.Height);
            }

            base.OnSubViewLayout(args);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var firstLine = Math.Max(0, Viewport.Y);
            var lastLine = Math.Min(renderedLines.Length, firstLine + Viewport.Height);
            for (var lineIndex = firstLine; lineIndex < lastLine; lineIndex++)
            {
                var x = 0;
                var y = lineIndex - firstLine;
                foreach (var run in renderedLines[lineIndex].Runs)
                {
                    SetAttribute(run.Attribute);
                    Move(x, y);
                    AddStr(run.Text);
                    x += run.Text.GetColumns();
                }
            }

            return true;
        }

        RenderLine[] LayoutTree(int width)
        {
            var lines = new List<RenderLine>();
            foreach (var root in roots)
                AddNode(root, true, true, [], width, lines);
            return [.. lines];
        }

        void AddNode(
            TreeNode node,
            bool isRoot,
            bool isLast,
            bool[] ancestorIsLast,
            int width,
            List<RenderLine> lines)
        {
            var prefix = isRoot
                ? string.Empty
                : string.Concat(ancestorIsLast.Select(x => x ? "    " : "│   "))
                  + (isLast ? "└── " : "├── ");
            prefix = FitPrefix(prefix, isLast, width);
            var continuationPrefix = new string(' ', prefix.GetColumns());

            for (var lineIndex = 0; lineIndex < node.Lines.Length; lineIndex++)
            {
                var logicalPrefix = lineIndex == 0 ? prefix : continuationPrefix;
                AddWrappedLine(node.Lines[lineIndex], logicalPrefix, width, lines);
            }

            var childAncestors = isRoot
                ? ancestorIsLast
                : [.. ancestorIsLast, isLast];
            for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
            {
                AddNode(
                    node.Children[childIndex],
                    false,
                    childIndex == node.Children.Length - 1,
                    childAncestors,
                    width,
                    lines);
            }
        }

        void AddWrappedLine(
            StyledLine line,
            string prefix,
            int width,
            List<RenderLine> lines)
        {
            var prefixWidth = prefix.GetColumns();
            var textWidth = Math.Max(1, width - prefixWidth);
            var wrapped = Wrap(line.Text, line.Kind, textWidth);

            for (var lineIndex = 0; lineIndex < wrapped.Length; lineIndex++)
            {
                var runs = new List<DrawRun>();
                if (prefixWidth > 0)
                {
                    runs.Add(new(
                        lineIndex == 0 ? prefix : new string(' ', prefixWidth),
                        GetAttributeForRole(VisualRole.Normal)));
                }

                runs.AddRange(wrapped[lineIndex]);
                lines.Add(new([.. runs]));
            }
        }

        DrawRun[][] Wrap(StyledText text, EffectKind kind, int width)
        {
            var tokens = Tokenize(text, kind);
            var lines = new List<DrawRun[]>();
            var current = new List<Glyph>();
            var currentWidth = 0;

            foreach (var token in tokens)
            {
                if (token.IsNewLine)
                {
                    Flush(true);
                    continue;
                }

                var tokenWidth = token.Glyphs.Sum(x => x.Width);
                if (token.IsWhitespace)
                {
                    if (current.Count > 0 && currentWidth + tokenWidth <= width)
                    {
                        current.AddRange(token.Glyphs);
                        currentWidth += tokenWidth;
                    }
                    else if (current.Count > 0)
                    {
                        Flush(false);
                    }

                    continue;
                }

                if (tokenWidth <= width)
                {
                    if (current.Count > 0 && currentWidth + tokenWidth > width)
                        Flush(false);
                    current.AddRange(token.Glyphs);
                    currentWidth += tokenWidth;
                    continue;
                }

                if (current.Count > 0)
                    Flush(false);
                foreach (var glyph in token.Glyphs)
                {
                    if (current.Count > 0 && currentWidth + glyph.Width > width)
                        Flush(false);
                    current.Add(glyph);
                    currentWidth += glyph.Width;
                }
            }

            if (current.Count > 0 || lines.Count == 0)
                Flush(true);
            return [.. lines];

            void Flush(bool allowEmpty)
            {
                while (current.Count > 0 && current[^1].IsWhitespace)
                {
                    currentWidth -= current[^1].Width;
                    current.RemoveAt(current.Count - 1);
                }

                if (current.Count > 0 || allowEmpty)
                    lines.Add(MergeRuns(current));
                current.Clear();
                currentWidth = 0;
            }
        }

        Token[] Tokenize(StyledText text, EffectKind kind)
        {
            var tokens = new List<Token>();
            var glyphs = new List<Glyph>();
            bool? isWhitespace = null;
            var normal = GetAttributeForRole(VisualRole.Normal);
            var enumerator = StringInfo.GetTextElementEnumerator(text.Value);

            while (enumerator.MoveNext())
            {
                var element = (string)enumerator.Current;
                if (element is "\r" or "\n" or "\r\n")
                {
                    FlushToken();
                    tokens.Add(new([], false, true));
                    continue;
                }

                var elementIsWhitespace = string.IsNullOrWhiteSpace(element);
                if (isWhitespace is not null && isWhitespace != elementIsWhitespace)
                    FlushToken();
                isWhitespace = elementIsWhitespace;

                var level = text.SkillLevels.FirstOrDefault(x =>
                    enumerator.ElementIndex >= x.Start
                    && enumerator.ElementIndex < x.Start + x.Length);
                var attribute = level.Length == 0
                    ? AttributeFor(kind, normal)
                    : AttributeForLevel(level.Level, normal);
                glyphs.Add(new(
                    element,
                    Math.Max(0, element.GetColumns()),
                    attribute,
                    elementIsWhitespace));
            }

            FlushToken();
            return [.. tokens];

            void FlushToken()
            {
                if (glyphs.Count == 0)
                    return;
                tokens.Add(new([.. glyphs], isWhitespace == true, false));
                glyphs.Clear();
                isWhitespace = null;
            }
        }

        static DrawRun[] MergeRuns(List<Glyph> glyphs)
        {
            var runs = new List<DrawRun>();
            var text = new StringBuilder();
            TAttribute? attribute = null;

            foreach (var glyph in glyphs)
            {
                if (attribute is not null && !attribute.Value.Equals(glyph.Attribute))
                {
                    runs.Add(new(text.ToString(), attribute.Value));
                    text.Clear();
                }

                attribute = glyph.Attribute;
                text.Append(glyph.Text);
            }

            if (attribute is not null)
                runs.Add(new(text.ToString(), attribute.Value));
            return [.. runs];
        }

        static string FitPrefix(string prefix, bool isLast, int width)
        {
            if (prefix.GetColumns() < width)
                return prefix;
            if (width >= 3)
                return isLast ? "└─" : "├─";
            if (width == 2)
                return isLast ? "└" : "├";
            return string.Empty;
        }

        static TAttribute AttributeFor(EffectKind kind, TAttribute normal)
            => kind switch
            {
                EffectKind.Success => WithForeground(new("#00FA9A"), normal),
                EffectKind.Failure => WithForeground(new("#FF0050"), normal),
                EffectKind.Unknown => WithForeground(new("#AFAFAF"), normal),
                _ => normal
            };

        static TAttribute AttributeForLevel(int level, TAttribute normal)
            => WithForeground(
                new(level switch
                {
                    <= 0 => StandardColor.Red,
                    < 5 => StandardColor.Yellow,
                    _ => StandardColor.Green
                }),
                normal);

        static TAttribute WithForeground(TColor foreground, TAttribute normal)
            => new(foreground, normal.Background, normal.Style);

        sealed record TreeNode(StyledLine[] Lines, TreeNode[] Children);
        readonly record struct StyledLine(StyledText Text, EffectKind Kind);
        readonly record struct RenderLine(DrawRun[] Runs);
        readonly record struct DrawRun(string Text, TAttribute Attribute);
        readonly record struct Glyph(string Text, int Width, TAttribute Attribute, bool IsWhitespace);
        readonly record struct Token(Glyph[] Glyphs, bool IsWhitespace, bool IsNewLine);
    }
}
