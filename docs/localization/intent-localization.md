# AI, accessibility and mouse settings localization

The recent AI learning/review, correction dialogs, accessibility controls, mouse start buttons, default gesture display names and AI veto notifications use a shared catalog in `GestureSign.Foundation/Localization/Intent`.

- 90 locale identifiers match `UiTranslationCatalog.SupportedCultureNames`.
- English keys are stable format templates. Insert counts, gesture names and other runtime values **after** translation.
- Catalogs are embedded in Foundation so the settings app and gesture daemon use the same strings. No translation service is used at runtime.
- Existing optional learning components may return Chinese status messages. The settings display boundary maps known messages to the selected locale. Stored sample data, gesture IDs, user-defined names and diagnostic logs are not rewritten.
- Existing inline/JSON localization for other application pages remains in place.

## Translation quality

The new translations started with Google machine translation, followed by placeholder, encoding and coverage validation and terminology corrections. They have **not** all been reviewed by native speakers. Regional variants share translations where appropriate; Serbian Latin uses transliteration from Serbian Cyrillic. Norwegian Nynorsk received vocabulary adjustments from the Norwegian draft and particularly needs native review. Quechua and other lower-resource language translations also need native review before describing linguistic quality as verified.

When adding text, update every locale and run `tests/GestureSign.LocalizationTests`. The test checks the embedded locale count, key parity, format placeholders, actual catalog lookup, dynamic sample/notification numbers, legacy runtime messages and source key coverage. It does not certify linguistic quality or inspect every translated layout.

## Mouse binding and command sequences

The drawing-button selector and direct mouse-trigger selector have separate localized captions and help text. The direct trigger works during mouse gesture capture; its help must mention holding a start button first. Keep `MouseGestureButton` and `MouseHotkey` as separate stored fields.

The combination selector is labeled "Mouse button combinations" in all 90 locales. Both action creation and editing display the enabled start button(s), a plus sign, and the second input selector. Combination-only actions may leave the gesture pattern empty. Action summaries also show the combination; user-defined names are not translated.

The command-sequence list, order controls, delay editor, and add/edit command captions use the shared embedded catalog in all 90 locales. Existing generic UI translations are reused where available. User-defined action and command names remain unchanged when switching languages. New source keys are covered by the existing source-coverage and runtime-lookup checks. Resource coverage does not certify native-language fluency or layout quality.
