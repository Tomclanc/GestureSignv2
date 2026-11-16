# Localization follow-up — 2026-11-16

This update fills 39 English-valued UI entries in each of 86 non-English
catalogs: 3,354 replacements. Existing translations in Japanese, Simplified
Chinese and the two English catalogs are retained. All 90 locales now pass the
strict source audit for 683 UI keys and 261 intent keys.

The updated workflows include configuration import/backup, diagnostic export,
gesture/action editing, desktop edges and drag lock. Placeholders and the space
before a concatenated backup path are preserved. Warnings continue to explain
that imports replace rather than merge configuration, existing files are backed
up before writing, and diagnostics stay local unless the user shares them.

Translations were machine-assisted using software-specific context, followed
by terminology corrections. Traditional Chinese, Korean and Nynorsk received
separate revisions; Serbian Latin entries were converted to the Latin script.
Corrections also cover false senses of “custom”, “export”, “mouse” and “logs”.
No new untranslated-text exemptions were added to the audit allowlist.

Culture resolution now trims whitespace, accepts underscore separators and
preserves an explicit script during regional fallback. For example,
`sr-Cyrl-ME` resolves to `sr-Cyrl-RS`, not the Latin catalog. Regression checks
cover this fallback, Chinese region aliases, right-to-left detection and the
previously English-only workflows. Gesture directions and command identities
remain numeric/stable values rather than translated captions.

## Validation

- UI lookup, formatting, fallback and workflow translation tests pass for all
  90 locales, including right-to-left language detection and Serbian scripts.
- Strict source audit reports zero missing entries, format/text issues,
  unexplained English copies or hard-coded captions.
- Intent localization and updater-language checks pass for all 90 locales.
- WinUI x64 and ARM64 builds pass with zero warnings and errors.
- Final package checks compare external UI catalogs, embedded intent resources
  and all 90 embedded updater catalogs against source for both architectures.
  A negative control confirms that stale UI and updater resources are rejected.

Re-run from the repository root:

```powershell
dotnet run --project tests/GestureSign.UiLocalizationTests -c Release -- .
dotnet run --project tests/GestureSign.LocalizationTests -c Release -- .
dotnet run --project tests/GestureSign.VolumeUpdateTests -c Release
dotnet run --project tools/LocalizationAudit -c Release -- . path/to/report.json path/to/portable
```

## Review limits

These checks establish resource coverage, formatting and lookup behavior. They
do not constitute native-speaker certification or visual inspection of every
screen in every language. Low-resource languages and regional wording, notably
Quechua and Valencian, still benefit from review by their speakers. Quechua uses
a general Quechua translation as a base; Valencian uses the Catalan base. They
are not claimed to be independently certified regional translations.
