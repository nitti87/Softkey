# Signeringskontroll 14 november 2026

**Senare uppdatering:** användaren har nu uttryckligen begärt SAC av även på testdatorn. Avstängningen är utförd och verifierad, med antivirus och realtidsskydd fortsatt aktiva. Den tidigare blockerade DLL-filen är nu signerad, accepterad av kodintegritetskontrollen, installerad och startkontrollerad. Se `obj\UIAccessTest\approved-private-install-result.json` och UIACCESS.md. Beskrivningen nedan avser felsökningen innan denna användargodkända systemändring.

Den nya DLL-filen är fortfarande blockerad av Windows. Den tidigare installerade versionen är bevarad, har giltiga signaturer och har startkontrollerats med Smart App Control, antivirus och realtidsskydd aktiva. Det är inte verifierat att nya paket kan installeras.

## Vad som kontrollerades

- Senaste koden byggdes som Debug utan fel eller varningar och signerades med Microsoft SignTool och det befintliga certifikatet. De nyare ändringarna i DragKey-filerna är bevarade.
- Både EXE och DLL har giltiga Authenticode-signaturer. Windows godkänner EXE-filen men avvisar DLL-filen med fel 4551, policy `VerifiedAndReputableDesktop`, `VerificationError 18`.
- Samma signerade paket startades normalt i en separat skyddad mapp under Program Files. .NET stoppades med `System.IO.FileLoadException`, `0x800711C7`, för DLL-filen. Felet gäller alltså verklig programstart och inte enbart installationsskriptets bildmappningskontroll. Behåll den kontrollen och återställningsskyddet.
- Microsoft Defenders säkerhetsinformation uppdaterades från `1.463.296.0` till `1.463.299.0`. Anslutningen till MAPS verifierades med administratörsbehörighet. DLL-filen blockerades även efter uppdateringen.
- Ingen skyddsinställning, policy eller certifikatbutik ändrades. Inga undantag lades till. Ordlista och inställningar var oförändrade efter installationsförsöket.
- Testmappen under Program Files togs bort efter uttrycklig kontroll av dess absoluta sökväg och innehåll. Den fungerande installationen startades igen.

Blockerad DLL: `C5900979833F33E9E82779D8D6B76554221DA14BEBE7C8DFEED01F99EECD6C45`.

Bevarad fungerande DLL: `B4F64D269D271B3D6BCA052526354D4890F4CEFF9BE610DA4F0F9A5E794DD63D`.

Resultat finns i `obj\UIAccessTest\native-launch-check-result.json`, `native-launch-failure-events.json`, `protection-update-result.json`, `signing-repair-attempt-result.json`, `native-launch-cleanup-result.json` och `signing-repair-existing-start-result.json`.

## Microsofts tidigare ärende

Ärende [4ca6f6c6-300d-439c-9763-1153b0609d8c](https://www.microsoft.com/en-us/wdsi/submission/4ca6f6c6-300d-439c-9763-1153b0609d8c) visade `Completed` vid kontroll den 14 november. Analytikerns kommentar bekräftar att minst en tidigare inskickad fil felaktigt blockerats av Smart App Control och att det hade åtgärdats. Filraderna visade en blandning av `Pending` och `Not malware`. Detta innebär inte ett verifierat godkännande av den nya DLL-filen, som har ett annat innehåll.

Ett uppföljningspaket är färdigt men inte inskickat: `obj\UIAccessTest\MicrosoftReview-CurrentDLL-20261114-173455.zip`. Det innehåller enbart den aktuella signerade DLL-filen, en granskningsbegäran med uppgiften om enbart privat bruk, en filhash och en sammanfattning av kontrollerna. Privat nyckel, personliga ord, inställningar, källkod och felsökningssymboler ingår inte. Paketet kontrollerades efter komprimering. Metadata: `obj\UIAccessTest\followup-review-package-result.json`.

## Officiell dokumentation

- [Microsofts betydelse av VerificationError 18](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-tag-explanations): signaturen matchar inte de signeringskrav som gäller för UMCI.
- [Signering för Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control): en giltig lokal självsignering är inte likvärdig med en signatur från en betrodd certifikatutfärdare.
- [Uppdatering av Defenders säkerhetsinformation](https://learn.microsoft.com/en-us/powershell/module/defender/update-mpsignature).

Fortsätt använda det befintliga certifikatet och kontrollerna i UIACCESS.md. Byt inte certifikat, ändra inte binärerna för att få nya hashvärden och ta inte bort Windows-kontrollerna som ett sätt att kringgå blockeringen. Framtida uppladdning av det nya paketet behöver användarens godkännande.

Senare den 14 november gav användaren en uttrycklig instruktion att det slutliga privata installationspaketet ska stänga av Smart App Control när appen är färdig. Detta framtida installationskrav, tidsgränsen 26 november och det förberedda stödet dokumenteras i [PRIVATE-INSTALLER.md](PRIVATE-INSTALLER.md) och AGENTS.md. Det ändrar inte resultaten ovan: SAC är fortfarande aktivt och ingen faktisk avstängning har körts under utvecklingen. Vänta med nya Microsoft-inskickningar tills användaren anser versionen färdig.
