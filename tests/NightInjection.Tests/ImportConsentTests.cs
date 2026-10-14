using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;
using NightInjection.UI.Services;
using NightInjection.UI.ViewModels;

namespace NightInjection.Tests;

public sealed class ImportConsentTests
{
    [Fact]
    public async Task CancelPreparedImportDiscardsPreviewWithoutWriting()
    {
        using var environment = new TestEnvironment();
        var file = environment.CreateFile("import/730.lua", "addappid(730)");
        var engine = new Engine(file);
        var vm = new InjectViewModel(new TestSettingsService(), engine, new AppIds(), new Imports(file), new Picker(), new Dialogs());
        await vm.ImportFromWebsiteAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        await vm.CancelImportCommand.ExecuteAsync(null);
        Assert.Empty(vm.Files);
        Assert.Empty(vm.PlannedEntries);
        Assert.Equal(0, engine.Writes);
        Assert.Contains("cancelled", vm.StatusMessage);
    }
    [Fact]
    public async Task ImportPreviewsAndNeverAppliesUntilUserConfirms()
    {
        using var environment = new TestEnvironment();
        var file = environment.CreateFile("import/730.lua", "addappid(730)");
        var engine = new Engine(file);
        var dialogs = new Dialogs();
        var imports = new Imports(file);
        var vm = new InjectViewModel(new TestSettingsService(), engine, new AppIds(), imports, new Picker(), dialogs);
        await vm.ImportFromWebsiteAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.Single(vm.PlannedEntries);
        Assert.Equal("730", vm.AppId);
        Assert.Equal(0, engine.Writes);
        await vm.ImportFromWebsiteAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.Equal(1, imports.Calls);
        await vm.ExecuteCurrentPlanCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.Prompts);
        Assert.Equal(0, engine.Writes);
        await vm.BuildFilePlanCommand.ExecuteAsync(null);
        dialogs.Confirmed = true;
        await vm.ExecuteCurrentPlanCommand.ExecuteAsync(null);
        Assert.Equal(1, engine.Writes);
    }

    private sealed class Engine(string file) : IInjectionService
    {
        public int Writes;
        public Task<InjectionPlan> BuildFilePlanAsync(IReadOnlyCollection<string> files, string path, CancellationToken token = default) =>
            Task.FromResult(new InjectionPlan { Entries = [new(file, "730.lua", "preview-only/730.lua", InjectionFileType.Lua, 10, false)] });
        public Task<OperationResult> ExecutePlanAsync(InjectionPlan plan, CancellationToken token = default)
        { Writes++; return Task.FromResult(OperationResult.Success("Applied")); }
        public Task<OperationResult> RemoveAppIdAsync(string path, string id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<OperationResult> ClearPluginsAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task CleanupPlanAsync(InjectionPlan plan) => Task.CompletedTask;
    }
    private sealed class Imports(string file) : IWebsiteImportService
    {
        public int Calls;
        public Task<WebsiteImportResult> DownloadAsync(WebsiteImportRequest request, CancellationToken token = default, IProgress<WebsiteImportProgress>? progress = null)
        { Calls++; return Task.FromResult(WebsiteImportResult.Success([file], Path.GetDirectoryName(file)!, "Verified")); }
    }
    private sealed class Dialogs : IDialogService
    {
        public int Prompts;
        public bool Confirmed;
        public Task<bool> ConfirmAsync(string title, string message, string text)
        { Prompts++; return Task.FromResult(Confirmed); }
        public Task ShowAsync(string title, string message) => Task.CompletedTask;
    }
    private sealed class Picker : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickInjectionFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> SaveTextAsync(string name, string extension, string content) => Task.FromResult<string?>(null);
    }
    private sealed class AppIds : IAppIdService
    {
        public Task<InjectionPlan> BuildPlanAsync(string path, string appId, bool force, CancellationToken token = default) => throw new NotSupportedException();
    }
}
