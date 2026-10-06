# Graph Report - Omniroute  (2026-10-05)

## Corpus Check
- 71 files · ~40,515 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 10 file(s) not represented in the graph (top: (none) 4, .proto 2, .ico 1)

## Summary
- 1115 nodes · 2112 edges · 62 communities (38 shown, 24 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 91 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `feac83db`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- system
- Page
- DeviceState
- HistoryEntry
- ProtoCodec
- DevicesPage
- SocRing
- EcoflowCloud
- Device
- MonitorService
- TrayIcon
- Protobuf schemas ef_delta3.proto and ef_dp3.proto
- App
- IControl
- AppSettings
- MqttLink
- ToggleControl
- DeviceDetailsPage
- Page
- LocalStore
- DeviceModel
- IDeviceProtocol
- What You Must Do When Invoked
- Credentials
- Repository
- .SyncStationsAsync
- PowerHub
- SliderControl
- graphify reference: extra exports and benchmark
- .Log
- ProtobufProtocolTests
- Check.csproj
- genstubs.py
- Format
- DeltaPro3Protocol
- PowerHub.Tests.csproj
- graphify reference: query, path, explain
- graphify reference: add a URL and watch a folder
- Lightning Bolt Brand Mark (white on green)
- graphify reference: commit hook and native CLAUDE.md integration
- ConnectionState
- graphify reference: incremental update and cluster-only
- Weak grid detection (voltage below 180 V threshold)
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- check.sh script
- session-start.sh
- extraction-spec.md
- JsonProtocolTests
- DeviceStateTests
- DeviceParams
- OutgoingMessage
- NotificationService
- BatteryFlow
- GridStatus
- JsonMessages

## God Nodes (most connected - your core abstractions)
1. `Device` - 67 edges
2. `DeviceState` - 43 edges
3. `DeviceParams` - 39 edges
4. `MonitorService` - 38 edges
5. `Repository` - 33 edges
6. `Page` - 33 edges
7. `Page` - 32 edges
8. `DeviceDetailsPage` - 31 edges
9. `TrayIcon` - 30 edges
10. `HistoryEntry` - 26 edges

## Surprising Connections (you probably didn't know these)
- `ProtoCodec (HeaderMessage frames, XOR pdata, reflection field unwrap, SetCommand)` --shares_data_with--> `Protobuf schemas ef_delta3.proto and ef_dp3.proto`  [INFERRED]
  docs/PROGRESS.md → Protocol/Proto/README.md
- `MonitorService` --references--> `MqttLink`  [EXTRACTED]
  Services/MonitorService.cs → Api/MqttLink.cs
- `App` --inherits--> `Application`  [EXTRACTED]
  App.xaml.cs → App.xaml
- `App` --references--> `Repository`  [EXTRACTED]
  App.xaml.cs → Data/Repository.cs
- `App` --references--> `TrayIcon`  [EXTRACTED]
  App.xaml.cs → Services/TrayIcon.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Omniroute to PowerHub rename and data migration** — claude_omniroute_legacy_name, docs_progress_legacy_folder_migration, claude_localstore_appdata, readme_dpapi_credentials [EXTRACTED 1.00]
- **MQTT monitoring flow (topics, message parsing, polling, reconnect)** — claude_mqtt_topics, docs_progress_mqtt_message_format_fix, docs_progress_latestquotas_polling, docs_progress_reconnect_backoff, claude_fresh_login_per_connect [INFERRED 0.85]
- **Protobuf protocol stack for Delta 3 / Delta Pro 3** — protocol_proto_readme_protobuf_schemas, protocol_proto_readme_pregenerated_csharp, docs_progress_protocodec, readme_supported_models [INFERRED 0.85]
- **UWP/MSIX App Visual Asset Set (lightning bolt branding)** — assets_splashscreen_splashscreen, assets_square150x150logo_square150x150logo, assets_square44x44logo_square44x44logo, assets_storelogo_storelogo, assets_wide310x150logo_wide310x150logo [INFERRED 0.85]

## Communities (62 total, 24 thin omitted)

### Community 0 - "system"
Cohesion: 0.06
Nodes (8): PowerHub.Protocol, PowerHub.Services, PowerHub.Data, PowerHub.Api, PowerHub.Models, PowerHub.Tests, PowerHub.Views, PowerHub

### Community 1 - "Page"
Cohesion: 0.09
Nodes (35): AcInText, AcOutText, CommandErrorBar, ControlsPanel, CyclesText, DcOutText, DeviceModelText, DeviceNameText (+27 more)

### Community 2 - "DeviceState"
Cohesion: 0.12
Nodes (16): DeviceState, AcInVolt, AcInW, AcOutW, BatteryTempC, ChargeRemainMin, Cycles, DcOutW (+8 more)

### Community 3 - "HistoryEntry"
Cohesion: 0.06
Nodes (16): HistoryDbContext, History, HistoryEntry, AcInWatts, BatteryLevel, BatteryWatts, HasAcInput, Id (+8 more)

### Community 4 - "ProtoCodec"
Cohesion: 0.16
Nodes (3): Delta3Protocol, Frame, ProtoCodec

### Community 5 - "DevicesPage"
Cohesion: 0.07
Nodes (18): ConnectionBar, DevicesList, EmptyPanel, Page, StatusText, SyncButton, SyncProgress, TilesGrid (+10 more)

### Community 6 - "SocRing"
Cohesion: 0.06
Nodes (10): SocRing, Animate, IsCharging, IsOnline, RingSize, Soc, SocValue, StrokeWidth (+2 more)

### Community 7 - "EcoflowCloud"
Cohesion: 0.07
Nodes (19): EcoflowCloud, EcoflowException, IsAuthError, MqttCredentials, Session, CloudDevice, EcoflowOpenApi, EmailBox (+11 more)

### Community 8 - "Device"
Cohesion: 0.05
Nodes (35): Device, BatteryLevel, BatteryPercent, BatteryText, CanRename, CardGridAlertText, CardPowerText, CardStatusText (+27 more)

### Community 9 - "MonitorService"
Cohesion: 0.12
Nodes (6): Protocols, MonitorService, IsRunning, State, Status, Snapshot

### Community 10 - "TrayIcon"
Cohesion: 0.15
Nodes (4): NOTIFYICONDATA, POINT, TrayIcon, WNDCLASSEX

### Community 11 - "Protobuf schemas ef_delta3.proto and ef_dp3.proto"
Cohesion: 0.08
Nodes (23): Android PowerHub app (Ecoflow-mon-android, reference implementation), graphify knowledge graph workflow (graphify-out, query before reading), %LocalAppData%\PowerHub data folder (settings.json, history.db, diag.log), EcoFlow MQTT topics (/app/device/property/{sn}, thing/property/get|set, get_reply|set_reply), Omniroute (legacy project name before 0.4.0), PowerHub (WinUI 3 EcoFlow client), Delta 3 offline diagnosis issue, DiagLog diag.log (256 KB, connection, parse failures with hex prefix) (+15 more)

### Community 12 - "App"
Cohesion: 0.10
Nodes (9): Application, App, IsExiting, MainWindow, Repository, RootFrame, Window, MainWindow (+1 more)

### Community 13 - "IControl"
Cohesion: 0.14
Nodes (14): Delta2Family, SolarKeys, Delta2MaxProtocol, SolarKeys, Delta2Protocol, SolarKeys, IControl, Id (+6 more)

### Community 14 - "AppSettings"
Cohesion: 0.08
Nodes (27): AppSettings, AccessKey, CloseToTray, Layout, LowBatteryThreshold, NotificationsEnabled, NotifyOnFullCharge, NotifyOnLowBattery (+19 more)

### Community 16 - "ToggleControl"
Cohesion: 0.09
Nodes (22): ChoiceControl, Command, Id, Label, Optimistic, Options, Read, Section (+14 more)

### Community 17 - "DeviceDetailsPage"
Cohesion: 0.13
Nodes (4): RenameButton, DeviceDetailsPage, SerialNumber, AppBarButton

### Community 18 - "Page"
Cohesion: 0.05
Nodes (40): Grid, DiagLog, AccessKeyBox, AutostartToggle, ClearKeysButton, CloseToTrayToggle, ConnectionBar, DiagText (+32 more)

### Community 19 - "LocalStore"
Cohesion: 0.15
Nodes (4): DeviceStore, LocalStore, Default, SettingsStore

### Community 20 - "DeviceModel"
Cohesion: 0.16
Nodes (11): DeviceModel, Delta2, Delta2Max, Delta3, Delta3Max, Delta3Plus, DeltaMax, DeltaPro3 (+3 more)

### Community 21 - "IDeviceProtocol"
Cohesion: 0.18
Nodes (5): IDeviceProtocol, TopicKind, Data, GetReply, SetReply

### Community 22 - "What You Must Do When Invoked"
Cohesion: 0.08
Nodes (24): For /graphify add and --watch, For /graphify query, For the commit hook and native CLAUDE.md integration, For --update and --cluster-only, /graphify, Honesty Rules, Interpreter guard for subcommands, Part A - Structural extraction for code files (+16 more)

### Community 23 - "Credentials"
Cohesion: 0.19
Nodes (8): CredentialStore, Credentials, AccessKey, ApiHost, Email, HasDeveloperKeys, Password, SecretKey

### Community 24 - "Repository"
Cohesion: 0.18
Nodes (7): Repository, ActiveDevices, Credentials, Devices, HiddenDevices, IsLoggedIn, Settings

### Community 26 - "PowerHub"
Cohesion: 0.18
Nodes (11): net8.0-windows10.0.22621.0, CommunityToolkit.Mvvm (8.3.2), Google.Protobuf (3.28.3), Microsoft.EntityFrameworkCore.Sqlite (8.0.10), Microsoft.Windows.SDK.BuildTools (10.0.22621.3233), Microsoft.WindowsAppSDK (1.5.240802000), MQTTnet (4.3.7.1207), System.Security.Cryptography.ProtectedData (8.0.0) (+3 more)

### Community 27 - "SliderControl"
Cohesion: 0.18
Nodes (11): SliderControl, Command, Id, Label, Max, Min, Optimistic, Read (+3 more)

### Community 28 - "graphify reference: extra exports and benchmark"
Cohesion: 0.22
Nodes (8): graphify reference: extra exports and benchmark, Step 6b - Wiki (only if --wiki flag), Step 7 - Neo4j export (only if --neo4j or --neo4j-push flag), Step 7a - FalkorDB export (only if --falkordb or --falkordb-push flag), Step 7b - SVG export (only if --svg flag), Step 7c - GraphML export (only if --graphml flag), Step 7d - MCP server (only if --mcp flag), Step 8 - Token reduction benchmark (only if total_words > 5000)

### Community 31 - "Check.csproj"
Cohesion: 0.20
Nodes (9): net8.0-windows10.0.22621.0, CommunityToolkit.Mvvm (8.3.2), Google.Protobuf (3.28.3), Microsoft.EntityFrameworkCore.Sqlite (8.0.10), Microsoft.Windows.SDK.BuildTools (10.0.22621.3233), Microsoft.WindowsAppSDK (1.5.240802000), MQTTnet (4.3.7.1207), System.Security.Cryptography.ProtectedData (8.0.0) (+1 more)

### Community 35 - "PowerHub.Tests.csproj"
Cohesion: 0.29
Nodes (6): net8.0, Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), Google.Protobuf (3.28.3), Microsoft.NET.Sdk

