# Gemensamt tangentbord och avskalad inloggningsvy

## Ändring den 17 november 2026

Användaren bekräftade att inloggningen fungerade och bad om en enklare vy utan titelrad och meny. Framtida gemensamma designändringar ska följa med till inloggningen. Ändringen är avgränsad till den befintliga vyuppdelningen och ett separat uppdateringskommando; tangentmotorn, Windows-startvägarna och den aktiva registreringens installationsplats byggs inte om.

### Visning

- Skrivbordet har titelrad, samtliga menyer, ordlista, ordförslag och fullständig DragKey. I **Tangentbord → Inställningar → Övriga inställningar** väljs om menyn ligger bredvid toppdockad DragKey eller på en egen rad ovanför. Menyn på den egna raden döljs och återställs tillsammans med titelraden via DragKey. Menyn bredvid DragKey ligger kvar när titelraden döljs. Windows-inloggningen använder sin egen avskalade vy.
- Inloggning och låsskärm använder `WindowStyle.None`, visas inte i aktivitetsfältet och har ingen meny, inklusive den tidigare Mus-menyn. Musinställningsdialogen kan inte öppnas i detta läge.
- Med dold titelrad ritar SoftKey hela fönsterytan och behåller tangentytans inre avstånd. Windows kan aktivera fönstret utan att måla över ytan med sin titelrad. Kanterna går att använda för att ändra storlek. Samma fönsterhantering gäller på skrivbordet, låsskärmen och PIN-skärmen.
- DragKey i inloggningen har fortsatt endast AutoClick. Autoklick startar på, utan upprepning. Pausknappen finns alltid kvar.
- En 20 DIP hög diskret flyttyta ersätter menyn. Vila där, flytta pekaren och vila igen för att släppa. Även vanlig musdragning stöds. Flytt återanvänder befintlig skärmbegränsning och flyttar bara SoftKeys eget fönster, utan att hålla eller skicka en Windows-musknapp. Viloklicksflytt kräver aktivt inmatningsskrivbord och AutoClick på.
- Fönstret kan storleksändras och har minimimått anpassade till den avskalade vyn. Statusraden finns kvar för återkoppling och fel. Tangentnamn och inloggningstext visas eller sparas inte där.
- Ctrl → Alt → Delete använder samma befintliga tangenter och SendSAS-väg. Inga nya knappar har lagts till.

### Var den gemensamma koden finns

`SoftKey.csproj`, `Logon/SoftKey.Logon.csproj` och `LockedDesktop/SoftKey.Locked.csproj` kompilerar samma rotfiler. De två senare länkar `../*.cs`, med egna startprogram. Tangenterna kommer från `KeyboardLayout.cs`, tangentrader och knappar från `KeyboardWindow.Layout.cs` och den gemensamma knappdesignen. `secureMode` väljer den begränsade inloggningsvyn.

Skrivbordet, inloggningsvyn och förhandsvisningen bygger samma tangentyta. Funktionsraden visas i grupperna F1–F4, F5–F8 och F9–F12. Navigeringstangenterna är ordnade i tre par. Tangenternas ordning och inmatning följer den befintliga layouten.

Den nya flyttytan och dess startspärrar finns i `KeyboardWindow.SignIn.cs`. Endast inloggningsläget bygger denna yta. `KeyboardWindow.Menu.cs` blockerar menyer och `KeyboardWindow.cs` väljer fönsterstil. `KeyboardWindow.DragKey.cs` och `KeyboardWindow.Autoclick.cs` använder den befintliga flyttmotorn för flyttytans viloklick.

Gör framtida ändringar av gemensamma tangenter, färger och layout i den delade koden. Lägg skrivbordsfunktioner bakom den befintliga lägesavgränsningen. Flytta inte personliga ord, profilmappar, tillägg eller körbara inställningar till inloggningsdelen.

### Gemensamma tangentavstånd och teckenstorlek

