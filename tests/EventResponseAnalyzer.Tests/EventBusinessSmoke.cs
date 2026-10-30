using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using EventI18n = EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;
using TAttribute = Terminal.Gui.Drawing.Attribute;
using TColor = Terminal.Gui.Drawing.Color;

static class EventBusinessSmoke
{
    internal static async Task Run(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("EventResponse-business");
        InitializeHostConfigForSmoke();
        AssertEventResponseAnalyzerEndpointCoverage(ui.Application);
        await AssertEventResponseAnalyzerDisplayStates(ui);
        await AssertEventResponseAnalyzerHistory(ui);
        await AssertEventResponseAnalyzerLegendExecCommandRendersEventPanel(ui);
    }
static void AssertEventResponseAnalyzerEndpointCoverage(IApplication application)
{
    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(application);
    plugin.Initialize(context);
    if (context.AnalyzerRegistry.Registrations.Count != 2)
        throw new InvalidOperationException(
            $"EventResponseAnalyzer must use exactly 2 programmatic registrations, actual={context.AnalyzerRegistry.Registrations.Count}.");

    var actual = context.AnalyzerRegistry.Registrations
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .SelectMany(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns))
        .Select(endpoint => endpoint.EndpointType)
        .Distinct()
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    var expected = new[]
    {
        typeof(GameApi.SingleMode.CheckEvent),
        typeof(GameApi.SingleMode.ExecCommand),
        typeof(GameApi.SingleMode.Load),
        typeof(GameApi.SingleModeArc.CheckEvent),
        typeof(GameApi.SingleModeArc.ExecCommand),
        typeof(GameApi.SingleModeArc.Load),
        typeof(GameApi.SingleModeBreeders.CheckEvent),
        typeof(GameApi.SingleModeBreeders.ExecCommand),
        typeof(GameApi.SingleModeBreeders.Load),
        typeof(GameApi.SingleModeCook.CheckEvent),
        typeof(GameApi.SingleModeCook.ExecCommand),
        typeof(GameApi.SingleModeCook.Load),
        typeof(GameApi.SingleModeFesta.CheckEvent),
        typeof(GameApi.SingleModeFesta.ExecCommand),
        typeof(GameApi.SingleModeFesta.Load),
        typeof(GameApi.SingleModeFree.CheckEvent),
        typeof(GameApi.SingleModeFree.ExecCommand),
        typeof(GameApi.SingleModeFree.Load),
        typeof(GameApi.SingleModeLegend.CheckEvent),
        typeof(GameApi.SingleModeLegend.ExecCommand),
        typeof(GameApi.SingleModeLegend.Load),
        typeof(GameApi.SingleModeLive.CheckEvent),
        typeof(GameApi.SingleModeLive.ExecCommand),
        typeof(GameApi.SingleModeLive.Load),
        typeof(GameApi.SingleModeMecha.CheckEvent),
        typeof(GameApi.SingleModeMecha.ExecCommand),
        typeof(GameApi.SingleModeMecha.Load),
        typeof(GameApi.SingleModeOnsen.CheckEvent),
        typeof(GameApi.SingleModeOnsen.ExecCommand),
        typeof(GameApi.SingleModeOnsen.Load),
        typeof(GameApi.SingleModePioneer.CheckEvent),
        typeof(GameApi.SingleModePioneer.ExecCommand),
        typeof(GameApi.SingleModePioneer.Load),
        typeof(GameApi.SingleModeRamen.CheckEvent),
        typeof(GameApi.SingleModeRamen.ExecCommand),
        typeof(GameApi.SingleModeRamen.Load),
        typeof(GameApi.SingleModeSport.CheckEvent),
        typeof(GameApi.SingleModeSport.ExecCommand),
        typeof(GameApi.SingleModeSport.Load),
        typeof(GameApi.SingleModeTeam.CheckEvent),
        typeof(GameApi.SingleModeTeam.ExecCommand),
        typeof(GameApi.SingleModeTeam.Load),
        typeof(GameApi.SingleModeVenus.CheckEvent),
        typeof(GameApi.SingleModeVenus.ExecCommand),
        typeof(GameApi.SingleModeVenus.Load),
    }.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    if (!actual.SequenceEqual(expected))
        throw new InvalidOperationException(
            "EventResponseAnalyzer response endpoint coverage mismatch."
            + $" expected=[{string.Join(", ", expected.Select(x => x.FullName))}],"
            + $" actual=[{string.Join(", ", actual.Select(x => x.FullName))}]");
    plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static async ValueTask AssertEventResponseAnalyzerDisplayStates(WorkspaceSmokeSession ui)
{
    WriteEventResponseAnalyzerHistorySettings(0);
    var originalCulture = EventI18n.Culture;
    var captures = new Dictionary<string, EventRenderCapture>();
    var narrowText = string.Empty;

    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(ui.Application);
    var target = Workspace.Create("EventResponseAnalyzer");
    target.SwitchTo();
    var emptyWorkspace = CaptureEventScreen(ui);
    var hostNormal = RequireEventAttribute(
        emptyWorkspace,
        RequireEventPoint(emptyWorkspace, "EventResponseAnalyzer 还没有输出。"));
    ui.Bootstrap.SwitchTo();
    try
    {
        EventI18n.Culture = CultureInfo.GetCultureInfo("zh-CN");
        plugin.Initialize(context);
        var skillFixtures = new UmamusumeResponseAnalyzer.Entities.SkillData[]
        {
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910001,
                    GroupId = 91001,
                    Rarity = 1,
                    Rate = 1,
                    Name = "零级技能",
                    Propers = []
                },
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910002,
                    GroupId = 91002,
                    Rarity = 1,
                    Rate = 1,
                    Name = "四级技能",
                    Propers = []
                },
                new UmamusumeResponseAnalyzer.Entities.SkillData
                {
                    Id = 910003,
                    GroupId = 91003,
                    Rarity = 1,
                    Rate = 1,
                    Name = "五级技能",
                    Propers = []
                }
        };
        var charaInfo = new SingleModeChara
        {
            skill_tips_array =
            [
                new() { group_id = 91001, rarity = 1, level = 0 },
                new() { group_id = 91002, rarity = 1, level = 4 },
                new() { group_id = 91003, rarity = 1, level = 5 }
            ]
        };

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143706,
                "[角色] 维克托瓦尔皮萨",
                "满天的星空",
                new Choice
                {
                    Option = "我也想试着连接星星",
                    SuccessEffect = "速度 +10"
                })
        ], skillFixtures);
        captures["single"] = await Render([CreateEvent(501143706, 1)]);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143707,
                "训练事件",
                "胜负结果",
                new Choice
                {
                    Option = "挑战",
                    SuccessEffect = "体力 +10",
                    FailedEffect = "体力 -10"
                })
        ], skillFixtures);
        captures["dual"] = await Render([CreateEvent(501143707, 1)]);

        await InitializeSmokeDatabase([], skillFixtures);
        captures["unknown"] = await Render([CreateEvent(999999999, 7)]);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143708,
                "长文本来源",
                "长文本事件",
                new Choice
                {
                    Option = "这是一个非常长的选择项文本，用来确认窄窗口下会自然换行，并且末尾内容不被水平截断",
                    SuccessEffect = "这是同样需要在狭窄宽度下完整换行显示的效果文本"
                })
        ], skillFixtures);
        captures["long"] = await Render([CreateEvent(501143708, 1)]);
        narrowText = ui.CaptureScreen(42, 30);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143709,
                "第一来源",
                "第一个事件",
                new Choice
                {
                    Option = string.Empty,
                    SuccessEffect = "体力 -10"
                })
        ], skillFixtures);
        captures["multiple"] = await Render(
        [
            CreateEvent(501143709, 1, 2),
            CreateEvent(999999998, 3)
        ]);
        foreach (var key in new[] { Key.CursorLeft, Key.CursorUp, Key.CursorRight, Key.CursorDown })
            ui.SendKey(key);
        captures["multipleAfterArrows"] = CaptureEventScreen(ui);

        await InitializeSmokeDatabase(
        [
            CreateStory(
                501143710,
                "技能事件",
                "技能等级",
                new Choice
                {
                    Option = "查看提示等级",
                    SuccessEffect = "获得「零级技能」「四级技能」「五级技能」提示"
                })
        ], skillFixtures);
        captures["skills"] = await Render([CreateEvent(501143710, 1)]);

        async ValueTask<EventRenderCapture> Render(SingleModeEventInfo[] events)
        {
            var response = new SingleModeCheckEventResponse
            {
                data = new()
                {
                    chara_info = charaInfo,
                    unchecked_event_array = events
                }
            };
            await DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode/check_event",
                MessagePackSerializer.Serialize(response));
            var framebuffer = ui.CaptureScreen();
            if (!ReferenceEquals(Workspace.Current, target))
                throw new InvalidOperationException("EventResponseAnalyzer did not switch to its canonical workspace.");
            if (!framebuffer.Contains(events[0].story_id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidOperationException("EventResponseAnalyzer content is not visible in the real Host framebuffer.");

            return CaptureEventScreen(ui);
        }
    }
    finally
    {
        await InitializeSmokeDatabase();
        EventI18n.Culture = originalCulture;
        await plugin.DisposeAsync();
    }

    target.SwitchTo();
    if (!ReferenceEquals(Workspace.Create("eventresponseanalyzer"), target)
        || ui.CaptureScreen().Contains("事件分析", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "EventResponseAnalyzer Dispose must remove its events panel without removing the shared workspace generation.");
    }

    var single = captures["single"];
    RequireEventText(single, "[角色] 维克托瓦尔皮萨");
    RequireEventText(single, "我也想试着连接星星");
    RequireEventText(single, "速度 +10");
    RequireEventLine(single, "└── 满天的星空(501143706)");
    RequireEventLine(single, "    └── 我也想试着连接星星");
    RequireEventLine(single, "        └── 速度 +10");
    foreach (var text in new[]
             {
                 "[角色] 维克托瓦尔皮萨",
                 "满天的星空",
                 "501143706",
                 "我也想试着连接星星",
                 "└── 满天的星空",
                 "速度 +10"
             })
    {
        RequireEvent(
            RequireEventAttribute(single, RequireEventPoint(single, text)).Equals(hostNormal),
            $"'{text}' did not inherit the Host normal attribute.");
    }

    var dual = captures["dual"];
    var successLine = RequireEventLine(dual, "        └── (成功时)体力 +10");
    var failureLine = RequireEventLine(dual, "            (失败时)体力 -10");
    RequireEvent(
        failureLine.Row == successLine.Row + 1,
        "success/failure rows no longer share one Spectre-style effect node.");
    var success = RequireEventPoint(dual, "(成功时)体力 +10");
    var failure = RequireEventPoint(dual, "(失败时)体力 -10");
    RequireEventForegroundOnly(
        dual,
        success,
        new("#00FA9A"),
        hostNormal,
        "success effect did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        dual,
        failure,
        new("#FF0050"),
        hostNormal,
        "failure effect did not preserve the Host normal background and style.");

    var unknown = captures["unknown"];
    RequireEventLine(unknown, "未知来源");
    RequireEventLine(unknown, "└── 未知事件(999999999)");
    RequireEventLine(unknown, "    └── 未知选项 @ 7");
    RequireEventLine(unknown, "        └── 未知效果");
    foreach (var text in new[] { "未知来源", "未知事件", "999999999", "未知选项 @ 7", "未知效果" })
    {
        RequireEvent(
            RequireEventAttribute(unknown, RequireEventPoint(unknown, text)).Equals(hostNormal),
            $"unknown-event text '{text}' did not inherit the Host normal attribute.");
    }

    RequireEvent(
        narrowText.Contains("这是一个非常长的选择项文本", StringComparison.Ordinal)
        && narrowText.Contains("水平截断", StringComparison.Ordinal)
        && narrowText.Contains("这是同样需要在狭窄宽度下", StringComparison.Ordinal)
        && narrowText.Contains("效果文本", StringComparison.Ordinal),
        "The real narrow Host framebuffer did not keep the long event content visible.");

    var multiple = captures["multiple"];
    RequireEventLine(multiple, "第一来源");
    RequireEventLine(multiple, "└── 第一个事件(501143709)");
    RequireEventLine(multiple, "    ├── 无选项");
    var negativeLine = RequireEventLine(multiple, "    │   └── 体力 -10");
    RequireEventLine(multiple, "    └── 未知选项 @ 2");
    var firstUnknownEffect = RequireEventLine(multiple, "        └── 未知效果", occurrence: 0);
    var secondSource = RequireEventLine(multiple, "未知来源", occurrence: 0);
    RequireEvent(
        secondSource.Row == firstUnknownEffect.Row + 1,
        "multiple Spectre-style trees should be adjacent without an artificial section gap.");
    var negative = RequireEventPoint(multiple, "体力 -10");
    RequireEvent(
        RequireEventAttribute(multiple, negative).Equals(hostNormal),
        "ordinary negative effect did not inherit the Host normal attribute.");
    var firstUnknown = RequireEventPoint(multiple, "未知效果", occurrence: 0);
    RequireEventForegroundOnly(
        multiple,
        firstUnknown,
        new("#AFAFAF"),
        hostNormal,
        "known unknown option did not preserve the Host normal background and style.");
    var unknownEventEffect = RequireEventPoint(multiple, "未知效果", occurrence: 1);
    RequireEvent(
        RequireEventAttribute(multiple, unknownEventEffect).Equals(hostNormal),
        "unknown-event effect did not inherit the Host normal attribute.");
    RequireEvent(negativeLine.Row < secondSource.Row, "multiple events are not rendered in source order.");
    RequireEvent(
        multiple.Text == captures["multipleAfterArrows"].Text,
        "historyLimit=0 must keep the complete live event batch instead of navigating retained entries.");

    var skills = captures["skills"];
    RequireEvent(
        !skills.Text.Contains("501143709", StringComparison.Ordinal)
        && !skills.Text.Contains("999999998", StringComparison.Ordinal),
        "historyLimit=0 did not replace the preceding complete response batch.");
    RequireEventText(skills, "零级技能(Lv.0)");
    RequireEventText(skills, "四级技能(Lv.4)");
    RequireEventText(skills, "五级技能(Lv.5)");
    var levelZero = RequireEventPoint(skills, "Lv.0");
    var levelPartial = RequireEventPoint(skills, "Lv.4");
    var levelMaximum = RequireEventPoint(skills, "Lv.5");
    RequireEventForegroundOnly(
        skills,
        new(levelZero.X + 3, levelZero.Y),
        new(StandardColor.Red),
        hostNormal,
        "level zero did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        skills,
        new(levelPartial.X + 3, levelPartial.Y),
        new(StandardColor.Yellow),
        hostNormal,
        "partial skill level did not preserve the Host normal background and style.");
    RequireEventForegroundOnly(
        skills,
        new(levelMaximum.X + 3, levelMaximum.Y),
        new(StandardColor.Green),
        hostNormal,
        "maximum skill level did not preserve the Host normal background and style.");
    RequireEvent(
        RequireEventAttribute(skills, levelZero).Equals(hostNormal),
        "skill level styling leaked into the Lv. prefix or changed its inherited attribute.");

    static Story CreateStory(int id, string triggerName, string name, params Choice[] choices)
        => new()
        {
            Id = id,
            TriggerName = triggerName,
            Name = name,
            Choices = [.. choices.Select(choice => new List<Choice> { choice })]
        };

    static SingleModeEventInfo CreateEvent(int storyId, params int[] selectIndices)
        => new()
        {
            story_id = storyId,
            event_contents_info = new EventContentsInfo
            {
                choice_array = [.. selectIndices.Select(CreateChoice)]
            }
        };

    static ChoiceArray CreateChoice(int selectIndex)
        => new()
        {
            select_index_info_array =
            [
                new SingleModeSelectIndexInfo { select_index = selectIndex }
            ]
        };
}

