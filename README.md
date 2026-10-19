# Kraina Survivalu Launcher

Launcher Windows dla serwerów DayZ Kraina Survivalu. C#, .NET 10, WPF; etap `0.6.0-alpha`.

Wersja 0.6 dodaje sprawdzanie i synchronizację modów Steam Workshop na komputerze gracza oraz przygotowany mechanizm aktualizacji launchera przez GitHub Releases. Każdy serwer ma własną listę odczytywaną na bieżąco. **GRAJ i uruchamianie DayZ pozostają niedostępne** w tym etapie.

Interfejs zachowuje ciemny, oliwkowy wygląd, ikonę, ilustracje serwerów, lokalny nick, ustawienia ścieżek, Discord i oryginalny regulamin w dziewięciu rozdziałach. Aktualności nie mają zakładki, edytora ani hostowanego feeda.

| Serwer | Adres gry | Steam Query |
| --- | --- | --- |
| Badlands | `145.239.237.112:27800` | `145.239.237.112:27801` |
| Deer Isle | `178.95.158.100:23000` | `178.95.158.100:9000` |

Porty pochodzą od administracji; nie wyliczamy jednych z drugich. Brak odpowiedzi oznacza brak bieżących danych, a nie potwierdzone wyłączenie serwera. Liczba graczy pozostaje wtedy nieznana. Zobacz [status serwerów](docs/STATUS-SERWEROW.md).

