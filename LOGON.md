# SoftKey vid Windows-inloggning

## Tydligare tangenttext, 26 november 2026

Den aktiva inloggningskopian använder samma större tangenttext, fetare bokstäver och siffror samt mindre inre marginaler som skrivbordet. Vyproven bekräftade att etiketterna ryms även vid minsta fönsterstorlek och att teckenstorleken matchar skrivbordet när tangentytan är densamma. Se [UIACCESS.md](UIACCESS.md) för installation och provresultat.

## Mindre marginal under statusraden, 25 november 2026

Den avskalade inloggningsvyn har samma kompakta nederkant som skrivbordsvyn utan titelrad. Under statusraden ligger tre pixlar vid 100 procents skärmskalning. Tester av vyn bekräftade nederkantens storleksändring, bevarade tangentmått vid byte av DragKey-storlek och matchning mot skrivbordets sparade tangentyta. Den aktiva inloggningskopian har uppdaterats och dess inställningar har verifierats. Se [UIACCESS.md](UIACCESS.md).

## Gemensam prestandauppdatering, 25 november 2026

Den aktiva inloggningskopian använder den nya gemensamma textmätningen för tangentetiketter. Återanvändningen gäller samma text, typsnitt, teckenstorlek och skärmskalning. Inloggningens befintliga AutoClick, flyttyta och avskalade tangentbord användes i de automatiska vyproven. Optimeringen för frågor om pekarmål i andra appar och den nya skrivfältskontrollen körs i skrivbordsläge.

Den samordnade uppdateringen verifierade att programfilerna matchar huvudinstallationens signerade filer och att inloggningsinställningarna är bytevis bevarade. Prov med HeadMouse på den riktiga Windows-inloggningsskärmen återstår. Se [UIACCESS.md](UIACCESS.md) för installation och provresultat.

## Avskalad vy installerad, 17 november 2026

Efter användarens bekräftelse att inloggningen fungerar har titelrad och hela menyn tagits bort i inloggnings- och låsskärmsvyn. Tangentbordet och enknappsversionen av AutoClick finns kvar. En diskret flyttyta kan starta och avsluta HeadMouse-flytt med viloklick utan någon ny verktygsknapp eller simulerad nedhållen musknapp. Den befintliga skärmbegränsningen, pausknappen och Ctrl → Alt → Delete-vägen behålls. Personliga data läses eller sparas inte i inloggningsläget.

Båda skyddade installationsmapparna är uppdaterade och deras inloggningsfiler har identiska hashvärden. Ord, personliga inställningar och inloggningsinställningar är bytevis bevarade. Samtliga tolv programfiler har giltiga signaturer och accepterad kodintegritet. 216 Startup-/sessions-/SAS-kontroller, 211 tillförlitlighetskontroller och 11 samordningskontroller passerade, liksom befintliga installationskontroller. Utseendet är granskat. Resultat: `obj\UIAccessTest\signin-shell-validation.json`.

Användaren bekräftade efter uppmaningen att prova flyttytan och inloggningen: **”Det funkade utmärkt!”** den 17 november. Den avskalade inloggningsvyn är därmed godkänd i användarens rapporterade prov. Detta är inte ett automatiserat prov av autentisering eller av en installation på en ny dator. Gemensam kod, historik och fortsatt uppdatering beskrivs i [SIGNIN-DESIGN.md](SIGNIN-DESIGN.md).

## Aktuell status: widgetsfelet bekräftat löst, 17 november 2026

Användaren bekräftade efter den senaste uppdateringen: **”Nu funkar det Perfekt!”** Det rapporterade felet vid Start → Lås betraktas som löst. Tidigare saknades tangentbordet på widgetsytan och kom först efter övergången mot PIN-rutan i en redan inloggad Windows-session. Godkännandet gäller detta rapporterade förlopp; omstart/uppvaknande och ett uttryckligt Ctrl → Alt → Delete-prov är separata driftprov.

