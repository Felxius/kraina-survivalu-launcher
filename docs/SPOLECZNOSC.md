# Treści społeczności — instrukcja moderatora

Launcher 0.5 czyta regulamin i adres Discorda z lokalnego pliku `config/community.json` znajdującego się obok aplikacji. Zmiana regulaminu nie wymaga kompilowania kodu C# ani WPF. Odświeżanie ponownie odczytuje ten plik, bez pobierania treści z serwera HTTPS.

W konfiguracji wydania pozostaje Discord `https://discord.gg/qEwYQqj7MD`, pełny regulamin przekazany przez właściciela w dziewięciu rozdziałach i `news: []`. Zakładka aktualności została usunięta na życzenie właściciela. Nie ma edytora aktualności ani hostowanego feeda.

## Jak zmienić treść

1. Zachowaj kopię obecnego `config/community.json` poza folderem aplikacji.
2. Edytuj kopię pliku w edytorze zapisującym UTF-8. Zmieniaj tylko zatwierdzone treści regulaminu i zachowaj wszystkie jego rozdziały.
3. Zachowaj nazwy pól i składnię JSON. Tekst w cudzysłowach wymaga zapisania dosłownego cudzysłowu jako `\"`, a nowego wiersza jako `\n`. JSON nie obsługuje komentarzy ani przecinka za ostatnim elementem.
4. Zastąp lokalny plik ukończoną kopią, a w aplikacji wybierz odświeżanie społeczności. Nie pozostawiaj częściowo zapisanego JSON-a w używanym pliku.
5. Sprawdź tytuły, podziały wierszy i rozdziały w launcherze. Przy błędzie wróć do kopii prawidłowego pliku i odśwież ponownie.

Ta zmiana dotyczy tylko komputera, na którym podmieniono plik. Aby udostępnić regulamin innym graczom, trzeba dostarczyć im aktualną konfigurację lub nową paczkę launchera. Automatyczne aktualizacje launchera nie są jeszcze zaimplementowane. Przed podmianą paczki zachowaj własną kopię regulaminu.

## Format community.json

Nazwy pól są zapisane w `camelCase`. Nieznane pola i duplikaty nazw pól JSON są odrzucane. Cały plik może mieć najwyżej 1 MiB. Wymagane pola tekstowe muszą zawierać treść; same spacje lub nowe wiersze nie wystarczą.

| Pole główne | Znaczenie |
| --- | --- |
| `schemaVersion` | Wymagane `1` |
| `discordUrl` | Zatwierdzony link zaproszenia Discord lub `null` |
| `rulesUrl` | Opcjonalny zewnętrzny adres HTTPS regulaminu albo `null`; nie zastępuje lokalnych rozdziałów |
| `newsUrl` | Pole zachowane dla zgodności modelu; w wydaniu pozostaje `null` |
| `rules` | `{ "title": "…", "sections": [ … ] }` albo `null` |
| `news` | Pole zachowane dla zgodności; w wydaniu pozostaje `[]`, bez widoku aktualności |

Brak pola `rules` oznacza brak regulaminu. W wydaniu Krainy Survivalu regulamin jest dostarczony — nie usuwaj go przypadkowo. Historyczny model wpisów pozostaje zgodny z wcześniejszą wersją, lecz nie jest udostępniony w interfejsie 0.5.

### Regulamin

Obiekt `rules` ma `title` i `sections`. Każdy rozdział zawiera:

| Pole rozdziału | Wymagania |
| --- | --- |
| `id` | 1–80 znaków ASCII: litery, cyfry, `_` lub `-`; unikalny bez rozróżniania wielkości liter |
| `title` | 1–200 znaków, bez znaków sterujących |
| `body` | Niepusta treść rozdziału, najwyżej 32 000 znaków; dopuszczone nowe wiersze i tabulatory |

Lista może mieć od 1 do 64 rozdziałów. W aktualnej konfiguracji oficjalnej jest **dziewięć rozdziałów przekazanego regulaminu**. Zachowaj ich kolejność i brzmienie; walidacja formatu nie sprawdza zgodności zasad z dokumentem właściciela.

### Adresy zewnętrzne i błędy

Adresy muszą być pełnymi URL-ami HTTPS, do 2048 znaków, bez loginu, hasła, znaków sterujących i ukośników odwrotnych. Zaproszenie Discord musi mieć postać `https://discord.gg/<kod>` albo `https://discord.com/invite/<kod>`, z domyślnym portem HTTPS, bez parametrów zapytania i fragmentu. Kod zaproszenia może mieć 1–100 liter ASCII, cyfr lub znaków `-`.

Niepoprawna struktura, przekroczony limit, duplikat identyfikatora, niepoprawna data lub niedozwolony URL powodują odrzucenie pliku. Błędu dostępu do pliku nie należy traktować jako potwierdzonej pustej listy. Aplikacja sygnalizuje problem, a moderator powinien przywrócić prawidłową kopię — nie tworzy się automatycznie zastępczego regulaminu ani aktualności.
