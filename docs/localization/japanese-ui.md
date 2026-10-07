# Japanese UI localization

Japanese settings UI text is loaded from `GestureSign.WinUI/Languages/UI/ja-JP.json`.
Keys are the exact English strings passed to `L`, `T` and `F`; values are Japanese.
`L` now takes four arguments (Simplified Chinese, English, Traditional Chinese,
Korean). Japanese translations should be edited in the JSON resource, not in C#.
Missing Japanese keys fall back to English, including new editor/dialog strings.

The initial resource and catalog migration were contributed by Lambchop1020:
https://github.com/Lambchop1020/GestureSignv2/tree/japanese-localization
Source revision: 2845902732c4218b334135b0a3b25539b5fe8869
The integration retains all 368 contributed entries and adds 39 existing Japanese
translations that were missing from that resource, for 407 entries total.
The contributor's original resource commit is retained in Git history.

Keep English keys, `{0}` / `{1}` format slots, paths and command identifiers
unchanged. Do not change user-entered gesture, application or command names.
The UI resource is copied to published and portable builds by the existing
`Languages/UI/*.json` content rule.

Optional intent learning text uses a separate shared catalog:
`GestureSign.Foundation/Localization/Intent/ja-JP.json`.

Validation:

```powershell
dotnet run --project tests/GestureSign.UiLocalizationTests -c Release -- .
dotnet run --project tests/GestureSign.LocalizationTests -c Release -- .
```

The UI checks cover resource loading, missing-key fallback, Japanese key coverage,
duplicate keys, format placeholders and language-independent command/template IDs.
Please also proofread the Japanese UI in the application, including editor dialogs.