Diagnostiken hade nya startposter för SYSTEM-tangentbordet på Winlogon men inga nya startposter för användarskrivbordets widgetsdel efter de senaste uppdateringarna. Det fanns bara en startuppgift för skrivbordsappen, vars startprogram väntade på upplåsning. Den vilande widgetsprocessen saknade egen oberoende start i den aktuella sessionen.

En provfix är nu installerad: `SoftKey-LoginBroker-<SID>` startar den signerade `SoftKey.Startup.exe --login-broker` med användarens interaktiva token, utan fördröjning, batteri-/nätvillkor eller körtidsgräns. Den kör direkt i bakgrunden under hela användarsessionen och begär den befintliga signerade låsskärmsdelen när Default-skrivbordet blir aktivt och Windows uttryckligen rapporterar låst. Den är kopplad till inloggningsvalet i Startup och fungerar oberoende av valet för automatisk skrivbordsstart. Den startas också omedelbart när inställningen installeras eller verkställs. Det behövs ingen ny knapp eller användarinställning.

Dubbelstart avvisas av uppgiftens IgnoreNew och befintlig instansspärr. En bakgrundsdel som startas på ett redan upplåst skrivbord öppnar inte skrivbordsappen på nytt. Gamla inloggningsinställningar migreras utan att inloggningsvalet stängs av. Fel vid skapande/start av uppgiften återställer tidigare registrering och uppgift. Uppdateringarna pausar och återstartar den egna bakgrundsuppgiften vid behov. Händelser lagras nu även per komponent med boolesk skrivbordsaktivitet, utan fönstertitlar, kontonamn, knapptryck eller autentiseringsuppgifter.

Statusraden visar inte längre nedtryckta modifierare eller någon lyckad Ctrl+Alt+Delete-begäran. Tangenternas färgmarkering och funktion finns kvar; felmeddelanden visas fortfarande. Detta gäller både skrivbords- och inloggningsläget.

136 Startup-/sessions-/SAS-kontroller, 211 tillförlitlighetskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. Den riktiga Windows-uppgiften har bekräftats som Running, med en bestående process på Default, och upprepad start behåller samma process. Appen och båda inloggningsdelarna är signerade och installerade. Se `obj\UIAccessTest\widgets-status-validation.json`.

**Användarprov:** det rapporterade Start → Lås-felet är bekräftat löst. Omstart/uppvaknande och ett uttryckligt Ctrl → Alt → Delete-prov är ännu inte separat dokumenterade. Ingen Windows-låsning eller autentisering har automatiserats. Den efterföljande DragKey-uppdateringen ändrar endast skrivbordsappen; de fungerande inloggningsdelarna och deras inställningar är bevarade.

Följande avsnitt är historik och kan innehålla äldre provstatus och lägen.

## Ctrl + Alt + Delete, 17 november 2026

Välj de vanliga tangenterna Ctrl, Alt och Delete i ordning. Funktionen använder Windows SendSAS när Delete väljs och lägger inte till någon extra knapp. Inloggningsdelens manifest begär nu UIAccess för Windows officiella stöd. Signerad installation, verklig UIAccess-behörighet samt Windows policy/UAC/export har verifierats utan att generera SAS. Samma stöd finns i låsskärmsdelen. Kontroll av rätt aktivt skrivbord, låsstatus och användar-/SYSTEM-kontext görs före varje begäran.

124 Startup-/sessions-/SAS-kontroller och 211 tillförlitlighetskontroller passerade. Inloggningsinställningarna är bytevis bevarade och antivirus/realtidsskydd är aktiva, SAC kvar i läge 0. Ingen PIN-inmatning, verklig SAS eller annan autentisering har automatiserats. Användarprov av Ctrl → Alt → Delete återstår. Se [SECURE-ATTENTION.md](SECURE-ATTENTION.md).

## Skrivbords-SoftKey kvar över låsskärmen, 16 november 2026

Sedan 19 november avslutas skrivbordsversionen vid låsning. [Aktuellt beteende efter låsning och upplåsning](STARTUP.md)

