# Kontrola wydania 0.6

Kontrola z 19 października 2026: 106 grup testów Core, 48 testów aktualizatora i 85 kontroli WPF zakończonych sukcesem. Test utworzenia rzeczywistego dowiązania katalogu został jawnie pominięty: Windows odmówił uprawnienia (Win32 1314). Testy odrzucania dowiązań zapisanych w ZIP-ie przeszły. WPF nie zgłosił błędów powiązań, a publikacja po teście pozostała bez zmian. Raporty odnoszą się do `win-x64-0.6.0-sync-review-04`.

Osobny test w zwykłej sesji użytkownika potwierdził połączenie roboczego pomocnika z zalogowanym Steam i stan zainstalowanego CF; manifest Workshop pozostał niezmieniony. Ten odczyt nie jest testem pobierania i nie należy przedstawiać go jako uruchomienia całej finalnej paczki. Rzeczywiste pobieranie brakującego moda i aktualizacja pomiędzy dwoma opublikowanymi wydaniami wymagają dalszych prób. Nie utworzono repozytorium GitHub ani trwałego klucza publikacji.

## Aplikacja i profil

1. `scripts/Build.ps1`: wymagane zero błędów i brak nieudanych testów Core.
2. Uruchom `--ui-smoke` na opublikowanym EXE z osobnym folderem ustawień. Zachowaj raport, obrazy i pełny odcisk publikacji; po teście pliki aplikacji muszą być niezmienione.
3. Sprawdź stronę główną, ustawienia i regulamin przy skalowaniu 100%, 125% i 150%, w minimalnym rozmiarze okna oraz z długą treścią. Ikona ma być widoczna w EXE i oknie; obie ilustracje muszą się ładować.
4. Zapisz poprawny nick z polskimi znakami, zamknij aplikację i sprawdź odczyt. Zweryfikuj pusty nick, `Survivor`, przekroczenie 32 znaków Unicode, cudzysłowy i znaki sterujące. Przy migracji starych ustawień zachowaj ścieżki i traktuj brak nicku jako nieustawiony.
5. Upewnij się, że nick nie jest przedstawiany jako przekazany do gry. GRAJ pozostaje niedostępne; synchronizacja rozpoznanych ID jest dostępna osobnym przyciskiem.
6. Potwierdź Badlands: gra `145.239.237.112:27800`, Query `27801`; Deer Isle: gra `178.95.158.100:23000`, Query `9000`. Statyczne `requiredWorkshopIds` pozostają `null`; dynamicznych wyników nie wpisujemy do tego pola.

## Status Query

1. Testy Core powinny sprawdzać prawidłowe i błędne pakiety, challenge, złożenie obsługiwanych fragmentów oraz timeout, anulowanie i limity danych. Testy muszą używać kontrolowanych odpowiedzi, bez zależności od dostępności produkcji.
2. Test UI powinien przejść przez odświeżanie, prawidłowy wynik i brak odpowiedzi. Dwa serwery mają niezależne stany i liczby graczy. Podczas odświeżania interfejs pozostaje dostępny; ponowne kliknięcie nie tworzy równoległych pętli.
3. Timeout i błąd sieci mają pokazywać brak odpowiedzi, a błędna odpowiedź problem odczytu. Żaden z tych wyników nie może być oznaczony jako potwierdzone offline ani jako `0` graczy. Prawidłowy serwer z zerem graczy nadal jest online.
4. Osobny test rzeczywistego UDP powinien zapisać datę, dokładny endpoint Query i otrzymany wynik. Jeśli środowisko blokuje UDP lub serwer nie odpowiada, zachowaj ten wynik i ograniczenie. Nie zmieniaj portów ani nie zastępuj wyniku domysłem.

## Wykrywanie modów

