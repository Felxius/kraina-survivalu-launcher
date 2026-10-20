# Aktualizacje launchera przez GitHub Releases

Przygotowano odczyt najnowszego publicznego wydania GitHub, automatyczne pobieranie paczki, sprawdzenie podpisu ECDSA P-256 i SHA-256 oraz pomocnika, który wymienia pliki po zamknięciu launchera. Właściciel wskazał repozytorium [Felxius/kraina-survivalu-launcher](https://github.com/Felxius/kraina-survivalu-launcher). W konfiguracji wpisano ten adres i publiczny klucz wygenerowany 19 października 2026. Klucz prywatny zachowano osobno poza projektem; nie trafia do źródeł ani paczki dla graczy. Źródła i sekret środowiska release są już skonfigurowane. GitHub zbudował szkic 0.6.1-alpha; podpis pobranego manifestu, rozmiar i SHA ZIP-a oraz 85 testów jego interfejsu przeszły kontrolę. Szkic nie jest publiczną aktualizacją. Wcześniejszy lokalny testowy ZIP 0.6.0 zachowuje swoją pustą konfigurację i pozostaje bez zmian.

Dla tego wdrożenia klucz już istnieje: nie generuj go ponownie. Następne kroki to wysłanie czystych źródeł do katalogu głównego wskazanego repozytorium, utworzenie środowiska `release` i dodanie istniejącego prywatnego PEM jako sekretu `KS_UPDATE_PRIVATE_KEY_PEM`. Klucza prywatnego nie dodaje się przez „Upload files” ani nie wkleja do rozmowy. Poniższa instrukcja generowania dotyczy dopiero przygotowania odrębnego, nowego wdrożenia.

## Uruchomienie przez administratora

1. Utwórz publiczne repozytorium GitHub należące do administracji. Umieść **zawartość folderu projektu** w jego katalogu głównym, tak aby `.github/workflows/release.yml` było bezpośrednio w repozytorium.
2. Zainstaluj PowerShell 7 na komputerze administratora. Jednorazowo uruchom `scripts/New-UpdateSigningKey.ps1`, podając własne pełne ścieżki `-PrivateKeyPath` i `-PublicKeyPath` w osobnym, prywatnym folderze poza projektem. Skrypt nie nadpisuje istniejących plików i nie wypisuje kluczy. Chroń plik prywatny, przygotuj bezpieczną kopię i nigdy nie dodawaj go do Git, ZIP-a, instalatora ani konfiguracji graczy.
3. Ustaw w `config/updates.json` faktyczne `gitHubOwner`, `gitHubRepository` i **wyłącznie publiczny** PEM z wygenerowanego pliku. JSON wymaga zapisania nowych wierszy PEM jako `\n`. Nie wpisuj adresu całej strony ani tokena dostępu. Publiczne wydania nie wymagają tokena na komputerze gracza.
4. W GitHub utwórz środowisko `release`, ogranicz je do zaufanych administratorów i ustaw sekret `KS_UPDATE_PRIVATE_KEY_PEM` z zawartością pliku prywatnego. Nie wklejaj go w źródła workflow. Skrypt publikacji dodatkowo sprawdza zgodność prywatnego klucza z publicznym kluczem zawartym w paczce.
5. Zbuduj i przekaż graczom pierwszą pełną paczkę zawierającą repozytorium i publiczny klucz. Dotychczasowe wersje 0.4 i 0.5 nie mają mechanizmu aktualizacji, więc pierwsze przejście na nową wersję wymaga ręcznego pobrania.

Wszystkie komputery muszą mieć ten sam publiczny klucz i repozytorium w początkowej zaufanej paczce. Zmiana samego repozytorium na stronie internetowej nie konfiguruje istniejących instalacji. Ta wersja narzędzi wymaga, aby prywatny klucz podpisujący odpowiadał publicznemu kluczowi w paczce. Rotacja lub utrata klucza oznacza więc przygotowanie nowej zaufanej paczki i jej ręczne pobranie przez graczy; automatyczna migracja między kluczami nie jest zaimplementowana.

## Wydawanie nowej wersji

W zakładce Actions uruchom „Przygotuj wydanie launchera” i podaj rosnącą wersję SemVer, np. `1.0.1`. Workflow buduje aplikację Windows x64 i samodzielnych pomocników w podfolderach `Updater` oraz `SteamWorker`, uruchamia testy logiki oraz aktualizacji i tworzy **szkic** GitHub Release. Administrator sprawdza paczkę i opis, a potem publikuje szkic. Nie jest wymagane kupowanie hostingu.

Każde wydanie musi zawierać oba pliki o stałych nazwach:

- `KS-Launcher-Windows-x64.zip` — kompletna publikacja, bez dodatkowego folderu nadrzędnego.
- `KS-Launcher-update.json` — podpisany manifest tego konkretnego ZIP-a.

Tag musi odpowiadać wersji manifestu: `v1.0.1` albo `1.0.1`. Nie podmieniaj plików pod istniejącą wersją; wydaj kolejny numer. Launcher pobiera `GET https://api.github.com/repos/{owner}/{repo}/releases/latest`. Ten endpoint pomija szkice i wydania oznaczone przez GitHub jako **pre-release**. Do publicznej aktualizacji opublikuj pełne wydanie i ustaw je jako Latest. Prefiks `-alpha` w samym numerze jest obsługiwany przez porównanie SemVer, lecz zaznaczenie opcji GitHub „pre-release” wyłącza dane wydanie z tego kanału.

Można przygotować pliki ręcznie w PowerShell 7:

```powershell
./scripts/Build.ps1 -DotNetPath 'C:/Program Files/dotnet/dotnet.exe' -Version '1.0.1' -Publish -PublishDirectory 'artifacts/release-app'
./scripts/New-ReleaseUpdate.ps1 -PublishDirectory 'artifacts/release-app' -Version '1.0.1' -PrivateKeyPath 'D:/PrywatneKlucze/ks-update-private.pem' -OutputDirectory 'artifacts/release-assets-1.0.1'
```

Ścieżka klucza powyżej jest przykładem administratora, nie istniejącym kluczem. Wybierz własną. Wydanie tworzone przez workflow nie jest automatycznie podpisywane certyfikatem Authenticode. Podpis manifestu zapewnia zaufanie wewnątrz aktualizatora; kwestie podpisu Windows i SmartScreen opisuje osobno `PODPISYWANIE.md`. Jeśli korzystasz z Authenticode, podpisz EXE **przed** utworzeniem ZIP-a i manifestu, ponieważ późniejsza zmiana plików zmienia SHA-256 paczki.

## Zachowanie na komputerze gracza

Po skonfigurowaniu aplikacja przy uruchomieniu sprawdza wydanie. Nowsza wersja pobierana jest do `%LOCALAPPDATA%/KrainaSurvivalu/Launcher/Updates`, wraz z postępem. Przed rozpakowaniem sprawdzany jest podpis przypiętym publicznym kluczem, rozmiar oraz SHA-256. Nieprawidłowa lub starsza paczka jest odrzucana. Wymiana plików następuje po wybraniu bezpiecznego ponownego uruchomienia launchera. Gra i Steam nie są zamykane ani zabijane.

Pomocnik działa z kopii własnych plików w folderze przygotowanej aktualizacji. Czeka do 90 sekund na zakończenie konkretnej instancji launchera, rozpoznawanej po identyfikatorze, czasie uruchomienia i ścieżce. Zakłada wyłączną blokadę pliku konkretnej instalacji, ponownie sprawdza podpis i ZIP, odczytuje aktualną wersję z instalacji, zachowuje kopie nadpisywanych plików i przy zwykłym błędzie przywraca ich zawartość. Blokada pozostaje aktywna aż do zakończenia wymiany lub przywracania, aby druga instancja nie mieszała plików z innym wydaniem. Nie wymaga uprawnień administratora; docelowy folder musi być zapisywalny dla gracza. Instalacja w chronionym `Program Files` może wymagać przeinstalowania do folderu użytkownika. Ustawienia, nick oraz biblioteki modów znajdują się poza folderem aplikacji i nie są przez pomocnika dotykane.

W razie błędu znajdziesz `apply-error.txt`, a w razie zastosowania `applied.json` i kopię `backup-*` w folderze danej aktualizacji. Kopia pozostaje zachowana, aby umożliwić ręczne odzyskanie. Przywracanie dotyczy zwykłych błędów plikowych; nie jest gwarancją atomowości przy awarii zasilania lub zakończeniu procesu przez system. W takim przypadku rozpakuj poprzednią pełną paczkę do nowego folderu; ustawienia użytkownika zostaną zachowane.

## Ograniczenia i walidacja

Odczyt metadanych ma limit 20 sekund, pobieranie 10 minut. Rozmiar ZIP-a jest ograniczony do 512 MiB; rozpakowane dane do 1,5 GiB i 5000 elementów. Odrzucane są dowiązania, ścieżki absolutne, wyjścia poza folder, duplikaty nazw bez rozróżnienia wielkości liter, specjalne nazwy Windows i nadmierna kompresja. Pobieranie ogranicza się do HTTPS i infrastruktury plików GitHub. Repozytorium i klucz aktualizacji są konfiguracją wydania administratora, a nie polem edytowanym przez gracza.

Testy używają atrap HTTP i jednorazowych lokalnych folderów. Sprawdzają m.in. podpis, manipulację ZIP-em, porównanie wersji, blokadę cofania, bezpieczne ścieżki, anulowanie i przywracanie po błędzie. Samo wpisanie repozytorium i klucza publicznego nie potwierdza aktualizacji: potrzebna jest jeszcze konfiguracja GitHub i próba pomiędzy dwoma opublikowanymi wydaniami.

Dokumentacja źródłowa: [GitHub REST — Releases](https://docs.github.com/en/rest/releases/releases#get-the-latest-release), [oficjalny actions/checkout](https://github.com/actions/checkout), [oficjalny actions/setup-dotnet](https://github.com/actions/setup-dotnet). Wersje workflow zweryfikowano w oficjalnych repozytoriach: checkout v7, setup-dotnet v6.