Användaren såg både det redan öppna skrivbordstangentbordet och det nya begränsade tangentbordet när Windows låstes via Start-menyn. Huvudappen har nu en egen sessionsvakt: WTS-låsmeddelande för den egna sessionen samt 200 ms reservkontroll. Den gömmer skrivbordets fönster och pausar dess inmatning och topmost-återställning medan sessionen är låst, okänd eller dess skrivbord inaktivt. Endast ett säkert upplåst, aktivt användarskrivbord återställer fönstren. Inloggnings- och låsskärmsdelarnas funktioner ändras inte av denna vakt.

Fönstren göms via native synlighet, vilket bevarar ShowDialog, positioner och minimering. Ingen WPF Hide/Show-cykel avslutar en pågående modal bekräftelse. Stängda dialoger återöppnas inte. Huvudinstansens lås hålls under hela övergången, och återvisningsbegäranden får inte visa dess fönster på låsskärmen.

Fixen är signerad, installerad och startkontrollerad med aktiva skydd. 89 Startup-/sessionskontroller och 159 tillförlitlighetskontroller passerade; inga verkliga autentiseringssteg automatiserades. Personliga filer och den befintliga separata inloggningsinstallationens inställningar är oförändrade. Windows Start → Lås behöver nu provas av användaren igen. Se `obj\UIAccessTest\desktop-lock-validation.json`.

Tekniskt underlag: [Microsofts sessionsregistrering](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification) och [lås-/upplåsningsmeddelanden](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change).

## Autoklick på och en enda DragKey-knapp, 16 november 2026

På användarens uttryckliga begäran har det tidigare pausade pilotläget ersatts. Både inloggnings- och låsskärmsdelen startar med autoklick på. En gammal StartPaused-inställning kan inte stänga av autoklick vid nästa start. DragKey visar bara autoklickknappen i ett kompakt 80 × 38-format. Knappen kan pausa och återuppta viloklick under den aktuella inloggningen; ingen personlig inställningsfil läses eller sparas.

Den aktiva installationen använder nu TakeOver. SofTypes secure-desktop-accommodation hänvisar till SoftKey och det tidigare värdet finns sparat för återställning. Numeriska inställningar för vilotid, jitter och fönsterstorlek är bevarade. Övriga muslägen är också spärrade i den begränsade delens åtgärdshanterare, så att borttagna knappar inte lämnar dolda val aktiva.

159 tillförlitlighetskontroller, 65 Startup-/dialogkontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. Enknappslayouten har renderats och granskats. Appen och den separata aktiva inloggningsdelen är signerade, installerade och verifierade. Tolv installerade EXE/DLL har giltiga signaturer, matchande hashvärden och accepterad kodintegritet. Personliga ord och inställningar är bytevis oförändrade. Antivirus och realtidsskydd är aktiva, SAC förblev 0. Resultat: `obj\UIAccessTest\login-autoclick-validation.json`.

Den nya autoklickfunktionen behöver provas vid riktig inloggning med HeadMouse. Ingen PIN-/lösenordsinmatning eller låsning av Windows har automatiserats. Följande avsnitt beskriver tidigare provversioner; deras SofTypeDwell-läge gäller inte längre den aktuella installationen.

## Bekräftat användarprov och Startup-dialog

Den 16 november bekräftade användaren att den uppdaterade inloggningsfunktionen fungerar perfekt efter provet av övergången mellan widgetlåsskärm och PIN-ruta. Detta ersätter den tidigare statusen att det rapporterade försvinnandet återstår att prova. Separat kallstarts-/uppvaknandeprov och automatisk skrivbordsstart är egna kontroller.

Startfunktionerna kan nu styras via `Arkiv → Startup…` i vanliga SoftKey. Se [STARTUP.md](STARTUP.md). Den fungerande separata inloggningsinstallationens filer och dess nuvarande viloklicksläge bevaras.

## Övergång mellan widgetlåsskärm och PIN-ruta, 16 november 2026

