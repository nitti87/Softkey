# Uppdatera den privata UIAccess-installationen

Skrivbordsappen, Startup, låsskärmsdelen och inloggningsdelen byggs med .NET 10. Utvecklingsdatorn behöver .NET 10 SDK. Den installerade x64-versionen använder .NET 10 Desktop Runtime för x64, även när Windows startar SoftKey före inloggning. Använd `Update-SoftKey.ps1` för att uppdatera alla delar tillsammans.

## Smidigare storleksändring, 26 november 2026

Tangenttexterna anpassas till det tillgängliga utrymmet under storleksändring. Den gemensamma grundstorleken räknas om när dragningen avslutas, både vid musdragning och viloklick. Samma beräkning används för slutstorleken på skrivbordet, vid låsning och vid inloggning.

I jämförelseprovet sjönk medianen per storlekssteg på skrivbordet från cirka 23 till 15 ms. De 22 riktade vyproven verifierade bland annat att texten ryms under dragningen och får rätt storlek efteråt. Alla 215 tillförlitlighetskontroller passerade. Den bredare sviten stannade vid AutoClick-målet för fliken Övriga inställningar. Samma kontroll föll med den tidigare storleksändringen.

Release byggde utan varningar. Uppdateraren nekades administratörsbehörighet innan installationen började. Kör den samordnade Release-uppdateringen från en PowerShell med administratörsbehörighet för att installera ändringen och verifiera signaturerna och programstarten.

## Tydligare tangenttext, 26 november 2026

Tangenternas text har 14 procent större grundstorlek och mindre utrymme mellan text och tangentens kant. Bokstäver och siffror använder fet stil. Den befintliga textanpassningen väljer en storlek som ryms när utrymmet är begränsat. Tangentbordets förhandsvisning, skrivbordet och inloggningsvyn använder samma regler.

Release installerades och startkontrollerades med den samordnade uppdateraren. Samtliga tolv programfiler har giltig signatur, antivirus och realtidsskydd var aktiva och SAC låg kvar i läge 0. Ord, sparade förslag och inloggningsinställningar bevarades. 116 riktade vyprov och hela sviten med 663 start-, dialog- och sessionskontroller passerade. Proven av inloggningens tangentbord kördes i ett testfönster. Se `obj\ReadableKeyLegendsChecks\installed-verification.json`.

## Mindre marginal under statusraden, 25 november 2026

I fönster utan titelrad ligger statusraden tre pixlar från nederkanten vid 100 procents skärmskalning. Nederkantens område för storleksändring är lika stort som tidigare. Mätningarna täckte båda menyplaceringarna, skrivbordets dockningslägen och den avskalade inloggningsvyn. Tangenterna behöll sina mått när titelraden doldes och när inloggningens DragKey-storlek växlades.

Release installerades med den samordnade uppdateraren. Skrivbordsappen startade, samtliga tolv programfiler hade giltig signatur, antivirus och realtidsskydd var aktiva och SAC låg kvar i läge 0. Ord, sparade förslag och inloggningsinställningar bevarades. 114 riktade vyprov, 661 start-, dialog- och sessionskontroller samt 11 samordningskontroller passerade. Se `obj\TitlelessStatusChecks\installed-verification.json`.

## Reservstart för skrivbordet, 25 november 2026

Release installerades med den samordnade uppdateraren. Skrivbordsappen startade och den signerade startkomponenten kör en bevakare för den. Samtliga tolv programfiler hade giltig signatur. Antivirus och realtidsskydd var aktiva och SAC förblev 0. Ord, sparade förslag och den skyddade inloggningsprofilen bevarades. Skrivbordsappen sparade om sin vanliga inställningsfil vid start.

Alla nio riktade reservstartsprov och båda simulerade låsproven passerade. Hela sviten med 649 kontroller passerade vid omkörning efter en avvikelse i ett befintligt skrivfokusprov. Verifieringen finns i `obj\DesktopCrashRecoveryChecks\installed-verification.json`. Se [STARTUP.md](STARTUP.md) för beteende och provens omfattning.

## Prestandauppdatering, 25 november 2026

AutoClick hoppar över externa målfrågor på samma pekarposition efter ett avslutat vänsterklick med upprepning av. Tangenttryck i ett bekräftat vanligt skrivfält skickas medan fältet kontrolleras i bakgrunden. Ordförslagen använder endast resultat för samma fält. Första kontrollen i okända fält och kontrollen före Enter använder den befintliga vägen. Tangentbordets gemensamma kod återanvänder textmått för samma text, typsnitt, storlek, språk, läsriktning och skärmskalning.

Release installerades med `Update-SoftKey.ps1` i huvudinstallationen och den aktiva separata inloggningskopian. Samtliga tolv programfiler har giltig signatur. Windows godkände programfilerna och skrivbordsappen startade med antivirus och realtidsskydd aktiva samt SAC i befintligt läge 0. Personliga ord och sparade förslag hade samma hashvärden. Vid start skrevs tre fönstermått i inställningarna om. En separat jämförelse med samma tidigare mått gav identisk fönsterstorlek och identiska textstorlekar i båda versionerna.

640 start-, dialog- och sessionskontroller samt 215 tillförlitlighetskontroller passerade vid omkörning. Alla 11 samordningskontroller passerade. Den första körningen av respektive fönstersvit hade en avvikelse vid återställning av ram eller fokus. Prov med HeadMouse på Windows riktiga inloggningsskärm återstår. Se `obj\PerformanceOptimizationChecks\installed-verification.json` och `obj\UIAccessTest\paired-update-result.json`.

## Avskalad inloggningsvy och gemensam uppdatering, 17 november 2026

Installerat i huvudappen och den aktiva separata inloggningsinstallationen: inloggning/låsskärm har ingen titelrad eller meny, inklusive den tidigare Mus-menyn. Den befintliga enknappsversionen av AutoClick är kvar. En diskret flyttyta fungerar med vanlig musdragning och HeadMouse/viloklick, via den befintliga begränsade flyttmotorn. Skrivbordets titelrad, menyer, tangentmotor och personliga ord är bevarade. Tangenter och grunddesign är fortsatt gemensamma rotfiler; ingen ny separat kopia av tangentbordets layout har skapats.

För framtida gemensamma designändringar används `Update-SoftKey.ps1`. Det bygger och signerar genom de befintliga installationsrutinerna och uppdaterar även den aktiva separata inloggningskopian med exakt samma signerade filer, utan ombyggnad eller omsignering av den kopian. Avstängd inloggning lämnas av. Återställning av den tidigare separata inloggningsdelen provas vid fel. Uppdateringskommandot väntar på administratörsprocessen med `WaitForExit()`, så att den återöppnade appen kan fortsätta köras när kommandot avslutas.

216 Startup-/sessions-/SAS-kontroller, 211 tillförlitlighetskontroller, 11 samordningskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. Den verkliga gemensamma installationen genomfördes. Samtliga tolv EXE/DLL är signerade, hashverifierade och accepterade av Windows kodintegritet; inloggningsfilerna är identiska mellan mapparna. Fem readonly-kontroller av SAS-förutsättningar och Startup-verifieringen passerade. Widgetsbrokern är Running. Personliga ord/inställningar och skyddade inloggningsinställningar är bytevis bevarade. Antivirus och realtidsskydd är aktiva och SAC förblev 0.

Den första verkliga uppdateringen slutförde installationen men dess yttre väntan följde även den öppnade GUI-processen. Endast den färdiga väntande uppdateringsprocessen avslutades; SoftKey lämnades igång. Väntan är korrigerad i koden. Ett senare anrop verifierade spärren mot uppdatering medan SoftKey är öppet och ändrade inga filer. `paired-update-result.json` beskriver senaste försöket och kan därför visa detta förväntade stopp. Den slutliga kontrollen av den faktiskt installerade versionen är `obj\UIAccessTest\signin-shell-validation.json`.

