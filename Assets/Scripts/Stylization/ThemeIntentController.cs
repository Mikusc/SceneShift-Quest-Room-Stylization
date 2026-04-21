using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class ThemeIntentController : MonoBehaviour
{
    [Header("Themes")]
    [SerializeField] private List<ThemeProfile> availableThemes = new();
    [SerializeField] private int defaultThemeIndex;

    [Header("Debug Input")]
    [SerializeField] private bool selectDefaultOnEnable = true;
    [SerializeField] private bool enableKeyboardShortcuts = true;
    [SerializeField] private KeyCode previousThemeKey = KeyCode.LeftBracket;
    [SerializeField] private KeyCode nextThemeKey = KeyCode.RightBracket;

    public event Action<ThemeProfile> ThemeChanged;

    public IReadOnlyList<ThemeProfile> AvailableThemes => availableThemes;
    public ThemeProfile ActiveTheme => _activeTheme;
    public int ActiveThemeIndex => _activeThemeIndex;

    private ThemeProfile _activeTheme;
    private int _activeThemeIndex = -1;

    private void OnEnable()
    {
        if (selectDefaultOnEnable && availableThemes.Count > 0)
        {
            SelectThemeByIndex(Mathf.Clamp(defaultThemeIndex, 0, availableThemes.Count - 1));
        }
    }

    private void Update()
    {
        if (!enableKeyboardShortcuts || availableThemes.Count == 0)
        {
            return;
        }

        if (WasShortcutPressed(previousThemeKey))
        {
            CycleTheme(-1);
        }

        if (WasShortcutPressed(nextThemeKey))
        {
            CycleTheme(1);
        }

        var maxShortcutCount = Mathf.Min(availableThemes.Count, 9);
        for (var index = 0; index < maxShortcutCount; index++)
        {
            var alphaKey = (KeyCode)((int)KeyCode.Alpha1 + index);
            var keypadKey = (KeyCode)((int)KeyCode.Keypad1 + index);
            if (WasShortcutPressed(alphaKey) || WasShortcutPressed(keypadKey))
            {
                SelectThemeByIndex(index);
                break;
            }
        }
    }

    public bool SelectThemeByIndex(int index)
    {
        if (index < 0 || index >= availableThemes.Count)
        {
            return false;
        }

        var theme = availableThemes[index];
        if (theme == null)
        {
            Debug.LogWarning($"[ThemeIntentController] Theme slot {index} is empty.", this);
            return false;
        }

        if (_activeTheme == theme && _activeThemeIndex == index)
        {
            return true;
        }

        _activeThemeIndex = index;
        _activeTheme = theme;
        ThemeChanged?.Invoke(theme);
        Debug.Log($"[ThemeIntentController] Active theme -> {theme.DisplayName}", this);
        return true;
    }

    public void CycleTheme(int direction)
    {
        if (availableThemes.Count == 0)
        {
            return;
        }

        var startIndex = _activeThemeIndex >= 0 ? _activeThemeIndex : Mathf.Clamp(defaultThemeIndex, 0, availableThemes.Count - 1);
        var nextIndex = (startIndex + direction + availableThemes.Count) % availableThemes.Count;
        SelectThemeByIndex(nextIndex);
    }

    public string GetDebugSummary()
    {
        var builder = new StringBuilder(256);
        builder.AppendLine("Theme");

        if (_activeTheme == null)
        {
            builder.AppendLine("  Active: none");
        }
        else
        {
            builder.AppendLine($"  Active: {_activeTheme.DisplayName}");
            builder.AppendLine($"  Category: {_activeTheme.Category}");
            builder.AppendLine($"  Accent: #{ColorUtility.ToHtmlStringRGB(_activeTheme.AccentColor)}");
        }

        if (availableThemes.Count > 0)
        {
            builder.Append("  Options:");
            for (var index = 0; index < availableThemes.Count; index++)
            {
                var theme = availableThemes[index];
                var label = theme != null ? theme.DisplayName : "Missing";
                builder.Append($" [{index + 1}] {label}");
            }

            builder.AppendLine();
            builder.AppendLine($"  Keys: {previousThemeKey} / {nextThemeKey} to cycle");
        }
        else
        {
            builder.AppendLine("  Options: no theme assets assigned");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool WasShortcutPressed(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        var keyControl = keyCode switch
        {
            KeyCode.LeftBracket => keyboard.leftBracketKey,
            KeyCode.RightBracket => keyboard.rightBracketKey,
            KeyCode.Alpha1 => keyboard.digit1Key,
            KeyCode.Alpha2 => keyboard.digit2Key,
            KeyCode.Alpha3 => keyboard.digit3Key,
            KeyCode.Alpha4 => keyboard.digit4Key,
            KeyCode.Alpha5 => keyboard.digit5Key,
            KeyCode.Alpha6 => keyboard.digit6Key,
            KeyCode.Alpha7 => keyboard.digit7Key,
            KeyCode.Alpha8 => keyboard.digit8Key,
            KeyCode.Alpha9 => keyboard.digit9Key,
            KeyCode.Keypad1 => keyboard.numpad1Key,
            KeyCode.Keypad2 => keyboard.numpad2Key,
            KeyCode.Keypad3 => keyboard.numpad3Key,
            KeyCode.Keypad4 => keyboard.numpad4Key,
            KeyCode.Keypad5 => keyboard.numpad5Key,
            KeyCode.Keypad6 => keyboard.numpad6Key,
            KeyCode.Keypad7 => keyboard.numpad7Key,
            KeyCode.Keypad8 => keyboard.numpad8Key,
            KeyCode.Keypad9 => keyboard.numpad9Key,
            _ => null,
        };

        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(keyCode);
#endif
    }
}