Användaren har bekräftat att den tidigare provversionen syns först men försvinner när låsskärmen med väder/nyheter visas, **innan** PIN/lösenord har angetts. Den senaste kontrollen hittade ingen registrerad SoftKey-krasch. Startmarkören bekräftade att SYSTEM-tangentbordet faktiskt hade laddats. Detta fastställer inte ensamt vilket fönsterbyte som orsakar det observerade felet.

En uppdaterad provversion är nu installerad med följande ändringar:

- SYSTEM-tangentbordet på `Winlogon` behålls vid skrivbordsbyte (`TerminateOnDesktopSwitch=0`), och kan åter bli tillgängligt när den skyddade skärmen aktiveras.
- En separat signerad UIAccess-del, `SoftKey.Locked.exe`/DLL, kan visa samma begränsade tangentbord på användarens `Default`-skrivbord när Windows uttryckligen rapporterar att sessionen är låst. Detta täcker övergången till den andra låsskärmsytan.
- Startdelen på användarskrivbordet kontrollerar låsstatus och begär denna UIAccess-del endast när sessionen är låst och skrivbordet är aktivt. Inga tangenttryck skickas av startdelen.
- Låsskärmsdelen avvisar start på en olåst session, blockerar all inmatning efter upplåsning och stänger sitt fönster. Okänd låsstatus tillåter ingen inmatning.
- Endast den aktiva skrivbordsdelens inmatning tillåts. Ingen del laddar eller sparar personliga ord i inloggningsläget.
- Statushistorik med högst åtta poster per process anger start, låst/olåst och aktiv/inaktiv skärm. Den innehåller inga kontonamn, fönstertitlar, tangenttryck eller skrivna tecken. SYSTEM-status finns i vår HKLM-hjälpmedelsnyckel, användarstatus i `HKCU\Software\SoftKey\LogonDiagnostics`.

153 tillförlitlighetskontroller, 19 installationskontroller och de 16 befintliga privata installationskontrollerna passerade. Windows låsstatus kunde läsas som olåst på det aktuella vanliga skrivbordet. Installeraren verifierade att Windows faktiskt gav UIAccess till den nya delen samt att den avvisade start som låsskärmstangentbord när sessionen var olåst. Samtliga fyra installerade EXE/DLL har giltig signatur och accepterad kodintegritet. Antivirus och realtidsskydd var aktiva och SAC bevarades som 0.

**Den observerade övergången behöver nu provas av användaren igen. Ingen lyckad verklig PIN-/lösenordsinmatning eller lösning av det rapporterade felet är ännu bekräftad.** Börja med att låsa Windows, låta widgetarna visas och kontrollera om tangentbordet finns kvar eller kommer tillbaka på PIN-rutan. Det vanliga SoftKey-programmets installerade filer har inte bytts ut. Aktuellt viloklicksläge är fortsatt `SofTypeDwell`.

## Privat provversion 16 november 2026

En separat inloggningsdel är byggd, signerad och installerad i `C:\Program Files\SoftKey-Logon-Test`.
Windows registrering för hjälpmedel, `SoftKey_Logon_v1`, är aktiverad för inloggning och den nuvarande användarens skrivbordsbyten.
Den befintliga installationen av vanliga SoftKey är inte utbytt. Andra hjälpmedels registreringar bevaras.

**Aktuellt första provläge: `SofTypeDwell`.** SofType är redan registrerat för både inloggning och skrivbord. Det fortsätter därför sköta viloklick medan SoftKey visar sitt tangentbord med eget viloklick pausat, så att dubbla tangenttryck undviks. Användarens val mellan detta läge och att låta SoftKey ta över är ännu inte inkommet.
Första provet gäller att SoftKeys tangentbord visas och kan skriva med det befintliga viloklicket. SoftKeys eget viloklick måste därefter provas separat.

Installationsrapport: `obj\UIAccessTest\logon-install-result.json`, status `EnabledAwaitingUserTest`.
Både inloggningsdelens och den nya låsskärmsdelens EXE och DLL har giltig signatur med det befintliga lokala certifikatet och godkänd Windows-kodintegritet.
De befintliga installerade `SoftKey.exe` och `SoftKey.dll` har också giltiga signaturer.
Antivirus och realtidsskydd var aktiva under installationen. SAC förblev 0.
Användarens `settings.json` och `words.json` har samma SHA256 som före installationen.