Användaren bekräftade efter uppmaningen att prova den nya flyttytan och inloggningen: ”Det funkade utmärkt!” den 17 november. Den avskalade vyn är godkänd i det rapporterade användarprovet. Ingen autentisering eller verklig Ctrl+Alt+Delete har automatiserats. Ändringar, berörda filer, gemensam kod, uppdateringsväg och återställningsbegränsningar finns i [SIGNIN-DESIGN.md](SIGNIN-DESIGN.md).

## Alla DragKey-knappar utom AutoClick spärras vid paus, 17 november 2026

Installerat efter användarens HeadMouse-prov av dölj/visa: när AutoClick är av är högerklick, dubbelklick, drag, tangentbordsikonen och avdockningsknappen inaktiverade. De använder samma grå pausutseende, även om tangentbordet är dolt och dess ikon tidigare var markerad. Endast AutoClick kan användas för att återuppta funktionen; därefter aktiveras tillgängliga knappar igen. Viloklick som redan hade valt ett annat mål avvisas också under pausen. Att öppna SoftKey på nytt kan fortfarande återvisa ett dolt fönster.

200 Startup-/sessions-/SAS-kontroller och 211 tillförlitlighetskontroller passerade. Paus och återupptagning provas både fristående och dockat på samtliga sidor, med synligt och dolt tangentbord. Den grå panelen är renderad och granskad. Endast skrivbordsappen uppdaterades; EXE/DLL är signerade och Windows kodintegritet samt programstart är verifierade med antivirus/realtidsskydd aktiva och SAC kvar i läge 0. Se `obj\UIAccessTest\dragkey-pause-validation.json`.

## Dölj och visa tangentbordet från DragKey, 17 november 2026

Installerat i skrivbordsappen: en tangentbordsikon i DragKey döljer SoftKeys fönster med `Hide()` utan att minimera eller avsluta appen. Ikonen markeras när tangentbordet är dolt och visar det igen vid nästa klick eller viloklick. En dockad DragKey blir tillfälligt fristående; vid återvisning återställs samma dockning och tangentbordsram. Det dolda läget är tillfälligt och sparas inte mellan programstarter. Öppna dialoger spärrar döljningen. Inloggningsdelens tidigare enknappslayout är bevarad. Den efterföljande pausfixen ovan kräver att AutoClick är på för att använda tangentbordsikonen.

176 Startup-/sessions-/SAS-kontroller och 211 tillförlitlighetskontroller passerade. Proven täcker samtliga dockningssidor, återvisning från minimering, bevarat skrivfokus och att låsning/upplåsning respekterar användarens dolda tangentbord. Utseendet har renderats och granskats. Huvudappens EXE/DLL är signerade, hashverifierade och accepterade av Windows kodintegritet; start verifierades med antivirus/realtidsskydd aktiva och SAC kvar i läge 0. Övriga installerade komponenter, ord och skyddade inloggningsinställningar är oförändrade. Appen sparade om sin storlek/position enligt den utökade panelens minimimått. Verifiering: `obj\UIAccessTest\keyboard-visibility-validation.json`. HeadMouse-provet av den nya knappen återstår.

## Ren statusrad och oberoende start vid Start → Lås, 17 november 2026

Installerat: aktiv-tangentindikatorn är borttagen ur statusraden i alla tre tangentbordsvyer. Modifierarnas funktion och markering på själva tangenterna är bevarade. En lyckad Ctrl+Alt+Delete-begäran skriver inte längre tangentnamnen i statusraden; felmeddelanden finns kvar. Metadata i de installerade DLL-filerna bekräftar att de gamla statuskontrollerna inte finns kvar.

Under slutgenomgången rapporterade användaren att SoftKey saknades på widgetsytan vid **Start → Lås**, men startade när widgetsytan lämnades. En egen användaruppgift för widgetsdelens bakgrundsprocess har installerats och startats. Den kör `SoftKey.Startup.exe --login-broker` direkt på Default med obegränsad körtid, noll fördröjning och normala användarrättigheter. Den håller den befintliga låsskärmsdelen tillgänglig oberoende av automatisk skrivbordsstart. Uppgiften följer inloggningskryssrutan och har verifierad migrering, återställning vid fel och dubbelstartsspärr. Användaren bekräftade den 17 november att det nu fungerar perfekt; det rapporterade widgetsfelet är därmed bekräftat löst.

136 Startup-/sessions-/SAS-kontroller, 211 tillförlitlighetskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. Den riktiga uppgiften är Running med bestående Default-process. En ytterligare vanlig uppdatering verifierade att den egna processen kan pausas för filbyte och återstartas. Samtliga tolv installerade EXE/DLL har giltiga signaturer, matchande hashvärden och accepterad kodintegritet. Personliga ord/inställningar och skyddade inloggningsinställningar är bytevis bevarade. Antivirus och realtidsskydd är aktiva, SAC förblev 0.

Resultat: `obj\UIAccessTest\widgets-status-validation.json` och `status-indicator-metadata.json`, kompletterade av användarens efterföljande godkännande av Start → Lås. Omstart/uppvaknande och ett uttryckligt Ctrl → Alt → Delete-prov är separata driftprov. Se aktuell status i [LOGON.md](LOGON.md).

## Ctrl + Alt + Delete via de befintliga tangenterna, 17 november 2026

Installerat enligt användarens förtydligande: Ctrl, Alt och Delete väljs på det vanliga tangentbordet. När Delete väljs med Ctrl och Alt hållna begär SoftKey Windows skyddade kombination via SendSAS. Ingen extra knapp eller menypost visas. Vanligt Delete och Ctrl+Delete behåller sina vanliga funktioner. Ägda modifierare släpps före SAS-begäran, och ett nekat uppsläpp stoppar den utan att skicka vanligt Delete.

Funktionen gäller skrivbords-, inloggnings- och låsskärmsdelarna. Alla tre är nu signerade UIAccess-program; SYSTEM-delen använder AsUser=false och användardelarna true. Windows förutsättningar kontrolleras, och fel visas i statusraden. Readonly-flaggorna `--verify-secure-attention` kontrollerar faktiskt UIAccess, UAC/policy och SAS-exporten utan att skicka kombinationen.

124 Startup-/sessions-/SAS-kontroller, 211 tillförlitlighetskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. De tre vyerna bygger utan fel/varningar och uppdateringen är signerad och installerad i huvudmappen samt den aktiva separata inloggningsmappen. Samtliga tolv EXE/DLL har giltiga signaturer, matchande hashvärden och accepterad kodintegritet. Fem kontroller av installerade komponenters Windows-förutsättningar passerade. SoftwareSASGeneration=3, EnableLUA=1 och skyddade inloggningsinställningar är bevarade. Ord är oförändrade; appen sparade om personliga settings.json under startprovet. Antivirus och realtidsskydd är aktiva och SAC förblev 0.

Ingen verklig Ctrl+Alt+Delete-växling eller autentisering automatiserades. Användaren behöver prova kombinationen på skrivbordet och vid inloggning. Resultat: `obj\UIAccessTest\sas-validation.json`. Se [SECURE-ATTENTION.md](SECURE-ATTENTION.md).

## DragKey hålls inom skärmens kanter, 16 november 2026

Installerat efter användarens förtydligande att gränsen ska vara skärmen, även när DragKey är fristående. Hela panelen begränsas till den valda bildskärmens användbara yta, så att den inte kan försvinna bakom skärmkanten eller aktivitetsfältet. Vanlig musdragning, native-flytt, viloklicksflytt och återställning av gamla placeringar använder samma gränser. Dockningsförhandsvisningen följer den begränsade panelpositionen. Negativa bildskärmskoordinater och faktiska native-dimensioner stöds utan magnetisk snäppning av DragKey.

