# Startup-inställningar

## Reservstart efter krasch på skrivbordet, 25 november 2026

Den installerade skrivbordsappen startar en liten bevakare i den befintliga signerade komponenten `SoftKey.Startup.exe`. Bevakaren väntar på den exakta skrivbordsprocessen i samma användarsession och använder Windows avslutskod för att skilja ett felavslut från en normal stängning. Det gäller även när SoftKey öppnas från en genväg. Bevakaren avslutas när programmet stängs normalt, inklusive när Windows låser datorn.

Vid ett felavslut kan den verifierade startvägen öppna SoftKey med den sparade profilen på det aktiva, upplåsta skrivbordet. Den befintliga instanskontrollen förhindrar dubbla tangentbord. Två snabba återstartsförsök är tillåtna. Efter en minuts drift är båda försöken tillgängliga igen. Funktionens första version gäller felavslut. En process som fryser och fortsätter köra samt en oberoende reserv för PIN-skärmen kräver egna lösningar.

De riktade proven kontrollerade både normala och avvikande avslut hos en testprocess, kontroll av processidentitet och gränsen för återstarter. Simulerade Windows-låsningar stängde appen normalt i båda vägarna. Det installerade tangentbordet startade och hade en egen körande bevakare i samma session. Ett framprovocerat felavslut i användarens aktiva tangentbord ingick inte i installationstestet. Se [UIACCESS.md](UIACCESS.md) för verifieringen.

## Start efter omstart

När **Visa SoftKey före inloggning** är aktiverat registrerar installationen SoftKey även i välkomstskärmens Windows-profil (`HKEY_USERS\.DEFAULT`). Det gör valet tillgängligt innan den vanliga användarprofilen har lästs in. Äldre installationer kompletteras under uppdatering och avaktivering tar bort endast SoftKeys egen post. Se [Microsofts dokumentation för hjälpmedelsregistrering](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration).

Skrivbordets startuppgift väntar på det upplåsta, aktiva skrivbordet. Den lämnar sedan den verifierade programstarten till Utforskaren så att tangentbordet kan fortsätta köra när uppgiften avslutas. Vid en avbruten start försöker den igen. Om försöken tar slut rapporterar den fel till Schemaläggaren, som kan starta om uppgiften. [Microsofts beskrivning av programstart via Utforskaren](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643)

Startuppgiften får en beredskapssignal när tangentbordet har ritats och visas på det aktiva, upplåsta skrivbordet. Signalen gäller för den körande instansen och upphör när den stängs. Ett pågående startförsök kan upprepas om signalen uteblir. En senare manuell stängning utlöser ingen ny start.

På låsskärmen väntar ett redan öppet tangentbord kvar om Windows tillfälligt inte kan rapportera låsläget. Inmatningen är pausad tills låsläget åter är bekräftat. Före PIN-rutan loggas att tangentbordet är klart när det har ritats och visas på den aktiva, skyddade skärmen.

## Tillbaka till skrivbordet från Ctrl+Alt+Delete

När inloggningstangentbordet visas på Windows säkerhetsskärm i en upplåst session rapporterar det en tidpunkt för den sessionen i sin skyddade Windows-registrering. Startprogrammet kan öppna SoftKey efter både **Avbryt** och **Aktivitetshanteraren** när den upplåsta sessionens skrivbord åter är aktivt. En ny rapport räcker även när skrivbordet hinner växla tillbaka mellan två kontroller. Tidigare rapporter förbrukas vid en lyckad återstart och får ingen effekt efter en bekräftad låsning. Starten använder samma signaturkontroll och instanslås som vanlig autostart. Vid låsning styr kryssrutan för upplåsning vad som händer efteråt. [Microsofts dokumentation om Windows skrivbord](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops)

## Skrivbordsstart efter upplåsning, 19 november 2026

**Starta SoftKey när du låser upp datorn** och **Starta SoftKey när du loggar in** väljs var för sig. Windows-uppgiften innehåller de starttillfällen som är aktiverade för användaren. Uppgiften väntar på det aktiva, upplåsta skrivbordet.

Skrivbordsversionen avslutas när Windows meddelar att datorn har låsts. Den sparar inställningarna och stänger DragKey och öppna dialoger utan bekräftelse. En kontroll var 200 ms upptäcker också bekräftad låsning om ett meddelande missas. Om start vid upplåsning är vald öppnar Windows en ny instans när datorn låses upp. [Microsofts dokumentation för låsmeddelandet](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change)

