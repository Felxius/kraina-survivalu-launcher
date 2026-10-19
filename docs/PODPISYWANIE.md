# Podpis wydawcy i komunikaty Windows

Obecna wersja alpha nie ma podpisu wydawcy Authenticode. Zmiana ikony lub dodanie podpisu nie daje gwarancji braku komunikatów antywirusa.

Authenticode potwierdza tożsamość wydawcy i integralność podpisanego pliku. SmartScreen osobno ocenia reputację pliku oraz wydawcy, więc także nowy prawidłowo podpisany program może otrzymać ostrzeżenie. Microsoft podaje, że certyfikaty EV nie zapewniają już natychmiastowego pominięcia SmartScreen. [Dokumentacja reputacji SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation), [opcje podpisywania](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

## Opcjonalne podpisanie wydania

Potrzebny jest prawdziwy certyfikat Code Signing z dostępnym kluczem prywatnym w magazynie `CurrentUser\My` lub `LocalMachine\My`, SignTool z Windows SDK oraz adres **HTTPS serwera RFC 3161** od dostawcy certyfikatu. Projekt nie tworzy certyfikatu, nie importuje kluczy i nie wybiera samodzielnie usługi podpisywania.

`scripts/Sign-Release.ps1` działa tylko po jawnym wskazaniu plików, certyfikatu i TSA. Wybiera certyfikat po jego thumbprincie, podpisuje SHA-256, dodaje timestamp SHA-256 i sprawdza wynik przez `signtool verify /pa /all /tw` oraz `Get-AuthenticodeSignature`. Odrzuca certyfikaty samopodpisane, nieważne, bez klucza lub EKU Code Signing oraz pliki mające już podpis. Nie podpisuje bibliotek Microsoft ani nie wykonuje zgłoszeń do skanerów AV. [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool).

```powershell
$tool = Read-Host 'Pełna ścieżka do signtool.exe z Windows SDK'
$thumb = Read-Host '40-znakowy thumbprint certyfikatu Code Signing'
$tsa = Read-Host 'Adres HTTPS serwera RFC 3161 dostawcy certyfikatu'
.\scripts\Sign-Release.ps1 -FilePath 'C:\Wydania\Launcher\KrainaSurvivalu.Launcher.exe' -SignToolPath $tool -CertificateThumbprint $thumb -TimestampUrl $tsa
```

Thumbprint SHA-1 identyfikuje certyfikat; algorytm podpisu pliku nadal jest SHA-256. Opcja `-CertificateStoreLocation LocalMachine` jawnie wybiera magazyn komputera. `-WhatIf` pokazuje zamiar operacji po walidacji parametrów, bez podpisywania. Wynik podpisywania jest zapisywany w nowym raporcie `artifacts/signing`; błąd może pozostawić częściowo podpisany zestaw i nie oznacza gotowego wydania.

Podpisz własne EXE/DLL **przed** końcowym testem i utworzeniem odcisku publikacji. Instalator podpisuje się osobno po jego skompilowaniu. Podpis zmienia bajty pliku — wcześniej policzone SHA i dowody dotyczące niepodpisanej paczki wymagają ponownego wykonania. Skrypt nie jest automatycznie wywoływany przez build ani instalator.

## Jeżeli pojawi się wykrycie

Zapisz nazwę produktu, nazwę detekcji, wersję definicji i SHA-256 konkretnego pliku. Sprawdź, czy badano właściwą paczkę. Błędne wykrycie Defendera można zgłosić Microsoft jako producent oprogramowania, według [procedury Microsoft](https://learn.microsoft.com/en-us/defender-xdr/developer-faq). Microsoft nie oferuje deweloperom programu gwarantującego brak przyszłych fałszywych wykryć. Taki wniosek o analizę pliku jest odrębny od reputacji SmartScreen.

W ramach przygotowania tego projektu niczego nie wysyłano do Microsoft ani chmurowych skanerów i nie wykonywano podpisywania. Decyzja o przekazaniu konkretnego pliku do analizy należy do właściciela wydania.