211 tillförlitlighetskontroller och 89 Startup-/sessionskontroller passerade. De nya proven omfattar alla fyra kanter, hörn, liten inloggningspanel, negativt placerad bildskärm, extrema koordinater, WM_MOVING, riktiga native-flyttar och viloklick utan verklig musinmatning. Ett sparat delvis osynligt läge som den tidigare allmänna återställningsregeln accepterade korrigeras nu helt innanför skärmen. Den senaste återgångsfunktionen vid avdockning passerade också sina befintliga prov.

Endast huvudappens EXE/DLL uppdaterades med `Sign-SoftKey.ps1 -IncludeStartup:$false`. Båda är signerade, installerade, hashverifierade och accepterade av Windows kodintegritet; appstart är verifierad med antivirus och realtidsskydd aktiva samt SAC kvar i läge 0. De sex övriga programfilerna i huvudinstallationen är bytevis oförändrade. Ord och skyddade inloggningsinställningar är oförändrade; den startade appen sparade om personliga settings.json under startprovet. Resultat: `obj\UIAccessTest\dragkey-screen-validation.json`. Praktisk kantdragning med HeadMouse återstår att prova av användaren.

## Återgå till platsen före dockningsdragningen, 16 november 2026

DragKey minns nu den fristående positionen vid början av en dockningsdragning. Dockningssläppet skriver inte över denna återgångsplats. Avdockningsikonen och dubbelklick på dockad bakgrund återställer platsen, även efter omstart. Pågående bakgrundsdragning behåller sitt befintliga grepp under pekaren. En flytt som avslutas utan dockning sparas fortsatt som en vanlig ny fristående placering.

Appändringen är begränsad till `KeyboardWindow.Docking.cs` och `KeyboardWindow.DragKey.cs`; befintliga `DragKeyPosition` används utan nytt inställningsformat. 181 tillförlitlighetskontroller och 89 Startup-/sessionskontroller passerade. De nya proven omfattar dockning på alla tre sidor, faktisk flytt före släpp, återgång med avdockningsknappen, omstart, dubbelklicksvägen, avbruten flytt och bevarat skrivfokus, med isolerade inställningar och utan verklig musinmatning. Endast skrivbordsappen ska uppdateras för denna ändring: `& C:\sofType\Sign-SoftKey.ps1 -IncludeStartup:$false`.

Denna återgångsfunktion är nu installerad tillsammans med skärmgränsfixen ovan. Ett tidigare uppdateringsförsök hade stoppats före signering och filbyte med `ElevationCancelled / Access is denied`; det senare avgränsade installationsförsöket lyckades och uppdaterade endast huvudappens EXE/DLL.

## Skrivbordsappen göms när Windows låses, 16 november 2026

Användaren rapporterade att vanliga SoftKey låg kvar över låsskärmen samtidigt som det begränsade inloggningstangentbordet öppnades efter Start → Lås. Skrivbordsappen saknade motsvarande låsövervakning. En signerad fix är installerad: den lyssnar på den egna sessionens Windows-låsmeddelanden och kontrollerar även låsstatus och aktivt skrivbord var 200 ms.

Vid låsning göms appens egna fönster, fristående DragKey och ägda dialoger. Dwell och topmost-återställning pausas. Tangenter, dwell och native-inmatning spärras för den personliga skrivbordsappen; det begränsade inloggningstangentbordet fortsätter fungera separat. När sessionen säkert är upplåst och användarskrivbordet aktivt återkommer tidigare synliga fönster utan fokusövertagande. Positioner, minimerat läge, dialogers modalitet och användarens autoklickval bevaras. En begäran att återvisa en befintlig appinstans ignoreras under låsningen.

89 Startup-/sessionskontroller och 159 tillförlitlighetskontroller passerade. De nya proven simulerar tidigt låsmeddelande, fördröjd låsflagga, missat upplåsningsmeddelande, okänd/inaktiv session, blockerad inmatning, fristående DragKey, öppna/stängda dialoger, modala bekräftelser och minimering. Fönsterproven använder riktiga WPF-fönster och native synlighet men låser inte Windows och skickar inte verklig inmatning.

Alla åtta uppdaterade EXE/DLL i huvudinstallationen har giltiga signaturer, matchande hashvärden och accepterad kodintegritet; appstart med aktiva skydd är verifierad. De fyra befintliga filerna i den separata fungerande inloggningsinstallationen är också verifierade och har inte ersatts för denna skrivbordsfix. Personliga ord/inställningar och skyddade inloggningsinställningar är bytevis oförändrade. Antivirus och realtidsskydd är aktiva och SAC förblev 0. Resultat: `obj\UIAccessTest\desktop-lock-validation.json`. Användarens verkliga prov av Start → Lås återstår.

## Autoklick på och endast en DragKey-knapp vid inloggning, 16 november 2026

Installerat enligt användarens begäran: inloggnings- och låsskärmsdelarna startar med autoklick på. Det gamla pausade pilotläget ignoreras vid inläsning och har migrerats i den aktiva installationen. DragKey visar enbart autoklickknappen i ett kompakt 80 × 38-format; knappen kan pausa och återuppta viloklick. Övriga muslägen kan inte väljas i den begränsade inloggningsdelen. Vanliga skrivbordsappen använder fortsatt sitt fullständiga verktygsfält.

159 tillförlitlighetskontroller, 65 Startup-/dialogkontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade. Enknappslayouten har renderats och granskats. Appen och den aktiva separata inloggningsdelen är signerade och uppdaterade; samtliga tolv installerade EXE/DLL har giltiga signaturer, matchande hashvärden och accepterad kodintegritet. Personliga ord och inställningar samt inloggningens tids-, jitter- och storleksinställningar är bevarade. SofTypes secure-desktop-accommodation pekar nu på SoftKey, med tidigare värde sparat för återställning. Antivirus och realtidsskydd är aktiva och SAC förblev 0. Verifiering: `obj\UIAccessTest\login-autoclick-validation.json`. Praktisk inloggning med HeadMouse återstår att prova av användaren.

Sökvägarna i produktionsskripten är genomgångna och det missvisande utvecklingskommandot i den framtida installationsbeskrivningen är korrigerat. Det portabla slutpaketet är ännu inte byggt; se [INSTALLATION-PATHS.md](INSTALLATION-PATHS.md).

## Snabbare autostart och hög prioritet, 16 november 2026

Installerat: SoftKey begär prioritet High och automatisk start använder Windows LogonTrigger utan inställd fördröjning. Den befintliga användarbrokern för inloggning kan också begära skrivbordsstart vid första upplåsta aktiva skrivbordet. SYSTEM-delen startar inte den personliga appen. Startvägarna samordnas så att en enda appinstans visas. Realtidsprioritet används inte eftersom den kan störa Windows mus- och tangentbordsinmatning.

59 Startup-/dialogkontroller och tre kontroller mot den riktiga installationen passerade, liksom 153 tillförlitlighetskontroller, 19 inloggningsinstallationskontroller och 16 privata installationskontroller. Den riktiga Windows-uppgiften har provstartats: synligt tangentbord, High-prioritet och en enda instans är bekräftade. Både appen och den aktiva separata inloggningsdelen är uppdaterade; samtliga tolv installerade EXE/DLL har giltiga signaturer, matchande hashvärden och godkänd kodintegritet. Antivirus och realtidsskydd är aktiva och SAC förblev 0. Ord och skyddade inloggningsinställningar är bytevis bevarade; den körande appen sparade om personliga settings.json under startproven. Verklig starttid vid nästa Windows-inloggning återstår att prova.

