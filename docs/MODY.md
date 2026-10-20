# Wykrywanie i synchronizacja modów w 0.7

Na stronie modów launcher odczytuje nazwy i ID zgłaszane przez wybrany serwer. Odczyt rozpoczyna się po otwarciu tej strony; przycisk odświeżania pozwala wykonać go ponownie. Badlands i Deer Isle mają osobne wyniki. Sam odczyt nie subskrybuje ani nie pobiera modów. W etapie 0.6 dodano osobne zadania sprawdzenia i synchronizacji Workshop na komputerze gracza. Wersja 0.7 używa tej samej synchronizacji także po naciśnięciu GRAJ, przed uruchomieniem DayZ.

`requiredWorkshopIds: null` w `config/servers.json` pozostaje bez zmian. To brak zatwierdzonej listy statycznej, a nie serwer bez modów. Wynik odczytu jest przechowywany osobno w pamięci i ma czas sprawdzenia. Po nieudanym odświeżeniu stare pozycje nie mogą wyglądać jak nowy, prawidłowy wynik.

## Kompletność i brakujące ID

- Dodatnie ID pochodzi z odpowiedzi serwera. Nie dopisujemy ID na podstawie podobnej nazwy ani wyników wyszukiwania.
- ID 0 oznacza pozycję lokalną lub nierozpoznaną; nie jest poprawnym ID Workshop i nie wolno jej pominąć przy ocenie kompletności.
- Nieznane flagi odpowiedzi są zachowywane. Nie przypisujemy im zgadywanego znaczenia. Lista może być pokazana, lecz jej kompletność wymaga potwierdzenia.
- Odebranie i złożenie wszystkich stron pakietu potwierdza transmisję. Nie jest gwarancją, że serwer ujawnił całą listę potrzebną do gry.
- Brak odpowiedzi, nieobsługiwany format lub błąd odczytu oznacza brak nowej listy, a nie pustą, poprawną listę modów.

Rozpoznane dodatnie ID można sprawdzić i synchronizować także wtedy, gdy cała lista pozostaje niepotwierdzona. ID 0 jest nadal widoczne jako nierozpoznane i nie może zniknąć z oceny wyniku. Flaga `0x08` nie jest automatycznie opisywana jako „ucięta lista” — nie ma potwierdzonej interpretacji. GRAJ blokuje start przy ID 0. Sama nieznana flaga nie ma potwierdzonego znaczenia: po synchronizacji wszystkich dodatnich ID launcher pozwala podjąć próbę wejścia, zachowując ostrzeżenie o niepotwierdzonej kompletności. Nie przedstawia tego jako udanego połączenia.

## Sprawdzanie i pobieranie przez Steam

**SPRAWDŹ MODY NA KOMPUTERZE** uruchamia odczyt stanu bez subskrypcji lub pobierania. **SYNCHRONIZUJ MODY** zleca subskrypcję rozpoznanych pozycji DayZ Workshop oraz pobranie brakujących plików i aktualizacji. Pobieranie wykonuje Steam do biblioteki gracza, nie serwer gry ani komputer administratora. Postęp i stan poszczególnych modów są widoczne w launcherze.

Kod nie traktuje samego `DownloadItem` ani obecności folderu jako sukcesu. Pomocnik obsługuje właściwe wyniki subskrypcji i pobierania Steam, sprawdza zgodność App ID i ID moda, a następnie potwierdza instalację oraz brak stanu oczekiwania, pobierania lub wymaganej aktualizacji. Informacja „pobieranie rozpoczęte” pozostaje różna od „gotowy”.

Lista serwera jest odczytywana świeżo przed zadaniem. Po synchronizacji następuje ponowny odczyt: dodane mody są uzupełniane, a usunięte przestają należeć do wymagań tego serwera. Liczba uzgodnień jest ograniczona; ciągle zmieniająca się lista wymaga późniejszego ponowienia. Błąd końcowego odczytu nie może być przedstawiony jako potwierdzona gotowość. Nie usuwamy plików ani nie anulujemy subskrypcji usuniętych pozycji — mogą być potrzebne na innych serwerach.

**ANULUJ ZADANIE** zatrzymuje własnego pomocnika. Steam może dokończyć pobieranie, które wcześniej przyjął do swojej kolejki. Launcher nie wstrzymuje globalnych pobrań, nie usuwa zawartości Workshop i nie zmienia jego manifestów ręcznie.

## Wymagania i ograniczenia integracji

Potrzebne są poprawnie wskazane DayZ i Steam, działający oraz zalogowany Steam, konto posiadające DayZ, połączenie z internetem i miejsce w bibliotece. Launcher i Steam muszą działać jako ten sam użytkownik i na tym samym poziomie uprawnień.

Osobny proces `SteamWorker` ładuje bibliotekę Valve `steam_api64.dll` z wybranej, zainstalowanej kopii DayZ. Launcher nie rozprowadza tej DLL, nie dostarcza `steam_appid.txt` i nie wymaga danych logowania. Kontekst aplikacji ustawiany jest wyłącznie dla procesu pomocnika; nie zmienia globalnego środowiska Steam. Dostęp do sesji Steam w takim pomocniku wymaga testu na docelowym Windows — nie stanowi gwarantowanego kontraktu Valve dla każdego zewnętrznego launchera.

