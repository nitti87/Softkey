# SoftKeys uppdateringsfunktion

## Uppdatera från SoftKey

1. När den installerade skrivbordsappen startar kontrollerar SoftKey versionsinformationen på offentliga GitHub Releases efter cirka tolv sekunder och sedan ungefär en gång i timmen. Kontrollen använder varken konto eller token.
2. En diskret blå pil visas vid **Arkiv** när en nyare version finns. Öppna **Arkiv → Uppdatering** för att se versionsnummer, publiceringsdatum, paketstorlek och versionsanteckningar. Om ingen version visas kan du välja **Sök efter uppdateringar** i dialogen.
3. Välj **Installera uppdateringen** när du vill fortsätta. Först då börjar hämtningen av ZIP-paketet. Förloppsrutan visar paketnamn och hämtad storlek, följt av signaturkontrollen.
4. Godkänn installationen när Windows frågar. SoftKey sparar skrivbordsinställningarna och stänger tangentbordet så att programfilerna kan bytas. Förloppsrutan ligger kvar och visar aktuell fil och antal filer. Installationsprogrammet verifierar den nya versionen och öppnar SoftKey igen.

## Lokal testinstallation

Utvecklingsdatorn behöver en signerad version med uppdateringsmenyn för att prova flödet. Stäng SoftKey via **Arkiv → Avsluta**, öppna **Windows PowerShell** som administratör och kör följande från valfri mapp.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "C:\sofType\Softkey\Update-SoftKey.ps1" -Configuration Release -SignToolPath "C:\sofType\obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe"
```

Det här lokala kommandot bygger, signerar och installerar skrivbordsappen och den aktiva separata inloggningskopian tillsammans. SoftKey öppnas efter en lyckad installation. Därefter använder du **Arkiv → Uppdatering**. Kraven för det framtida installationspaketet till en ny dator finns i [PRIVATE-INSTALLER.md](PRIVATE-INSTALLER.md).

## Bygg och publicera en uppdatering

Kör utvecklingsskripten från Git-utcheckningen. Paketering och publicering använder Git Credential Manager för `nitti87`, eller `SOFTKEY_GITHUB_ACTIONS_TOKEN` respektive `SOFTKEY_GITHUB_CONTENTS_TOKEN` när de redan har angetts i miljön. Signering använder byggdatorns befintliga certifikat och privata nyckel.

| Steg | Skript och resultat |
|---|---|
| Bygg paketet | `New-SoftKeyUpdatePackage.ps1` tar `-RunId` från en lyckad körning av **Build and test SoftKey** efter en push till `main` och `-SignToolPath` till Microsoft-signerad SignTool. Skriptet bygger den testade committen. Körningens byggnummer blir en del av filnamnet `obj\Updates\SoftKey-update-<byggnummer>.zip`. |
| Förbered releasen | `Publish-SoftKeyUpdatePackage.ps1 -PackagePath <zip>` verifierar paketet, skapar eller återanvänder ett utkast för samma commit och paketdigest och provhämtar filen. Ange `-ReleaseNotes` när utkastet skapas för att fylla texten i uppdateringsdialogen. |
| Publicera | Med `-PublishPreview` blir utkastet en publik förhandsversion efter verifierad provhämtning. Taggen är `softkey-preview-<byggnummer>` och ZIP-filen måste ha samma byggnummer. Den publicerade versionen finns på [SoftKey Releases](https://github.com/nitti87/Softkey/releases). |
| Kontrollera hämtningen | `Get-SoftKeyUpdatePackage.ps1 -InstalledBuildNumber <byggnummer>` gör en uttrycklig provhämtning av en nyare publicerad version utan inloggning. Verifierade paket sparas i `obj\Updates\Received`. |

`Start-SoftKeyDelivery.ps1 -Configure -SignToolPath <signtool.exe>` (körd en gång i **Windows PowerShell som administratör**) ställer in Windows-uppgiften `SoftKey-DeliverUpdates` med hög körnivå (`RunLevel Highest`) på byggdatorn. Den söker efter lyckade pushar till `main` var femte minut, bygger och signerar tyst med byggdatorns lokala certifikat, fyller automatiskt i versionsanteckningar från commit-rubrikerna och publicerar efter verifierad provhämtning. Kontot för uppgiften behöver kunna läsa GitHub Actions, publicera releaser och använda signeringsnyckeln. Uppgiften skriver resultat och fel till `%LOCALAPPDATA%\SoftKey\Delivery\status.json`. Äldre konfigurationer med ett krypterat token fungerar tills de konfigureras om.

Om byggdatorns GitHub-inloggning har återkallats kan samma Windows-användare förnya den med `git credential-manager github login --username nitti87 --browser --force`. För manuell kopiering till en odelad OneDrive-mapp finns `Publish-SoftKeyUpdatePackage.ps1 -PackagePath <zip> -ToOneDrive`. Skriptet sparar den verifierade ZIP-filen i `SoftKey\Updates` och återanvänder en identisk fil vid omkörning. Skrivbordsappen söker efter publicerade uppdateringar på GitHub.

## Verifiering och återställning

- Versionskontrollen väljer den högsta publicerade förhandsversionen med rätt tagg och ZIP-fil. SoftKey jämför byggnumret med det högsta av versionsnumret i programmet och det skyddade `installed-update-build.txt`.
- Hämtningen jämför storlek och SHA-256 med GitHubs uppgifter. Arkivets signerade manifest måste matcha SoftKeys betrodda certifikat, den valda committen och byggnumret. Manifestet räknar upp de 16 filerna för skrivbord, autostart, låsskärm och Windows-inloggning, med storlek och SHA-256 för varje fil. Kontrollerade paket sparas i `%LOCALAPPDATA%\SoftKey\Updates`.
- Installationssteget kopierar paketet till en skyddad mapp och verifierar det igen. Det kontrollerar EXE- och DLL-signaturer, byter programfilerna i den ordinarie installationen och uppdaterar även den separata inloggningskopian om den är aktiv. Därefter kontrolleras de installerade filerna och programstarten. Defender och realtidsskydd ska vara aktiva och Smart App Control behåller sitt aktuella läge.
- Om en filkontroll eller ett startprov misslyckas försöker installeraren återställa de programfiler som hunnit bytas. Säkerhetskopiorna sparas om återställningen behöver slutföras manuellt. Personliga ord, inställningar och sparade förslag ligger utanför filbytet.

## Felsökning

| Vad du ser | Vad du kontrollerar |
|---|---|
| Ingen pil vid **Arkiv** | Öppna **Arkiv → Uppdatering** och sök igen. Kontrollera anslutningen och att GitHub har en publicerad förhandsversion med högre byggnummer och rätt ZIP-fil. |
| **Installera uppdateringen** är inaktiv | Öppna den installerade skrivbordsappen. En lokal förhandsvisning kan visa versionsinformationen. |
| Hämtningen eller verifieringen misslyckas | Försök igen från dialogen. Kontrollera på byggdatorn att rätt signerat paket ligger på rätt release. |
| Windows avbryter installationen | Starta om uppdateringen från skrivbordsappen och godkänn Windows dialog när den visas. |
| Lokal testinstallation får **Access is denied** eller SignTool hittar inget certifikat | Kör den lokala installationsraden i Windows PowerShell som administratör. Kontrollera vid fortsatta signeringsfel byggdatorns certifikat och åtkomst till nyckeln enligt [SIGNING-DIAGNOSIS.md](SIGNING-DIAGNOSIS.md). Resultaten finns i `UIAccess-install-result.json` och `obj\UIAccessTest\paired-update-result.json`. |

## Kod och teststatus

| Del | Kod |
|---|---|
| Schemalagd sökning, ikon och menyval | [KeyboardWindow.Updates.cs](KeyboardWindow.Updates.cs), [KeyboardWindow.Menu.cs](KeyboardWindow.Menu.cs) |
| Versionsdialog, förlopp och omstart | [SoftKeyUpdateWindow.cs](SoftKeyUpdateWindow.cs), [SoftKeyUpdateProgressView.cs](SoftKeyUpdateProgressView.cs), [SoftKeyUpdates.cs](SoftKeyUpdates.cs) |
| GitHub, pakethämtning och signaturkontroll | [SoftKey.UpdateDistribution.ps1](SoftKey.UpdateDistribution.ps1), [SoftKey.UpdatePackage.ps1](SoftKey.UpdatePackage.ps1) |
| Installation, förloppsfönster och återställning | [SoftKey.ApplyUpdate.ps1](SoftKey.ApplyUpdate.ps1), [SoftKey.InstallProgress.ps1](SoftKey.InstallProgress.ps1) |

[UpdatePackage.Tests.ps1](tests/UpdatePackage.Tests.ps1) provar att sökningen bara läser versionsinformationen, att en uttrycklig hämtning rapporterar filförlopp och att ändrade eller obehörigt signerade paket avvisas. Proven passerade i Windows PowerShell 5.1 och PowerShell 7. Dialogen och det fristående förloppsfönstret provades lokalt. Den 27 november 2026 rapporterade den samordnade lokala installationen att både skrivbordsappen och den separata inloggningsdelen hade uppdaterats. En komplett GitHub-uppdatering och ett praktiskt prov med HeadMouse på den riktiga Windows-inloggningsskärmen återstår att verifiera.