Vanliga uppdateringar migrerar ett befintligt eget Run-kommando till den nya uppgiften utan att aktivera ett avstängt autostartval. Den uttryckliga flaggan `Sign-SoftKey.ps1 -EnableAutomaticStartup` användes för användarens begärda aktivering. Se [STARTUP.md](STARTUP.md) och `obj\UIAccessTest\fast-start-validation.json`.

## Samma design i alla dialogrutor, 16 november 2026

Autoklick, Ordlista, Ta bort ord, Stäng SoftKey och informationsrutan vid startfel följer nu den godkända Startup-designen: vit innehållsyta, diskreta avdelare och en gemensam nedre knapprad. Layouten är samlad i SoftKeyPopupDesign. Funktionerna för inmatning, ord och bekräftelser behålls.

44 dialog-/Startup-kontroller och 153 tillförlitlighetskontroller passerade. Samtliga rutor renderades och granskades, inklusive arbete, fel, sökning/redigering och långa ord. Uppdateringen är signerad och installerad både i vanliga appen och i den separata inloggningsdelen. Båda paketens EXE/DLL har giltiga signaturer, matchande hashvärden och godkänd kodintegritet. Antivirus och realtidsskydd är aktiva, SAC förblev 0 och personliga ord, inställningar och inloggningsinställningar är bevarade. Se [DIALOG-DESIGN.md](DIALOG-DESIGN.md).

## Renare Startup-dialog, 16 november 2026

Startup-dialogen har fått en kompaktare layout med vita inställningsrader, diskret avdelare och samlad status-/knapprad. Inga startfunktioner ändrades. Laddningsindikatorn har reserverat utrymme och spärrade knappar tonas ned. Alla 39 Startup-kontroller och 153 tillförlitlighetskontroller passerade; normalläge, förlopp och fel renderades och granskades.

Uppdateringen är signerad, installerad och startkontrollerad. Alla åtta EXE/DLL fick giltig signatur och accepterad kodintegritet. Antivirus och realtidsskydd är aktiva, SAC förblev 0 och personliga ord och inställningar är bytevis oförändrade. Resultat: `obj\UIAccessTest\startup-design-validation.json`.

## Arkiv → Startup, 16 november 2026

Startup-dialogen är implementerad och installerad med kryssrutor för start vid Windows-inloggning respektive automatisk start på skrivbordet. OK och Verkställ genomför verkliga systemändringar via en signerad installerad komponent, visar förloppet, verifierar resultatet och återställer tidigare inställningar vid fel. OK stänger efter lyckad ändring; Verkställ lämnar rutan öppen. Under arbetet kan dialogen inte stängas.

39 nya Startup-kontroller, 153 befintliga tillförlitlighetskontroller och 16 privata installationskontroller passerade. Layouten granskades i normalläge, under arbete och vid fel. Alla åtta installerade EXE/DLL har giltig signatur och godkänd Windows-kodintegritet. Programstart samt den installerade Startup-komponentens signatur- och läskontroll är verifierade. Antivirus och realtidsskydd är aktiva och SAC bevarades som 0. Den fungerande separata inloggningsinstallationen har inte bytts ut.

`Sign-SoftKey.ps1` inkluderar Startup som standard så att vanliga uppdateringar behåller komponenterna. Se [STARTUP.md](STARTUP.md) för funktion, verifiering och aktuella begränsningar.

## Låsskärmens widgetar och övergång till PIN-rutan, 16 november 2026

Användaren rapporterade att inloggningstangentbordet först visas men försvinner när väder/nyheter visas före PIN-inmatningen. En uppdaterad provversion är installerad: SYSTEM-delen behålls över skrivbordsbyten, och en separat signerad UIAccess-del visar det begränsade tangentbordet på användarens låsta skrivbord. Den nya delen tillåter ingen inmatning efter upplåsning eller när dess skrivbord är inaktivt. Ordförslag, inlärning och personlig lagring förblir avstängda i båda delarna.

153 tillförlitlighetskontroller, 19 installationskontroller och 16 privata installationskontroller passerade. Fyra installerade EXE/DLL har giltiga signaturer och godkänd kodintegritet. Faktisk UIAccess-behörighet och avvisad start på olåst session verifierades. Antivirus och realtidsskydd är aktiva och SAC förblev 0. Vanliga SoftKeys installerade programfiler har inte ersatts. Användarens prov av den rapporterade övergången återstår; någon lyckad inloggning med den nya versionen är ännu inte bekräftad. Se [LOGON.md](LOGON.md).

## Inloggningsprov 16 november 2026

En separat signerad inloggningsdel är installerad i `C:\Program Files\SoftKey-Logon-Test` och registrerad som Windows-hjälpmedel. Både EXE och DLL har giltiga signaturer och godkänd kodintegritet. Antivirus och realtidsskydd är aktiva, SAC förblev 0 och personliga ord/inställningar är oförändrade. Vanliga SoftKeys installerade programfiler har inte ersatts.

141 tillförlitlighetskontroller, 19 nya installationskontroller och 16 befintliga privata installationskontroller passerade. Det installerade förhandsvisningsfönstret har granskats. Verklig PIN-/lösenordsinmatning med HeadMouse på den skyddade skärmen återstår att prova av användaren, därefter uppvaknande och kallstart. Automatisk återstart efter krasch på samma skyddade skärm är ännu inte implementerad.

SofType är redan registrerat för inloggning och skrivbord. Det aktuella första provläget är därför `SofTypeDwell`: SoftKey visar sitt tangentbord med eget viloklick pausat medan befintliga SofType fortsätter sköta viloklick, för att undvika dubbel inmatning. Valet att låta SoftKey ta över på den skyddade skärmen är förberett med återställning men ännu inte valt av användaren.

Se [LOGON.md](LOGON.md) för uppbyggnad, begränsningar och provsteg utan terminalkommandon. `Install-SoftKeyLogonTest.ps1` använder det vanliga signeringsskriptet med den nya valfria flaggan `-IncludeLogon`. Befintliga uppdateringar utan flaggan fungerar som tidigare. Genvägen `Stäng av inloggningstest.lnk` avregistrerar enbart denna provfunktion.

Tangenternas tecken i inloggningsdelen följer det aktiva inmatningsfönstrets tangentbordsspråk. Endast en starttid skrivs i vår egen hjälpmedelsregistrering när ett riktigt skyddat fönster har laddats; inga tangenttryck eller inloggningsuppgifter loggas.

## Snäppning och skärmgränser 15 november 2026

SoftKey snäpper automatiskt till arbetsytans överkant eller underkant när motsvarande fönsterkant är högst 1 procent av arbetsytans höjd från kanten. Den tidigare 3-procentszonen har ersatts. När båda kanterna ligger inom zonen väljs den närmaste. Hela fönstrets native-rektangel används, inte pekarens position. Arbetsytan lämnar aktivitetsfältet åtkomligt och hämtas från den bildskärm som fönstrets föreslagna rektangel tillhör. Negativa bildskärmskoordinater stöds.

Både vanlig Windows-dragning och flytt med DragKey använder samma beräkning. Utanför den vertikala snäppzonen kan fönstret dras loss igen. Förflyttningen stoppas vid alla fyra kanter, inklusive vänster och höger sida av den användbara skärmbredden. Det finns ingen magnetisk zon vid vänster/höger kant; inom skärmbredden förblir sidledsflytten fri. Vid storleksändring stoppas den valda kanten vid gränsen och den motsatta kanten behålls. Fristående DragKey omfattas inte av SoftKeys snäppning. Sparad SoftKey-position återställs med samma skärmgränser, och det begränsade läget sparas som tidigare utan filskrivningar under varje musrörelse.