### Community 36 - "graphify reference: query, path, explain"
Cohesion: 0.33
Nodes (5): For /graphify explain, For /graphify path, graphify reference: query, path, explain, Step 0 — Constrained query expansion (REQUIRED before traversal), Step 1 — Traversal

### Community 37 - "graphify reference: add a URL and watch a folder"
Cohesion: 0.50
Nodes (3): For /graphify add, For --watch, graphify reference: add a URL and watch a folder

### Community 38 - "Lightning Bolt Brand Mark (white on green)"
Cohesion: 0.33
Nodes (6): Lightning Bolt Brand Mark (white on green), SplashScreen (green background, white lightning bolt), Square150x150Logo (medium tile), Square44x44Logo (app list / taskbar icon), StoreLogo (package/store icon), Wide310x150Logo (wide tile)

### Community 39 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 40 - "ConnectionState"
Cohesion: 0.18
Nodes (6): ConnectionState, Connected, Connecting, Failed, Reconnecting, Stopped

### Community 41 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 42 - "Weak grid detection (voltage below 180 V threshold)"
Cohesion: 0.50
Nodes (4): Background mode: tray icon, close-to-tray, autostart, Battery state by energy flow (charging/discharging/idle/full), Weak grid detection (voltage below 180 V threshold), Windows notifications (power lost/restored, weak voltage, low/full charge, offline)

