# Slutlig privat installation

## Läget den 17 november 2026

Användaren har godkänt den senaste avskalade inloggningsvyn men därefter uttryckligen förtydligat att appen har långt kvar och att **ingen första version ska skapas nu**. Arbetet fortsätter som utveckling. Den framtida privata installeraren behöver fortfarande hantera båda appdelarna, versionshantering, personliga data och övergången från testregistreringarna. Det portabla installationspaketet har ännu inte byggts. Den fungerande installationen och testregistreringarna ska behållas under den fortsatta utvecklingen.

### Framtida automatiska uppdateringar

GitHub Actions bygger och testar varje push till `main`. `New-SoftKeyUpdatePackage.ps1` förbereder ett signerat uppdateringspaket på utvecklingsdatorn för en lyckad körning.

Skriptet kan köras i Windows PowerShell 5.1 eller PowerShell 7 från en utcheckning vars historik innehåller den testade committen. Ange körningens numeriska ID och `-SignToolPath` till en lokal Microsoft-signerad SignTool. Det frågar efter en GitHub-token med läsrättighet till repots Actions och döljer inmatningen. För automatiserad körning kan tokenet anges via miljövariabeln `SOFTKEY_GITHUB_ACTIONS_TOKEN`. Signeringen använder det befintliga lokala certifikatet och dess behörigheter.

Skriptet bygger från ett rent utdrag av den testade committen. Paketet `obj\Updates\SoftKey-update-<nummer>.zip` innehåller de fyra SoftKey-delarna och ett signerat manifest med körningens versionsnummer, commit och SHA-256 för samtliga filer. Paketeringen verifierar arkivet före sparning. En befintlig version ersätts aldrig vid omkörning. För att undvika att äldre uppdateringar används kan verifieraren jämföra versionsnumret med den installerade versionen.

`Publish-SoftKeyUpdatePackage.ps1 -PackagePath <zip>` verifierar paketet, lägger det i ett releaseutkast i det privata repot och provhämtar det. Ange ett GitHub-token med skrivbehörighet till repots Contents när skriptet frågar. Omkörning återanvänder ett utkast med samma commit och paketdigest. Med `-PublishPreview` publiceras en förhandsversion först när GitHub-kopian har hämtats tillbaka och verifierats.

`Get-SoftKeyUpdatePackage.ps1 -InstalledBuildNumber <nummer>` testar hämtning av den senaste publicerade förhandsversionen med ett token som har läsbehörighet till repots Contents. Skriptet accepterar endast högre byggnummer och sparar verifierade paket i `obj\Updates\Received`. Båda skripten verifierar paketet mot SoftKeys betrodda certifikat och körs från Windows PowerShell 5.1 eller PowerShell 7.

`Start-SoftKeyDelivery.ps1 -Configure -SignToolPath <signtool.exe>` kopplar en lokal Windows-uppgift till utvecklingsmappen. Engångskonfigurationen behöver Actions-läsbehörighet och Contents-skrivbehörighet för det privata repot. Tokenet krypteras för Windows-användaren. Uppgiften körs som vanlig användare, tittar efter lyckade pushar till `main` och publicerar paketet efter verifierad provhämtning. För nya paket måste användaren som kör uppgiften ha signeringsbehörighet till det lokala certifikatet. Vid nekad nyckelåtkomst sparar uppgiften ett fel i `%LOCALAPPDATA%\SoftKey\Delivery\status.json`.

Skrivbordsmenyn **Uppdateringar** tar emot ett separat GitHub-token med läsbehörighet till Contents en gång. SoftKey krypterar det för den inloggade Windows-användaren, söker i bakgrunden och verifierar paketet före **Installera och starta om**. Windows godkänner installationen när användaren väljer den. Installationsarbetet använder en verifierad kopia i den skyddade programmappen, byter programfiler i skrivbords- och eventuella inloggningsmappar och återställer tidigare filer vid ett installations- eller startprov som misslyckas. Personfilerna ligger utanför transaktionen. Ett riktigt inloggningsprov med HeadMouse krävs fortfarande vid första utrullningen.