static async ValueTask AssertEventResponseAnalyzerHistory(WorkspaceSmokeSession ui)
{
    var originalCulture = EventI18n.Culture;
    try
    {
        EventI18n.Culture = CultureInfo.GetCultureInfo("zh-CN");
        await InitializeSmokeDatabase([]);
        await AssertIndexedHistory();
        await AssertTrimmedHistory();
    }
    finally
    {
        EventI18n.Culture = originalCulture;
        await InitializeSmokeDatabase();
    }

    async ValueTask AssertIndexedHistory()
    {
        WriteEventResponseAnalyzerHistorySettings(16);
        var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
        using var context = new RuntimePluginContext(ui.Application);
        try
        {
            plugin.Initialize(context);

            const int storyA = 990_001;
            const int storyB = 990_002;
            const int storyC = 990_003;
            const int storyAcrossEndpoints = 990_004;
            const int longStory = 990_005;

            await DispatchCheck(context.AnalyzerRegistry, 11, 22,
                CreateHistoryEvent(storyA, 1),
                CreateHistoryEvent(storyB, 2),
                CreateHistoryEvent(storyA, 3));
            RequireCurrent(storyB, 2, storyA);

            Navigate(Key.CursorLeft, storyA, 3, storyB);
            Navigate(Key.CursorDown, storyB, 2, storyA);
            Navigate(Key.CursorUp, storyA, 3, storyB);
            Navigate(Key.CursorRight, storyB, 2, storyA);

            await DispatchCheck(context.AnalyzerRegistry, 12, 22, CreateHistoryEvent(storyA, 4));
            RequireCurrent(storyA, 4);
            await DispatchCheck(context.AnalyzerRegistry, 11, 23, CreateHistoryEvent(storyA, 5));
            RequireCurrent(storyA, 5);
            await DispatchCheck(context.AnalyzerRegistry, 11, 22, CreateHistoryEvent(storyC, 6));
            RequireCurrent(storyC, 6);

            await DispatchCheck(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 7));
            await DispatchExecCommand(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 8));
            await DispatchLoad(context.AnalyzerRegistry, 30, 40, CreateHistoryEvent(storyAcrossEndpoints, 9));
            RequireCurrent(storyAcrossEndpoints, 9);

            Navigate(Key.CursorLeft, storyA, 3, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyB, 2, storyA, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyA, 4, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyA, 5, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyC, 6, storyA, storyB, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyAcrossEndpoints, 9, storyA, storyB, storyC);
            Navigate(Key.CursorDown, storyAcrossEndpoints, 9, storyA, storyB, storyC);

            Navigate(Key.CursorLeft, storyA, 3, storyB, storyC, storyAcrossEndpoints);
            await DispatchCheck(context.AnalyzerRegistry, 11, 22, CreateHistoryEvent(storyA, 10));
            RequireCurrent(storyA, 10, storyB, storyC, storyAcrossEndpoints);
            Navigate(Key.CursorDown, storyB, 2, storyA, storyC, storyAcrossEndpoints);

            Navigate(Key.CursorRight, storyAcrossEndpoints, 9, storyA, storyB, storyC);
            await DispatchCheck(
                context.AnalyzerRegistry,
                50,
                60,
                CreateHistoryEvent(longStory, [.. Enumerable.Range(1, 60)]));
            RequireCurrent(longStory, 1, storyA, storyB, storyC, storyAcrossEndpoints);

            var paged = ui.CaptureScreen();
            if (paged.Contains("未知选项 @ 60", StringComparison.Ordinal))
                throw new InvalidOperationException("The long Event history entry unexpectedly started at its final page.");
            for (var i = 0; i < 10 && !paged.Contains("未知选项 @ 60", StringComparison.Ordinal); i++)
            {
                ui.SendKey(Key.PageDown);
                paged = ui.CaptureScreen();
            }
            if (!paged.Contains("未知选项 @ 60", StringComparison.Ordinal))
                throw new InvalidOperationException("PageDown did not reach the end of a long Event history entry.");

            for (var i = 0; i < 10 && !paged.Contains("未知选项 @ 1", StringComparison.Ordinal); i++)
            {
                ui.SendKey(Key.PageUp);
                paged = ui.CaptureScreen();
            }
            if (!paged.Contains("未知选项 @ 1", StringComparison.Ordinal))
                throw new InvalidOperationException("PageUp did not return to the start of a long Event history entry.");

            Navigate(Key.CursorUp, storyAcrossEndpoints, 9, storyA, storyB, storyC, longStory);
            Navigate(Key.CursorDown, longStory, 1, storyA, storyB, storyC, storyAcrossEndpoints);
        }
        finally
        {
            await plugin.DisposeAsync();
        }

        void Navigate(Key key, int storyId, int selectIndex, params int[] hiddenStoryIds)
        {
            ui.SendKey(key);
            RequireCurrent(storyId, selectIndex, hiddenStoryIds);
        }
    }

    async ValueTask AssertTrimmedHistory()
    {
        WriteEventResponseAnalyzerHistorySettings(4);
        var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
        using var context = new RuntimePluginContext(ui.Application);
        try
        {
            plugin.Initialize(context);

            const int storyA = 991_001;
            const int storyB = 991_002;
            const int storyC = 991_003;
            const int storyD = 991_004;
            const int storyE = 991_005;
            const int storyF = 991_006;

            await DispatchCheck(context.AnalyzerRegistry, 70, 80,
                CreateHistoryEvent(storyA, 1),
                CreateHistoryEvent(storyB, 2));
            RequireCurrent(storyB, 2, storyA);
            ui.SendKey(Key.CursorLeft);
            RequireCurrent(storyA, 1, storyB);

            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyC, 3));
            RequireCurrent(storyA, 1, storyB, storyC);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyD, 4));
            RequireCurrent(storyA, 1, storyB, storyC, storyD);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyE, 5));
            RequireCurrent(storyE, 5, storyA, storyB, storyC, storyD);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyF, 6));
            RequireCurrent(storyF, 6, storyA, storyB, storyC, storyD, storyE);

            ui.SendKey(Key.CursorLeft);
            RequireCurrent(storyC, 3, storyA, storyB, storyD, storyE, storyF);
            await DispatchCheck(context.AnalyzerRegistry, 70, 80, CreateHistoryEvent(storyC, 77));
            RequireCurrent(storyC, 77, storyA, storyB, storyD, storyE, storyF);
            ui.SendKey(Key.CursorRight);
            RequireCurrent(storyF, 6, storyA, storyB, storyC, storyD, storyE);
        }
        finally
        {
            await plugin.DisposeAsync();
        }
    }

    void RequireCurrent(int storyId, int selectIndex, params int[] hiddenStoryIds)
    {
        var screen = ui.CaptureScreen();
        if (!ReferenceEquals(Workspace.Current, Workspace.Create("EventResponseAnalyzer")))
            throw new InvalidOperationException("Event history navigation left the canonical workspace.");
        if (!screen.Contains(storyId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !screen.Contains($"未知选项 @ {selectIndex}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected Event history ({storyId}, select={selectIndex}) is not visible in the real Host framebuffer.");
        }

        foreach (var hiddenStoryId in hiddenStoryIds)
        {
            if (screen.Contains(hiddenStoryId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidOperationException($"Event history unexpectedly rendered story {hiddenStoryId} in the selected entry.");
        }
    }

    static SingleModeEventInfo CreateHistoryEvent(int storyId, params int[] selectIndices)
        => new()
        {
            story_id = storyId,
            event_contents_info = new EventContentsInfo
            {
                choice_array = [.. selectIndices.Select(selectIndex => new ChoiceArray
                {
                    select_index_info_array = [new SingleModeSelectIndexInfo { select_index = selectIndex }]
                })]
            }
        };

    static SingleModeChara CreateHistoryChara(int charaId, int turn)
        => new()
        {
            single_mode_chara_id = charaId,
            turn = turn,
            skill_tips_array = []
        };

    static ValueTask DispatchCheck(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/check_event",
            MessagePackSerializer.Serialize(new SingleModeCheckEventResponse
            {
                data = new()
                {
                    chara_info = CreateHistoryChara(charaId, turn),
                    unchecked_event_array = events
                }
            }));

    static ValueTask DispatchExecCommand(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/exec_command",
            MessagePackSerializer.Serialize(new SingleModeExecCommandResponse
            {
                data = new()
                {
                    chara_info = CreateHistoryChara(charaId, turn),
                    unchecked_event_array = events
                }
            }));

    static ValueTask DispatchLoad(
        RecordingAnalyzerRegistry registry,
        int charaId,
        int turn,
        params SingleModeEventInfo[] events)
        => DispatchHostResponse(
            registry,
            "https://api.games.umamusume.jp/umamusume/single_mode/load",
            MessagePackSerializer.Serialize(new SingleModeLoadResponse
            {
                data = new()
                {
                    single_mode_load_common = new SingleModeLoadCommon
                    {
                        chara_info = CreateHistoryChara(charaId, turn),
                        unchecked_event_array = events
                    }
                }
            }));
}

static EventRenderCapture CaptureEventScreen(WorkspaceSmokeSession ui)
{
    var copy = ui.CaptureCells();
    return new(copy, new(0, 0, copy.GetLength(1), copy.GetLength(0)));
}

static (int Row, string Text) RequireEventLine(
    EventRenderCapture capture,
    string text,
    int occurrence = 0)
{
    for (var y = capture.Region.Top; y < capture.Region.Bottom; y++)
    {
        var line = capture.GetLine(y);
        if (!line.Contains(text, StringComparison.Ordinal))
            continue;
        if (occurrence-- == 0)
            return (y, line);
    }

    throw new InvalidOperationException($"EventResponseAnalyzer render is missing line '{text}'.");
}

static Point RequireEventPoint(
    EventRenderCapture capture,
    string text,
    int occurrence = 0)
{
    for (var y = capture.Region.Top; y < capture.Region.Bottom; y++)
    {
        var line = capture.GetLine(y);
        var searchFrom = 0;
        while (searchFrom <= line.Length - text.Length)
        {
            var index = line.IndexOf(text, searchFrom, StringComparison.Ordinal);
            if (index < 0)
                break;
            if (occurrence-- == 0)
                return new(capture.GetColumn(y, index), y);
            searchFrom = index + text.Length;
        }
    }

    throw new InvalidOperationException($"EventResponseAnalyzer render is missing '{text}'.");
}

static TAttribute RequireEventAttribute(EventRenderCapture capture, Point point)
    => capture.Cells[point.Y, point.X].Attribute
        ?? throw new InvalidOperationException(
            $"EventResponseAnalyzer render cell at ({point.X}, {point.Y}) has no attribute.");

static void RequireEventForegroundOnly(
    EventRenderCapture capture,
    Point point,
    TColor foreground,
    TAttribute normal,
    string message)
    => RequireEvent(
        RequireEventAttribute(capture, point).Equals(
            new TAttribute(foreground, normal.Background, normal.Style)),
        message);

static void RequireEventText(EventRenderCapture capture, string text)
    => RequireEvent(
        capture.Text.Contains(text, StringComparison.Ordinal),
        $"EventResponseAnalyzer render is missing '{text}'.");

static void RequireEvent(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async ValueTask AssertEventResponseAnalyzerLegendExecCommandRendersEventPanel(WorkspaceSmokeSession ui)
{
    var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
    using var context = new RuntimePluginContext(ui.Application);
    var target = Workspace.Create("EventResponseAnalyzer");
    try
    {
        plugin.Initialize(context);
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/exec_command",
                MessagePackSerializer.Serialize(CreateExecCommandResponse(830241003))),
            switchesToTarget: true,
            storyId: 830241003,
            "Legend ExecCommand group card event");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/load",
                MessagePackSerializer.Serialize(CreateLoadResponse(830241003))),
            switchesToTarget: true,
            storyId: 830241003,
            "Legend Load group card event");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/check_event",
                MessagePackSerializer.Serialize(CreateCheckEventResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend CheckEvent buff selection");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/exec_command",
                MessagePackSerializer.Serialize(CreateExecCommandResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend ExecCommand buff selection");
        await AssertPanelUpdate(
            () => DispatchHostResponse(
                context.AnalyzerRegistry,
                "https://api.games.umamusume.jp/umamusume/single_mode_legend/load",
                MessagePackSerializer.Serialize(CreateLoadResponse(400010112))),
            switchesToTarget: false,
            storyId: 400010112,
            "Legend Load buff selection");
    }
    finally
    {
        await plugin.DisposeAsync();
    }

    target.SwitchTo();
    if (!ReferenceEquals(Workspace.Create("eventresponseanalyzer"), target)
        || ui.CaptureScreen().Contains("400010112", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "EventResponseAnalyzer Dispose must remove its events panel without removing the shared workspace generation.");
    }

    async ValueTask AssertPanelUpdate(Func<ValueTask> dispatch, bool switchesToTarget, int storyId, string scenario)
    {
        target.RemovePanel("events");
        ui.Bootstrap.SwitchTo();
        ui.Flush();

        await dispatch();
        var expectedCurrent = switchesToTarget ? target : ui.Bootstrap;
        if (!ReferenceEquals(Workspace.Current, expectedCurrent))
        {
            throw new InvalidOperationException(
                $"{scenario}: expected final workspace '{expectedCurrent.Title}', actual '{Workspace.Current?.Title ?? "<null>"}'.");
        }
        target.SwitchTo();
        if (!ui.CaptureScreen().Contains(storyId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidOperationException($"{scenario}: final events panel is not visible in the real Host framebuffer.");
        expectedCurrent.SwitchTo();
    }

    static SingleModeLegendCheckEventResponse CreateCheckEventResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendCheckEventResponse.CommonResponse
            {
                chara_info = CreateLegendEventChara(),
                unchecked_event_array = [CreateLegendEvent(storyId)]
            }
        };

    static SingleModeLegendExecCommandResponse CreateExecCommandResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendExecCommandResponse.CommonResponse
            {
                chara_info = CreateLegendEventChara(),
                unchecked_event_array = [CreateLegendEvent(storyId)]
            }
        };

    static SingleModeLegendLoadResponse CreateLoadResponse(int storyId)
        => new()
        {
            data = new SingleModeLegendLoadResponse.CommonResponse
            {
                single_mode_load_common = new SingleModeLoadCommon
                {
                    chara_info = CreateLegendEventChara(),
                    unchecked_event_array = [CreateLegendEvent(storyId)]
                }
            }
        };

    static SingleModeChara CreateLegendEventChara()
        => new()
        {
            state = 0,
            playing_state = 5,
            turn = 25,
            skill_tips_array = []
        };

    static SingleModeEventInfo CreateLegendEvent(int storyId)
        => new()
        {
            event_id = 21151,
            story_id = storyId,
            play_timing = 6,
            event_contents_info = new EventContentsInfo
            {
                support_card_id = 30241,
                choice_array = [CreateChoice(1), CreateChoice(2), CreateChoice(3)]
            }
        };

    static ChoiceArray CreateChoice(int selectIndex)
        => new()
        {
            select_index_info_array =
            [
                new SingleModeSelectIndexInfo
                {
                    branch_number = 1,
                    select_index = selectIndex
                }
            ],
            gain_select_id_index = selectIndex
        };
}

static async ValueTask DispatchHostResponse(
    RecordingAnalyzerRegistry registry,
    string canonicalUrl,
    byte[] payload)
{
    if (!Server.TryResolveEndpoint(canonicalUrl, out var endpoint))
        throw new InvalidOperationException($"Host did not resolve smoke endpoint: {canonicalUrl}");

    var context = new AnalyzerDispatchContext(
        endpoint,
        payload,
        new("sid-1", "1.2.3", "2026070301", "123456789", "android", "phone"));
    var registrations = registry.Registrations
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .Where(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns)
            .Any(candidate => candidate.EndpointType == endpoint.EndpointType))
        .OrderBy(registration => registration.Priority)
        .ToArray();
    if (registrations.Length == 0)
        throw new InvalidOperationException($"No programmatic analyzer matched {endpoint.Path}.");

    foreach (var registration in registrations)
        await registration.Invoke(context);
}

