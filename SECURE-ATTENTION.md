# Ctrl + Alt + Delete

Implementerat 17 november 2026. Funktionen aktiveras genom att användaren trycker de befintliga tangenterna Ctrl, Alt och Delete i ordning. Ingen extra knapp eller menypost finns. Ctrl och Alt hålls av SoftKeys befintliga modifierarsystem tills Delete väljs.

Kombinationen fångas före vanlig Delete-inmatning och går genom Windows dokumenterade `sas.dll!SendSAS`. Ägda modifierare släpps före begäran. Ett misslyckat modifieraruppsläpp stoppar begäran och behåller ägarskapet för nytt försök. Vanligt Delete och Ctrl+Delete fortsätter som vanliga tangentkombinationer. AltGr feltolkas inte som Alt.

Funktionen använder `AsUser=true` för användarens skrivbord/låsskärm och `false` för SYSTEM-tangentbordet på Winlogon. Endast aktivt rätt skrivbord och rätt låstillstånd tillåts. Behörigheten UIAccess, aktiv UAC och tillåtande Windows SAS-policy kontrolleras. Systembiblioteket laddas från Windows systemkatalog. Inga personliga ord, inmatade tecken eller autentiseringsuppgifter loggas eller skickas till någon tjänst.

Inloggningsdelens manifest begär nu också UIAccess, som krävs av Windows API för ett hjälpmedelsprogram utan tjänst. Den signeras och installeras på skyddad plats liksom de andra UIAccess-delarna. Programflaggorna `--verify-secure-attention` läser tokenbehörighet, UAC/policy och kontrollerar SAS-exporten utan att anropa SendSAS eller ändra skärm.

På utvecklingsdatorn var SoftwareSASGeneration redan 3 och EnableLUA redan 1. Dessa värden bevaras; ingen policyändring behövdes. Slutpaketet för en ny dator måste kontrollera och hantera denna förutsättning enligt PRIVATE-INSTALLER.md. En saknad/avstängd policy tillåter enligt Microsoft bara hjälpmedel på den skyddade skärmen, vilket inte räcker för begäran från det vanliga skrivbordet.

124 Startup-/sessions-/SAS-kontroller och 211 tillförlitlighetskontroller passerade. 19 inloggningsinstallationskontroller och 16 privata installationskontroller passerade också. SAS-anrop simulerades i testerna; ingen verklig Ctrl+Alt+Delete-växling eller autentisering automatiserades. Separat användarprov krävs på skrivbordet och vid inloggningen. SendSAS saknar returvärde och UI-statusen anger därför att begäran skickats, inte att Windows skärmbyte bevisligen lyckats.

Källor: [Microsoft SendSAS](https://learn.microsoft.com/en-us/windows/win32/api/sas/nf-sas-sendsas) och [Microsoft SoftwareSASGeneration](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-winlogon#softwaresasgeneration).
