# Stan implementacji 0.7.0-alpha

GRAJ przygotowuje mody na komputerze gracza i uruchamia DayZ przez BattlEye z wybranym adresem oraz nickiem. Wymagania serwera odczytuje przed synchronizacją i po niej; zmiany listy są uzgadniane w ograniczonej liczbie prób. Nierozpoznane ID, brak odpowiedzi, niegotowe pliki lub działająca już gra wstrzymują start. Nieznane flagi pozostają ostrzeżeniem o kompletności; nie przypisujemy im zgadywanego znaczenia.

Dostępne: dwa profile serwerów, status/liczba graczy, osobne dynamiczne listy modów, inspekcja i synchronizacja Steam, anulowanie własnego zadania, zapis nicku i ścieżek, Discord, oryginalny regulamin, ikona i ilustracje. Aktualności usunięto zgodnie z decyzją właściciela. Rozpoznane mody można nadal synchronizować osobno bez uruchamiania gry. Usunięcie moda z wymagań nie usuwa jego plików ani subskrypcji.

GitHub Releases: repozytorium Felxius/kraina-survivalu-launcher, sekret środowiska release i klucz publiczny są skonfigurowane. Pierwszy szkic 0.6.1-alpha zbudowany na GitHub przeszedł kontrolę podpisu, całej paczki i 85 testów WPF. Szkic nie jest publiczną aktualizacją. Poprzednie archiwa i raporty pozostają niezmienione.

Dotychczasowy test rzeczywistego Steam był odczytem pomocnika w sesji użytkownika, bez pobierania i bez zmian manifestu Workshop. Testy logiki i WPF używają kontrolowanych usług oraz obojętnych plików. Wyniki 0.7 należy odczytać z raportów tego wydania; nie utożsamiać ich z udanym wejściem do rzeczywistego serwera.

Pozostało: ręczna próba pobierania/aktualizacji modów i wejścia do DayZ z 0.7; potwierdzenie brakującego ID Badlands przez administrację, jeśli nadal występuje; próba aktualizacji pomiędzy dwiema publicznymi podpisanymi paczkami; kompilacja/test instalatora na czystym Windows. Authenticode i reputacja SmartScreen/AV są osobnym etapem. Szczegóły: URUCHOMIENIE.md, TESTOWANIE.md, AKTUALIZACJE.md, PODPISYWANIE.md.
