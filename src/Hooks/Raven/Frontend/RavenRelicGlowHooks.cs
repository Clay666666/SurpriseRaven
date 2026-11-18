using Godot;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace SurpriseRaven.Hooks.Raven;

// Use the game's teal particle scene without editing its shared texture or process material.
internal sealed class RavenRelicGlowHooks : IDisposable
{
    private readonly Control _anchor;
    private readonly NCardUncommonGlow? _glow;
    private bool _disposed, _resizedSubscribed;
    internal const string NodeName = "RavenTealGlow";
    internal NCardUncommonGlow? Effect => _glow;

    internal RavenRelicGlowHooks(Control anchor)
    {
        _anchor = anchor;
        try
        {
            _glow = NCardUncommonGlow.Create();
            if (_glow is not null)
            {
                _glow.Name = NodeName;
                _glow.ShowBehindParent = true;
                _glow.Visible = false;
                _glow.Emitting = false;
                anchor.AddChild(_glow);
                anchor.Resized += UpdateLayout;
                _resizedSubscribed = true;
                UpdateLayout();
            }
        }
        catch (Exception error) { Logger.Log($"[Raven] Native teal glow unavailable: {error.Message}"); }
    }

    private void UpdateLayout()
    {
        if (_glow is null || !GodotObject.IsInstanceValid(_glow) || !GodotObject.IsInstanceValid(_anchor)) return;
        _glow.Position = _anchor.Size / 2;
        // Native relics use a 68px local square; fallback icons use the full displayed size.
        _glow.Scale = Vector2.One * (0.3f * Math.Min(_anchor.Size.X, _anchor.Size.Y) / 68f);
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed || _glow is null || !GodotObject.IsInstanceValid(_glow)) return;
        _glow.Visible = visible;
        _glow.Emitting = visible;
    }

    internal static void SuppressNativeRarityGlows(Control relic)
    {
        // Both verified native holders put their rarity particles directly under NRelic.
        // This is called only for our newly initialized standalone chest holders.
        foreach (var glow in relic.GetChildren().OfType<GpuParticles2D>())
        {
            glow.Hide();
            glow.Emitting = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_glow is null) return;
        if (_resizedSubscribed && GodotObject.IsInstanceValid(_anchor)) _anchor.Resized -= UpdateLayout;
        if (!GodotObject.IsInstanceValid(_glow)) return;
        _glow.Hide();
        _glow.Emitting = false;
        _glow.QueueFree();
    }
}
