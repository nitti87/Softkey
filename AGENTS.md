# SoftKey

Bygg nya och ändrade SoftKey-dialoger enligt [DIALOG-DESIGN.md](DIALOG-DESIGN.md). Återanvänd den gemensamma dialogramen, titelraden och kontrollerna. **Sparade lösenord** och **Sparade telefonnummer** är designreferenserna.

Gemensamma ändringar i tangentbordets design/layout uppdateras med [Update-SoftKey.ps1](Update-SoftKey.ps1), som återanvänder de befintliga signerade installationsrutinerna och även uppdaterar den aktiva separata inloggningskopian. Behåll den avskalade inloggningsvyn och den gemensamma tangentbordskoden; se ändringshistorik, avgränsningar och återställning i [SIGNIN-DESIGN.md](SIGNIN-DESIGN.md). Använd inte en enbart skrivbordsinriktad uppdatering för gemensamma designändringar.

SoftKey är avsett för privat bruk på användarens datorer. För en uppdatering av den lokalt installerade UIAccess-versionen, följ [UIACCESS.md](UIACCESS.md) och använd [Install-Local-UIAccess.ps1](Install-Local-UIAccess.ps1). Bygg med UIAccess-manifestet och signera både `SoftKey.exe` och `SoftKey.dll` med det befintliga lokala certifikatet. Kontrollera båda signaturerna efter installationen och verifiera programstart med antivirus och realtidsskydd aktiva samt det av användaren avsedda SAC-läget.

Användarens tidsgräns är **26 november 2026**: SofTypes provperiod går ut om 12 dagar räknat från 14 november. Prioritera att färdigställa appen före den dagen.

Användaren har uttryckligen begärt att det **slutliga privata installationspaketet**, när appen är färdig, självt ska stänga av Smart App Control för hela datorn. Integrera då `-DisableSmartAppControl` i paketets installationsstart enligt [PRIVATE-INSTALLER.md](PRIVATE-INSTALLER.md). Användaren har därefter den 14 november också uttryckligen godkänt att SAC stängs av **nu på utvecklingsdatorn** för att kunna testa appen. Behåll antivirus och realtidsskydd, signera och kontrollera båda programfilerna samt verifiera programstart med det avsedda SAC-läget. När SAC är av ska vanliga appuppdateringar bevara det läget. Skicka inte fler versioner till Microsoft medan appen är under utveckling.

Installationspaketet ska även fungera vid byte till en ny dator utan manuella terminalkommandon eller förberedelser. Paketet ska hantera skyddsinställningen, UIAccess, nödvändigt offentligt signeringscertifikat, programmets beroenden, skyddad installationsplats, genvägar och standardinställningar. Det ska installera ett redan signerat program utan att den nya datorn behöver utvecklingsverktyg eller den privata signeringsnyckeln. Bevara befintliga personliga inställningar och ord vid uppdatering.
