using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using GameText = UmamusumeResponseAnalyzer.Localization.Game;
using UiText = UmamusumeResponseAnalyzer.Localization.TerminalGui;

static class PackageLoadSmoke
{
    const string InternalName = "EventResponseAnalyzer";
    static readonly GameHttpHeaders Headers = new("zip-smoke-sid", "zip-smoke-app", "zip-smoke-res", "zip-smoke-viewer", null, null);

    internal static void Run(WorkspaceSmokeSession ui)
    {
        var package = FindPackage();
        using var directory = new TempCurrentDirectory(InternalName + "-fresh-zip");
        Config.Initialize();
        InitializeDatabase();
        Directory.CreateDirectory("Plugins");
        File.Copy(package, Path.Combine("Plugins", InternalName + ".zip"));
        Assembly? loadedAssembly = null;
        try
        {
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            PluginManager.TriggerStartedAsync().GetAwaiter().GetResult();
            Require(PluginManager.FailedPlugins.Count == 0 && PluginManager.LoadedPlugins.Count == 1,
                $"Fresh {InternalName} ZIP failed to load: {string.Join(",", PluginManager.FailedPlugins)}");
            var plugin = PluginManager.LoadedPlugins.Single();
            loadedAssembly = plugin.GetType().Assembly;
            var context = AssemblyLoadContext.GetLoadContext(loadedAssembly)!;
            var metadata = PluginManager.Metadatas[InternalName];
            Require(metadata.PackagePath == Path.GetFullPath(Path.Combine("Plugins", InternalName + ".zip")), "Loaded package path is not the isolated fresh ZIP.");
            using var archive = ZipFile.OpenRead(package);
            using var entry = archive.GetEntry(metadata.AssemblyEntry)!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            bytes.Position = 0;
            using var pe = new PEReader(bytes);
            var reader = pe.GetMetadataReader();
            var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            Require(!context.IsCollectible && context != AssemblyLoadContext.Default && loadedAssembly.ManifestModule.ModuleVersionId == mvid,
                "Executed assembly did not come from the fresh ZIP in a fixed load context.");
            foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(SingleModeChara).Assembly, typeof(IApplication).Assembly })
                Require(ReferenceEquals(context.LoadFromAssemblyName(shared.GetName()), shared), $"Duplicated shared assembly: {shared.GetName().Name}");
            AssertEventChoices(ui);
        }
        finally
        {
            PluginManager.ShutdownAsync().GetAwaiter().GetResult();
            ui.Flush();
        }
        Require(PluginManager.LoadedPlugins.Count == 0 && PluginManager.Contexts.Count == 0
            && PluginManager.RequestAnalyzerMethods.Count == 0 && PluginManager.ResponseAnalyzerMethods.Count == 0,
            "ZIP shutdown left instances or analyzer registrations.");
        var workspace = Workspace.Create("EventResponseAnalyzer");
        workspace.SwitchTo();
        Require(!ui.CaptureScreen().Contains("ZIP 选择事件", StringComparison.Ordinal), "ZIP shutdown left the event panel mounted.");
        ui.Bootstrap.SwitchTo();
        Console.WriteLine($"PASS fresh ZIP {InternalName}: MVID, shared assembly identity, business callback, shutdown");
    }

    static string FindPackage()
    {
        var root = Environment.GetEnvironmentVariable("URA_PLUGIN_SMOKE_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(root) && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")
                ?? throw new InvalidOperationException("GITHUB_WORKSPACE is required in GitHub Actions.");
            root = Path.Combine(workspace, "artifacts");
        }
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("URA_PLUGIN_SMOKE_PACKAGE_ROOT must point to this run's fresh ZIP output directory.");
        var matches = new List<string>();
        foreach (var candidate in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
        {
            using var archive = ZipFile.OpenRead(candidate);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null) continue;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.GetProperty("InternalName").GetString() == InternalName) matches.Add(candidate);
        }
        Require(matches.Count == 1, $"Expected exactly one {InternalName} ZIP under {root}, found {matches.Count}.");
        var validated = PluginPackageValidator.Validate(matches[0], true);
        string[] dependencies = [];
        Require(validated.Manifest.Dependencies.Order(StringComparer.Ordinal).SequenceEqual(dependencies.Order(StringComparer.Ordinal)), "ZIP manifest Dependencies differ from the plugin contract.");
        return matches[0];
    }

    internal static void InitializeDatabase()
    {
        WriteDatabaseFile(Database.EVENT_NAME_FILEPATH, """[{"Id":501143706,"TriggerName":"ZIP 角色","Name":"ZIP 选择事件","Choices":[[{"Option":"ZIP 选项","SuccessEffect":"速度 +10"}]]}]""");
        WriteDatabaseFile(Database.NAMES_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILLS_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, "[]");
        WriteDatabaseFile(Database.TALENT_SKILLS_FILEPATH, "{}");
        WriteDatabaseFile(Database.FACTOR_IDS_FILEPATH, "{}");
        WriteDatabaseFile(Database.SADDLE_IDS_FILEPATH, "[]");
        WriteDatabaseFile(Database.SUCCESSION_RELATION_FILEPATH, "{}");
        Require(Database.Initialize().GetAwaiter().GetResult() == DatabaseAvailability.Ready, "ZIP database fixture did not initialize.");
    }

    static void WriteDatabaseFile(string path, string json)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(brotli);
        writer.Write(json);
    }
    static void AssertEventChoices(WorkspaceSmokeSession ui)
    {
        var chara = new SingleModeChara { single_mode_chara_id = 903, turn = 4, skill_tips_array = [] };
        PrepareChara(chara, 1);
        DispatchResponse(typeof(GameApi.SingleMode.CheckEvent), new SingleModeCheckEventResponse
        {
            data = new()
            {
                chara_info = chara,
                unchecked_event_array = [new()
                {
                    story_id = 501143706,
                    event_contents_info = new() { choice_array = [new() { select_index_info_array = [new() { select_index = 1 }] }] }
                }]
            }
        });
        AssertPanel(ui, "EventResponseAnalyzer", "ZIP 选择事件", "ZIP 选项", "速度 +10");
        Console.WriteLine("PASS ZIP business EventResponseAnalyzer: known event, option and effect panel");
    }

    static void PrepareChara(SingleModeChara chara, int scenario)
    {
        // The status-writer fixture requires a valid character and six evaluation slots.
        chara.state = 1;
        chara.scenario_id = scenario;
        chara.card_id = 100100;
        chara.rarity = 5;
        chara.skill_array = [];
        chara.skill_tips_array = [];
        chara.skill_upgrade_info_array = [];
        chara.chara_effect_id_array = [];
        chara.evaluation_info_array = [.. Enumerable.Range(1, 6).Select(id => new EvaluationInfo { target_id = id, evaluation = 80 })];
    }

    static void DispatchResponse(Type endpointType, object response)
        => Dispatch(AnalyzerKind.Response, GameEndpointCatalog.ByEndpointType[endpointType], MessagePackSerializer.Serialize(response.GetType(), response));

    static void Dispatch(AnalyzerKind kind, GameEndpointDescriptor endpoint, byte[] payload)
    {
        var registrations = PluginManager.SnapshotAnalyzerRegistrations(kind, endpoint.EndpointType);
        Require(registrations.Length != 0, $"No production registrations matched {kind} {endpoint.Path}.");
        var context = new AnalyzerDispatchContext(endpoint, payload, Headers);
        foreach (var registration in registrations)
        {
            using var callback = PluginManager.TryEnterPluginCallback(registration.Plugin);
            Require(callback is not null && !registration.IsFaulted, $"ZIP analyzer unavailable: {registration.Source}.");
            using var owner = HotkeyManager.RegisterScope(registration.Plugin);
            registration.Handler(context).GetAwaiter().GetResult();
        }
    }
    static void AssertPanel(WorkspaceSmokeSession ui, string title, params string[] expected)
    {
        Workspace.Create(title).SwitchTo();
        var screen = ui.CaptureScreen(200, 80);
        foreach (var token in expected)
            Require(screen.Contains(token, StringComparison.Ordinal), $"{title} panel omitted '{token}':\n{screen}");
    }


    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