Uppdateringen läser starttillfällena från den redan installerade uppgiften. Båda skrivbordsvalen visas som aktiverade när uppgiften har både inloggning och upplåsning. Upplåsningen använder Windows användarspecifika sessionsutlösare. [Microsofts dokumentation för sessionsutlösare](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-sessionstatechangetrigger-triggergroup-element)

## Oberoende widgetsstart, 17 november 2026

Inloggningsvalet hanterar nu även en separat användaruppgift `SoftKey-LoginBroker-<SID>`. Den startar `SoftKey.Startup.exe --login-broker` omedelbart och vid användarens Windows-inloggning, oberoende av automatisk skrivbordsstart. Uppgiften har normal processprioritet, noll fördröjning, obegränsad körtid och inga batteri-/nätkrav. Den faktiska låsskärmsdelen kör fortfarande med High-prioritet och får bara skicka inmatning på ett aktivt, låst Default-skrivbord. SYSTEM-delen på Winlogon behålls separat.

Befintlig aktiverad hjälpmedelsregistrering känns igen som aktiverad även om uppgiften ännu saknas, så uppdateringen kan migrera den utan att stänga av inloggningen. Skapande, borttagning och omedelbar start ingår i verkställningens verifiering och återställning vid fel. Fristående skrivbordsuppgiften och dess prioritet ändras inte av detta inloggningsval. Se aktuell provstatus i [LOGON.md](LOGON.md).

## Inloggningens autoklick, 16 november 2026

Inloggningsdelen startar nu alltid med eget autoklick på, även om en gammal `StartPaused:true` ligger kvar i ett konfigurationsunderlag. DragKey visar enbart autoklickknappen i detta läge. Vid aktivering av inloggningsvalet förbereder startkomponenten också återställningsuppgifter för SofType om det finns installerat och låter SoftKey vara dess secure-desktop-accommodation. Avaktivering återställer tidigare värde. Den aktiva separata inloggningsinstallationen är uppdaterad med `-DwellMode TakeOver -PreserveSettings`; flaggan bevarar numeriska inställningar och migrerar endast det gamla pausade läget. De tidigare historiska avsnittens SofTypeDwell-läge har därmed ersatts på användarens uttryckliga begäran.

## Snabbare skrivbordsstart och hög prioritet, 16 november 2026

Automatisk start använder nu en användarspecifik Windows-uppgift `SoftKey-Autostart-<SID>` med LogonTrigger, fördröjning `PT0S` och prioritet 1 (High). Den körs med användarens interaktiva token, utan sparat lösenord och utan administrativ körnivå. Batteridrift, inaktivitet och nätanslutning blockerar inte starten. Startkomponenten väntar vid behov på en säkert upplåst, aktiv Default-desktop; den begär inte omstart av programmet när användaren senare stänger det.

När även inloggningsfunktionen är aktiv kan dess redan startade användarbroker begära skrivbordsstart vid första upplåsta aktiva skrivbordet. Kontrollen sker var 250 ms. SYSTEM-delen startar aldrig den personliga appen. Brokern läser uppgiftens fasta startkomponent, kontrollerar användare, startvillkor, skyddad installationsplats och båda signaturerna. Startvägarna samordnas med ett gemensamt startlås och appens befintliga instanslås. Ett lyckat överlämnande upprepas inte vid senare låsningar i samma brokersession.

Vanliga appen och de två tangentbordsdelarna begär processprioritet High vid start. Windows realtidsprioritet används inte eftersom den kan störa mus- och tangentbordsinmatning. Hög prioritet styr CPU-tilldelning; Windows garanterar fortfarande inte att SoftKey blir det första synliga programmet.

Det gamla egna Run-kommandot identifieras och ersätts när startinställningar verkställs eller appen uppdateras. Uppgiften skapas först; sedan tas bara det matchande gamla Run-värdet bort. Vid fel återställs tidigare uppgifts-XML, Run-värde och eventuella inloggningsändringar. Andra autostartprogram påverkas inte. Uppdatering bevarar befintligt av/på-val; installationsflaggan `-EnableAutomaticStartup` aktiverar valet uttryckligen. Användaren har begärt denna aktivering för det aktuella provet.

