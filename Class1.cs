using Gallop;
using Gallop.Endpoints;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using static EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;

namespace EventResponseAnalyzer
{
    public class EventResponseAnalyzer : IPlugin
    {
        const string WorkspaceTitle = "EventResponseAnalyzer";
        const string EventsPanelKey = "events";
        const int EventDisplayPriority = 2;
        const int LegendBuffSelectionPlayingState = 5;
        const int LegendBuffSelectionStoryId = 400010112;
        const int DefaultHistoryLimit = 100;
        const int MaximumHistoryLimit = 1000;

        static readonly Regex ExtractSkillNameRegex = new("「(.*?)」");
        static readonly JsonSerializerOptions SettingsJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };

        readonly object historyGate = new();
        readonly List<HistoryEntry> history = [];
        readonly WorkspaceContent historyContent;

        IApplication? application;
        Workspace? workspace;
        HistoryView? historyView;
        EventDisplayRenderer.DisplaySnapshot? liveSnapshot;
        int historyLimit = DefaultHistoryLimit;
        int selectedHistoryIndex = -1;
        bool initialized;
        bool disposed = true;
        bool hasUnreadHistory;
        bool eventsPublished;

        public EventResponseAnalyzer()
            => historyContent = new(CreateHistoryView);

        public void Initialize(IPluginContext context)
        {
            var settings = LoadSettings();
            lock (historyGate)
            {
                application = context.Application;
                historyLimit = settings.HistoryLimit;
                initialized = true;
                disposed = false;
            }

            context.Analyzers.Register<SingleModeCheckEventResponse>(
                AnalyzerKind.Response,
                [EndpointPattern.Regex(@"^/umamusume/single_mode(?:_[^/]+)?/(?:check_event|exec_command)$")],
                invocation => Analyze(invocation.Endpoint, invocation.Payload),
                EventDisplayPriority);
            context.Analyzers.Register<SingleModeLoadResponse>(
                AnalyzerKind.Response,
                [EndpointPattern.Wildcard("/umamusume/single_mode*/load")],
                invocation => Analyze(invocation.Endpoint, invocation.Payload),
                EventDisplayPriority);
        }

        public void Dispose()
        {
            HistoryView? view;
            Workspace? target;
            lock (historyGate)
            {
                disposed = true;
                initialized = false;
                history.Clear();
                liveSnapshot = null;
                selectedHistoryIndex = -1;
                hasUnreadHistory = false;
                view = historyView;
                historyView = null;
                target = workspace;
                workspace = null;
                application = null;
            }

            view?.Deactivate();
            if (eventsPublished && target is not null)
                target.RemovePanel(EventsPanelKey);
            eventsPublished = false;
        }