153 tillförlitlighetskontroller, varav 38 nya för inloggning och låsskärm, samt 19 installationskontroller och 16 befintliga privata installationskontroller passerade.
Tangenttryck och musklick i de nya testerna är simulerade. Registreringstesterna använder ett register helt i minnet.
Det installerade förhandsvisningsfönstret startades och granskades visuellt på det vanliga skrivbordet; svenska tangenter, Mus-menyn och DragKey finns.
Förhandsvisningen startar med viloklick pausat för att inte konkurrera med användarens vanliga tangentbord. Den har därefter stängts.

**Fullständig användning på Windows skyddade skärm är ännu inte verifierad.** Start har bekräftats men användaren har rapporterat att tangentbordet försvinner före PIN-rutan. Den nya övergångshanteringen, viloklick, PIN/lösenord, återgång till skrivbordet, uppvaknande och kallstart återstår att prova.
Kallstart och nya datorer är separata kontroller; denna lokala provinstallation är inte det färdiga privata installationspaketet.

## Prova utan terminalkommandon

1. Behåll befintlig fungerande möjlighet att logga in tillgänglig under det första provet.
2. Lås själv Windows från Start-menyns kontomeny eller med ditt befintliga tangentbord.
3. Kontrollera att `SoftKey – Inloggning` visas automatiskt och att HeadMouse styr pekaren.
4. Använd SofTypes befintliga viloklick för att välja PIN-/lösenordsfältet och trycka på SoftKeys tangenter. SoftKeys eget viloklick är pausat i det första provläget.
5. Ange din kod enbart i Windows och logga in själv. Skicka aldrig kod eller lösenord till chatten.
6. Kontrollera att inloggningsdelen försvinner och att vanliga SoftKey fungerar igen.

Om tangentbordet inte syns eller viloklick inte fungerar: använd din tidigare fungerande inloggningsmetod och rapportera vilket steg som misslyckades.
Gör ingen omstart förrän låsning och upplåsning har fungerat.
Genvägen `Stäng av inloggningstest.lnk` i projektmappen avregistrerar enbart denna SoftKey-funktion med administratörsgodkännande. Övriga hjälpmedel och programfiler bevaras.

## Uppbyggnad

`Logon\SoftKey.Logon.csproj` delar tangentbord, native-inmatning, DragKey och viloklick med den vanliga appen.
Den har en egen startpunkt och kör alltid `KeyboardWindow` i det begränsade inloggningsläget.
Ordlistor laddas inte, ordspårning/inlärning/förslag avbryts vid samtliga ingångar, ordlistemenyer kan inte öppnas och ingen personlig lagring används.
Inställningarna läses från en begränsad, skrivskyddad `logon-settings.json` i installationsmappen.
Endast vilotid, rörelsetolerans, bredd och höjd kopieras vid installationen. Det skyddade pilotvalet `StartPaused` styr vilket program som sköter viloklick. Alla numeriska värden begränsas; inga kommandon, DLL-namn, ord eller användarsökvägar stöds.