59 Startup-/dialogkontroller omfattar även migrering, felåterställning, nollfördröjning, prioritet, rätt användare och överlämnandets låsgränser. Tre ytterligare kontroller mot den riktiga installationen verifierar att användaren läser den normaliserade Windows-uppgiften och kan begära start genom brokerns överlämningsväg. Windows skriver om triggerns SID till kontonamn och utelämnar standardvärden; verifieringen accepterar dessa likvärdiga former utan att acceptera administrativ körnivå, fördröjning eller främmande åtgärder.

Uppdateringen är signerad och installerad både i appmappen och den aktiva separata inloggningsmappen. Det tidigare inloggningsvalet bevarades och automatisk skrivbordsstart aktiverades enligt användarens begäran. Alla tolv installerade EXE/DLL har giltiga signaturer, matchande paket-hashar och accepterad kodintegritet. Ett verkligt startprov genom Windows uppgift bekräftade synligt tangentbord, processprioritet High och en enda instans vid samtidiga startbegäranden. Antivirus och realtidsskydd var aktiva och SAC förblev 0. Personliga ord och inloggningsinställningar är bytevis oförändrade; den körande appen har sparat om sin personliga settings.json under startproven. Installationskoden ändrar inte den filen. Se `obj\UIAccessTest\fast-start-validation.json`.

153 tillförlitlighetskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade också. Den aktiva inloggningsdelen uppdaterades med `Install-SoftKeyLogonTest.ps1 -UsePreparedPackage -PreserveSettings -DwellMode SofTypeDwell`; flaggan bevarar dess befintliga konfigurationsfil exakt och avvisar samtidigt byte av viloklicksläge. Tiden från verklig Windows-inloggning till synligt tangentbord måste provas vid nästa inloggning. Ingen låsning, PIN-inmatning eller utloggning har automatiserats.

## Finjusterad dialog, 16 november 2026

Dialogen har förenklats efter användarens återkoppling: vit sammanhållen inställningsyta, två rymliga rader med tydliga kryssrutor, en diskret avdelare och en samlad nedre yta för status och knappar. Den extra grupprubriken och ramarna runt varje alternativ är borttagna. Höjden är nu cirka 270 bildpunkter vid 100 procent, jämfört med cirka 318 tidigare. Förloppsindikatorns utrymme är reserverat så att den inte flyttar knapparna när arbetet börjar. Avstängda knappar tonas ned under arbetet.

Funktionerna är oförändrade. De 39 Startup-kontrollerna och 153 tillförlitlighetskontrollerna passerade. Normalläge, arbete och felmeddelande renderades och granskades. Den nya versionen är signerad, installerad och startverifierad med antivirus och realtidsskydd aktiva samt SAC kvar i läge 0. Samtliga åtta installerade EXE/DLL har giltiga signaturer och godkänd kodintegritet. Personliga inställningar och ord är bytevis oförändrade. Verifiering: `obj\UIAccessTest\startup-design-validation.json`.

## Tillgängligt i appen

`Arkiv → Startup…` öppnar en dialogruta i SoftKeys befintliga stil med tre oberoende val:

- **Visa SoftKey före inloggning** registrerar SoftKeys tangentbord för lås- och inloggningsskärmen.
- **Starta SoftKey när du loggar in** öppnar skrivbordsversionen efter inloggning.
- **Starta SoftKey när du låser upp datorn** öppnar skrivbordsversionen på nytt efter upplåsning.

Kryssrutorna läser verklig Windows-konfiguration när rutan öppnas. De sparas inte som separata, potentiellt inaktuella booleska värden i personliga settings.json.

**OK** genomför och verifierar ändringarna och stänger sedan rutan. **Verkställ** gör samma arbete och lämnar rutan öppen. **Avbryt** eller stängning utan verkställning ändrar ingenting.
Under arbetet visas en förloppsindikator och aktuell åtgärd. Kryssrutor och knappar spärras, och dialogen kan inte stängas innan arbetet är klart. Vid fel visas ett meddelande och dialogen hålls öppen. Tidigare registerinställningar återställs vid delvis misslyckade ändringar.

Dialogen fungerar med SoftKeys vanliga viloklick. Öppning släpper SoftKeys egna modifierartangenter, och tangentbordet skickar inga bokstavstangenter medan Startup-rutan är öppen. Startup finns inte i inloggningsdelens begränsade menyer.

## Installation och komponenter

`Sign-SoftKey.ps1` inkluderar nu Startup-komponenterna som standard. `Install-Local-UIAccess.ps1 -IncludeStartup` bygger och signerar app, inloggningsdel, låsskärmsdel och startinställningsdel; både EXE och DLL kontrolleras för samtliga komponenter.