Discord: [Kraina Survivalu](https://discord.gg/qEwYQqj7MD). Przycisk otwiera zaproszenie w domyślnej przeglądarce. Regulamin jest dostępny w launcherze ze spisem treści i filtrem.

## Uruchomienie na Windows

Rozpakuj **cały folder** publikacji Windows x64, zachowując `config`, `SteamWorker`, `Updater` i dołączone biblioteki, a następnie uruchom `KrainaSurvivalu.Launcher.exe`. Publikacja self-contained zawiera .NET i WPF; gracz nie musi osobno instalować .NET. Sam plik EXE nie wystarcza.

W **Ustawieniach** wskaż folder DayZ zawierający `DayZ_x64.exe` i `DayZ_BE.exe` oraz właściwy plik `steam.exe`. Uruchom Steam i zaloguj się na konto posiadające DayZ. Steam i launcher powinny działać jako ten sam użytkownik, na tym samym poziomie uprawnień. Kroki i rozwiązywanie problemów opisuje [URUCHOMIENIE.md](docs/URUCHOMIENIE.md).

Nick zostaje zapisany lokalnie; nie jest jeszcze przekazywany do gry. Może mieć 1–32 znaki Unicode: litery, cyfry, znaki diakrytyczne, zwykłe spacje wewnętrzne i `_ - [ ] ( )`. `Survivor`, cudzysłowy i znaki sterujące są odrzucane.

## Mody na komputerze gracza

Na stronie modów wybranego serwera:

- **SPRAWDŹ MODY NA KOMPUTERZE** odczytuje lokalny stan. Nie zleca subskrypcji ani pobierania.
- **SYNCHRONIZUJ MODY** zleca Steam pobranie brakujących modów i aktualizacji, pokazując stan oraz postęp. Pliki trafiają do biblioteki Workshop gracza.
- **ANULUJ ZADANIE** kończy zadanie pomocnika launchera. Pobieranie już przyjęte do kolejki Steam może nadal działać; launcher nie zatrzymuje globalnej kolejki Steam.

Samo otwarcie strony nie uruchamia pobierania. Przed zadaniem odczytywana jest aktualna lista serwera; po synchronizacji jest sprawdzana ponownie. Jeśli administracja doda mody podczas pobierania, zadanie uzupełnia listę w ograniczonej liczbie prób. Usunięte pozycje przestają należeć do wymagań tego serwera. Launcher nie kasuje ich plików ani nie anuluje subskrypcji, ponieważ mogą służyć innym serwerom.

Rozpoczęcie pobierania nie oznacza gotowości: kod czeka na właściwe wyniki Steam i potwierdza końcowy stan plików. ID `0` pozostaje nierozpoznane, a nieznane flagi odpowiedzi, w tym `0x08`, nie dostają zgadywanego znaczenia. Rozpoznane ID można synchronizować, lecz kompletność listy nadal wymaga potwierdzenia. Szczegóły: [MODY.md](docs/MODY.md).

Pomocnik korzysta z biblioteki Valve z zainstalowanego DayZ. Paczka nie zawiera cudzej `steam_api64.dll`, nie wymaga hasła Steam ani nie zmienia manifestów Workshop ręcznie. Test wykonany 19 października 2026 w zwykłej sesji Windows potwierdził połączenie i odczyt zainstalowanego moda CF, bez zmiany manifestu Workshop. Nie był to test pobierania ani aktualizacji plików; testy z atrapami również nie dowodzą rzeczywistego pobrania.

## Aktualizacje launchera

Przygotowano GitHub Releases: sprawdzanie nowej wersji, pobieranie, podpis manifestu ECDSA P-256, kontrolę rozmiaru i SHA-256 oraz pomocnika wymiany plików z kopią zapasową. Po skonfigurowaniu źródła launcher automatycznie sprawdza i pobiera nowszą wersję. Instalację uruchamia gracz przyciskiem **ZAINSTALUJ I URUCHOM PONOWNIE**; gra i Steam nie są zamykane.

W konfiguracji roboczego projektu wskazano `Felxius/kraina-survivalu-launcher` oraz wygenerowany publiczny klucz aktualizacji. Klucz prywatny pozostaje poza projektem. Źródła trzeba jeszcze przesłać, skonfigurować sekret wydawania i przygotować pierwsze wydanie; niczego dotąd nie opublikowano. Wcześniejszy ZIP `0.6.0-alpha-Windows-x64` zachowuje swoją pustą konfigurację i wymaga później ręcznego przejścia na pierwszą skonfigurowaną paczkę. Instrukcje: [AKTUALIZACJE.md](docs/AKTUALIZACJE.md).

Podpis manifestu aktualizacji i podpis wydawcy Windows to różne mechanizmy. Alpha nie ma podpisu Authenticode; nie ma gwarancji braku ostrzeżeń SmartScreen lub antywirusa. Zobacz [PODPISYWANIE.md](docs/PODPISYWANIE.md).

## Budowanie ze źródeł

Potrzebujesz Windows x64 ze wsparciem dla .NET 10 oraz **.NET 10 SDK**. WPF wymaga Windows. SDK zainstaluj według [instrukcji Microsoft](https://learn.microsoft.com/en-us/dotnet/core/install/windows); obsługiwane systemy są opisane w [wykazie .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

W PowerShell w folderze projektu:

```powershell
dotnet --list-sdks
dotnet --version
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

`dotnet --version` powinien wskazać stabilne `10.0.x`. Skrypt buduje aplikację i pomocnika Steam oraz uruchamia testy logiki i aktualizacji. Nie uruchamia DayZ, pobierania modów ani instalatora.

Do pracy nad samym interfejsem:

```powershell
dotnet run --project .\src\KrainaSurvivalu.Launcher.App\KrainaSurvivalu.Launcher.App.csproj
```

Uruchomienie projektu WPF nie tworzy automatycznie kompletnego folderu z pomocnikami. Do integracji Steam i aktualizatora użyj pełnej publikacji:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Publish
```

Skrypt poda świeży folder publikacji. Zawiera aplikację, runtime, trzy pliki `config/servers.json`, `config/community.json`, `config/updates.json` i osobne foldery `SteamWorker` oraz `Updater`, również self-contained. WPF publikujemy bez trimming. Więcej: [publikacja .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/) i [ograniczenie trimming dla WPF](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities#wpf).

Opcjonalne parametry: `-DotNetPath 'C:\Narzedzia\dotnet10\dotnet.exe'`, `-PublishDirectory 'C:\Wydania\Launcher-0.6'`, `-Version '0.6.0-alpha'`, `-RestoreConfigFile 'C:\Narzedzia\NuGet.Offline.Config'`. Folder publikacji musi być pusty. Ścieżki ze spacjami zapisuj w cudzysłowach. Skrypty obsługują też `DOTNET_ROOT` i SDK z `PATH`.

## Testy i granice weryfikacji

Testy konsolowe:

```powershell
dotnet run --project .\tests\KrainaSurvivalu.Launcher.Core.Tests\KrainaSurvivalu.Launcher.Core.Tests.csproj --configuration Release
dotnet run --project .\tests\KrainaSurvivalu.Launcher.Updater.Tests\KrainaSurvivalu.Launcher.Updater.Tests.csproj --configuration Release
```

Kontrolują m.in. profile i ścieżki, błędne odpowiedzi Query, zmiany list modów podczas zadania, wyniki Steam, podpis i integralność aktualizacji, bezpieczne rozpakowanie oraz przywracanie po błędzie. Testy sieciowe i Workshop korzystają z kontrolowanych odpowiedzi; nie subskrybują rzeczywistych modów. Wynik inny niż zero oznacza niepowodzenie.

Test WPF renderuje okno i sprawdza zachowanie z atrapami usług:

```powershell
dotnet run --project .\src\KrainaSurvivalu.Launcher.App\KrainaSurvivalu.Launcher.App.csproj -- --ui-smoke '.\artifacts\ui-smoke'
```

Wymaga sesji Windows z działającym pulpitem. Wyniki 0.6 należy odczytać z raportów danego wydania; wyniki 0.5 nie są weryfikacją 0.6. Rzeczywisty odczyt z zalogowanego Steam został sprawdzony osobno; pobieranie pozostaje testem ręcznym. Produkcyjny odczyt UDP potwierdza odpowiedź tylko w chwili wykonania. Instalator i aktualizacja między opublikowanymi wydaniami wymagają oddzielnego sprawdzenia. Zobacz [TESTOWANIE.md](docs/TESTOWANIE.md).

## Wygenerowanie instalatora EXE

Zainstaluj [Inno Setup 6](https://jrsoftware.org/isinfo.php) i podaj rzeczywistą ścieżkę do kompilatora:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Skrypt buduje, testuje, publikuje pełny folder i kompiluje `installer/setup.iss`. Nie uruchamia instalatora. Wynik dla 0.6:

```text
artifacts\installer\KrainaSurvivalu-Launcher-Setup-0.6.0-alpha-win-x64.exe
artifacts\installer\KrainaSurvivalu-Launcher-Setup-0.6.0-alpha-win-x64.exe.sha256
```

Instalator działa dla bieżącego użytkownika bez wymagania administratora, domyślnie w `%LOCALAPPDATA%\Programs\KrainaSurvivalu\Launcher`, i dostarcza skrót Start oraz dezinstalator. Skrót pulpitu jest opcjonalny. Stały AppId pozwala zachować ustawienia przy ponownej instalacji. Więcej: [uprawnienia Inno Setup](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) i [kompilator](https://jrsoftware.org/ishelp/topic_compilercmdline.htm). Przygotowany skrypt nie oznacza, że instalator tego wydania został już skompilowany i sprawdzony na czystym Windows.

## Dane projektu

| Ścieżka | Zawartość |
| --- | --- |
| `config/servers.json` | Rzeczywiste adresy i porty obu serwerów |
| `config/community.json` | Discord i regulamin; historyczny model news zachowany |
| `config/updates.json` | Repozytorium Felxius/kraina-survivalu-launcher i publiczny klucz aktualizacji |
| `src/KrainaSurvivalu.Launcher.App/` | Interfejs WPF i obsługa pulpitu |
| `src/KrainaSurvivalu.Launcher.Core/` | Logika, walidacja, Query, synchronizacja i aktualizacje |
| `src/KrainaSurvivalu.SteamWorker/` | Osobny proces integracji Steam |
| `src/KrainaSurvivalu.Launcher.Updater/` | Wymiana plików po zamknięciu launchera |
| `tests/` | Testy konsolowe |
| `scripts/` | Budowanie, instalator i przygotowanie wydania |
| `docs/ETAPY.md` | Stan funkcji i pozostałe etapy |

Ilustracje serwerów są oryginalnymi grafikami promocyjnymi wygenerowanymi przez AI, a nie zrzutami z serwerów. Ikona i obrazy są zasobami aplikacji.

Ustawienia zapisują się w `%LOCALAPPDATA%\KrainaSurvivalu\Launcher\settings.json`, poza instalacją. Błędny JSON zachowuje się jako `settings.json.invalid-<identyfikator>.bak`. Dezinstalator nie usuwa ustawień. Aktualizator nie ingeruje w nick ani bibliotekę modów.

`requiredWorkshopIds: null` oznacza brak zatwierdzonej listy statycznej, a nie pustą listę. Odczytane wymagania są przechowywane osobno dla obu serwerów. Regulamin można zmienić lokalnie według [instrukcji moderatora](docs/SPOLECZNOSC.md); odświeżenie pliku nie rozsyła treści do innych graczy. Pole `news` zachowano dla zgodności danych; aktualności nie wracają do interfejsu.
