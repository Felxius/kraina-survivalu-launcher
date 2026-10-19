# Uruchomienie launchera na Windows

## Gotowa paczka dla gracza

1. Rozpakuj pełną paczkę Windows x64 do zapisywalnego folderu. Zachowaj wszystkie pliki i podfoldery, zwłaszcza `config`, `SteamWorker` i `Updater`.
2. Uruchom `KrainaSurvivalu.Launcher.exe`. Nie wymaga osobnej instalacji .NET — gotowa publikacja zawiera runtime.
3. W **Ustawieniach** wskaż folder z `DayZ_x64.exe` i `DayZ_BE.exe` oraz plik `steam.exe`. Możesz skorzystać z wykrywania instalacji, sprawdzić ścieżki i zapisać ustawienia.
4. Wpisz własny nick. Zapis jest lokalny; przycisk GRAJ i przekazanie nicku do DayZ pozostają niedostępne w etapie 0.6.
5. Uruchom Steam i zaloguj się na konto posiadające DayZ. Steam oraz launcher uruchamiaj jako ten sam użytkownik, z takim samym poziomem uprawnień.

Nie przenoś samego EXE. Brak pomocnika Steam lub aktualizatora jest zgłaszany jako brak plików paczki, a nie sukces operacji.

## Sprawdzanie i pobieranie modów

Wybierz Badlands lub Deer Isle i otwórz stronę modów. Launcher odczyta wymagania tego serwera. Następnie:

- **SPRAWDŹ MODY NA KOMPUTERZE** sprawdza stan w Steam bez zlecania subskrypcji lub pobierania.
- **SYNCHRONIZUJ MODY** subskrybuje rozpoznane pozycje Workshop oraz zleca pobranie brakujących plików i aktualizacji na ten komputer. Wymagane jest połączenie z internetem i wolne miejsce w bibliotece Steam.
- **ANULUJ ZADANIE** kończy pomocnika launchera. Steam może nadal wykonywać pobieranie przyjęte wcześniej do własnej kolejki. Stan tej kolejki jest widoczny w Steam.

Pobieranie nie zaczyna się od samego przeglądania strony. Przed synchronizacją i po niej odczytywana jest świeża lista serwera; zmiany administracji są uzgadniane w ramach ograniczonej liczby prób. Launcher nie usuwa plików ani subskrypcji modów, które zniknęły z listy tego serwera.

Status „gotowy” wymaga potwierdzenia końcowego stanu Steam. Samo przyjęcie pobierania do kolejki nie wystarcza. Mod z ID `0` wymaga informacji od administracji. Nieznane flagi listy serwera, np. `0x08`, pozostawiają kompletność niepotwierdzoną. Można synchronizować rozpoznane ID, lecz nie jest to potwierdzenie pełnej listy potrzebnej do gry. Zobacz [MODY.md](MODY.md).

## Jeśli połączenie ze Steam nie działa

Sprawdź wybraną instalację DayZ i Steam, zalogowane konto, posiadanie DayZ oraz uprawnienia obu procesów. Pomocnik potrzebuje prawidłowej biblioteki Valve `steam_api64.dll` z zainstalowanego DayZ. Nie pobieraj przypadkowych DLL z internetu ani nie kopiuj ich z innego programu. W razie brakujących plików zweryfikuj instalację DayZ w Steam.

Proces pomocnika może nie mieć dostępu do sesji Steam w odizolowanym środowisku testowym. Testy z atrapami oraz sam działający interfejs nie potwierdzają rzeczywistego połączenia i pobierania na komputerze gracza. Wtedy wymagany jest test pełnej paczki w zwykłej sesji Windows.

## Aktualizacja samego launchera

Roboczy projekt zawiera już konfigurację `Felxius/kraina-survivalu-launcher` i publiczny klucz. Dopóki administracja nie wyśle źródeł, nie ustawi sekretu i nie opublikuje pierwszego wydania, sprawdzanie aktualizacji może zgłaszać brak dostępnego wydania. Wcześniejszy ZIP 0.6 pozostaje nieskonfigurowany; nie został podmieniony.

Po przygotowaniu źródła przez administrację launcher przy starcie sprawdzi nową wersję i pobierze ją z pokazaniem postępu. Sprawdzi podpis manifestu, rozmiar oraz SHA-256 paczki. Po pobraniu wybierz **ZAINSTALUJ I URUCHOM PONOWNIE**. Pomocnik poczeka na zamknięcie launchera, wymieni pliki i uruchomi nową wersję. Nie zamknie gry ani Steam. Nick, ustawienia i biblioteki Workshop pozostają poza wymienianymi plikami.

Administratorzy: [konfiguracja i wydawanie aktualizacji](AKTUALIZACJE.md). Przejście z 0.4 lub 0.5 na pierwszą skonfigurowaną paczkę wymaga ręcznego pobrania; te wcześniejsze wersje nie mają aktualizatora.

## Budowanie i instalator

Potrzebne są Windows x64 i .NET 10 SDK. W folderze projektu uruchom:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Publish
```

Otwórz EXE w folderze wskazanym przez skrypt. Ta publikacja zawiera też oba procesy pomocnicze. `dotnet run` samego projektu WPF służy do pracy nad interfejsem i nie przygotowuje pełnego układu plików integracji.

Instalator można wygenerować po zainstalowaniu Inno Setup 6:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Instaluje się dla bieżącego użytkownika, domyślnie w `%LOCALAPPDATA%\Programs\KrainaSurvivalu\Launcher`. Skrypt nie uruchamia instalatora. Gotowy instalator wymaga oddzielnego testu na czystym Windows. Wersja alpha nie ma podpisu Authenticode; podpis manifestu aktualizacji nie zastępuje podpisu Windows i nie gwarantuje wyniku SmartScreen/AV. Szczegóły: [README](../README.md) i [PODPISYWANIE.md](PODPISYWANIE.md).
