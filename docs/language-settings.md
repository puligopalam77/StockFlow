# Language settings

The top-bar language dropdown automatically saves one business-wide language.
Owners/admins with Company settings Edit permission can change it; staff see
the configured language. There is no separate Configuration page or Save button. English stays
visible and translations appear in parentheses. Every non-English choice in the
language catalog has a local JSON pack under `src/StockFlow.Web/wwwroot/languages`.
The browser loads only the selected pack and caches it for that page session.
The Help panel loads a separate local pack under `languages/help` for the same
selection. It keeps English instructions alongside translated paragraphs and
supports searching either language. Help wording starts with machine translations;
native review of shop terminology remains necessary.
Saving applies the new pack immediately. Other tabs check the setting on focus
and active pages check every 30 seconds. Customer/product data and saved values
are not translated. English is used for newly introduced, unmapped captions.

The initial additional packs are machine translations of public interface wording.
Existing Telugu and Hindi captions are retained. Native-language review can improve
shop terminology by editing the corresponding JSON values. Keep keys lowercase,
and preserve `{label}` and `{limit}` in validation templates.

`tests/StockFlow.UiChecks/generate-language-packs.cjs` is a development-only,
resumable generator. It extracts static page captions, placeholders and common
validation templates. Its PowerShell helper sends those public strings to public
translation services. It never connects to the business database or reads records,
passwords or connection settings. The running app never uses these services.
Generated files are committed; generation is not part of normal build/startup.

Source language-code references: [Google translation language codes](https://docs.cloud.google.com/translate/docs/languages)
and [Microsoft translation language codes](https://learn.microsoft.com/azure/ai-services/Translator/language-support).
The app keeps its existing codes; generation maps Konkani `kok` to `gom` and
Manipuri `mni` to `mni-Mtei` for Google. Bodo and Kashmiri use Microsoft translations.

The browser checks select all 22 languages, verify captions and validation tokens,
exercise English reset and verify that screen data values remain unchanged. These
checks verify application behavior and pack coverage, not linguistic accuracy.
No additional database migration is needed after migration 036.
