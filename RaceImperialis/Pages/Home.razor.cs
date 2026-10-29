using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.Input;
using WolfNET.Engine.World;
using WolfNET.Engine.Graphics;
using Microsoft.Extensions.DependencyInjection;
using WolfNET.Engine.UI;
using WolfNET.Engine.Browser.Graphics;
using WolfNET.Engine.Browser.Input;
using WolfNET.Engine.Browser.UI;

namespace RaceImperialis.Pages;

public partial class Home
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = null!;
    private IServiceScope? _services;
    private Wolf Wolf => _services!.ServiceProvider.GetRequiredService<Wolf>();
    private WebGpuRenderer Renderer => _services!.ServiceProvider.GetRequiredService<WebGpuRenderer>();

    protected override void OnInitialized()
    {
        _services = ScopeFactory.CreateScope();
        _graphicsDraft = GraphicsOptions.From(Settings);
    }
    private ElementReference _canvas;
    private HTMLCanvasElement? _canvasElement;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading = true;
    private string _loadingMessage = "Loading shaders…";
    private bool _disposed;

    private string? _error;
    private MouseTracker? _observedMouse;
    private long _lastLoadingProgress;
    private ElementReference _graphicsDialog;
    private IJSObjectReference? _graphicsMenuModule;
    private GraphicsOptions _graphicsDraft = null!;
    private bool _showGraphicsOnRender;
    private bool _applyingGraphics;
    private string? _graphicsError;
    private string SelectedResolution
    {
        get => $"{_graphicsDraft.Width}x{_graphicsDraft.Height}";
        set => _graphicsDraft.SelectResolution(value);
    }

    private void OpenGraphicsMenu()
    {
        if (_loading || _disposed) return;
        Wolf.PauseForMenu();
        _graphicsDraft = GraphicsOptions.From(Settings);
        _graphicsError = null;
        _showGraphicsOnRender = true;
    }

    private async Task CloseGraphicsMenuAsync()
    {
        if (_graphicsMenuModule != null) await _graphicsMenuModule.InvokeVoidAsync("close", _graphicsDialog);
    }

    private void ResetGraphicsDefaults()
    {
        _graphicsDraft = GraphicsPreferences.Defaults;
        _graphicsError = null;
    }

    private async Task ApplyGraphicsSettingsAsync()
    {
        if (_applyingGraphics || _canvasElement == null || _disposed) return;
        _applyingGraphics = true;
        _graphicsError = null;
        try
        {
            Wolf.ApplyGraphicsSettings(_graphicsDraft.ToRendererSettings(Settings), _canvasElement);
            try { await GraphicsPreferences.SaveAsync(); }
            catch (Microsoft.JSInterop.JSException)
            {
                _graphicsError = "Settings applied, but this browser could not save them for next time.";
                return;
            }
            await CloseGraphicsMenuAsync();
        }
        catch (Exception ex)
        {
            _graphicsError = $"Could not apply graphics settings: {ex.Message}";
        }
        finally { _applyingGraphics = false; }
    }

    private async Task ShowLoadingProgressAsync(string message)
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        // Counter updates must not impose a browser timer delay for every texture
        // layer. Stage transitions still paint immediately.
        var now = Environment.TickCount64;
        if (message.Contains('(') && now - _lastLoadingProgress < 75) return;
        _lastLoadingProgress = now;
        _loadingMessage = message;
        await InvokeAsync(StateHasChanged);
        // Allow the browser to paint the message before the next loading step.
        await Task.Delay(1, _lifetime.Token);
    }

    private void ObserveMouse()
    {
        if (_observedMouse != null) _observedMouse.CaptureStateChanged -= OnCaptureStateChanged;
        _observedMouse = Wolf.MouseTracker;
        _observedMouse.CaptureStateChanged += OnCaptureStateChanged;
    }

    private void OnCaptureStateChanged()
    {
        if (!_disposed) _ = InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_showGraphicsOnRender)
        {
            _showGraphicsOnRender = false;
            _graphicsMenuModule ??= await UiRuntime.InvokeAsync<IJSObjectReference>("import", "./graphics-menu.js");
            await _graphicsMenuModule.InvokeVoidAsync("open", _graphicsDialog);
        }
        if (!firstRender)
            return;

        Console.WriteLine("[APP] Component initialized");
        Wolf.MapSwitchRequested += OnMapSwitchRequested;

        try
        {
            Console.WriteLine("[APP] Loading shader");

            var shaders = await ShaderSources.LoadAsync(Http, _lifetime.Token);
            _canvasElement = new HTMLCanvasElement(_canvas);
            await ShowLoadingProgressAsync("Starting graphics...");
            await Renderer.InitializeAsync(_canvasElement, shaders, _lifetime.Token);
            Wolf.AttachUi(await BrowserUiHost.CreateAsync(UiRuntime, _canvas, _lifetime.Token));

            await Wolf.SwitchMapAsync(_canvasElement, Wolf.DefaultMapPath, _lifetime.Token, ShowLoadingProgressAsync);
            ObserveMouse();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _error = ex.ToString();

            Console.Error.WriteLine(
                $"[APP] Initialization failed: {ex}");

            if (!_disposed) StateHasChanged();
        }
        finally
        {
            _loading = false;
            if (!_disposed) StateHasChanged();
        }
    }

    private async Task LoadRaceAsync(RaceDifficulty? difficulty = null)
    {
        if (_loading || _disposed || _canvasElement == null) return;
        _loading = true;
        _error = null;
        try
        {
            await Wolf.SwitchMapAsync(_canvasElement, Wolf.DefaultMapPath, _lifetime.Token, ShowLoadingProgressAsync, difficulty);
            ObserveMouse();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _error = $"Could not load map: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private Task RestartRaceAsync() => LoadRaceAsync();

    private Task SelectDifficultyAsync(RaceDifficulty difficulty) => Wolf.Difficulty != difficulty
        ? LoadRaceAsync(difficulty) : Task.CompletedTask;

    private void OnMapSwitchRequested(string mapPath)
    {
        if (!_disposed) _ = InvokeAsync(async () =>
        {
            await RestartRaceAsync();
            if (!_disposed) StateHasChanged();
        });
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Wolf.MapSwitchRequested -= OnMapSwitchRequested;
        if (_observedMouse != null) _observedMouse.CaptureStateChanged -= OnCaptureStateChanged;
        _lifetime.Cancel();
        Wolf.Stop();
        _canvasElement?.Dispose();
        _services?.Dispose();
        _lifetime.Dispose();
        if (_graphicsMenuModule != null) await _graphicsMenuModule.DisposeAsync();
        Console.WriteLine("[APP] Disposing");
    }
}
