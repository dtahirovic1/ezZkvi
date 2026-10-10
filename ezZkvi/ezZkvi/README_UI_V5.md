# ezZkvi — konzistentni UI (V5)

Ovo izdanje nadograđuje V4. Zadržava postojeće ASP.NET Core 8.0 kontrolere, modele, servise, migracije i serversku obradu kviza.

## Ključne izmjene

- **Jedinstveni header** na svim prijavljenim stranicama osim dashboarda: `Views/Shared/_Layout.cshtml` s dosljednim naslovom i dugmetom „Nazad“. Na stranicama dodavanja/uređivanja/detalja/brisanju vraća na indeks odgovarajućeg kontrolera, a inače na dashboard uloge.
- **Priprema**: uklonjen lokalni dupli header; kompaktnija traka „Pitanje 1 / N“, sužen panel oblasti, pitanje i odgovori visoko postavljeni, statistika ispod kartice; scroll samo onoliko koliko nedostaje da se vidi sadržaj.
- **Simulacija**: uklonjeno duplo zaglavlje; isti CSS vrijedi za `Simulate`, `StartSimulation`, `ContinueSimulation` i `SubmitSimulation`, jer se isti Razor view vraća iz različitih akcija. Normalizirani font, prikaz rezultata i dugmad.
- **Feedback**: uklonjen dupli header, manji selector tipa prijave i kompaktniji formular; serversko slanje ostaje isto.
- **Administrator i moderator**: duple gornje trake zamijenjene zajedničkim headerom i pravilima za kartice, tabelarne prikaze i akcijska dugmad.
- **Vizuelni stil**: Inter, skoro crna podloga, jedan ljubičasti brend akcent, neutralne akcijske tipke, suptilne ivice i ponovljiva tipografska hijerarhija.

## Gdje je kod?

- `Views/Shared/_Layout.cshtml`: jedna navigacija i zajednički page header.
- `wwwroot/css/ez-unified-v5.css`: svi novi layout i responsive propisi, učitava se **posljednji**.
- `Views/Student/Prepare.cshtml`, `Views/Student/Simulate.cshtml`, `Views/Feedback/Create.cshtml`: odstranjena redundantna zaglavlja i prilagođeni rasporedi.

## Pokretanje

1. Otvori `eZzkvi.sln` u Visual Studio i postavi odgovarajući startup projekt `ezZkvi`.
2. Postavi svoje lokalne postavke za bazu i e-mail; ne mijenjaj produkcijske kredencijale zbog UI izmjena.
3. Pokreni aplikaciju, prijavi se kao Student, Moderator i Admin i provjeri sve glavne tokove.
4. Za uklanjanje cacheiranog CSS-a uradi **Ctrl + F5** ili uključi Disable cache u DevTools.

## Šta je testirano / šta nije

- Statička provjera CSS-a, JS sintakse, projektnog arhiva i prisutnosti navigacijskih pravila.
- Nisam pokrenuo puni ASP.NET build, browser rendering sa stvarnim podacima, bazu ni end-to-end korisničke tokove: .NET SDK nije dostupan u okruženju, a Chromium ne uspijeva završiti pokretanje.
- Na vrlo malim ekranima i sa jako dugim tekstom pitanja/prijave prirodno je potrebno skrolanje. Dizajn uklanja **nepotrebni** scroll prije prvog pitanja i uobičajenog kratkog feedback obrasca.

Samostalni HTML pregled V5 je zasebna demo datoteka (nije povezana s bazom).
