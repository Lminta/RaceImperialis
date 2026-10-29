using System.Numerics;
using WolfSlop.Engine.Graphics;
using WolfSlop.Engine.UI;

namespace RaceImperialis.Game;

public sealed class NativeMenu
{
    private readonly UiDocument _document;
    private readonly RendererSettings _settings;
    private readonly GraphicsPreferences _preferences;
    private readonly Action _pause;
    private readonly UiPanel _mask, _fields;
    private readonly UiText _error;
    private GraphicsOptions _draft = null!;
    public bool IsOpen => _mask.Visible;
    public NativeMenu(UiDocument document, RendererSettings settings, GraphicsPreferences preferences, Action pause,
        Func<RaceDifficulty> difficulty, Action<RaceDifficulty> restart, Action<RendererSettings> apply)
    {
        _document = document; _settings = settings; _preferences = preferences; _pause = pause;
        var options = document.Root.Add(new UiPanel("options") { Style = new() { Anchor = new(1, 0), Pivot = new(1, 0), Position = new(-12, 12),
            Width = 220, Layout = UiLayoutMode.Column, Gap = 6, Padding = new(10), Background = "#111923d9", CornerRadius = 8 } });
        options.Add(new UiText { Text = "Race Imperialis", Font = new(Size: 19, Weight: 700) });
        Button(options, "Normal difficulty", () => restart(RaceDifficulty.Normal));
        Button(options, "Easy difficulty", () => restart(RaceDifficulty.Easy));
        Button(options, "Restart race (R)", () => restart(difficulty())); Button(options, "Graphics (F1)", Open);
        document.Root.Add(new UiText { Text = "W accelerate · S reverse · A/D steer · Space brake\nV camera · Mouse look · Esc pause · Click to drive",
            Font = new(Size: 12), Style = new() { Anchor = new(1, 1), Pivot = new(1, 1), Position = new(-12, -12), Padding = new(8), Background = "#111923cc", ZIndex = 10 } });
        _mask = document.Root.Add(new UiPanel("graphics-mask") { Visible = false, BlocksInput = true,
            Style = UiStyle.Fill with { Background = "#071019cc", ZIndex = 200 } });
        var panel = _mask.Add(new UiPanel("graphics-panel") { Style = new() { Anchor = new(.5f), Pivot = new(.5f), Width = 520,
            Padding = new(18), Layout = UiLayoutMode.Column, Gap = 10, Background = "#111923", CornerRadius = 10 } });
        panel.Add(new UiText { Text = "Graphics settings", Font = new(Size: 24, Weight: 700) });
        _fields = panel.Add(new UiPanel("graphics-fields") { Style = new() { Width = UiLength.Percent(1), Layout = UiLayoutMode.Column, Gap = 6 } });
        _error = panel.Add(new UiText { Wrap = true, Style = new() { Width = UiLength.Percent(1), Color = "#ffb4a8" } });
        var actions = panel.Add(new UiPanel { Style = new() { Layout = UiLayoutMode.Row, Gap = 8 } });
        Button(actions, "Defaults", () => { _draft = _preferences.Defaults; BuildFields(); }); Button(actions, "Cancel", Close);
        Button(actions, "Apply", () => {
            try { apply(_draft.ToRendererSettings(_settings)); _preferences.SaveAsync().GetAwaiter().GetResult(); Close(); }
            catch (Exception ex) { _error.Text = ex.Message; }
        });
    }
    private static UiButton Button(UiElement parent, string text, Action click)
    {
        var button = parent.Add(new UiButton { Text = text, Font = new(Size: 14), Style = new() { Padding = new(10, 7), CornerRadius = 4, BorderWidth = 1 } });
        button.Clicked += _ => click(); return button;
    }
    public void Open() { _pause(); _draft = GraphicsOptions.From(_settings); _error.Text = ""; BuildFields(); _mask.Visible = true; _document.ModalRoot = _mask; }
    public void Close() { _document.ModalRoot = null; _mask.Visible = false; }
    private void BuildFields()
    {
        _fields.Children.Clear();
        void Cycle(string label, Func<string> value, Action next)
        {
            UiButton? button = null;
            button = Button(_fields, label + ": " + value(), () => { next(); button!.Text = label + ": " + value(); });
            button.Style = button.Style with { Width = UiLength.Percent(1) };
        }
        Cycle("Resolution", () => $"{_draft.Width} × {_draft.Height}", () => {
            var presets = GraphicsOptions.ResolutionPresets; var index = presets.ToList().FindIndex(p => p.Width == _draft.Width && p.Height == _draft.Height);
            _draft.SelectResolution(presets[(index + 1) % presets.Count].Value);
        });
        Cycle("FPS limit", () => _draft.TargetFPS.ToString(), () => { int[] values = [30, 60, 90, 120, 144, 165, 240, 360]; _draft.TargetFPS = values[(Array.IndexOf(values, _draft.TargetFPS) + 1) % values.Length]; });
        Cycle("Anisotropy", () => _draft.MaxAnisotropy + "×", () => _draft.MaxAnisotropy = (ushort)(_draft.MaxAnisotropy >= 16 ? 1 : _draft.MaxAnisotropy * 2));
        Cycle("Columns", () => "Every " + _draft.ColumnInterlaceFactor, () => _draft.ColumnInterlaceFactor = _draft.ColumnInterlaceFactor % 3 + 1);
        Cycle("Field of view", () => _draft.FovDegrees + "°", () => _draft.FovDegrees = _draft.FovDegrees >= 120 ? 50 : _draft.FovDegrees + 5);
        Cycle("Render distance", () => _draft.RenderDistance.ToString(), () => {
            _draft.RenderDistance = _draft.RenderDistance >= 120 ? 20 : _draft.RenderDistance + 10;
            _draft.FogStartDistance = Math.Min(_draft.FogStartDistance, _draft.RenderDistance - 1);
        });
        Cycle("Fog start", () => _draft.FogStartDistance.ToString(), () => _draft.FogStartDistance = (_draft.FogStartDistance + 5) % _draft.RenderDistance);
    }
}
