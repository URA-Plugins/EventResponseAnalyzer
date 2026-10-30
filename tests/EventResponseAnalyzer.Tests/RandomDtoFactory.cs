using System.Reflection;
using Gallop;
using UmamusumeResponseAnalyzer.PluginTesting;

static class RandomDtoFactory
{
    public static object Create(Type dtoType, string salt)
    {
        var value = RandomDtoGenerator.Create(dtoType, salt);
        StabilizeEventResponseAnalyzerResponse(value);
        return value;
    }

    static void StabilizeEventResponseAnalyzerResponse(object value)
    {
        var data = value.GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value)
            ?? throw new InvalidOperationException($"{value.GetType().Name} smoke DTO has no data.");

        var eventContainer = data.GetType().GetField("single_mode_load_common", BindingFlags.Instance | BindingFlags.Public) is null
            ? data
            : EnsureObjectField(data, "single_mode_load_common");

        StabilizeArrayField(eventContainer, "unchecked_event_array", 1, (eventInfo, _) =>
        {
            SetIntField(eventInfo, "event_id", 1);
            SetIntField(eventInfo, "chara_id", 1);
            SetIntField(eventInfo, "story_id", 1001);

            var contents = EnsureObjectField(eventInfo, "event_contents_info");
            StabilizeArrayField(contents, "choice_array", 2, (choice, index) =>
            {
                StabilizeArrayField(choice, "select_index_info_array", 1, (selectIndexInfo, _) =>
                {
                    SetIntField(selectIndexInfo, "branch_number", 0);
                    SetIntField(selectIndexInfo, "select_index", index + 1);
                });
            });
        });

        var charaInfo = (SingleModeChara?)eventContainer.GetType().GetField("chara_info", BindingFlags.Instance | BindingFlags.Public)?.GetValue(eventContainer);
        if (charaInfo is not null)
        {
            charaInfo.state = 1;
            charaInfo.turn = 1;
            charaInfo.skill_array = [];
            charaInfo.skill_tips_array = [];
            StabilizeProperFields(charaInfo);
        }
    }

    static void StabilizeProperFields(SingleModeChara charaInfo)
    {
        charaInfo.proper_distance_short = charaInfo.proper_distance_mile =
            charaInfo.proper_distance_middle = charaInfo.proper_distance_long = 1;
        charaInfo.proper_running_style_nige = charaInfo.proper_running_style_oikomi =
            charaInfo.proper_running_style_sashi = charaInfo.proper_running_style_senko = 1;
        charaInfo.proper_ground_turf = charaInfo.proper_ground_dirt = 1;
    }

    static void StabilizeArrayField(object value, string fieldName, int length, Action<object, int> stabilize)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field?.FieldType.IsArray != true)
            return;

        var elementType = field.FieldType.GetElementType()!;
        var array = Array.CreateInstance(elementType, length);
        for (var i = 0; i < length; i++)
        {
            var item = Activator.CreateInstance(elementType)
                ?? throw new InvalidOperationException($"Cannot create DTO array value: {elementType.FullName}");
            stabilize(item, i);
            array.SetValue(item, i);
        }

        field.SetValue(value, array);
    }

    static void SetIntField(object value, string fieldName, int fieldValue)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field is not null && field.FieldType == typeof(int))
            field.SetValue(value, fieldValue);
    }

    static object EnsureObjectField(object value, string fieldName)
    {
        var field = value.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{value.GetType().Name} has no field '{fieldName}'.");
        var current = field.GetValue(value);
        if (current is not null)
            return current;

        var created = Activator.CreateInstance(field.FieldType)
            ?? throw new InvalidOperationException($"Cannot create DTO field value: {field.FieldType.FullName}");
        field.SetValue(value, created);
        return created;
    }
}
