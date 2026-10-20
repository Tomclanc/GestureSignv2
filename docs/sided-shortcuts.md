# Sided and standalone modifier shortcuts (18.4)

The shared recorder stores physical left/right Ctrl, Alt, Shift and Win keys. A standalone modifier can be recorded by pressing and releasing it, or chosen from the recorder context menu. The context menu also restores generic Ctrl/Alt/Shift matching for existing combinations. Key names use standard keyboard legends across all languages.

The command editor exposes four horizontal shortcuts: LCtrl, RCtrl, LWin and RWin. Selecting one configures the command without immediately sending input. The same editor is used for new actions, command editing and edge commands.

Sending a shortcut supports a modifier as the only key (for example `{"KeyCode":[163]}` sends right Ctrl). Exact chord modifiers are stored as `ModifierKeyCodes` alongside the legacy modifier mask/booleans. Older configurations without that field retain their original side-independent matching and left-side sending defaults. Cloning and serialization preserve exact modifier codes.

Standalone global modifier triggers confirm on release. A second key, auto-repeat, preheld key, reload or an injected command cannot create an extra standalone trigger. Modifier events themselves continue to Windows, preserving ordinary typing and modifier combinations. Sided combinations consume their main key only; callbacks are posted to the owner message loop, outside the keyboard hook.

Tests: key normalization, legacy/sided matching, modifier tap state transitions and native LCtrl/RCtrl/LWin/RWin sender tests for both normal and fallback plugins, including extended-key flags. Injected input is excluded to prevent command recursion.