UIAccess Debug bygger utan varningar eller fel. 115 tillförlitlighetskontroller och samtliga 498 befintliga funktionstester passerade. Kontrollerna omfattar precis innanför/på/utanför 1-procentsgränsen, att den gamla 3-procentszonen inte längre snäpper, olika arbetsytor, negativa koordinater, stabil upprepad snäppning, verklig native-fönsterflytt, WM_MOVING-rektangeln, att dra loss med dwell, vänster/höger gränser och resize-ankring, hörn, fokus och lagring av det begränsade läget. Inmatning simulerades utan att trycka verkliga musknappar. Provning med användarens fysiska pekdon återstår efter installation.

Regressionstestets första körning fick ett avvikande dwell-mål för AutoClick direkt efter omdockning till höger. Två efterföljande fulla körningar passerade utan ändring av appkoden; extra diagnostik har lagts till i testet för att kunna undersöka ett eventuellt återfall.

**Denna version är ännu inte installerad.** Det normala signerings-/installationsförsöket stoppades före signering och filbyte med `ElevationCancelled / Access is denied`. Installerad EXE och DLL har oförändrade SHA256-hashar och giltiga signaturer. Stäng SoftKey och kör `C:\sofType\Sign-SoftKey.ps1` i PowerShell som administratör för att installera och startkontrollera det nya bygget. Inga skyddsinställningar ändrades.

## Sparade fönsterpositioner 15 november 2026

Privat skrivläge har tagits bort helt ur gränssnittet, inställningarna och ordhanteringen enligt användarens önskemål. En gammal sparad inställning för läget ignoreras och tas inte med när nya inställningar sparas. Ordinlärning och ordförslag fungerar som tidigare.

SoftKeys skärmposition och den fristående DragKeys skärmposition sparas nu separat som `KeyboardPosition` och `DragKeyPosition` i settings.json. Native-fönsterkoordinater används även på bildskärmar med negativa koordinater. Dockningssida och position längs kanten fortsätter sparas separat. Både vanlig fönsterflytt, flytt med DragKey och flytt som Windows gör uppdaterar positionerna. Positionerna hålls aktuella i minnet, skrivs efter avslutad flytt eller 400 ms utan ytterligare förflyttning och fångas även vid stängning. Pågående dragning skriver inte för varje musrörelse.

Vid nästa start används respektive sparad position. Om bildskärmar har kopplats bort och fönstrets överkant inte längre är åtkomlig placeras det inom närmaste bildskärms arbetsyta. Avsiktlig delvis överlappning vid en skärmkant bevaras när överkanten fortfarande går att nå. Saknas sparad position används den tidigare standardplaceringen. Minimering skriver inte över den normala SoftKey-positionen med Windows minimerade koordinater. Tillfällig fristående DragKey under minimering skriver inte heller över den tidigare riktiga fristående positionen.

UIAccess Debug bygger utan varningar eller fel. 69 tillförlitlighetskontroller och de 498 befintliga funktionstesterna passerade. De nya kontrollerna använder isolerad lagring och riktiga WPF-fönster som stängs och skapas på nytt: båda fönstrens återställning, dwell-flytt, native flyttloop, uppskjuten lagring, dockning tillsammans med fristående position, minimering och skärmkanter är kontrollerade. Användarens filer används inte för de nya omstartstesterna. Bygget måste installeras med signeringsskriptet nedan innan den installerade appen får ändringarna.

Dragningens återspelning av 160 mushändelser passerade även efter positionslagringen: median 0,47–0,64 ms, p95 högst 1,12 ms, ingen ändrad fönsterordning eller skrivfokus. Uppdateringsförsöket den 15 november kl. 18:59 stoppades före signering och filbyte med `ElevationCancelled / Access is denied`. Installerad EXE och DLL är bytevis oförändrade och har fortfarande giltiga signaturer. Denna version är alltså ännu inte installerad; kör `C:\sofType\Sign-SoftKey.ps1` i PowerShell som administratör med SoftKey stängt. Inga skyddsinställningar ändrades.

## Tillförlitlighetsgenomgång 15 november 2026

Den aktuella genomgången omfattade inmatning, native-callbacks, timers och dragning, ordhantering, lagring, programstart och signerings-/installationsflödet. Inget nätverksanrop eller extern paketreferens hittades i appkoden. Ordlistan är liten; ingen ombyggnad av sökalgoritmen eller ändring av dwell-timerns intervall motiverades av kontrollerna. Dragningens befintliga sammanslagning av mushändelser behålls.

Följande ändringar är byggda:

- En namngiven mutex tillåter en inmatningsmotor per Windows-användare och interaktiv session. En andra start skickar en begäran att återvisa befintligt fönster utan att ta skrivfokus. Låset kan återtas efter en krasch.
- JSON läses från ström. Vid sparning behålls föregående läsbara generation som `.bak`. Saknad eller ogiltig huvudfil kan läsas från kopian. En skadad huvudfil bevaras som `.corrupt-<id>` innan den ersätts, utan att skriva över en läsbar backup med den skadade filen. En oläsbar/låst huvudfil blockerar överskrivning. Detta gäller både inställningar och ordlista.
- Ett nekat modifieraruppsläpp behåller tangentens ägarskap och låsmarkering för nytt försök. Vanlig stängning avbryts om kvarhållna modifierare inte kan släppas. Windows avstängning blockeras inte.
- Hanterade undantag fångas vid mus- och fönsterhändelsernas native-callbacks. Mushändelser skickas vidare; den befintliga reservtimern finns kvar.

UIAccess Debug bygger utan varningar eller fel. 44 nya tillförlitlighetskontroller och samtliga 498 befintliga funktionstester passerade. Lagringsfelen injicerades i en isolerad temporär mapp och inmatningsfel simulerades utan att trycka verkliga tangenter. Återspelning av 160 mushändelser med riktiga fönster passerade: median 0,53–0,63 ms och p95 högst 1,36 ms från återspelad händelse till native-flytt; inget fokusbyte eller ändrad fönsterordning. Detta är inte en mätning av användarens fysiska pekdon, en fullständig CPU-profilering eller en garanti mot alla fel.

**Denna version är ännu inte installerad.** `Sign-SoftKey.ps1` stoppades den 15 november vid administratörsstarten med `ElevationCancelled / Access is denied`, före signering och filbyte. Både installerad EXE och DLL har oförändrade SHA256-hashar och giltiga signaturer. Inga skyddsinställningar ändrades. Stäng SoftKey och kör `C:\sofType\Sign-SoftKey.ps1` i PowerShell som administratör för att installera och verifiera det nya bygget med det befintliga certifikatet.

Antivirusets aktuella status kunde inte läsas från denna begränsade session eftersom Defender/CIM nekade åtkomst. Den normala administratörskörningen av signeringsskriptet måste slutföra skydds- och startkontrollerna efter installationen. SAC avlästes som 0 och ändrades inte.

De nya kontrollerna körs från projektroten med:

```powershell
dotnet run --project .\tests\ReliabilityChecks\ReliabilityChecks.csproj -c Release -- C:\sofType\obj\UIAccessTest\bin\SoftKey\debug\SoftKey.dll
```

Tidigare funktionsändringar och installationsanvisningar följer nedan.

Drag-knappen kan nu även storleksändra hela SoftKey. Välj Drag, vila på en av Windows fyra fönsterkanter eller fyra hörn, flytta pekaren och vila igen för att avsluta. Windows WM_NCHITTEST skiljer storlekskanter från namnlist och systemknappar. Varje kant/hörn har ett eget dwell-mål. Under storleksändringen följs pekaren genom den befintliga snabba fönsterdragningen; valda kanter flyttas med SetWindowPos utan fokusbyte eller syntetiskt nedhållen musknapp. Motsatta kanter förankras och minimimåtten räknas i rätt skärmkoordinater. Paus, stängning och andra avbrott avslutar storleksändringen.