**Tangentbord → Inställningar…** förhandsvisar de gemensamma tangentraderna. **Verkställ** och **OK** sparar horisontellt och vertikalt avstånd mellan 3 och 12 px samt teckenstorlek mellan 75 och 125 % i den personliga skrivbordsprofilen. Med **Använd samma inställningar vid Windows-inloggning** ikryssad sparar den signerade inställningshjälparen även samma värden i `HKLM\SOFTWARE\SoftKey\KeyboardLayout` efter Windows godkännande. Dialogen kommer ihåg det sparade valet. Om rutan avmarkeras används den senast sparade inloggningslayouten. Låsskärmen och inloggningsvyn läser den skyddade posten när respektive vy blir aktiv. Teckenstorleken är en andel av den storlek som passar tangentbordets aktuella mått.

Den äldre posten `KeyboardSpacing` används som reserv med 100 % teckenstorlek. Äldre värden under 3 px höjs till 3 px i respektive riktning. Saknas båda posterna används 4 px och 100 % vid inloggning. En äldre skrivbordsprofil utgår från den skyddade layout som redan gällde tills användaren sparar en egen layout. Den gemensamma uppdateraren för över äldre skrivbordsavstånd första gången en skyddad post behövs och hoppar över profiler med en egen sparad layout. Övriga inloggningsinställningar ligger kvar i den skyddade installationsmappen. Inloggningsvyn läser inga personliga profilfiler.

Skrivbordsappen mäter den renderade tangentytans bredd och höjd i enheter oberoende av skärmens pixeltäthet. Med samma inloggningsinställningar ikryssade sparar hjälparen även de två måtten i den skyddade posten `KeyboardAreaSize`. Den gemensamma uppdateraren för över den aktuella tangentytan första gången posten behövs. Låsskärmen och PIN-skärmen passar tangentytan till de måtten i sin avskalade ram. När skärmens arbetsyta är mindre skalas tangentytan proportionellt så långt minimimåtten tillåter. Navigeringstangenterna får samma radmått i båda vyerna. Efter en senare storleksändring på skrivbordet sparas de nya måtten för inloggning via samma kryssruta och **Verkställ** eller **OK**.

De sparade tangentavstånden gäller vid 100 %. Vid en annan teckenstorlek skalas mellanrummen med samma procent och avrundas inom 3–12 px. Förhandsvisningen visar både valt och anpassat värde när de skiljer sig. Indrag av knappens synliga yta och skugga använder del av det utrymmet där texten ryms med marginal. Klickytan täcker även indraget. Piltangenterna använder samma tydliga symboler i alla tre vyer och i förhandsvisningen.

Små tangentetiketter anpassas till skärmens pixlar. Vid trånga tangenter väljs en teckenstorlek som ryms före ritningen. DragKey väljer kompakt symbolgrafik efter det faktiska antalet skärmpixlar, även för AutoClick i inloggningsvyn och på låsskärmen.

### Autoklick vid Windows-inloggning

**Mus → Autoklick… → Verkställ** sparar **Tid före klick** och **Rörelsetolerans** för skrivbordet, låsskärmen och inloggningsvyn. Frågetecknet vid respektive inställning förklarar det när pekaren vilar över ikonen. Vid ändrade inloggningsvärden använder den installerade appen den signerade hjälparen och verifierar posten `HKLM\SOFTWARE\SoftKey\AutoclickTiming`. Den gemensamma uppdateraren för över befintliga skrivbordsvärden när posten saknas. Låsskärmen och inloggningsvyn använder de skyddade värdena när de startar och när de åter blir aktiva. Om posten saknas eller är ogiltig används `logon-settings.json` med befintliga gränsvärden. Inloggningsvyn startar med Autoklick aktivt och utan upprepade klick.

### Dragkeys storlek vid Windows-inloggning

