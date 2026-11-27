# Gemensam dialogdesign

**Sparade lösenord** och **Sparade telefonnummer** är referenserna för alla dialogrutor som SoftKey bygger. Återanvänd de gemensamma komponenterna så att ramen, titelraden och hoverläget utvecklas tillsammans.

Dialogerna följer temat **Varm Sten & Skiffer**. `SoftKeyPopupDesign` hämtar de varma ytorna och skifferaccenterna från tangentbordets palett i `SoftKeyKeyDesign`. Använd de delade färgerna även i egna fält, ikoner, listor och reglage. Laddningsindikatorer använder `SoftKeyPopupDesign.ProgressTemplate` för både bestämt och obestämt förlopp.

## Bygg dialogen uppifrån och ned

1. **Fönster och titelrad.** Sätt `Title` och anropa `SoftKeyPopupDesign.ConfigureWindow(this)`. Sätt sedan `Content` med `SoftKeyPopupDesign.Frame`. Ramen ger rundade hörn, tunn kant, mjuk skugga och en varm ljusgrå innehållsyta. Titelraden är dragbar och har titeln till vänster och stängknappen till höger. Titeltexten använder Segoe UI Semibold i storlek 12 via `SoftKeyPopupDesign.TitleFontSize`.
2. **Innehåll.** Lägg sökning eller filter ovanför resultaten när listan behöver det. Visa listposter och inställningsval som ljusa, rundade kort med tunn kant och luft mellan raderna. Hela kortet är klickbart när det representerar ett val. Visa en huvudrad och en eventuell detaljrad, till exempel namn och telefonnummer. Låt lång text radbrytas och gör långa listor rullningsbara.
3. **Åtgärder.** Använd `SoftKeyPopupDesign.Sections` när rutan behöver en nedre knapprad. Bygg knapparna med `SoftKeyButtonDesign.Action` och placera dem med `SoftKeyButtonDesign.Row`. Huvudåtgärden har skifferblå bakgrund och ljus text och ligger längst till höger. Vid stängning av appen eller borttagning är **Avbryt** standardvalet. I en dialog där valet av ett kort slutför åtgärden räcker stängknappen i titelraden.
4. **Verkställ.** Använd `SoftKeyButtonDesign.Apply` och håll knappen inaktiv tills en inställning har en ändring kvar att spara. Uppdatera läget vid redigering, återställning och sparande med `SoftKeyButtonDesign.SetApplyState`.

## Kort och interaktion

| När dialogen innehåller | Återanvänd | Referens |
| --- | --- | --- |
| En lista där användaren väljer ett sparat värde | `SavedSuggestionPickerDesign.Configure`, `ScrollCards` och `Card`. `Configure` sätter redan upp den gemensamma dialogramen. | [PhoneNumberWindow.cs](PhoneNumberWindow.cs), [PasswordSuggestionWindow.cs](PasswordSuggestionWindow.cs) |
| Inställningar med kryssrutor | `SoftKeyCheckBox.SettingsRow` för hela krysskortet. | [StartupSettingsWindow.cs](StartupSettingsWindow.cs), [MouseSettingsWindow.cs](MouseSettingsWindow.cs) |
| Inställningskort med symbol | `SoftKeySettingsDesign.CheckOption` och `RadioOption` för gemensamma kort med en enkel symbol samt rubrik och förklaring. | [StartupSettingsWindow.cs](StartupSettingsWindow.cs), [DragKeySettingsWindow.cs](DragKeySettingsWindow.cs), [KeyboardSettingsWindow.cs](KeyboardSettingsWindow.cs) |
| Flikar | `SoftKeyTabDesign.Headers` för lika breda flikar, en blå markering för den aktiva fliken samt gemensamma hover- och fokuslägen. | [KeyboardSettingsWindow.cs](KeyboardSettingsWindow.cs), [TabbedWordListWindow.cs](TabbedWordListWindow.cs) |
| Sökning eller redigering i en lista | Ordlistans inmatningsfält och valbara rader. | [WordListWindow.cs](WordListWindow.cs) |
| En fråga eller ett informationsmeddelande | Den gemensamma ramen och den nedre knappraden. | [RemoveWordConfirmationWindow.cs](RemoveWordConfirmationWindow.cs), [SoftKeyNoticeWindow.cs](SoftKeyNoticeWindow.cs) |