Dockad DragKey har nu en mild och begränsad skalning. Referensen är fönstrets startstorlek för den aktuella körningen. Panelens skala följer 20 procent av ändringen i fönstrets geometriska medelstorlek och begränsas till 90–110 procent av panelens ursprungliga mått. Tangentbordet fortsätter använda sin vanliga starkare skalning. Förhandsvisning, dockningsavkänning och position längs kanten använder samma panelmått; fristående DragKey behåller sin vanliga storlek. Sparade fönstermått räknas fortsatt med dockningens grundmått, så att den totala fönsterstorleken inte driver mellan omstarter. Minimistorleken rymmer även den största tillåtna dockade panelen.

DragKey kan nu placeras fritt längs hela den tillåtna dockningskanten. Förhandsvisning och slutlig panel följer verktygsfältets läge i sidled på toppen respektive i höjdled på sidorna, begränsat så att hela panelen får plats. Varje sida har en separat sparad position mellan 0 och 1, som anpassas till tillgängligt utrymme när fönstret ändrar storlek. Äldre inställningar börjar vid kantens början. Avkänningen använder den flyttade panelens hela skärmrektangel och överlappande yta mot de tre dockningsremsorna, inte muspekarens punkt. Dwell-drag använder den beräknade fönsterrektangeln för nästa flytt och vanlig dragning använder den föreslagna RECT som Windows skickar i WM_MOVING. Den sida som har störst överlappning väljs, med stabilisering vid nästan lika överlappning i hörn. Ingen kandidat behålls när överlappningen har upphört. Toppläget är fortsatt under menyn, vänsterläget efter ordförslagen och inget bottenläge finns.

Dockningsplaceringarna är korrigerade: Mus-menyn har inte längre några val för dockningssida eller fristående läge. Dessa val ska senare få plats i DragKey-inställningarna; ingen sådan inställningsruta har skapats nu. Menyraden och ordförslagen ligger utanför dockningsytan. Toppläget hamnar därför under menyraden och vänsterläget mellan ordförslagen och tangenterna. Den dockade panelens namnlist är dold och ersatt med en femte ikon för Avdocka DragKey. Ikonen fungerar både med vanlig knappaktivering och viloklick, även med AutoClick pausat. Fristående läge har kvar namnlisten och sina fyra musknappar. Förhandsvisningen följer samma femikonslayout utan namnlist.

Dockningen är finjusterad efter SofTypes dokumenterade bakgrundsdragning och dubbelklicksurkoppling. Namnlisten och övrig tom bakgrund kan greppas; ett enkelklick flyttar ingenting och en fysisk dragning börjar först när Windows rörelsetröskel passerats. Både källkontroll och knappgränser kontrolleras så att knapparnas klick inte förvandlas till fönsterdragning. Dubbelklick på dockad bakgrund lossar panelen. Dwell-användare får samma urdockning genom att välja Dubbelklick och sedan vila på bakgrunden. Ett grepp långt ned på en lodrät panel hålls under pekaren när panelen blir kortare och fristående. Förhandsvisningen återanvänder våra symboldefinitioner, färger och mått utan extra interaktiva knappar. Inga hjälpbubblor eller nya klickfunktioner har lagts till.

DragKey dockas som en del av SoftKeys faktiska WPF-layout på vänster sida, höger sida eller överst. Överst är standard när ingen placering tidigare har sparats. Dockningsvalen är borttagna ur Mus-menyn; inget bottenläge finns. Samma knappar flyttas mellan den fristående fönsterramen och tangentbordets dockningsyta. På sidorna ordnas knapparna lodrätt. Vid minimering visas panelen tillfälligt fristående så att AutoClick kan återupptas; återställning återför den till vald sida.

Layouten lämnar nu plats redan under dragningen. En diskret dockningskontur med våra befintliga symboler reserverar den kommande panelens utrymme och tangentytan anpassas direkt, inom samma fönsterram. Släpp behåller exakt den förhandsvisade layouten; rörelse bort från kanten eller avbruten dragning tar bort förhandsvisningen. Förhandsvisningen sparar ingen placering och flyttar inte de riktiga knapparna. Kantavkänningen utgår från tangentytans fasta värdyta, efter ordförslagen och under menyn, som inte flyttas när tangenterna lämnar plats. En liten tolerans vid kanten motverkar att förhandsvisningen växlar fram och tillbaka. Både dwell-drag och Windows WM_MOVING vid vanlig namnlistedragning uppdaterar förhandsvisningen. Layoutarbete görs när dockningskandidaten, positionen längs kanten eller panelens milda skala ändras. Fönstrets gemensamma minimimått rymmer alla tre placeringarna. Sparade mått är fortsatt exklusive den valda dockningsytan så att befintliga inställningar kan läsas utan byte av lagringsformat. Inspiration: SofTypes manual, avsnittet "Placement & Orientation", https://www.orin.com/access/docs/st45_manual2.pdf.

UIAccess-bygget för Debug passerade utan varningar eller fel och 498 kontroller passerade. De omfattar dockning och befintliga musfunktioner samt direkt uppmätning av tangentytan under förhandsvisning, stabilitet vid upprepade layoutberäkningar, kanttolerans, återställning, släpp utan ytterligare layoutförflyttning och namnlistedragningens callback. Layouten och förhandsvisningarna renderades och granskades. Kontrollerna använder återspelade positioner och ersätter inte provning med användarens fysiska pekdon. Personliga inställningar och ord är bytevis oförändrade. Denna version är ännu inte installerad: Sign-SoftKey.ps1 stoppades vid administratörssteget med ElevationCancelled / Access is denied före signering och filbyte. Slutför uppdateringen med Sign-SoftKey.ps1 från en PowerShell som administratör och prova sedan dockningen med användarens pekdon.

Den föregående ändringen gäller hackig flytt med Drag-knappen. Användaren bekräftade att den föregående renderingsstyrda lösningen fortfarande hackade efter installationen den 14 november kl. 20:20. Dess funktionstester fastställde korrekta positioner och fönsterordning, men verifierade inte en mjuk rörelse med användarens pekdon. Den tidigare mätningen hade medianintervall runt 31-36 ms och ett längsta intervall på cirka 84 ms mellan flyttar.

Flyttvägen använder nu WM_MOUSEMOVE från programmets befintliga Windows-muskrok. Händelsens skärmkoordinater skickas vidare genom en enda köad Dispatcher-operation med Send-prioritet, före WPF-ritningen. Nytillkomna rörelser ersätter den väntande positionen. Beroendet av CompositionTarget.Rendering och dess 120-gräns har tagits bort. Muskroken utför ingen fönsterflytt själv och skickar alltid vidare händelsen. En separat 16 ms-reservtimer tar över vid minst 64 ms utan mushändelser, exempelvis om kroken saknas. Autoklickets dwell-timer bestämmer fortsatt upptagning och släpp. Släpp stoppar reservtimern och avbryter väntande flyttarbete. Hjälptexterna i DragKey är fortsatt borttagna.

UIAccess-bygget gick igenom utan varningar eller fel. Körningskontrollen spelade upp 160 WM_MOUSEMOVE-paket genom den faktiska krokmetoden och flyttade riktiga fönster både över och utanför SoftKey. Samtliga positioner stämde, fönsterordning och skrivfokus bevarades, skurar av rörelser slogs ihop till den senaste positionen och inga flyttar skedde efter släpp. Medianfördröjningen från återspelad mushändelse till native-flytt var cirka 0,55 ms; över tangentbordet var p95 1,18 ms och max 2,95 ms. Detta är ett automatiskt återspelningstest, inte en mätning av användarens fysiska pekdon eller en garanti för upplevd mjukhet. Resultat: obj\UIAccessTest\dragkey-pointer-check.json.