**Mus → Dragkey… → Verkställ** delar storleksvalet mellan skrivbordet, låsskärmen och PIN-skärmen. **Normal** ger ordinarie storlek vid inloggningen. **Liten** skalar AutoClick-ikonen och dess panel till 80 % på båda axlarna, även när panelen är dockad. Den signerade hjälparen sparar enbart storleksvalet i `HKLM\SOFTWARE\SoftKey\SmallDragKeyIcons`. Den gemensamma uppdateraren hämtar det befintliga skrivbordsvalet första gången posten behövs. Vid ändrat storleksval anpassas inloggningsramen runt tangentytan så att de matchade tangentmåtten och flyttytan behålls.

### En gemensam lokal uppdatering

Kör `Update-SoftKey.ps1` på utvecklingsdatorn med SoftKey stängt. Detta är utvecklingsdatorns uppdateringsväg, inte det framtida installationspaketet för en ny dator.

1. Befintligt inloggningsval och registrerad installationsplats läses. Okända platser och motstridiga Windows-val stoppar före installation.
2. `Sign-SoftKey.ps1` bygger, signerar och installerar alla komponenter via `Install-Local-UIAccess.ps1`, och verifierar programstart och skydd. Båda EXE/DLL signeras som tidigare. Varken autostartval eller SAC ändras genom ett nytt val.
3. Om inloggningen redan använder huvudmappen är den delen också uppdaterad. Om inloggningen är av lämnas den av.
4. Om den aktiva inloggningen använder `SoftKey-Logon-Test` sparas dess kända filer och inställningar i en skyddad `.paired-update-<GUID>`-mapp. Befintlig avregistreringsrutin pausar de egna delarna. `Install-SoftKeyLogonTest.ps1 -UseInstalledPackage -PreserveSettings` installerar exakt de nyss signerade filerna från huvudinstallationen, utan en andra byggning eller signering, och återstartar den befintliga brokern.
5. Filernas hashvärden jämförs mellan installationerna. Inloggningsinställningarna ska vara bytevis bevarade. Rapporten blir `obj/UIAccessTest/paired-update-result.json`.

`-UseInstalledPackage` är ett nytt alternativ i den befintliga inloggningsinstallationen. Det accepterar bara huvudinstallationens skyddade plats, rätt installationsrapport och de verifierade signaturerna/hashvärdena. Det kan inte kombineras med `-UsePreparedPackage`. De gamla installationsvägarna finns kvar.

Om uppdateringen av den separata inloggningsdelen misslyckas efter säkerhetskopieringen gör uppdateraren ett kontrollerat återställningsförsök av filerna, registreringen och widgetsbrokern. Backupens signaturer och samtliga hashvärden kontrolleras före filåterställning. SYSTEM-processer tvångsavslutas inte. Säkerhetskopian behålls som återställningsunderlag. Om även återställningen misslyckas redovisas det separat och backupens plats finns i rapporten.

Uppdateringen är inte en enda transaktion över båda installationsmapparna: huvudappen kan vara ny medan den tidigare inloggningsdelen återställts. Då rapporteras misslyckad gemensam uppdatering, aldrig färdig installation. Åtgärda det redovisade felet och kör om med SoftKey stängt. Undvik separata skrivbordsuppdateringar för gemensamma layoutändringar; annars fortsätter den separata aktiva inloggningskopian använda tidigare kod.

### Kontroller och återstående användarprov

`tests/StartupChecks` kontrollerar titelrad/menyer, alla delade tangenter, spärrade inställningar, verklig flytt av ett testfönster utan inmatning/fokusbyte, paus och inaktivt skrivbord. Den renderar `signin-simple-keyboard.png`. Befintliga SAS-, sessions- och DragKey-prov fortsätter användas. `tests/PairedUpdate.Tests.ps1` provar lyckad samordning, avstängd/integrerad inloggning, fel före avregistrering och återställning efter avregistrering/installation/verifiering samt misslyckad återställning, med simulerade steg.

Inga PIN-koder, lösenord, faktiska Ctrl+Alt+Delete-anrop eller Windows-låsningar automatiseras. Efter den ändrade vyn behöver användaren prova flyttytan och fortsatt inloggning med HeadMouse. Aktuellt installations- och provresultat dokumenteras överst i UIACCESS.md och LOGON.md.
