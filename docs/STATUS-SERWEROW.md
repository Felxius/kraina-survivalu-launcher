# Status serwerów i Steam Query

Adresy przekazał właściciel Krainy Survivalu. Są osobne dla ruchu gry i zapytań statusu:

| Serwer | Gra | Query |
| --- | --- | --- |
| Badlands | `145.239.237.112:27800` | `145.239.237.112:27801` |
| Deer Isle | `178.95.158.100:23000` | `178.95.158.100:9000` |

Launcher używa zapytania A2S_INFO przez UDP. Prawidłowa odpowiedź pozwala pokazać stan online i liczbę graczy. Odczyt nie uruchamia gry, nie sprawdza hasła serwera i nie potwierdza, że gracz ma wymagane mody. Od 0.5 osobny odczyt A2S_RULES służy do wykrywania nazw i ID modów; statyczne `requiredWorkshopIds` nadal pozostają `null`. Patrz [MODY.md](MODY.md).

## Jak czytać wynik

- **Online**: otrzymano prawidłową odpowiedź. Zero graczy jest poprawnym wynikiem, jeżeli właśnie tak odpowiedział serwer.
- **Brak odpowiedzi**: zapytanie przekroczyło limit czasu albo wystąpił błąd sieci/DNS. Może to wynikać także z zapory lub filtrowania UDP. Nie jest dowodem, że serwer jest wyłączony.
- **Błędna odpowiedź**: danych nie można bezpiecznie odczytać lub format nie jest obsługiwany. Liczba graczy pozostaje nieznana.
- **Brak konfiguracji**: nie podano potwierdzonego portu Query; zapytanie nie jest wysyłane. W dostarczonej konfiguracji 0.4 oba porty są podane.

Odczyt jest chwilowy. Brak bieżących danych nie może być zastępowany zgadywaną liczbą graczy. Wynik kontrolowanego testu nie jest dowodem dostępności serwerów produkcyjnych.

## Zakres odczytu statusu

Odczyt obsługuje odpowiedź INFO, challenge oraz nieskompresowane odpowiedzi dzielone w formacie Source. Maksymalny czas operacji to 5 sekund; obowiązują limity 16 datagramów i 64 KiB danych oraz najwyżej dwa żądania challenge. Sprawdzane są struktura pakietu i UTF-8. Liczniki 0–255 są zachowywane tak, jak podał je serwer, także gdy liczba graczy przekracza deklarowaną pojemność. Odpowiedź skompresowana bzip2 jest zgłaszana jako nieobsługiwana, bez udawania poprawnego wyniku.

Sam A2S_INFO nie zawiera listy modów. Wykrywanie A2S_RULES w 0.5 jest osobną operacją; do pobierania z Workshop i przycisku GRAJ nadal potrzebna jest dalsza implementacja.

Konfiguracja znajduje się w `config/servers.json`. Przed wydaniem musi być zgodna bajtowo ze źródłem. Zmiana portu wymaga potwierdzenia przez właściciela; nie należy próbować kolejnych sąsiednich portów ani ich wyliczać z portu gry.

## Obserwacja wydania 0.4

Rzeczywisty klient Query z opublikowanej biblioteki Core sprawdził oba endpointy 19 października 2026 o **16:38:36 UTC**. Wyniki zapisano w `artifacts/testing/live-status-query-report.json`, wraz z odciskami biblioteki i konfiguracji:

| Profil | Stan | Nazwa podana przez serwer | Mapa podana przez serwer | Gracze | Czas odpowiedzi |
| --- | --- | --- | --- | --- | --- |
| Badlands | Online | `Kraina Survivalu ^ 1shot1kill.pl` | `nasdara` | `0 / 8` | około 46,6 ms |
| Deer Isle | Online | `DayZ Server [PL]` | `deerisle` | `0 / 64` | około 15,8 ms |

Nazw i map z odpowiedzi nie zastąpiono nazwami marketingowymi. Liczby i czasy opisują jedynie ten odczyt. Zrzuty `11-server-status-online.png` i `12-server-status-no-response.png` pochodzą z odrębnych, kontrolowanych testów interfejsu i nie przedstawiają danych produkcyjnych.

Dokumentację protokołu publikuje [Valve: Server queries](https://developer.valvesoftware.com/wiki/Server_queries). W tym środowisku strona zwróciła HTTP 403, więc nie deklarujemy, że odczytano jej aktualną treść. Zachowanie implementacji weryfikują lokalne testy pakietów; wynik żywego UDP jest dokumentowany oddzielnie.