### Community 57 - "BatteryFlow"
Cohesion: 0.33
Nodes (5): BatteryFlow, Charging, Discharging, Full, Unknown

### Community 58 - "GridStatus"
Cohesion: 0.29
Nodes (5): DeviceStateLogic, GridStatus, None, Ok, Weak

## Knowledge Gaps
- **278 isolated node(s):** `session-start.sh script`, `IsAuthError`, `IsConnected`, `MainWindow`, `Repository` (+273 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 449 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **24 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Device` connect `Device` to `system`, `DeviceState`, `NotificationService`, `DevicesPage`, `SocRing`, `MonitorService`, `DeviceDetailsPage`, `LocalStore`, `DeviceModel`, `.SliderRow`, `Repository`, `BatteryFlow`, `GridStatus`?**
  _High betweenness centrality (0.191) - this node is a cross-community bridge._
- **Why does `Repository` connect `Repository` to `system`, `Device`, `App`, `.SaveSettings`, `AppSettings`, `LocalStore`, `DeviceModel`, `Credentials`, `.SyncStationsAsync`, `.GetHistoryAsync`?**
  _High betweenness centrality (0.105) - this node is a cross-community bridge._
- **Why does `DeviceDetailsPage` connect `DeviceDetailsPage` to `system`, `Page`, `Format`, `EcoflowCloud`, `Device`, `DeviceParams`, `.SliderRow`?**
  _High betweenness centrality (0.100) - this node is a cross-community bridge._
- **Are the 14 inferred relationships involving `DeviceState` (e.g. with `.Charging_UsesOnlyChargeEstimate()` and `.Discharging_UsesOnlyDischargeEstimate()`) actually correct?**
  _`DeviceState` has 14 INFERRED edges - model-reasoned connections that need verification._
- **Are the 6 inferred relationships involving `DeviceParams` (e.g. with `.Delta2AcToggle_BuildsDocumentedCommand()` and `.Delta2WithoutGridVoltage_HasNoGrid()`) actually correct?**
  _`DeviceParams` has 6 INFERRED edges - model-reasoned connections that need verification._
- **What connects `session-start.sh script`, `IsAuthError`, `IsConnected` to the rest of the system?**
  _278 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `system` be split into smaller, more focused modules?**
  _Cohesion score 0.06091825307950728 - nodes in this community are weakly interconnected._