Den senaste ändringen är ännu inte installerad. Sign-SoftKey.ps1 stoppades med ElevationCancelled / Access is denied före signering och filbyte när administratörsstarten nekades. De tidigare installerade programfilerna har giltiga signaturer och oförändrade hashvärden, personliga inställningar och ord är oförändrade och SAC är fortsatt 0. Resultat och återställningskopia: obj\UIAccessTest\dragkey-pointer-install-result.json. Slutför uppdateringen genom att köra Sign-SoftKey.ps1 från en PowerShell som administratör och prova sedan rörelsen i appen.
[Sign-SoftKey.ps1](Sign-SoftKey.ps1) är den rekommenderade startfilen för att bygga, signera och installera senaste versionen. Den använder `Debug` som standard, samma konfiguration som den senaste installationen. Den använder Microsofts SignTool, `Install-Local-UIAccess.ps1` och det befintliga lokala certifikatet, kontrollerar båda filernas signaturer och verifierar programstart med antivirus och realtidsskydd aktiva. Utan särskild SAC-flagga bevaras och kontrolleras det befintliga SAC-läget. Stäng SoftKey före körningen. `-Configuration Release` kan anges uttryckligen; ett giltigt signerat paket måste fortfarande godkännas av Windows kodintegritetskontroll innan det installeras.

**Aktuellt läge den 14 november:** på användarens uttryckliga begäran stängdes Smart App Control av även på testdatorn. Registerläget är `0` och både SAC:s verkställande policy och utvärderingspolicy visade `IsEnforced=false`. Antivirus och realtidsskydd är aktiva. Resultaten av avstängningen finns i `obj\UIAccessTest\approved-private-install-result.json`, `approved-sac-policy-inspection.json` och `private-install-security-result.json`.

Senaste appuppdateringen tar bort ordförslagens vita ruta, kant och skugga samt minskar de nästlade indragen. Felet där `mamma` klipptes återskapades genom mätning av den faktiska WPF-layouten vid fönsterstorleken 659×308. Textutrymmet ökade från 39 till 63, medan ordet behövde cirka 40,23. Efter ändringen fick alla provord plats i 48 mätningar över fyra fönsterstorlekar och 1, 3 respektive 8 förslag. Tomikon och tomma förslags inaktiva tillstånd är bevarade. Debug-paketet signerades och installerades; båda filerna fick `Valid` och `Accepted` och appstart verifierades. Inställningar och ord var oförändrade, SAC förblev `0`, antivirus och realtidsskydd var aktiva. Resultat: `obj\UIAccessTest\suggestion-clipping-before.json`, `suggestion-clipping-after.json` och `suggestion-clipping-install-result.json`. Installerad DLL: `157AA66555CEC3D6393656EDBEF687DDDAD4FE1F7881D47AE23E98C47C8005E6`.

För det slutliga privata installationspaketet har användaren den 14 november uttryckligen begärt att installationen självt ska stänga av Smart App Control. Stödet för `-DisableSmartAppControl` är förberett enligt [PRIVATE-INSTALLER.md](PRIVATE-INSTALLER.md). Ordinarie utvecklingskörningar ändrar inte skyddet. SofTypes provperiod löper ut **26 november 2026**.

Användaren vill fortsätta utveckla, bygga och signera lokalt. Vänta med nya inskickningar till Microsoft tills användaren anser den aktuella versionen färdig och uttryckligen godkänner inskickandet. Det förberedda uppföljningspaketet ska inte skickas nu. Detta är inte ett krav på att pausa kodarbetet. Använd samma kontroller vid varje uppdatering och behåll den fungerande installationen om Windows avvisar ett paket.

Historiken för blockeringen med aktiv SAC finns i [SIGNING-DIAGNOSIS.md](SIGNING-DIAGNOSIS.md). Det paketet är nu installerat efter användarens godkända avstängning av SAC. Microsofts tidigare ärende är `Completed` med bekräftad felblockering av minst en tidigare inskickad fil; ett separat granskningspaket för DLL-filen är förberett men inte inskickat.

Den nya ljusa tangentbordsdesignen installerades och startkontrollerades den 14 november 2026 som `Debug` med SignTool. Båda filerna fick `Valid` och `Accepted`, medan `Release`-paketets DLL avvisades i den körningen. Resultat och återställningskopia dokumenteras i `obj\UIAccessTest\keyboard-design-install-result.json`. För att bygga om samma konfiguration används `Sign-SoftKey.ps1 -Configuration Debug`. Detta ändrar inte kravet att kontrollera varje nytt paket.

Den efterföljande ändringen av ordförslagens design och avaktivering av tomma förslag installerades den 14 november 2026 som `Release`, med båda signaturerna och Windows-kontrollerna godkända samt verifierad programstart. `Debug` avvisades i den körningen. Resultat: `obj\UIAccessTest\suggestion-design-install-result.json`. De separata interaktionstesterna kunde inte slutföras: testbygget blockerades av Windows och UI-automatiseringen kunde inte öppna appfönstret.

Den senaste designen samlar ordförslagen i en gemensam vit ruta med rundade hörn, tunn kant och mjuk skugga. Höjden följer antalet ord och fönstrets storlek. När inga förslag finns döljs hela rutan och området är tomt. Den versionen installerades den 14 november 2026 som `Debug` med SignTool efter att `Release`-paketets DLL blockerats. Båda installerade filerna fick `Valid` och `Accepted`, och programstart verifierades med Windows-skyddet aktivt. Ordlista och inställningar var oförändrade. Resultat och återställningskopia: `obj\UIAccessTest\fitting-suggestions-install-result.json`. Release-blockeringen finns i `obj\UIAccessTest\fitting-suggestions-release-blocked.json`. Startfilen använder nu `Debug` som standard och varje nytt paket måste kontrolleras.

Statusradens text minskades därefter och följer tangentbordets textstorlek, med högst 12 som teckenstorlek. Samma storlek används för aktuellt ord, statusmeddelanden och indikatorn för aktiva tangenter. Ändringen installerades den 14 november 2026 som `Debug`; båda signaturerna och Windows-kontrollerna godkändes, och programstart verifierades med skyddet aktivt. Ordlista och inställningar var oförändrade. Resultat och återställningskopia: `obj\UIAccessTest\status-text-install-result.json`.

Det tomma området för ordförslag visar nu en centrerad grå pratbubbla med ett streck. Ikonen följer fönstrets storlek och döljs när förslag finns; den vita förslagsrutan är fortsatt dold när listan är tom. Ändringen installerades den 14 november 2026 som `Debug`. Båda filerna fick `Valid` och `Accepted`, och programstart verifierades med Windows-skyddet aktivt. Ordlista och inställningar var oförändrade. Senaste resultat och återställningskopia: `obj\UIAccessTest\empty-suggestions-install-result.json`.