`Update-SoftKey.ps1` är utvecklingsdatorns lokala bygg-, signerings- och installationsverktyg. En framtida installerad app ska i stället kunna kontrollera en publicerad version, hämta ett färdigt signerat paket och kontrollera dess äkthet innan installation. Signeringsnyckeln ska stanna på byggdatorn. Skrivbords- och inloggningsdelarna ska uppdateras samordnat, med bevarade ord/inställningar och möjlighet att återgå till fungerande programfiler.

Automatisk kontroll/hämtning och installation utan administratörsdialog är skilda frågor. Installation i de skyddade programmapparna behöver rätt behörighet. En förinstallerad uppdateringsdel med avgränsade rättigheter är en möjlig lösning för tyst installation, men arkitekturen är inte beslutad. Uppdateringar ska planeras så att de inte avbryter skrivande eller pågående inloggning. Vanliga uppdateringar ska fortsätta bevara datorns avsedda skyddsläge.

## Användarens krav

- Färdigställ appen före **26 november 2026**, då SofTypes provperiod löper ut enligt användarens uppgift den 14 november.
- Appen och installationspaketet är endast för användarens privata bruk, även vid flytt till en ny egen dator.
- När appen är färdig och det slutliga privata installationspaketet byggs ska paketets installationsstart automatiskt ange `-DisableSmartAppControl`.
- Installationen ska informera om att detta stänger av Smart App Control för hela datorn. Återaktivering beror på Windows-versionen och får inte utlovas som en enkel återställning.
- Behåll Microsoft Defender Antivirus och realtidsskydd. Behåll signering med det befintliga lokala certifikatet, kontroll av både EXE och DLL, kodintegritetskontroll och säker återställning av programfiler vid installationsfel.
- Ingen ny Microsoft-inskickning medan appen är under utveckling.

## Ny dator och nuvarande testdator

Användaren har den 14 november även uttryckligen godkänt avstängning av SAC nu på den nuvarande datorn för att kunna testa appen. Detta ersätter den tidigare begränsningen att endast förbereda stödet under utvecklingen. Antivirus och realtidsskydd ska fortsatt vara aktiva.

Det slutliga paketet ska sköta samtliga förberedelser även på en ny egen dator: kontroll av Windows-version och behörighet, SAC-inställningen, installation av nödvändigt offentligt certifikat för UIAccess, programmets beroenden, Program Files-installation, genvägar och standardinställningar. Inga manuella terminalsteg ska behövas. Vid uppdatering ska personliga inställningar och ord bevaras.

Ctrl+Alt+Delete-stödet tillagt den 17 november kräver dessutom aktiv UAC och att Windows SoftwareSASGeneration tillåter hjälpmedelsappar från användarskrivbordet. På nuvarande dator är värdena redan EnableLUA=1 och SoftwareSASGeneration=3 och har bevarats. Slutpaketet måste hantera denna kontroll och nödvändig installation på en ny dator; en befintlig tillåtande konfiguration ska bevaras. Enbart kopiering av EXE/DLL bevisar inte att förutsättningarna finns. Alla tre tangentbordsdelar ska vara signerade UIAccess-program. Se [SECURE-ATTENTION.md](SECURE-ATTENTION.md).

Det nuvarande lokala byggskriptet kräver utvecklingsmiljön och ett befintligt certifikat med privat nyckel. Det är därför **inte** ett färdigt portabelt installationspaket. När slutpaketet byggs ska EXE och DLL redan vara signerade; den nya datorn ska endast få nödvändigt offentligt certifikat och ska varken behöva privat signeringsnyckel, SignTool eller utvecklings-SDK. Alla nödvändiga programberoenden ska ingå eller hanteras av paketet. Exportera inte den privata nyckeln.

## Förberett stöd

`Sign-SoftKey.ps1` och `Install-Local-UIAccess.ps1` accepterar `-DisableSmartAppControl`. Flaggan kan inte kombineras med `-SignOnly`. Utan flaggan bevaras det befintliga SAC-läget vid uppdatering och kontrolleras vid programstart; flaggan begär uttryckligen läge 0.