Den 16 november installerades uppdateringen som Debug i `C:\Program Files\SoftKey-UIAccess-Test`. Samtliga åtta installerade EXE/DLL fick giltig signatur med det befintliga lokala certifikatet och accepterad Windows-kodintegritet. Programstart verifierades med antivirus och realtidsskydd aktiva samt oförändrat SAC-läge 0.

Den redan fungerande inloggningsinstallationen i `C:\Program Files\SoftKey-Logon-Test` används fortsatt om den finns. Dess programfiler ersätts inte av denna uppdatering. På en ny installation finns nödvändiga inloggningsfiler i appens eget paket. Aktuellt viloklicksläge och befintlig logon-settings.json bevaras när funktionerna ändras. Det slutliga portabla paketets övriga beroenden och offentliga certifikat hanteras enligt PRIVATE-INSTALLER.md; Startup-inställningarna behöver inga utvecklingsverktyg eller privat signeringsnyckel.

## Hur ändringen genomförs

`StartupClient` startar den redan installerade och signerade `SoftKey.Startup.exe` med Windows administratörsgodkännande när inställningarna behöver ändras. Samma Windows-konto behöver inte nödvändigtvis stå för godkännandet: hjälpprogrammet identifierar den riktiga SoftKey-processens användare och ändrar den användarens registergren.

En unik lokal named pipe tillåter dialogens användare och administratörer. Båda sidor kontrollerar den anslutna processens ID. Hjälpprogrammet verifierar att beställningen kommer från rätt signerade SoftKey-program i den skyddade installationsmappen. Meddelandena innehåller endast startvalen, begränsade numeriska tangentbordsinställningar och status. Inga ord eller inloggningsuppgifter överförs.

Hjälpprogrammet accepterar inga godtyckliga program- eller utmatningssökvägar. Det använder de fasta komponentnamnen i den skyddade appinstallationen eller den befintliga privata inloggningsinstallationen. Authenticode verifieras med Windows WinVerifyTrust och certifikatets befintliga tumavtryck. Kontrollen behöver inte nätåtkomst.

Inloggningsvalet använder `SoftKey_Logon_v1` och bevarar andra hjälpmedels poster. Avregistrering återställer SofTypes tidigare secure-desktop-accommodation om det hänvisade till SoftKey och sparade återställningsuppgifter finns.
Automatisk skrivbordsstart använder den egna Windows-startuppgiften enligt avsnittet ovan. Det äldre Run-värdet känns igen enbart för migrering. Inloggningsläget och automatisk skrivbordsstart kan användas oberoende av varandra.

Ett globalt lås serialiserar egna ändringar. Konfigurationslistor ändras med bara vår listpost, så att återställning bevarar andra hjälpmedels eventuella samtidiga tillägg. Vid fel återställs också hela vår tidigare hjälpmedelsregistrering och egna startvärden. Vid en ofullständig återställning visas ett uttryckligt fel.

## Verifiering

- 39 nya kontroller passerade för de fyra kombinationerna av startval, bevarande av andra program, avvisad felaktig konfiguration, återställning efter fel, protokoll, laddningsvisning, stängningsspärr, OK, Verkställ och felmeddelanden.
- 153 befintliga tillförlitlighetskontroller och 16 privata installationskontroller passerade.
- Dialogen renderades och granskades i normalt läge, under arbete och med ett felmeddelande. Bilder finns i `obj\UIAccessTest\startup-dialog.png`, `startup-progress.png` och `startup-error.png`.
- Den installerade startkomponentens läs- och signaturkontroll `--verify-installation` avslutades med kod 0. Den ändrar inga inställningar.
- Installationsrapporten finns i `UIAccess-install-result.json`; testresultat i `obj\UIAccessTest\startup-checks.txt` och `startup-reliability-checks.txt`.

Konfigurations- och dialogtesterna simulerar systemändringarna utan att slå av den fungerande inloggningsfunktionen. Faktisk automatisk start vid en ny Windows-inloggning återstår att prova efter användarens val i dialogen.

## Källor

- [Microsoft: Run- och RunOnce-nycklar](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [Microsoft: logon trigger](https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger)
- [Microsoft: prioritet för startuppgifter](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-priority)
- [Microsoft: processprioritet och varning för realtime](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities)
- [Microsoft: named pipe med åtkomstkontroll](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create)
- [Microsoft: registrering av hjälpmedel](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration)