Använd kortens färger från `SoftKeyPopupDesign` och övergången från `SoftKeyHoverTransition`. Bakgrund och kant tonas mellan normal- och hoverläge på 150 ms när Windows animeringar är aktiva. Korten i förslagslistorna tonar även in en skugga. Låt övergången vända från sitt aktuella läge när pekaren lämnar ett kort mitt i toningen. Kortet ligger still så att klickytan förblir stabil för Autoklick. Fokus och valt läge ska gå att urskilja även utan pekaren över kontrollen.

Använd `SoftKeyTabDesign.ResizePage` när dialogens höjd ändras vid ett flikbyte. Övergången börjar vid aktuell höjd, följer Windows animationsinställning och håller dialogrutan inom arbetsytan med `SoftKeyPopupDesign.KeepResizeInWorkArea`.

## Placering av dialogrutor

Tilldela en ägare före `Show()` eller `ShowDialog()` när rutan öppnas från SoftKey. Den gemensamma placeringen centrerar dialogen inom arbetsytan på SoftKeys bildskärm, även för bekräftelser som öppnas från en annan dialog. En fristående dialog centreras på sin bildskärm. Anpassa bredd och höjd efter innehållet och den tillgängliga arbetsytan.

## Kontrollera en ändrad dialog

Granska titel, ram, normalläge, hover och tangentbordsfokus. Prova klick och Autoklick där det är relevant. Kontrollera Escape, stängknappen, standardvalet och att text och knappar ryms med lång text eller en lång lista. Testa sökning, bekräftelse och felmeddelanden där rutan har dem. Vid ändringar som också berör Autoklick-dialogen vid inloggning följer installationen den gemensamma uppdateringsvägen i [AGENTS.md](AGENTS.md).

## Verifiering av den tidigare dialogdesignen, 16 november 2026

44 dialog-/Startup-kontroller och 153 tillförlitlighetskontroller passerade. Dialogerna renderades och granskades i normalläge, vid arbete och fel, med sökning/redigering samt med ett mycket långt ord. Kontrollernas ordlista och inställningar är isolerade från användarens filer.

Förhandsvisningarna finns i `obj\UIAccessTest` som `dialog-autoclick.png`, `dialog-word-list.png`, `dialog-word-search.png`, `dialog-word-edit.png`, `dialog-close.png`, `dialog-remove-word.png`, `dialog-remove-long-word.png` och `dialog-notice.png`.

Den signerade uppdateringen är installerad i `C:\Program Files\SoftKey-UIAccess-Test`. Även den separata inloggningsdelen i `C:\Program Files\SoftKey-Logon-Test` har uppdaterats, så att Autoklick-rutan där följer samma design. Dess befintliga logon-settings.json och viloklicksläge bevarades bytevis respektive oförändrat. Den använde de redan signerade filerna från huvudpaketet, med ny signatur-, hash- och kodintegritetskontroll i den ordinarie inloggningsinstalleraren.

En överflödig återställningsskrivning av logon-settings.json nekades i den icke upphöjda startprocessen. En oberoende kontroll bekräftade att filen redan var bytevis identisk med originalet efter installationen; ingen återställning behövdes.

Installerade signaturer och filhashar är verifierade. Programstart och Startup-komponentens läs-/signaturkontroll passerade med antivirus och realtidsskydd aktiva och SAC kvar i läge 0. Personliga ord och inställningar är bevarade.

Resultat finns i `obj\UIAccessTest\all-dialog-design-validation.json`, `all-dialog-protection-check.json`, `all-dialog-checks.txt` och `all-dialog-reliability-checks.txt`.
