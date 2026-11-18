using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace SurpriseRaven.Hooks.Raven;

// Native widget animations assume an untransformed parent. Use the same local coordinates
// only for our instances, preserving the native tweens, timing, easing and input behaviour.
[HarmonyPatch]
internal static class RavenNativeLayoutHooks
{
    private static readonly ConditionalWeakTable<Control, object> Owned = new();
    private static readonly MethodInfo GlobalGetter = AccessTools.PropertyGetter(typeof(Control), nameof(Control.GlobalPosition));
    private static readonly MethodInfo GlobalSetter = AccessTools.PropertySetter(typeof(Control), nameof(Control.GlobalPosition));

    internal static void Register(Control widget) => Owned.GetValue(widget, _ => new object());
    internal static Control Mount(Control widget, Vector2 anchor, Vector2 offset, float scale = 1)
    {
        var mount = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore, Scale = Vector2.One * scale,
            AnchorLeft = anchor.X, AnchorRight = anchor.X, AnchorTop = anchor.Y, AnchorBottom = anchor.Y,
            OffsetLeft = offset.X, OffsetRight = offset.X, OffsetTop = offset.Y, OffsetBottom = offset.Y
        };
        Register(widget);
        widget.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft, Control.LayoutPresetMode.KeepSize);
        mount.AddChild(widget);
        return mount;
    }
    internal static string PositionPath(Control widget) => Owned.TryGetValue(widget, out _) ? "position" : "global_position";
    internal static Vector2 InitialPosition(Control widget) => Owned.TryGetValue(widget, out _) ? widget.Position : widget.GlobalPosition;
    internal static void SetPosition(Control widget, Vector2 position)
    {
        if (Owned.TryGetValue(widget, out _)) widget.Position = position;
        else widget.GlobalPosition = position;
    }

    // Verified against local sts2.dll with ILSpy: all are instance methods without arguments.
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(NCommonBanner), nameof(NCommonBanner._Ready));
        yield return AccessTools.DeclaredMethod(typeof(NCommonBanner), nameof(NCommonBanner.AnimateIn));
        yield return AccessTools.DeclaredMethod(typeof(NBackButton), "OnEnable");
        yield return AccessTools.DeclaredMethod(typeof(NBackButton), "OnDisable");
        yield return AccessTools.DeclaredMethod(typeof(NBackButton), nameof(NBackButton.MoveToHidePosition));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "global_position"))
            {
                var load = new CodeInstruction(instruction) { opcode = OpCodes.Ldarg_0, operand = null };
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RavenNativeLayoutHooks), nameof(PositionPath)));
                replacements++;
                continue;
            }
            var replacement = instruction.Calls(GlobalGetter) ? nameof(InitialPosition)
                : instruction.Calls(GlobalSetter) ? nameof(SetPosition) : null;
            if (replacement is not null)
            {
                yield return new CodeInstruction(instruction)
                {
                    opcode = OpCodes.Call, operand = AccessTools.Method(typeof(RavenNativeLayoutHooks), replacement)
                };
                replacements++;
            }
            else yield return instruction;
        }
        if (replacements != 1)
            throw new InvalidOperationException($"Native layout API changed: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} has {replacements} coordinate sites.");
    }

    // The native scenes lay themselves out relative to the game window. In our HUD their parent
    // already supplies the responsive anchor. Keep the native hide offset and all entry/exit tweens.
    [HarmonyPatch]
    private static class MountedWidgetLayout
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(NCommonBanner), "OnWindowChange");
            yield return AccessTools.DeclaredMethod(typeof(NBackButton), "OnWindowChange");
        }

        private static void Postfix(Control __instance, ref Vector2 ____showPos, ref Vector2 ____hidePos)
        {
            if (!Owned.TryGetValue(__instance, out _) || __instance.GetParent()?.GetParent() is not RavenHudHooks) return;
            __instance.Position -= ____showPos;
            ____hidePos -= ____showPos;
            ____showPos = Vector2.Zero;
        }
    }
}
