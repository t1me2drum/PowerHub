# PowerHub — контекст для Claude

WinUI 3 клієнт для Windows 11 для моніторингу й керування зарядними станціями EcoFlow —
Windows-версія Android-застосунку PowerHub: https://github.com/t1me2drum/Ecoflow-mon-android
(коли логіка протоколів неясна, еталоном є він).
Репозиторій: https://github.com/t1me2drum/PowerHub. До версії 0.4.0 проєкт звався Omniroute (репозиторій, простір імен, exe);
дані зі старої папки `%LocalAppData%\Omniroute` переносяться при першому запуску (`LocalStore.MigrateLegacyFolder`), старий запис автозапуску прибирає `Autostart`.

## Стек
- .NET 8, `net8.0-windows10.0.22621.0`, WinUI 3 / Windows App SDK 1.5, платформи x64 та ARM64
- Збирається як звичайний .exe без MSIX (`WindowsPackageType=None`) з вбудованим Windows App SDK (`WindowsAppSDKSelfContained`): не потрібні сертифікат, режим розробника і встановлений Windows App Runtime
- MQTTnet 4.3.x (телеметрія й команди), EF Core Sqlite (історія), Google.Protobuf, CommunityToolkit.Mvvm
- UI українською, коментарі в коді українською

## Збирання
```bash
dotnet build -c Release -p:Platform=x64
```
Результат: `bin/x64/Release/net8.0-windows10.0.22621.0/win-x64/PowerHub.exe` (запускати разом з усією папкою).
Дані застосунку лежать у `%LocalAppData%\PowerHub\`: `settings.json` (налаштування, станції, зашифровані облікові дані), `history.db`, `diag.log` (журнал діагностики, як DiagLog в Android: з'єднання, збої розбору з hex-префіксом кадру, невдалі команди, падіння).
Через те що пакета немає, `ApplicationData`/`Windows.Storage` не використовуємо, лише `Data/LocalStore`.

### Реліз
- `.github/workflows/release.yml` (GitHub Actions, windows-latest): тести, `dotnet build`, архів `PowerHub-<версія>-win-x64.zip` (папка `PowerHub` без `.pdb`), SHA-256 у описі, публікація на GitHub Releases
- Запуск: пуш тега `vX.Y.Z` або вручну — Actions → Release → Run workflow (версія без «v»). Перед цим: `<Version>` у `PowerHub.csproj` = версія релізу і є опис `docs/release-notes/<версія>.md`, інакше workflow зупиниться
- З хмарної сесії теги не пушаться — запускати вручну (workflow_dispatch), тег створить сам workflow

### Перевірка без Windows (Linux, хмарні сесії Claude)
- `dotnet test tests/PowerHub.Tests` — тести протоколів, логіки стану й форматування (перенесені з Android `app/src/test`); компілюють лише `Protocol/`, `Models/DeviceModel.cs`, `Models/Format.cs`
- `tools/check-build/check.sh` — компілює весь C# проти справжніх збірок Windows App SDK; XAML-компілятор на Linux не працює, тому `genstubs.py` генерує заглушки `x:Name`/`InitializeComponent` і перевіряє обробники подій та шляхи `x:Bind`. Успіх — `Check -> …PowerHubCheck.dll` без `error CS` (MSB4062 після нього очікувані). Саму розмітку XAML перевіряє лише збирання на Windows
- .NET 8 SDK на Ubuntu: `apt-get install dotnet-sdk-8.0` (dot.net через проксі недоступний)

## Структура
- `Api/` — `EcoflowCloud` (вхід, REST), `EcoflowOpenApi` (Developer API з підписом HMAC), `MqttLink` (MQTT-клієнт з власним перепідключенням)
- `Protocol/` — `IDeviceProtocol`, реєстр `Protocols.For(model)` і протоколи, перенесені з Android-версії один в один:
  `Delta2Family` → `Delta2Protocol`, `Delta2MaxProtocol`, `DeltaMaxProtocol`, `River2MaxProtocol` (JSON); `Delta3Protocol` (Standard/Max) і `DeltaPro3Protocol` (protobuf через `ProtoCodec`). Схеми — `Protocol/Proto/*.proto`, C#-код із них заздалегідь згенеровано в `Protocol/Proto/Generated` (див. README там)
- `Data/` — `Repository` (потокобезпечний, подія `DevicesChanged`, синхронізація станцій: назви станцій з акаунта завжди беруться звідти, перейменувати в PowerHub можна лише додані вручну, `Device.CanRename`; `MonitorService` перечитує список кожні 10 хв), `CredentialStore` (DPAPI: email, пароль, сервер, ключі Developer API), `DeviceStore`, `SettingsStore`, `HistoryDbContext` (SQLite, новий контекст на кожну операцію)
- `Services/` — `MonitorService` (з'єднання, телеметрія, історія, статус, `SendAsync` для команд, `Summary` для трею), `NotificationService` (правила `Evaluate` як AlertEngine в Android, натискання відкриває станцію), `DiagLog`, `TrayIcon` (Win32 `Shell_NotifyIcon` без сторонніх пакетів), `Autostart` (HKCU Run, ключ `--background`)
- `Models/` — `Device` (телеметрія лише в пам'яті, `UpdateTelemetry` з UI-потоку, готові рядки для карток), `DeviceModel`, `Format` (Вт/кВт, хвилини, текст стану батареї); логіка стану (`GridStatus`, `BatteryFlow`, `IsChargingFromGrid`) — `Protocol/DeviceStateLogic.cs`
- `Views/` — Login, Devices (два вигляди за `AppSettings.Layout`: компактні картки списком або дашборд плиток `StationTile`; сортування, перетягування), DeviceDetails (вкладки Огляд / Керування / Графіки / Дані), Settings; елементи без XAML: `SocRing` (кільце заряду з анімацією), `HistoryPanel` (графіки), `Ui` (діалоги, кольори). Логіка в code-behind; `ViewModels/` порожня
- Застосунок один на користувача (Mutex + подія активації); закриття вікна ховає в трей, якщо не вимкнено в налаштуваннях

## Як влаштований моніторинг
- MQTT-топіки станції: `/app/device/property/{sn}` (телеметрія), `/app/{userId}/{sn}/thing/property/get|set` (запити) і `…/get_reply|set_reply`
- Формат JSON: телеметрія у `params`, відповідь `latestQuotas` у `data.quotaMap`
- Потоки: MQTT-колбеки й таймер працюють у фонових потоках; властивості `Device` (INotifyPropertyChanged, прив'язані до UI) змінюються **тільки** через DispatcherQueue в UI-потоці
- Токен і облікові дані MQTT не зберігаються: при кожному підключенні виконується новий вхід

## Домовленості
- Протокол кожної моделі реалізує окремий клас `IDeviceProtocol`; модель визначається за префіксом серійного номера (таблиця в README)
- Облікові дані не логувати й не комітити. `*.db` і `*.pfx` мають бути в `.gitignore`
- Прогрес і наступні кроки ведемо в `docs/PROGRESS.md`. Наприкінці сесії треба оновити його і зробити коміт із пушем

## Робота на кількох комп'ютерах
Історія сесій Claude між комп'ютерами не синхронізується, тому весь контекст проєкту має бути в цьому файлі та в `docs/PROGRESS.md`.

## Граф знань (graphify)
- `graphify-out/` лежить у репозиторії: `graph.json`, `graph.html` (відкривається в браузері), `GRAPH_REPORT.md`, семантичний кеш документів
- На питання про код спершу `graphify query "<питання>"` (вузли названо англійською, як у коді: запит українською часто нічого не знаходить), зв'язки — `graphify path "<A>" "<B>"`, окреме поняття — `graphify explain "<поняття>"`. Вони повертають вузький підграф, менший за `GRAPH_REPORT.md` чи вивід grep
- Якщо є `graphify-out/wiki/index.md` — ним користуватися для загальної навігації замість перегляду сирих файлів
- `GRAPH_REPORT.md` читати лише для огляду архітектури або коли query/path/explain не дали достатньо контексту
- Після змін у коді: `graphify update .` (лише AST, без LLM); повне оновлення з LLM — `/graphify . --update`. Згенерований protoc-код виключено через `.graphifyignore`
- Хуки `PreToolUse` у `.claude/settings.json` (`graphify hook-guard search|read`) нагадують про це перед Grep/Read; вони нічого не блокують. Команда `graphify` має бути в PATH
- Інструмент — PyPI-пакет `graphifyy` 0.9.72 (на ПК встановлено через uv), скіл `/graphify` лежить у репозиторії: `.claude/skills/graphify` (копія того, що ставить `graphify install --platform claude`). Оновлюючи версію пакета, оновити й скіл
- Хмарні сесії: `.claude/hooks/session-start.sh` (зареєстровано в `.claude/settings.json`, працює лише при `CLAUDE_CODE_REMOTE=true`) ставить `graphifyy`, .NET 8 SDK і відновлює пакети тестів