1. Otwarcie listy modów uruchamia odczyt wybranego serwera. Sprawdź nazwy, ID, czas odczytu, ręczne odświeżanie i niezależne wyniki obu serwerów. Podczas oczekiwania okno nadal odpowiada, a kolejne kliknięcia nie uruchamiają duplikatów tego samego zapytania.
2. Sprawdź challenge INFO i odczyt RULES z tym samym cookie oraz gniazdem UDP. Kontrole Core obejmują również błędne i brakujące strony, limity danych, nieobsługiwany format i anulowanie.
3. ID 0 ma zostać widoczne jako nierozpoznany/lokalny mod, bez zamiany na fikcyjne ID Workshop. Nieznana flaga odpowiedzi musi pozostawić listę widoczną, ale blokować deklarację potwierdzonej kompletności. Kompletne strony transmisji nie oznaczają kompletnej listy wymaganej do gry.
4. Timeout lub błędna odpowiedź czyści poprzednie pozycje tego serwera zamiast przedstawiać stare dane jako nowy sukces. Nie wpływa na ostatni wynik drugiego serwera. Zamknięcie aplikacji anuluje oczekiwanie.
5. Testy UI używają atrap i kontrolowanych nazw/ID. Osobny raport żywego odczytu musi wskazywać dokładne endpointy i SHA opublikowanej biblioteki Core oraz konfiguracji. Nie są to testy instalacji ani pobierania modów; GRAJ pozostaje zablokowane.

## Synchronizacja i aktualizacje

1. Sprawdź osobno inspekcję bez pobierania, synchronizację, postęp i anulowanie. Żadna z tych operacji nie może uruchamiać gry, usuwać modów ani zatrzymywać globalnej kolejki Steam.
2. Przed operacją i po pobieraniu odczytaj aktualną listę serwera. Sprawdź dodanie i usunięcie moda podczas zadania, niedostępność serwera, nierozpoznane ID oraz limit kolejnych zmian listy. Stary wynik nie może być przedstawiany jako bieżąca gotowość.
3. Gotowość wymaga stanu Steam Installed bez NeedsUpdate/Downloading/DownloadPending i poprawnego folderu instalacji. Samo przyjęcie subskrypcji lub pobierania nie oznacza sukcesu.
4. Testy aktualizatora używają atrap HTTP, kluczy tylko w pamięci i tymczasowych instalacji. Sprawdzają podpis, hash, wersję, kompletność paczki, ścieżki ZIP, limit danych, blokadę równoległej wymiany i przywrócenie po błędzie. Nie publikują wydania.
5. Przy pustym `updates.json` aplikacja nie kontaktuje się z GitHub. Po konfiguracji dwóch rzeczywistych wydań sprawdź pobranie, restart i zachowanie profilu gracza; instrukcja w [AKTUALIZACJE.md](AKTUALIZACJE.md).

## Społeczność i dane

1. Discord wskazuje `https://discord.gg/qEwYQqj7MD`; test automatyczny przechwytuje kliknięcie, bez otwierania przeglądarki.
2. Regulamin zachowuje dziewięć oryginalnych rozdziałów. Sprawdź filtr, wynik pusty, wybór rozdziału i odświeżanie lokalnego JSON-a. Błędny plik ma zgłaszać problem, bez wymyślania treści zastępczej.
3. Zakładka aktualności i edytor nie istnieją w interfejsie. Produkcyjny `news` pozostaje pusty wyłącznie dla zgodności modelu.
4. Sprawdź brak/błędną konfigurację oraz odzyskiwanie uszkodzonych ustawień na kopii testowej. Dane testowe nie mogą trafić do paczki.

## Paczka i instalator

1. Świeża publikacja self-contained ma trzy pliki JSON zgodne ze źródłem oraz kompletne podfoldery `SteamWorker` i `Updater`. Uruchom EXE również na czystym Windows bez osobnego .NET.
2. Sprawdź CRC ZIP i SHA wszystkich zawartych plików. Zachowaj bajty testowanej publikacji; nie dołączaj SDK, cache, sandboxów ani kluczy prywatnych.
3. Inno Setup 6: sprawdź instalację na zwykłym koncie, ikonę instalatora, Start i opcjonalny pulpit, aktualizację oraz dezinstalację. Ustawienia w LocalAppData mają pozostać.
4. Jeśli właściciel wybierze podpisywanie, podpisz własne pliki rzeczywistym certyfikatem, zweryfikuj podpis i timestamp, potem ponów test i odciski podpisanej paczki. Nie podpisuj bibliotek Microsoft.

Podpisywanie oraz ocena SmartScreen/AV nie są zastępowane testem UI. Nie deklaruj braku wykryć bez konkretnego wyniku; nie wysyłaj plików do analizy bez decyzji właściciela.
