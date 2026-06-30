using System.Globalization;
using System.Text;
using GammaHotkey.Models;

namespace GammaHotkey.Services;

/// <summary>
/// Generates a Logitech G HUB / LGS Lua script that maps mouse buttons to the
/// keyboard hotkeys GammaHotkey listens for. Only keyboard triggers need G HUB;
/// real mouse-button triggers are detected by GammaHotkey directly.
/// </summary>
public static class LuaGenerator
{
    /// <summary>First mouse button number assigned to a hotkey (4 = "back" side button).</summary>
    private const int FirstButton = 4;

    public static string Generate(AppConfig cfg)
    {
        var presetValues = cfg.Presets.ToDictionary(p => p.Level, p => p.Value);
        double ValueOf(GammaLevel l) => presetValues.TryGetValue(l, out var v) ? v : GammaPresets.DefaultValue(l);

        // Collect the keyboard hotkeys that the ACTIVE mode actually listens for.
        var hotkeys = new List<(int Vk, string Description)>();

        if (cfg.Mode == TriggerMode.Cycle)
        {
            if (cfg.Cycle.Trigger.Kind == TriggerKind.Keyboard && !cfg.Cycle.Trigger.IsEmpty)
            {
                string steps = string.Join(" -> ", cfg.Cycle.Steps.Select(GammaPresets.DisplayName));
                hotkeys.Add((cfg.Cycle.Trigger.VirtualKey, $"advance gamma cycle ({steps})"));
            }
        }
        else
        {
            foreach (var b in cfg.Direct)
            {
                if (b.Trigger.Kind == TriggerKind.Keyboard && !b.Trigger.IsEmpty)
                {
                    string v = ValueOf(b.Level).ToString("0.00", CultureInfo.InvariantCulture);
                    hotkeys.Add((b.Trigger.VirtualKey, $"set gamma to {GammaPresets.DisplayName(b.Level)} ({v})"));
                }
            }
        }

        var sb = new StringBuilder();
        AppendHeader(sb);

        sb.AppendLine("local bindings = {");
        sb.AppendLine("    -- [mouseButton] = \"hotkey\",   -- what it does");

        if (hotkeys.Count == 0)
        {
            sb.AppendLine("    -- No keyboard hotkeys are configured in GammaHotkey yet.");
            sb.AppendLine("    -- Add an F13-F24 trigger in the app, then re-generate this script.");
            sb.AppendLine("    -- Example (sends F13 when you press the back side button):");
            sb.AppendLine("    [4] = \"f13\",");
        }
        else
        {
            int button = FirstButton;
            foreach (var (vk, description) in hotkeys)
            {
                string? key = KeyNames.GhubKeyName(vk);
                if (key == null)
                {
                    sb.AppendLine($"    -- (skipped: \"{KeyNames.DisplayName(vk)}\" can't be sent from G HUB)");
                    continue;
                }
                sb.AppendLine($"    [{button}] = \"{key}\",   -- {description}");
                button++;
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
        AppendBody(sb);
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("-- =====================================================================");
        sb.AppendLine("--  GammaHotkey - generated G HUB / LGS Lua script");
        sb.AppendLine("--  Maps Logitech mouse buttons to the keyboard hotkeys GammaHotkey");
        sb.AppendLine("--  listens for, so a button press changes your gamma - no NVIDIA");
        sb.AppendLine("--  Control Panel needed.");
        sb.AppendLine("--");
        sb.AppendLine("--  HOW TO USE");
        sb.AppendLine("--    1. In G HUB: your profile -> Assignments -> Scripting -> create/edit");
        sb.AppendLine("--       a Lua script (Help menu has the scripting API).");
        sb.AppendLine("--    2. Paste this whole script, then Save. Keep that profile active.");
        sb.AppendLine("--    3. Keep GammaHotkey running with \"Listening\" turned on.");
        sb.AppendLine("--");
        sb.AppendLine("--  Change the button numbers below to match the buttons you want:");
        sb.AppendLine("--    2=Right  3=Middle  4=Back(side)  5=Forward(side)  6+=extra buttons");
        sb.AppendLine("--  Tip: uncomment the OutputLogMessage line to print arg in the G HUB");
        sb.AppendLine("--  console so you can discover your own mouse's button numbers.");
        sb.AppendLine("-- =====================================================================");
        sb.AppendLine();
    }

    private static void AppendBody(StringBuilder sb)
    {
        sb.AppendLine("local HOLD_MS = 20   -- tiny hold so Windows/the app registers the keypress");
        sb.AppendLine();
        sb.AppendLine("local function sendKey(key)");
        sb.AppendLine("    PressKey(key)");
        sb.AppendLine("    Sleep(HOLD_MS)");
        sb.AppendLine("    ReleaseKey(key)");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("function OnEvent(event, arg, family)");
        sb.AppendLine("    if event == \"PROFILE_ACTIVATED\" then");
        sb.AppendLine("        -- Uncomment ONLY if you bind the left button (button 1):");
        sb.AppendLine("        -- EnablePrimaryMouseButtonEvents(true)");
        sb.AppendLine("        OutputLogMessage(\"GammaHotkey script loaded.\\n\")");
        sb.AppendLine("    end");
        sb.AppendLine();
        sb.AppendLine("    if event == \"MOUSE_BUTTON_PRESSED\" then");
        sb.AppendLine("        -- OutputLogMessage(\"button %d\\n\", arg)  -- uncomment to find button numbers");
        sb.AppendLine("        local key = bindings[arg]");
        sb.AppendLine("        if key then");
        sb.AppendLine("            sendKey(key)");
        sb.AppendLine("        end");
        sb.AppendLine("    end");
        sb.AppendLine("end");
    }
}