static void InitializeHostConfigForSmoke()
    => UmamusumeResponseAnalyzer.Config.Initialize();

static async Task InitializeSmokeDatabase(
    IEnumerable<Story>? events = null,
    IEnumerable<UmamusumeResponseAnalyzer.Entities.SkillData>? skills = null)
{
    WriteBrotliJson(Database.EVENT_NAME_FILEPATH, (events ?? []).ToList());
    WriteBrotliJson(
        Database.NAMES_FILEPATH,
        new List<BaseName>
        {
            new SupportCardName(10001, "fixture support 1", "S1", 101, 1001),
            new SupportCardName(10002, "fixture support 2", "S2", 105, 1002),
            new SupportCardName(10003, "fixture support 3", "S3", 102, 1003),
            new SupportCardName(10004, "fixture support 4", "S4", 103, 1004),
            new SupportCardName(10005, "fixture support 5", "S5", 106, 1005),
            new SupportCardName(10006, "fixture support 6", "S6", 0, 1006),
        },
        new() { TypeNameHandling = TypeNameHandling.All });
    WriteBrotliJson(Database.SKILLS_FILEPATH, (skills ?? []).ToList());
    WriteBrotliJson(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new List<SkillUpgradeSpeciality>());
    WriteBrotliJson(Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
    WriteBrotliJson(Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
    WriteBrotliJson(Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
    WriteBrotliJson(Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());
    if (await Database.Initialize() != DatabaseAvailability.Ready)
        throw new InvalidOperationException("Smoke database fixture did not load atomically.");
}

static void WriteEventResponseAnalyzerHistorySettings(int historyLimit)
{
    var dataDirectory = Path.Combine("PluginData", "EventResponseAnalyzer");
    Directory.CreateDirectory(dataDirectory);
    File.WriteAllText(
        Path.Combine(dataDirectory, "settings.json"),
        System.Text.Json.JsonSerializer.Serialize(
            new { historyLimit },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

static void WriteBrotliJson<T>(string path, T value, JsonSerializerSettings? settings = null)
{
    using var file = File.Create(path);
    using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
    using var writer = new StreamWriter(brotli, Encoding.UTF8);
    using var json = new JsonTextWriter(writer);
    (settings is null
        ? Newtonsoft.Json.JsonSerializer.CreateDefault()
        : Newtonsoft.Json.JsonSerializer.CreateDefault(settings)).Serialize(json, value);
}

}
sealed record EventRenderCapture(
    Cell[,] Cells,
    Rectangle Region)
{
    public string GetLine(int y)
    {
        var line = new StringBuilder();
        for (var x = Region.Left; x < Region.Right; x++)
        {
            var grapheme = Cells[y, x].Grapheme;
            line.Append(grapheme);
            x += Math.Max(1, grapheme.GetColumns()) - 1;
        }

        return line.ToString().TrimEnd();
    }

    public int GetColumn(int y, int textIndex)
    {
        var currentIndex = 0;
        for (var x = Region.Left; x < Region.Right; x++)
        {
            var grapheme = Cells[y, x].Grapheme;
            if (textIndex < currentIndex + grapheme.Length)
                return x;
            currentIndex += grapheme.Length;
            x += Math.Max(1, grapheme.GetColumns()) - 1;
        }

        throw new ArgumentOutOfRangeException(nameof(textIndex));
    }

    public string Text
    {
        get
        {
            var output = new StringBuilder();
            for (var y = Region.Top; y < Region.Bottom; y++)
                output.AppendLine(GetLine(y));

            return output.ToString().TrimEnd();
        }
    }
}