Efter ett blockerat `Release`-försök den 14 november ändrades `Sign-SoftKey.ps1` till `Debug` som standard för att följa den installerade konfigurationen. Ett nytt standardförsök valde korrekt `Debug`, men även det paketets DLL blockerades trots giltig signatur. Standardvalet löser alltså inte Windows godkännande. Den befintliga installationen med ikonen var oförändrad och startades därefter med båda signaturerna giltiga och Windows-skyddet aktivt. Blockerat försök och återställningskopia: `obj\UIAccessTest\default-signing-fix-result.json`. Startkontroll av den befintliga installationen: `obj\UIAccessTest\installed-start-after-block-result.json`. Det ursprungliga Release-felet är sparat i `obj\UIAccessTest\release-blocked-before-default-fix.json`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File C:\sofType\Sign-SoftKey.ps1 -Configuration Debug
```

Lägg till `-SignOnly` om du enbart vill skapa ett nytt signerat paket utan att installera eller starta det. Paketets sökväg skrivs till `obj\UIAccessTest\signing-result.json`. Standardkörningen ändrar inga certifikat eller säkerhetsinställningar. Endast den uttryckliga flaggan `-DisableSmartAppControl` ändrar SAC vid slutlig privat installation. Vid en blockering avbryts uppdateringen av installationsskriptet.

SignTool finns lokalt i `obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe` och kommer från Microsofts NuGet-paket `Microsoft.Windows.SDK.BuildTools` version `10.0.28000.2957`. Verktygets Microsoft-signatur kontrolleras före användning. Källa och filhashar finns i `obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\tool-source.json`. Behåll verktygsmappen vid städning av byggfiler. Om verktyget flyttas kan en annan sökväg anges med `-SignToolPath`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File C:\sofType\Sign-SoftKey.ps1 -SignToolPath "C:\sofType\obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe"
```

SignTool väljer det befintliga certifikatet i `LocalMachine\My` med `/sm /s My /sha1`, signerar båda filerna med SHA256 och kontrollerar signaturerna med `verify /pa /all`. Installationsskriptet kontrollerar sedan signaturerna igen och Windows kodintegritet före filbytet. Resultatfilens `SigningTool` visar vilket verktyg som användes. Direktkörning av `Install-Local-UIAccess.ps1` utan `-SignToolPath` använder fortfarande den tidigare PowerShell-signeringen; använd startfilen ovan för den provade SignTool-rutinen.

SignTool-testet den 13 november 2026 godkände båda programfilerna och verifierade start av den installerade versionen med Windows-skyddet aktivt. Resultatet finns i `obj\UIAccessTest\signtool-test-result.json`. Det är ett verifierat resultat för det paketet; framtida uppdateringar måste genomgå samma kontroller.

Vid kontroll senare samma dag blockerade Windows båda filerna i ett nytt paket. Den redan installerade versionen startade fortfarande med skyddet aktivt, medan normal start av en separat skyddad testkopia nekades. EXE-filen hade samma SHA256 i installationen och testkopian. Resultaten finns i `obj\UIAccessTest\installed-startup-check.json` och `obj\UIAccessTest\launch-probe-result.json`. Orsaken till skillnaden mellan filkopiorna är inte fastställd. SignTool ska därför inte beskrivas som en varaktig lösning på blockeringen, och kodintegritetskontrollen ska behållas.

Ett nytt signeringsförsök på användarens begäran den 13 november 2026 godkände senare samma paket: EXE `BD13781C19EEA17856D54B2E7B4EB81787AD1EC19AFA64969C385305DC4F7913`, DLL `166B5B8858F18E2A993D1C885F133B963F03553474928E0371488283B1EF5329`. Båda fick `Valid` och `Accepted`; senaste versionen installerades och startkontrollerades med skyddet aktivt. Ordlistan var oförändrad. Resultat: `obj\UIAccessTest\continued-signing-result.json`. Detta fastställer inte orsaken till det ändrade godkännandet eller Microsofts beslut i granskningsärendet.

Ett granskningsärende skickades till Microsoft den 13 november 2026 efter användarens godkännande. Användaren slutförde CAPTCHA och inskickandet. Ärende-ID: `4ca6f6c6-300d-439c-9763-1153b0609d8c`. Ärendet finns på https://www.microsoft.com/en-us/wdsi/submission/4ca6f6c6-300d-439c-9763-1153b0609d8c och visade `Submitted` med slutlig bedömning `Pending` vid kontrollen. Texten anger uttryckligen att SoftKey endast ska användas privat på ägarens dator och inte marknadsföras eller erbjudas till allmänheten. Lokal bekräftelse: `obj\UIAccessTest\review-package-result.json`.

Vid `BlockedByWindows` har signeringen kunnat lyckas trots att Windows avvisar en programfil. Kontrollera `Files` i resultatfilen: `SignatureStatus` och `WindowsCodeIntegrity` är separata kontroller. Det befintliga certifikatet är självsignerat; en giltig lokal signatur garanterar inte godkännande av Smart App Control.

För att identifiera den blockerande policyn, kör följande i PowerShell som administratör. Kommandot läser policyer och SoftKey-relaterade logghändelser och sparar `obj\UIAccessTest\policy-diagnostics.json`. Det ändrar inga säkerhetsinställningar.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File C:\sofType\Get-UIAccessDiagnostics.ps1
```

[Install-Local-UIAccess.ps1](Install-Local-UIAccess.ps1) är signerings- och installationsskriptet i `C:\sofType`. Skriptet väljer det befintliga certifikatet från `Cert:\LocalMachine\My` via tumavtrycket i parametern `CertificateThumbprint`. Det signerar både `SoftKey.exe` och `SoftKey.dll` innan filerna installeras.

Installationsskriptet kan även köras direkt med SignTool enligt nedan. `-Build` bygger den aktuella koden med UIAccess-manifestet före signeringen. Varje körning skapar nya signaturer med det befintliga certifikatet.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File C:\sofType\Install-Local-UIAccess.ps1 -Build -SignToolPath "C:\sofType\obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe"
```

`UIAccess-install-result.json` visar signaturstatus, filernas SHA256 och Windows kodintegritetskontroll för båda programfilerna. Kontrollera att slutstatus är `Installed`. Om Windows avvisar paketet sparas det som `BlockedByWindows`. En ny signering garanterar inte att Windows godkänner paketet.

Det går även att bygga och signera i separata steg.

1. Bygg Release från projektmappen med UIAccess-manifestet. Skriptet hämtar filerna från `obj\UIAccessTest\bin\SoftKey\release`.

   ```powershell
   Set-Location C:\sofType
   dotnet build .\SoftKey.csproj --artifacts-path .\obj\UIAccessTest -c Release -p:EnableUIAccess=true
   ```

2. Stäng SoftKey och behåll en kopia av den fungerande installationen tills den nya versionen har startats och kontrollerats. Kör installationsskriptet och godkänn Windows administratörsprompt.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Install-Local-UIAccess.ps1 -SignToolPath "C:\sofType\obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe"
   ```

   Använd `-Configuration Debug` om du har byggt med `-c Debug`. För att bara provsignera bygget, lägg till `-SignOnly`. Sökvägen till de signerade filerna skrivs till `obj\UIAccessTest\signing-result.json`.

3. Skriptet kontrollerar både signaturerna och Windows kodintegritet före filbytet. Vid `BlockedByWindows` finns den fungerande installationen kvar. Skriptet kontrollerar även filerna på deras installerade plats och återställer föregående version om den kontrollen misslyckas. Kontrollera att `UIAccess-install-result.json` visar `Installed` och att båda installerade filerna visar signaturstatus `Valid`.

   ```powershell
   Get-AuthenticodeSignature -LiteralPath 'C:\Program Files\SoftKey-UIAccess-Test\SoftKey.exe',
       'C:\Program Files\SoftKey-UIAccess-Test\SoftKey.dll' | Select-Object Path, Status
   ```

4. Starta `C:\Program Files\SoftKey-UIAccess-Test\SoftKey.exe` med Windows-skyddet aktivt och kontrollera att appen startar. Vid en ny blockering, kontrollera vilken fil Windows loggade under `Microsoft-Windows-CodeIntegrity/Operational`. En giltig signatur och en lyckad programstart är separata kontroller.

Det vanliga bygget använder `SoftKey.manifest` och begär administratörsrättigheter vid start. UIAccess-bygget använder `SoftKey.UIAccess.manifest` och behöver en giltig signatur samt installation i en skyddad mapp. Inmatning till upphöjda program kräver en matchande integritetsnivå. Windows säkra UAC-skrivbord omfattas av separata åtkomstregler.
