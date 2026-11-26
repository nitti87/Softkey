# Sökvägar och portabel installation

Genomgång 16 november 2026, efter användarens fråga om hårdkodade skriptsökvägar.

## Befintliga skript

`Sign-SoftKey.ps1`, `Install-Local-UIAccess.ps1`, `Install-SoftKeyLogonTest.ps1` och `Get-UIAccessDiagnostics.ps1` hittar egna projektfiler, byggresultat och stödfiler med `$PSScriptRoot` och `Join-Path`. Program Files hämtas från Windows. Windows PowerShell och systemverktyg hittas från Windows systemkatalog. De har ingen inbyggd hänvisning till `C:\sofType` eller en bestämd användares hemmapp.

Namnen `SoftKey-UIAccess-Test` och `SoftKey-Logon-Test` är fasta katalognamn för utvecklingsinstallationerna, inte fasta enhetsbokstäver. Tester har också fasta exempel för isolerade register-/filmodeller. Tillfälliga verifieringsskript i `obj` kan peka direkt på den aktuella datorns projekt och provinstallation; de är lokala provunderlag och ska inte distribueras som installationsstart.

Byggskripten förutsätter fortfarande källkod, .NET SDK och det lokala signeringsverktyget/certifikatet. En annan projektplats fungerar när hela utvecklingsmiljön följer med, men kopiering av dessa skript ger inte ett färdigt installationspaket för en ny dator.

## Appen efter installation

Appen hittar sina egna komponenter från sin programkatalog (`AppContext.BaseDirectory`). Personliga filer ligger under Windows användardatamapp. Startuppgiften och hjälpmedelsregistreringen innehåller den aktuella absoluta programsökvägen, skapad vid installationen. Brokern hittar den signerade startkomponenten via den verifierade startuppgiften. Den befintliga separata inloggningsmappen stöds som övergångslösning; på en ny installation finns komponenterna i appens eget paket.

UIAccess kräver den skyddade installationsplatsen och giltiga signaturer. Att manuellt flytta en installerad mapp uppdaterar inte Windows registreringar. En riktig installation eller uppdatering ska beräkna och registrera rätt sökvägar.

## Krav på slutpaketet

Paketet ska innehålla ett redan signerat program och nödvändiga beroenden, hitta sina egna installationsresurser och välja målkatalog från Windows Program Files. Det ska installera det offentliga certifikatet enligt den privata installationsplanen och skriva genvägar, startuppgift och hjälpmedelsregistrering med den faktiska målkatalogen. Ingen källkatalog, utvecklarprofil, privat signeringsnyckel eller byggmiljö ska behövas på den nya datorn. Befintliga ord och inställningar ska bevaras.

Slutpaketet är ännu inte byggt eller verifierat på en ny dator. Den tidigare dokumentationens exempel med `C:\sofType\Sign-SoftKey.ps1` var ett utvecklingskommando och har korrigerats i PRIVATE-INSTALLER.md.
