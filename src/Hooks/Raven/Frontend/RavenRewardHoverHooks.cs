using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace SurpriseRaven.Hooks.Raven;

internal sealed partial class RavenRewardSlotHooks
{
    private bool _fallbackHovered, _fallbackTipShown;

    private void OnNativeMouseExited()
    {
        if (RavenFocusHooks.DirectionalNavigation) return;
        ClearHover();
    }

    private void OnFallbackMouseEntered()
    {
        _fallbackHovered = true;
        RefreshFallbackHover();
    }

    private void OnFallbackMouseExited()
    {
        _fallbackHovered = false;
        if (!RavenFocusHooks.DirectionalNavigation && _fallback?.IsInsideTree() == true)
            _fallback.ReleaseFocus();
        RefreshFallbackHover();
    }

    private void RefreshFallbackHover()
    {
        if (_fallback is null) return;
        bool active = !_disposed && _interactive && _fallback.IsVisibleInTree()
            && (_fallbackHovered || (RavenFocusHooks.DirectionalNavigation && _fallback.HasFocus()));
        _hover(active);
        if (!active) RemoveHoverTip();
        else if (!_fallbackTipShown)
        {
            // 111 cannot replace an existing owner; mouse and focus signals share one tip.
            _fallbackTipShown = NHoverTipSet.CreateAndShow(_fallback, _relic.HoverTips,
                HoverTip.GetHoverTipAlignment(_fallback)) is not null;
        }
    }

    private void OnVisibilityChanged()
    {
        if (Root.IsVisibleInTree()) return;
        _fallbackHovered = false;
        ClearHover();
    }

    internal void ClearHover()
    {
        if (_native?.IsInsideTree() == true) _native.ReleaseFocus();
        if (_fallback?.IsInsideTree() == true) _fallback.ReleaseFocus();
        _hover(false);
        RemoveHoverTip();
    }

    internal void RemoveHoverTip()
    {
        if (_native is not null) NHoverTipSet.Remove(_native);
        if (_fallback is not null) NHoverTipSet.Remove(_fallback);
        _fallbackTipShown = false;
    }
}
