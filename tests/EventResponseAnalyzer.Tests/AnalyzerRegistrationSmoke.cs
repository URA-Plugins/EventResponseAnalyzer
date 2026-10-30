using Gallop.Endpoints;
using MessagePack;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

static class AnalyzerRegistrationSmoke
{
    internal static async Task Run(WorkspaceSmokeSession ui)
    {
        using var directory = new TempCurrentDirectory("EventResponseAnalyzer-analyzers");
        PackageLoadSmoke.InitializeDatabase();
        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new EventResponseAnalyzer.EventResponseAnalyzer();
        ui.Bootstrap.SwitchTo();
        var before = ui.CaptureScreen();
        try
        {
            plugin.Initialize(context);
            if (context.AnalyzerRegistry.Registrations.Count != 2)
                throw new InvalidOperationException("Programmatic analyzer registration count changed.");
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap) || before != ui.CaptureScreen())
                throw new InvalidOperationException("Initialize changed the visible workspace or framebuffer.");
            foreach (var registration in context.AnalyzerRegistry.Registrations.OrderBy(item => item.Priority))
            foreach (var descriptor in PluginManager.ExpandEndpointPatterns(registration.Patterns))
            {
                var type = registration.Kind == AnalyzerKind.Request ? descriptor.RequestType : descriptor.ResponseType;
                var dto = RandomDtoFactory.Create(type,
                    $"programmatic:{registration.Kind}:{descriptor.Path}");
                await registration.Invoke(new AnalyzerDispatchContext(descriptor,
                    MessagePackSerializer.Serialize(type, dto), new("sid-1", "1.2.3", "2026070301", "123456789", "android", "phone")));
            }
        }
        finally { await plugin.DisposeAsync(); }
    }
}
