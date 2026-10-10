# ezZkvi — V6: jedinstvena tamna tema s ljubičastim detaljima

Ova verzija nastavlja V5. Promjene su samo u prikazu i jednom informativnom tekstu u demo-kvizu.

- `ezZkvi/Views/Home/Index.cshtml` — nova početna stranica, objašnjenje namjene i predmetâ (DM i TP), informacija o pitanjima iz prethodnih godina, diskretan pregled funkcija i interaktivni ilustrativni demo.
- `ezZkvi/wwwroot/css/ez-landing-v6.css` — poseban stil nove početne, usklađen s ostatkom aplikacije.
- `ezZkvi/wwwroot/css/ez-purple-v6.css` — ljubičasti detalji za sva korisnička sučelja (student, moderator, administrator), aktivna navigacija, input fokus, napredak, filteri i oznake. Primarna dugmad ostaju neutralna.
- `ezZkvi/Views/Shared/_Layout.cshtml` — učitava V6 stilove posljednje da se primjenjuju i na postojeće prikaze.
- `ezZkvi/wwwroot/js/ez-ui.js` — odgovori interaktivnog demo-pitanja sada odgovaraju matematičkom primjeru na početnoj.

**Pokretanje:** otvoriti `.sln` u Visual Studiju, obnoviti NuGet pakete i pokrenuti projekat. Refresh browsera s Ctrl + F5 (Windows) ako postoji stari cache.

**Napomena:** nisu mijenjani C# kontroleri, modeli, baze, migracije ni autentifikacija. U ovom okruženju nije bio dostupan .NET SDK za runtime build. Browser je blokirao lokalno otvaranje demo-stranice (`ERR_BLOCKED_BY_ADMINISTRATOR`), tako da grafički test nije potvrđen. Preporučena je lokalna provjera desktop i mobilnih širina.