Vid slutlig installation byggs och signeras paketet först. Först när båda signaturerna är giltiga och SoftKey är stängt ändras SAC-inställningen med administratörsrättigheter. Den Microsoft-signerade `CiTool.exe` uppdaterar policyn. Installationen kontrollerar både registerläget och att SAC-policyn inte längre är verkställande innan programfilerna ersätts. Andra App Control-policyer ändras inte; deras kontroller kan fortfarande stoppa en installation.

SAC-åtgärden ligger i `SoftKey.Installation.ps1`. Filen ska finnas med när installationsstarten paketeras. Åtgärden journalför föregående läge, resultat och eventuella fel i `obj\UIAccessTest\private-install-security-result.json`. Vid ett fel görs ingen automatisk återaktivering, eftersom det inte fungerar på alla Windows-versioner. Programfilernas återställningsskydd är kvar.

`Sign-SoftKey.ps1 -Configuration Release -DisableSmartAppControl` är ett lokalt bygg-, signerings- och testflöde på utvecklingsdatorn. Det ska inte användas som installationsstart på en ny dator.

Den färdiga privata installationsstarten ska använda paketets egen plats för att hitta redan signerade programfiler och inkluderade resurser. Målkatalogen ska räknas fram från Windows Program Files-mapp. Hjälpmedelsregistrering, autostartuppgift och genvägar ska skapas med den faktiska målsökvägen på just den datorn. Paketets installationsrutin ska anropa det förberedda SAC-stödet med `-DisableSmartAppControl`; bygg- och signeringsverktyg ska inte köras på måldatorn.

En absolut sökväg som Windows sparar efter installationen är avsiktlig och nödvändig för startregistreringen. En inbyggd hänvisning till utvecklingsmappen `C:\sofType` eller utvecklarens användarprofil ska däremot inte finnas i slutpaketets installationsflöde. Se [INSTALLATION-PATHS.md](INSTALLATION-PATHS.md) för genomgången av befintliga skript. Det slutliga portabla installationspaketet har ännu inte byggts eller körts.

## Kontroll utan systemändring

SAC-funktionen stöder `-WhatIf`, som endast visar den avsedda åtgärden. Automatiska regressionstester simulerar registerskrivning och policyuppdatering så att testerna inte ändrar datorns skydd.

Den 14 november passerade 16 kontroller i `tests\PrivateInstaller.Tests.ps1`, inklusive simulerade fel och tidsgränser vid policyuppdatering samt kontroll att båda startskripten avvisar `-SignOnly -DisableSmartAppControl`. Resultat: `obj\UIAccessTest\private-installer-check-result.json`.

Den första faktiska policyuppdateringen ändrade SAC till av men CiTool-processen fastnade efter ändringen. Just den verktygsprocessen avbröts efter kontroll av dess identitet. Rutinen har därför nu en tidsgräns på 45 sekunder och fortsätter efter en sådan tidsgräns endast om en separat policykontroll verkligen bekräftar att SAC inte verkställs. En redan avstängd och verifierad SAC kräver ingen ny policyuppdatering. Även textvärdena `True` och `False` från CiTool hanteras korrekt.

SAC-avstängning och lokal testinstallation är nu faktiskt genomförda på användarens uttryckliga begäran. SAC-läget är `0`, SAC-policyerna är inte verkställande, antivirus och realtidsskydd är aktiva, båda installerade signaturerna är giltiga, Windows kodintegritetskontroller accepterar filerna och appstart är verifierad. Personliga ord och inställningar är oförändrade. Resultat: `obj\UIAccessTest\approved-private-install-result.json`. Den tidigare avbrutna körningen finns i `approved-private-install-initial-refresh-failure.json` och `private-install-security-initial-refresh-failure.json`. Detta är en lokal installation på utvecklingsdatorn, inte ett test av det ännu ej byggda portabla slutpaketet på en ny dator.

Microsofts dokumenterade administrationsmetod finns i [Application Control for Windows](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/appcontrol). Information om återaktivering finns i [Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions).
