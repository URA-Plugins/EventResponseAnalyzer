using Gallop;
using MessagePack;

var culture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
System.Globalization.CultureInfo.CurrentCulture = culture;
System.Globalization.CultureInfo.CurrentUICulture = culture;
RepositoryChecks.Run();
SourceDtoContract.Run();
if (args.Length != 0 && args is not ["--package-load"]) throw new ArgumentException("Usage: EventResponseAnalyzer.Tests [--package-load]");
if (args.Length == 0)
{
EventHistoryConfigSmoke.Run();
}
using var ui = new WorkspaceSmokeSession();
if (args.Length == 0)
{
WorkspaceLifecycleSmoke.Run(ui, () => new EventResponseAnalyzer.EventResponseAnalyzer(), "EventResponseAnalyzer");
await AnalyzerRegistrationSmoke.Run(ui);
await EventBusinessSmoke.Run(ui);
}
PackageLoadSmoke.Run(ui);

var exec = new SingleModeExecCommandResponse
{
    data = new()
    {
        chara_info = new() { card_id = 10042, playing_state = 5 },
        unchecked_event_array = [new() { story_id = 400010112 }]
    }
};
var projectedCheck = MessagePackSerializer.Deserialize<SingleModeCheckEventResponse>(
    MessagePackSerializer.Serialize(exec));
Assert(projectedCheck.data.chara_info.card_id == 10042, "exec_command chara_info projection");
Assert(projectedCheck.data.unchecked_event_array is [{ story_id: 400010112 }], "exec_command event projection");

var ramenLoad = new SingleModeRamenLoadResponse
{
    data = new()
    {
        single_mode_load_common = new()
        {
            chara_info = new() { card_id = 20084 },
            unchecked_event_array = [new() { story_id = 123456 }]
        }
    }
};
var projectedLoad = MessagePackSerializer.Deserialize<SingleModeLoadResponse>(
    MessagePackSerializer.Serialize(ramenLoad));
Assert(projectedLoad.data.single_mode_load_common.chara_info.card_id == 20084, "scenario load chara_info projection");
Assert(projectedLoad.data.single_mode_load_common.unchecked_event_array is [{ story_id: 123456 }], "scenario load event projection");

Console.WriteLine("PASS catalog Gallop DTO projections preserve EventResponse fields");

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL {name}");
}