        public async Task ConfigPromptAsync(
            IApplication application,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (application.TopRunnable is null &&
                Environment.CurrentManagedThreadId != application.MainThreadId)
            {
                throw new InvalidOperationException(
                    "EventResponseAnalyzer 无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");
            }

            int draft;
            lock (historyGate)
                draft = initialized ? historyLimit : LoadSettings().HistoryLimit;

            int saved;
            if (Environment.CurrentManagedThreadId == application.MainThreadId)
            {
                saved = RunConfigDialog(application, draft, cancellationToken);
            }
            else
            {
                var completion = new TaskCompletionSource<int>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                application.Invoke(() =>
                {
                    try
                    {
                        completion.SetResult(RunConfigDialog(application, draft, cancellationToken));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                });
                saved = await completion.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            SaveSettings(new(saved));
            lock (historyGate)
            {
                if (!initialized || disposed)
                    return;
            }
            ApplyHistoryLimit(saved);
        }

        ValueTask Analyze(GameEndpointDescriptor endpoint, SingleModeCheckEventResponse response)
            => IsLegendEndpoint(endpoint)
                ? AnalyzeLegend(response.data.chara_info, response.data.unchecked_event_array)
                : Analyze(response.data.chara_info, response.data.unchecked_event_array);

        ValueTask Analyze(GameEndpointDescriptor endpoint, SingleModeLoadResponse response)
        {
            var loadCommon = response.data.single_mode_load_common;
            return IsLegendEndpoint(endpoint)
                ? AnalyzeLegend(loadCommon.chara_info, loadCommon.unchecked_event_array)
                : Analyze(loadCommon.chara_info, loadCommon.unchecked_event_array);
        }

        static bool IsLegendEndpoint(GameEndpointDescriptor endpoint)
            => endpoint.Path.StartsWith("/umamusume/single_mode_legend/", StringComparison.Ordinal);

        ValueTask AnalyzeLegend(SingleModeChara charaInfo, SingleModeEventInfo[] uncheckedEvents)
            => Analyze(
                charaInfo,
                uncheckedEvents,
                switchToWorkspace: !IsLegendBuffSelection(charaInfo, uncheckedEvents));

        ValueTask Analyze(
            SingleModeChara charaInfo,
            SingleModeEventInfo[] uncheckedEvents,
            bool switchToWorkspace = true)
        {
            ParseSingleModeCheckEventResponse(charaInfo, uncheckedEvents, switchToWorkspace);
            return ValueTask.CompletedTask;
        }

        void ParseSingleModeCheckEventResponse(
            SingleModeChara charaInfo,
            SingleModeEventInfo[] uncheckedEvents,
            bool switchToWorkspace = true)
        {
            lock (historyGate)
            {
                if (disposed)
                    return;
            }

            var events = ImmutableArray.CreateBuilder<EventDisplayRenderer.Event>(uncheckedEvents.Length);
            foreach (var i in uncheckedEvents)
                events.Add(Database.Events.TryGetValue(i.story_id, out var story)
                    ? BuildKnownEventTree(charaInfo, i, story)
                    : BuildUnknownEventTree(i));

            if (events.Count == 0)
                return;

            var snapshot = new EventDisplayRenderer.DisplaySnapshot(events.ToImmutable());
            Workspace target;
            bool notifyUnread;
            lock (historyGate)
            {
                if (disposed)
                    return;

                target = DisplayWorkspace;
                target.SetPanel(
                    EventsPanelKey,
                    "事件分析",
                    historyContent,
                    fullBleed: true,
                    switchToWorkspace: switchToWorkspace);
                eventsPublished = true;
                notifyUnread = ApplyEventBatchLocked(
                    charaInfo.single_mode_chara_id,
                    charaInfo.turn,
                    snapshot);
            }
            if (notifyUnread)
                target.Notify("有新的事件记录。按 → 查看最新。");
            RefreshHistoryView();
        }

        static bool IsLegendBuffSelection(SingleModeChara charaInfo, SingleModeEventInfo[] uncheckedEvents)
            => charaInfo.playing_state == LegendBuffSelectionPlayingState
               && uncheckedEvents.Any(x => x.story_id == LegendBuffSelectionStoryId);

        static EventDisplayRenderer.Event BuildKnownEventTree(
            SingleModeChara charaInfo,
            SingleModeEventInfo eventInfo,
            Story story)
        {
            var options = ImmutableArray.CreateBuilder<EventDisplayRenderer.Option>(eventInfo.event_contents_info.choice_array.Length);
            for (var j = 0; j < eventInfo.event_contents_info.choice_array.Length; ++j)
            {
                var originalChoice = GetStoryChoice(story, eventInfo.event_contents_info.choice_array[j], j);

                var option = string.IsNullOrEmpty(originalChoice.Option)
                    ? I18N_NoOption
                    : originalChoice.Option;
                var successEffect = EnrichSkillEffect(originalChoice.SuccessEffect, charaInfo);
                var failedEffect = EnrichSkillEffect(originalChoice.FailedEffect, charaInfo);
                ImmutableArray<EventDisplayRenderer.Effect> effects = string.IsNullOrEmpty(originalChoice.FailedEffect)
                    ? [new(successEffect, EventDisplayRenderer.EffectKind.Normal)]
                    : originalChoice.SuccessEffect == I18N_UnknownEffect
                      && originalChoice.FailedEffect == I18N_UnknownEffect
                        ?
                        [
                            new(
                                EventDisplayRenderer.StyledText.Plain(I18N_UnknownEffect),
                                EventDisplayRenderer.EffectKind.Unknown)
                        ]
                        :
                        [
                            new(
                                successEffect.Prepend($"({I18N_WhenSuccess})"),
                                EventDisplayRenderer.EffectKind.Success),
                            new(
                                failedEffect.Prepend($"({I18N_WhenFail})"),
                                EventDisplayRenderer.EffectKind.Failure)
                        ];
                options.Add(new(option, effects));
            }

            return new(story.TriggerName, story.Name, eventInfo.story_id, options.ToImmutable());
        }

        static EventDisplayRenderer.Event BuildUnknownEventTree(SingleModeEventInfo eventInfo)
            => new(
                I18N_UnknownSource,
                I18N_UnknownEvent,
                eventInfo.story_id,
                [.. eventInfo.event_contents_info.choice_array.Select(choice =>
                    new EventDisplayRenderer.Option(
                        string.Format(I18N_UnknownOption, FirstSelectIndex(choice)),
                        [
                            new(
                                EventDisplayRenderer.StyledText.Plain(I18N_UnknownEffect),
                                EventDisplayRenderer.EffectKind.Normal)
                        ]))]);

        static Choice GetStoryChoice(Story story, ChoiceArray choice, int index)
        {
            if (story.Choices.Count <= index || story.Choices[index].Count == 0)
                return new Choice
                {
                    Option = string.Format(I18N_UnknownOption, FirstSelectIndex(choice)),
                    SuccessEffect = I18N_UnknownEffect,
                    FailedEffect = I18N_UnknownEffect,
                };

            var originalChoice = story.Choices[index][0];
            return new Choice
            {
                Option = originalChoice.Option,
                SuccessEffect = originalChoice.SuccessEffect,
                FailedEffect = originalChoice.FailedEffect,
            };
        }

        static EventDisplayRenderer.StyledText EnrichSkillEffect(
            string effect,
            SingleModeChara charaInfo)
        {
            var enriched = new StringBuilder(effect.Length);
            var skillLevels = ImmutableArray.CreateBuilder<EventDisplayRenderer.SkillLevelSpan>();
            var copiedUntil = 0;

            foreach (Match matchedSkillName in ExtractSkillNameRegex.Matches(effect))
            {
                var skillNameGroup = matchedSkillName.Groups[1];
                var skillName = skillNameGroup.Value;
                enriched.Append(effect.AsSpan(copiedUntil, skillNameGroup.Index - copiedUntil));
                if (!Database.Skills.TryFindByName(skillName, out var skill))
                {
                    enriched.Append(skillName);
                    copiedUntil = skillNameGroup.Index + skillNameGroup.Length;
                    continue;
                }

                var tip = charaInfo.skill_tips_array?.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity);
                var level = tip?.level ?? 0;
                var levelText = level.ToString();
                enriched.Append(skillName).Append("(Lv.");
                skillLevels.Add(new(enriched.Length, levelText.Length, level));
                enriched.Append(levelText).Append(')');
                copiedUntil = skillNameGroup.Index + skillNameGroup.Length;
            }

            enriched.Append(effect.AsSpan(copiedUntil));
            return new(enriched.ToString(), skillLevels.ToImmutable());
        }

        static int FirstSelectIndex(ChoiceArray choice)
        {
            return choice.select_index_info_array?.FirstOrDefault()?.select_index ?? 0;
        }

        bool ApplyEventBatchLocked(
            int singleModeCharaId,
            int turn,
            EventDisplayRenderer.DisplaySnapshot snapshot)
        {
            liveSnapshot = snapshot;
            if (historyLimit == 0)
            {
                history.Clear();
                selectedHistoryIndex = -1;
                hasUnreadHistory = false;
                return false;
            }

            EventKey? selectedKey = selectedHistoryIndex >= 0 && selectedHistoryIndex < history.Count
                ? history[selectedHistoryIndex].Key
                : null;
            var wasBrowsingOlder = selectedHistoryIndex >= 0 &&
                                   selectedHistoryIndex < history.Count - 1;
            var hadUnreadHistory = hasUnreadHistory;
            var inserted = false;

            foreach (var item in snapshot.Events)
            {
                var key = new EventKey(singleModeCharaId, turn, item.StoryId);
                var index = history.FindIndex(entry => entry.Key == key);
                if (index >= 0)
                {
                    history[index] = new(key, item);
                }
                else
                {
                    history.Add(new(key, item));
                    inserted = true;
                }
            }

            var overflow = history.Count - historyLimit;
            if (overflow > 0)
                history.RemoveRange(0, overflow);

            var retainedSelection = selectedKey is { } currentKey
                ? history.FindIndex(entry => entry.Key == currentKey)
                : -1;
            if (selectedKey is not null && retainedSelection < 0)
            {
                selectedHistoryIndex = history.Count - 1;
                hasUnreadHistory = false;
            }
            else if (!wasBrowsingOlder || selectedKey is null)
            {
                selectedHistoryIndex = history.Count - 1;
                hasUnreadHistory = false;
            }
            else
            {
                selectedHistoryIndex = retainedSelection;
                if (inserted)
                    hasUnreadHistory = true;
                if (selectedHistoryIndex == history.Count - 1)
                    hasUnreadHistory = false;
            }

            return inserted && wasBrowsingOlder && !hadUnreadHistory && hasUnreadHistory;
        }

        void ApplyHistoryLimit(int value)
        {
            lock (historyGate)
            {
                EventKey? selectedKey = selectedHistoryIndex >= 0 && selectedHistoryIndex < history.Count
                    ? history[selectedHistoryIndex].Key
                    : null;
                historyLimit = value;
                if (value == 0)
                {
                    history.Clear();
                    selectedHistoryIndex = -1;
                    hasUnreadHistory = false;
                }
                else
                {
                    var overflow = history.Count - value;
                    if (overflow > 0)
                        history.RemoveRange(0, overflow);

                    selectedHistoryIndex = selectedKey is { } currentKey
                        ? history.FindIndex(entry => entry.Key == currentKey)
                        : -1;
                    if (selectedHistoryIndex < 0 && history.Count != 0)
                    {
                        selectedHistoryIndex = history.Count - 1;
                        hasUnreadHistory = false;
                    }
                    else if (selectedHistoryIndex == history.Count - 1)
                    {
                        hasUnreadHistory = false;
                    }
                }
            }

            RefreshHistoryView();
        }

        bool TryNavigate(HistoryNavigation navigation)
        {
            EventDisplayRenderer.DisplaySnapshot? snapshot = null;
            HistoryView? view;
            Workspace? target;
            string? notification = null;
            lock (historyGate)
            {
                if (disposed || historyLimit == 0)
                    return false;

                view = historyView;
                target = workspace;
                if (history.Count != 0)
                {
                    if (selectedHistoryIndex < 0 || selectedHistoryIndex >= history.Count)
                        selectedHistoryIndex = history.Count - 1;
                    selectedHistoryIndex = navigation switch
                    {
                        HistoryNavigation.Older => Math.Max(0, selectedHistoryIndex - 1),
                        HistoryNavigation.Newer => Math.Min(history.Count - 1, selectedHistoryIndex + 1),
                        HistoryNavigation.Oldest => 0,
                        HistoryNavigation.Newest => history.Count - 1,
                        _ => selectedHistoryIndex,
                    };
                    if (selectedHistoryIndex == history.Count - 1)
                        hasUnreadHistory = false;
                    snapshot = new([history[selectedHistoryIndex].Event]);
                    notification = $"事件历史 {selectedHistoryIndex + 1}/{history.Count}";
                }
            }

            if (snapshot is not null)
                view?.SetSnapshot(snapshot);
            if (notification is not null)
                target?.Notify(notification);
            return true;
        }

        void RefreshHistoryView()
        {
            IApplication? targetApplication;
            lock (historyGate)
                targetApplication = application;
            if (targetApplication is null)
                return;

            void Refresh()
            {
                HistoryView? view;
                EventDisplayRenderer.DisplaySnapshot snapshot;
                lock (historyGate)
                {
                    if (disposed)
                        return;
                    view = historyView;
                    snapshot = VisibleSnapshotLocked();
                }
                view?.SetSnapshot(snapshot);
            }

            if (Environment.CurrentManagedThreadId == targetApplication.MainThreadId)
                Refresh();
            else
                targetApplication.Invoke(Refresh);
        }

        View CreateHistoryView()
        {
            IApplication targetApplication;
            lock (historyGate)
            {
                targetApplication = application
                    ?? throw new InvalidOperationException("EventResponseAnalyzer 尚未初始化。");
            }
            return new HistoryView(this, targetApplication);
        }

        EventDisplayRenderer.DisplaySnapshot AttachHistoryView(HistoryView view)
        {
            lock (historyGate)
            {
                if (disposed)
                    return new([]);
                historyView = view;
                view.Activate();
                return VisibleSnapshotLocked();
            }
        }

        void DetachHistoryView(HistoryView view)
        {
            lock (historyGate)
            {
                if (ReferenceEquals(historyView, view))
                    historyView = null;
            }
        }

        bool IsHistoryWorkspaceActive(HistoryView view, IApplication targetApplication)
        {
            lock (historyGate)
            {
                if (disposed || !ReferenceEquals(Workspace.Current, workspace))
                    return false;
            }

            for (var focused = targetApplication.TopRunnableView?.MostFocused;
                 focused is not null;
                 focused = focused.SuperView)
            {
                if (ReferenceEquals(focused, view))
                    return true;
            }
            return false;
        }

        EventDisplayRenderer.DisplaySnapshot VisibleSnapshotLocked()
            => historyLimit > 0 && selectedHistoryIndex >= 0 && selectedHistoryIndex < history.Count
                ? new([history[selectedHistoryIndex].Event])
                : liveSnapshot ?? new([]);

        static HistorySettings LoadSettings()
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
                return new(DefaultHistoryLimit);

            HistorySettings settings;
            try
            {
                settings = JsonSerializer.Deserialize<HistorySettings>(
                        File.ReadAllText(path),
                        SettingsJson)
                    ?? throw new JsonException("配置内容为 null。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"EventResponseAnalyzer 配置文件无效: {path}。{ex.Message}",
                    ex);
            }

            ValidateHistoryLimit(settings.HistoryLimit, path);
            return settings;
        }