Windows startar inloggningsdelen genom sin registrering för hjälpmedel med argumentet `--ease-of-access`.
Själva tangentbordet kräver både SYSTEM-identitet och skrivbordet `Winlogon`.
På användarens `Default`-skrivbord övervakar en registrerad instans låsstatus utan att själv visa ett fönster eller skicka inmatning. När sessionen är låst och skrivbordet aktivt startas den signerade UIAccess-delen. Windows kan hålla separata instanser tillgängliga på skrivborden (`TerminateOnDesktopSwitch=0`).
Inloggningsdelens manifest begär därför `asInvoker`, `uiAccess=false`: Windows tillhandahåller SYSTEM-kontexten. UIAccess ger inte ensamt åtkomst till SYSTEM-gränssnittet.
Vanliga `SoftKey.exe` byggs fortsatt med UIAccess-manifestet. Både dess EXE/DLL och inloggningsdelens EXE/DLL signerades vid paketförberedelsen.
Inloggningsdelens samtliga native-inmatningsanrop kontrollerar att processens skrivbord fortfarande är det aktiva inmatningsskrivbordet.
Tangenternas tecken följer det aktiva inmatningsfönstrets tangentbordsspråk, vilket kan skilja sig från SYSTEM-kontots standardspråk.
När ett riktigt skyddat fönster har laddats sparas endast `LastSecureStartUtc` i vår egen hjälpmedelsregistrering som startbevis; inga tangenttryck eller inloggningsuppgifter loggas.
Windows sköter autentiseringen; SoftKey känner inte till eller lagrar inloggningsuppgifterna.

Stängning och minimering av det riktiga inloggningstangentbordet blockeras för att det ska förbli åtkomligt. Förhandsvisning på vanliga skrivbordet kan stängas.
Startdelen kan på nytt begära låsskärmens UIAccess-del efter att den har avslutats, med minst fem sekunder mellan försök. Dess nya Windows-uppgift har även tre återstartsförsök med en minuts mellanrum efter fel. Någon separat övervakare av SYSTEM-tangentbordet finns inte; dess start sköts av Windows hjälpmedelsregistrering. Återhämtning behöver fortfarande provas för självständig daglig användning.

## Installation och avregistrering

`Install-SoftKeyLogonTest.ps1` använder `Install-Local-UIAccess.ps1 -Build -IncludeLogon -SignOnly` för signeringen.
De redan signerade inloggningsfilerna kopieras till en separat skyddad mapp. Signaturer, filhashar, kodintegritet, antivirus, realtidsskydd och oförändrat SAC-läge kontrolleras före aktivering.
Det ordinarie SoftKey-programmet och personliga data ersätts inte.
`-UsePreparedPackage` återanvänder ett signerat paket efter ny signatur- och hashkontroll. `-Disable` tar bort enbart vår registrering och listpost i HKLM/HKCU.

Standardläget `-DwellMode SofTypeDwell` bevarar SofType som aktivt hjälpmedel. Det alternativa `-DwellMode TakeOver` förbereds för användarens val: SofTypes `SecureDesktopAccommodation` hänvisas då till vår registrerade inloggningsdel enligt Microsofts dokumenterade mekanism. SofTypes vanliga startprogram och skrivbordsfunktion lämnas kvar.
Föregående värde/presens sparas först i `softype-secure-desktop-backup.json` i den skyddade installationsmappen. `-Disable` eller återgång till `SofTypeDwell` återställer det, utan att skriva över senare oberoende ändringar.
När provet inaktiveras avslutas också enbart provprogrammets egna vilande processer under den nuvarande användaren i samma session. Ett SYSTEM-tangentbord på den skyddade skärmen avslutas inte.
Den nya SYSTEM-delen observerar själv att registreringen tas bort och stänger då sitt eget fönster. Installeraren ger den tid att avsluta utan tvångsavslutning.

Registrering sker i 64-bitarsvyn under `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility`.
Konfigurationslistorna bevarar övriga poster, och registerfel återställer vår tidigare registrering/listmedlemskap.
Om registeråterställning behöver uppmärksamhet bevaras de signerade filerna och felet rapporteras i installationsrapporten.
Skyddade stagingmappar behålls som återställningsunderlag.

Det första installationsförsöket avbröts före filinstallation/registrering när ett befintligt behörighetsnamn inte kunde översättas. Behörigheter läses nu direkt som SID, och nästa körning lyckades.
Det första felet finns i `obj\UIAccessTest\logon-install-initial-acl-failure.json`.

## Källor

- [Microsoft: registrering av hjälpmedel och secure desktop](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration)
- [Microsoft: UIAccess och SYSTEM-gränssnitt](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
- [Microsoft: Windows sessionslåsstatus](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w)

