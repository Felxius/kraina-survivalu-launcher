# Stan implementacji

## 0.6.0-alpha: synchronizacja Steam i przygotowanie aktualizacji

| Funkcja | Stan |
| --- | --- |
| Interfejs WPF, dwa serwery, ustawienia ścieżek | Dostępne |
| Nick gracza | Lokalny zapis i walidacja |
| GRAJ i przekazanie nicku do DayZ | Niedostępne w tym etapie |
| Ikona i ilustracje obu serwerów | Dostępne; ilustracje promocyjne, nie zrzuty serwerów |
| Discord i oryginalne dziewięć rozdziałów regulaminu | Dostępne, lokalny odczyt i filtr |
| Aktualności | Zakładka usunięta; bez edytora i hostowanego feeda |
| Status A2S i liczba graczy | Dostępne; osobne porty Query podane przez administrację |
| Listy modów z serwerów | Dostępne, osobny stan i czas sprawdzenia dla każdego serwera |
| Sprawdzenie modów na komputerze | Jawne zadanie; bez subskrypcji i pobierania |
| Synchronizacja Workshop | Implementacja przygotowana: pobieranie brakujących modów i aktualizacji przez Steam na komputer gracza, postęp i anulowanie |
| Zmiana wymagań serwera | Świeży odczyt przed zadaniem i po synchronizacji; ograniczone uzgadnianie zmian |
| ID 0 i nieznane flagi | Jawna niepotwierdzona kompletność; bez zgadywania ID lub znaczenia flag |
| Aktualizacje launchera | Roboczy projekt ma repozytorium Felxius/kraina-survivalu-launcher i publiczny klucz; przesłanie źródeł, sekret GitHub oraz publikacja pozostają do wykonania |
| Zastosowanie aktualizacji | Po pobraniu i sprawdzeniu podpisu, przez bezpieczne ponowne uruchomienie wybrane przez gracza |
| Instalator i podpis Windows | Skrypty przygotowane; osobna kompilacja/test instalatora i rzeczywisty certyfikat nadal wymagane |

Badlands: gra `145.239.237.112:27800`, Query `27801`. Deer Isle: gra `178.95.158.100:23000`, Query `9000`. Brak odpowiedzi nie oznacza potwierdzonego wyłączenia serwera i nie daje podstaw do pokazania zera graczy. Zobacz [STATUS-SERWEROW.md](STATUS-SERWEROW.md).

Rozpoznane ID Workshop można synchronizować także przy niepotwierdzonej kompletności całej listy. Nie wolno przedstawiać tego jako gotowości do uruchomienia gry. Launcher nie usuwa ani nie wypisuje z subskrypcji modów pominiętych przez nową listę. Anulowanie dotyczy własnego zadania; kolejka Steam może działać dalej. Szczegóły: [MODY.md](MODY.md).

## Weryfikacja

Wyniki 0.6 należy odczytać z raportów danego wydania. Testy z atrapami obejmują zachowanie interfejsu, uzgadnianie list i błędy usług, a testy aktualizatora korzystają z kontrolowanego HTTP i jednorazowych folderów. Nie zastępuje to rzeczywistej synchronizacji Workshop w zalogowanej sesji Steam.

Próba natywnego odczytu w odizolowanym środowisku narzędziowym nie miała dostępu do działającego Steam, mimo potwierdzenia użytkownika. Nie potwierdzono w niej rzeczywistego połączenia ani pobierania; wymagany jest test pełnej paczki na komputerze docelowym. Nie należy przypisywać tego wyniku automatycznie błędowi lub wylogowaniu Steam u gracza.

Historyczne 0.5 przeszło 88 testów Core i 65 UI. Produkcyjny odczyt modów z 19 października 2026, 17:36:25 UTC, zwrócił Badlands 12 pozycji (jedna z ID 0) i Deer Isle 30 (flaga `0x08`). Był to test odczytu, bez pobierania; nie jest weryfikacją 0.6 ani stałą listą serwerów. Dotychczasowe archiwa i raporty pozostają oddzielnie.

## Pozostałe etapy

1. Sprawdzić integrację z rzeczywistym Steam i pobieranie/aktualizację na komputerze gracza; zachować raport odróżniający ten test od atrap.
2. Potwierdzić brakujące ID i kompletność list. Nie dopisywać zgadywanych ID ani znaczenia flag odpowiedzi.
3. Wdrożyć GRAJ z walidacją gotowości, nickiem oraz prawidłowym uruchomieniem DayZ i połączeniem z serwerem.
4. Skonfigurować rzeczywiste GitHub Releases i publiczny klucz; sprawdzić aktualizację między dwiema podpisanymi paczkami zgodnie z [AKTUALIZACJE.md](AKTUALIZACJE.md).
5. Sprawdzić instalację, aktualizację i dezinstalację na czystym Windows. Podpis Authenticode, SmartScreen i AV pozostają osobnymi kontrolami; zobacz [PODPISYWANIE.md](PODPISYWANIE.md).