        static void SaveSettings(HistorySettings settings)
        {
            ValidateHistoryLimit(settings.HistoryLimit, SettingsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(settings, SettingsJson));
        }

        static void ValidateHistoryLimit(int value, string source)
        {
            if (value is < 0 or > MaximumHistoryLimit)
            {
                throw new InvalidDataException(
                    $"EventResponseAnalyzer historyLimit 必须在 0 到 {MaximumHistoryLimit} 之间: {source}，实际值 {value}。");
            }
        }

        static int RunConfigDialog(
            IApplication application,
            int draft,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var dialog = new Dialog
            {
                Title = "EventResponseAnalyzer 配置",
                Width = 62,
                Height = 10,
            };
            var input = new TextField
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill(1),
                Text = draft.ToString(CultureInfo.InvariantCulture),
            };
            var validation = new Label
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill(1),
                Height = 2,
            };
            dialog.Add(
                new Label { X = 1, Y = 1, Text = $"History 上限 (0-{MaximumHistoryLimit})" },
                input,
                validation);

            var accepted = false;
            var result = draft;
            var save = new Button { Text = "保存", IsDefault = true };
            save.Accepting += (_, e) =>
            {
                if (!int.TryParse(
                        input.Text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out result) || result is < 0 or > MaximumHistoryLimit)
                {
                    validation.Text = $"History 上限必须是 0 到 {MaximumHistoryLimit} 之间的整数。";
                    e.Handled = true;
                    return;
                }

                accepted = true;
                application.RequestStop(dialog);
                e.Handled = true;
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, e) =>
            {
                application.RequestStop(dialog);
                e.Handled = true;
            };
            dialog.AddButton(cancel);
            dialog.AddButton(save);
            input.SetFocus();

            using (cancellationToken.Register(
                       () => application.Invoke(() => application.RequestStop(dialog))))
                application.Run(dialog);
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
            {
                throw new OperationCanceledException(
                    "EventResponseAnalyzer 配置已取消。",
                    cancellationToken);
            }
            return result;
        }

        sealed class HistoryView : View
        {
            readonly EventResponseAnalyzer owner;
            readonly IApplication application;
            View? content;
            bool active;

            internal HistoryView(EventResponseAnalyzer owner, IApplication application)
            {
                this.owner = owner;
                this.application = application;
                Id = "event-history-root";
                Width = Dim.Fill();
                Height = Dim.Fill();
                CanFocus = true;
                TabStop = TabBehavior.TabGroup;
                SetSnapshot(owner.AttachHistoryView(this));
            }

            internal void Activate()
            {
                if (active)
                    return;
                active = true;
                application.Keyboard.KeyDown += ApplicationKeyDown;
            }

            internal void SetSnapshot(EventDisplayRenderer.DisplaySnapshot snapshot)
            {
                var focused = Contains(application.TopRunnableView?.MostFocused);
                if (content is not null)
                {
                    Remove(content);
                    content.Dispose();
                }

                content = EventDisplayRenderer.Render(snapshot).CreateView();
                content.Width = Dim.Fill();
                content.Height = Dim.Fill();
                Add(content);
                SetNeedsLayout();
                SetNeedsDraw();
                if (focused)
                    content.SetFocus();
            }

            internal void Deactivate()
            {
                if (!active)
                    return;
                active = false;
                application.Keyboard.KeyDown -= ApplicationKeyDown;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Deactivate();
                    owner.DetachHistoryView(this);
                }
                base.Dispose(disposing);
            }

            void ApplicationKeyDown(object? sender, Key key)
            {
                if (!active || key.Handled || key.IsCtrl || key.IsAlt || key.IsShift ||
                    !owner.IsHistoryWorkspaceActive(this, application))
                {
                    return;
                }

                HistoryNavigation? navigation = key.KeyCode switch
                {
                    var code when code == Key.CursorUp.KeyCode => HistoryNavigation.Older,
                    var code when code == Key.CursorDown.KeyCode => HistoryNavigation.Newer,
                    var code when code == Key.CursorLeft.KeyCode => HistoryNavigation.Oldest,
                    var code when code == Key.CursorRight.KeyCode => HistoryNavigation.Newest,
                    _ => null,
                };
                if (navigation is { } requested && owner.TryNavigate(requested))
                    key.Handled = true;
            }

            bool Contains(View? focused)
            {
                for (; focused is not null; focused = focused.SuperView)
                {
                    if (ReferenceEquals(focused, this))
                        return true;
                }
                return false;
            }
        }

        static string SettingsFilePath
            => Path.Combine("PluginData", WorkspaceTitle, "settings.json");

        sealed record HistorySettings(
            [property: JsonRequired]
            [property: JsonPropertyName("historyLimit")]
            int HistoryLimit);

        readonly record struct EventKey(int SingleModeCharaId, int Turn, int StoryId);
        readonly record struct HistoryEntry(EventKey Key, EventDisplayRenderer.Event Event);

        enum HistoryNavigation
        {
            Older,
            Newer,
            Oldest,
            Newest,
        }

        Workspace DisplayWorkspace => workspace
            ??= Workspace.Create(WorkspaceTitle);
    }

}