Testy automatyczne logiki i UI używają atrap. Próba natywnego odczytu w odizolowanym środowisku narzędziowym nie widziała działającego Steam mimo potwierdzenia użytkownika, więc nie potwierdziła integracji i nie uruchamiała pobierania. Osobny odczyt pomocnika uruchomionego przez użytkownika potwierdził stan CF i niezmieniony manifest Workshop. Sprawdzenie pobierania, aktualizacji i startu DayZ na komputerze gracza pozostaje osobnym testem pełnej paczki. Instrukcje: [URUCHOMIENIE.md](URUCHOMIENIE.md).

Semantykę subskrypcji, stanów i wyników pobierania opisuje [oficjalna dokumentacja ISteamUGC](https://partner.steamgames.com/doc/api/isteamugc?language=english).

## Zakres odczytu

Odczyt używa jednej sesji UDP: INFO, krótka przerwa 300 ms i RULES z otrzymanym cookie. Gdy INFO nie wymaga challenge, RULES zaczyna się od standardowego żądania challenge. Obowiązują łączne limity 5 sekund, 16 datagramów i 64 KiB oraz najwyżej dwa challenge na fazę. Odpowiedzi dzielone są składane i walidowane; kompresja bzip2 pozostaje nieobsługiwana. Niepowodzenie nie uruchamia poszukiwania innych portów.

## Obserwacje diagnostyczne przed publikacją 0.5

Badlands zgłosił 12 pozycji. Jedną z nich jest `@craft-nails` z ID 0, dlatego metadane nie wystarczają do pełnej synchronizacji Workshop.

Dla Deer Isle bezpośrednie zapytanie RULES nie zwracało odpowiedzi. Dnia **19 października 2026 o 17:10:06–17:10:07 UTC** wykonano INFO, obsłużono challenge, a następnie wysłano RULES z tym samym cookie i gniazdem UDP. Odczytano 27 nazw z dodatnimi ID. Odpowiedź miała jednak nieznaną flagę `0x08`, więc nie deklarujemy potwierdzonej kompletności ani konkretnego znaczenia tej flagi. Surowy dowód zachowano w `work/launcher-tools/mod-discovery-20261019/deer-same-socket-20261019-171006-780073.json` w nadrzędnym workspace.

To obserwacje z konkretnej chwili, nie lista wpisana na stałe do launchera. Historyczny pakiet 27 pozycji pozostaje również materiałem kontrolnym parsera; nie oznacza aktualnej liczby modów na serwerze.

## Wynik końcowego odczytu 0.5

Produkcyjny klient z opublikowanej biblioteki Core wykonał odczyt **19 października 2026 o 17:36:25 UTC**. Raport `artifacts/testing/live-mods-query-report.json` wiąże wynik z SHA biblioteki oraz `servers.json`:

| Serwer | Odczytane pozycje | Pozycje z dodatnim ID | Flagi | Potwierdzona kompletność |
| --- | --- | --- | --- | --- |
| Badlands | 12 | 11; `@craft-nails` ma ID 0 | `0x00` | Nie |
| Deer Isle | 30 | 30 | `0x08` | Nie; znaczenie flagi niezweryfikowane |

To odczyt nowego klienta 0.5, a nie wynik dawnego skryptu diagnostycznego. Zmiana Deer Isle z 27 na 30 pozycji pokazuje, dlaczego lista jest odczytywana dynamicznie. Nie instalowano modów ani nie uruchamiano DayZ. Testy UI z nazwami zaczynającymi się od `TEST` używają osobnych atrap; ich obrazy nie przedstawiają tych danych produkcyjnych.

## Źródła protokołu

[Dokumentacja Steamworks: ServerRules](https://partner.steamgames.com/doc/api/ISteamMatchmakingServers#ServerRules) opisuje odczyt reguł serwera, ale nie binarny układ listy DayZ. [Komunikat inżyniera Valve o challenge](https://steamcommunity.com/discussions/forum/14/2974028351344359625/) potwierdza, że INFO może wymagać ponowienia z otrzymanym challenge. Wymóg ponownego użycia gniazda przy tym konkretnym serwerze wynika z naszej obserwacji sieciowej.

Układ DayZ v2 porównano z oryginalną implementacją autora biblioteki WoozyMasta/a2s: [składanie reguł](https://raw.githubusercontent.com/WoozyMasta/a2s/master/pkg/a3sb/rules.go), [rekordy modów i ID](https://raw.githubusercontent.com/WoozyMasta/a2s/master/pkg/a3sb/mods.go), [różnica DayZ i Arma](https://raw.githubusercontent.com/WoozyMasta/a2s/master/pkg/a3sb/version.go) oraz [nieudokumentowane flagi](https://raw.githubusercontent.com/WoozyMasta/a2s/master/pkg/a3sb/flags.go). Jest to źródło autora parsera, nie oficjalna specyfikacja Bohemii. Nie importowano ani nie uruchamiano zdalnego kodu.
