using Gallop;
using MessagePack;

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
