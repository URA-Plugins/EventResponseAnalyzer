using System.Runtime.CompilerServices;

static class SourceDtoContract
{
    internal static void Run([CallerFilePath] string entryPoint = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entryPoint)!, "..", "..", "Class1.cs"));
        var source = File.ReadAllText(path);
        foreach (var pattern in new[] { "MessagePackSerializer.ConvertToJson", "JsonConvert.DeserializeObject<SingleMode", ".ToObject<SingleMode", "JObject.Parse" })
            if (source.Contains(pattern, StringComparison.Ordinal))
                throw new InvalidOperationException($"{path} must consume Gallop DTOs directly: {pattern}");
    }
}
