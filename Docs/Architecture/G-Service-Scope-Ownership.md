# G: service contracts and scope ownership

Статус: действующий контракт реализации `G`.

`G` публикует committed resolver Global scope и выдаёт сервисы только по интерфейсу. Concrete-типы остаются деталями bootstrap, scene composition и NGO spawn lifecycle.

## Иерархия scopes

```text
Global (ProjectContext: Ready -> Dispose)
└── Session (TryOpenSessionScope -> close after NGO stop)
    ├── Scene[Scene.handle] (Install -> Uninstall/Dispose)
    ├── Player[NetworkObjectId] (OnNetworkSpawn -> OnNetworkDespawn)
    │   └── Local (только IsClient && IsOwner)
    └── Scene[другой Scene.handle]
```

- `Global` создаётся bootstrap-ом один раз и живёт до `ProjectContext.Dispose`.
- `Session` открывается синхронно через `NetworkSessionShutdownCoordinator.TryOpenSessionScope`. Закрытие происходит ровно один раз после остановки NGO client/server; только затем отправляется `SessionStopped`.
- `Scene` принадлежит `SceneRuntimeScopeRegistry` и идентифицируется только `Scene.handle`. Game shell и additive Map имеют независимые scopes.
- `Player` принадлежит конкретному spawned player `NetworkObject`; `NetworkObjectId` допустим только внутри активной session.
- `Local` является child конкретного Player scope и создаётся только для player object, которым владеет локальный client.

## Ownership table

| Scope | Contract | Текущая реализация | Регистрация | Удаление | Multiplayer authority |
|---|---|---|---|---|---|
| Global | `IProjectSceneRegistry` | `ProjectSceneRegistry` | bootstrap Compose | global Dispose | read-only конфигурация на всех peers |
| Global | `IGameStateService` | `GameStateMachine` | bootstrap Compose | global Dispose | локальная проекция состояния; сетевые переходы инициирует server flow |
| Global | `IProjectSceneFlowService` | `ProjectSceneFlowService` | после Initialize | Shutdown перед global Dispose | network scene load запускает только server; offline load локальный |
| Global | `INetworkSessionService` | `NetworkSessionOrchestrator` | после Initialize | global Shutdown | принимает UI intent; не является источником server gameplay authority |
| Global | `IUiErrorService` | `UiErrorManager` | bootstrap Compose | global Dispose | только local presentation |
| Global | `IAudioService` | `AudioManager` | bootstrap Compose | global Dispose | только local presentation |
| Global | `IMusicService` | `MusicManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | только local presentation |
| Global | `IUiSoundService` | `UiSoundManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | только local presentation |
| Global | `IGameplaySoundService` | `GameplaySoundManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | воспроизводит локальную проекцию подтверждённых gameplay events |
| Global, read-only | `IGameMapCatalog` | `GameMapCatalog` | bootstrap Compose | unregister при global Dispose; asset остаётся Unity-owned | одинаковая конфигурация должна быть доступна server и clients |
| Session | `IGameMapSessionService` | `GameMapService` | synchronous Session transaction | после NGO stop, до `SessionStopped` | map selection/load подтверждает server; clients читают результат |
| Session | `IGameplayNoiseService` | `GameplayNoiseWorldService` | synchronous Session transaction | после NGO stop, до `SessionStopped` | запись и поиск noise events разрешены только server |
| Session | `ISessionServiceRegistry` | `SessionServiceRegistry` | synchronous Session transaction | вместе с Session scope | read/subscription API для динамических Session contracts; mutable scope не публикуется |
| Session | `IPlayerScopeRegistry` | `PlayerScopeRegistry` | synchronous Session transaction | `CloseAll` до Session Dispose | read-only registry Player scopes по `NetworkObjectId` на каждом peer |
| Session, NetworkObject | `IChatReadService`, `IChatCommandService` | `NetworkChatSession` | atomic batch на `OnNetworkSpawn` | registration handles на `OnNetworkDespawn`, до закрытия session | client вызывает command contract, server валидирует и реплицирует сообщения |
| Player | `IPlayerNetworkService`, `IReplicatedPlayerStateService`, `IEnemyAttackReceiver` | `PlayerScopeLifetime`, `PlayerNetwork`, `PlayerEnemyAttackReceiver` | Player transaction на `OnNetworkSpawn` | Player scope на `OnNetworkDespawn` | replicated state читается на peers; gameplay mutation остаётся server-authoritative |
| Player/Local | `ILocalPlayerInputService`, `ILocalPlayerCameraService`, `ILocalPlayerPresentationService` | `PlayerInputHandler`, `CameraLook`, `PlayerUI` | Local transaction только для owner | вместе с Player scope | никогда не создаётся для remote player или dedicated server |
| Scene: Lobby | `ILobbyReadService`, `ILobbyCommandService` | `NetworkLobbyService` | scene transaction commit после install Lobby feature | reverse uninstall Lobby scope | client отправляет intent, server владеет lobby state и start decision |
| Scene: Game shell | `IPauseService` | `GamePauseService` | scene transaction commit после install `PauseSceneFeature` | reverse uninstall Game scope | local-only pause UI; не останавливает server simulation |

## Не являются root-сервисами G

| Объект | Владелец | Причина |
|---|---|---|
| `ProjectContext` | Bootstrap scene | composition root, а не dependency gameplay-кода |
| `NetworkManager` | Bootstrap/NGO | внешняя инфраструктура; передаётся network services явно |
| `NetworkConnectionService` | Bootstrap network composition | internal-владелец NGO start/stop; не регистрируется в Global scope и скрыт за `INetworkSessionService` |
| `NetworkConnectionApprovalService` | Global composition | внутренняя часть network session, скрыта за facade |
| `NetworkSessionShutdownCoordinator` | Global composition | владелец Session scope, а не сервис для feature-кода |
| `SceneRuntimeScopeRegistry` | `ProjectContext` | владелец Scene scopes |
| `NetworkGameFlow`, objectives, `GameMapRoot` | соответствующий Scene scope | scene/network entities; связываются scene features или NGO lifecycle |
| player input, camera и player presentation | Player/Local scope | принадлежат одному local player и не должны попадать в replicated Player, Global или Session registry |

## Обязательные правила G

1. Регистрация выполняется по interface contract. На один contract допускается ровно один instance внутри конкретного scope; duplicate registration должна завершаться ошибкой.
2. Child scope может разрешать зависимости из parent scope. Parent scope не видит child services.
3. Global scope принимает только contracts из `GlobalServiceContractPolicy`; в частности `IPauseService`, `IChatReadService`, `IChatCommandService`, replicated/local Player contracts и технические network services отклоняются до регистрации.
4. Scene scope key — только `Scene.handle`; имя и path сцены не гарантируют уникальный runtime instance.
5. Любая ошибка Compose/Initialize/Install откатывает только уже выполненные регистрации в обратном порядке.
6. Unregister выполняется до `Dispose`. Повторные Shutdown, Uninstall и Dispose должны быть idempotent.
7. NetworkObject service регистрируется не раньше `OnNetworkSpawn` и удаляется не позже `OnNetworkDespawn`.
8. Новый код не использует `Find*`, `Resources.Load`, `ProjectContext.Instance`, `AudioManager.Instance` или `NetworkManager.Singleton` как fallback service resolution.
9. `G` является единственной глобальной runtime-точкой доступа; ambient `Instance` у runtime-компонентов запрещён.
10. `G` не определяет network authority: каждый contract сохраняет server/client правила из ownership table.

## Public G API

- Публичный API ограничен `G.IsReady`, `G.Resolve<T>()` и `G.TryResolve<T>(out T)`.
- `G` не публикует сам `IServiceResolver` и не предоставляет registration API.
- Public allowlist содержит только `IProjectSceneRegistry`, `IGameStateService`, `IProjectSceneFlowService`, `INetworkSessionService`, `IUiErrorService`, `IAudioService` и `IGameMapCatalog`.
- Запрос contract вне allowlist завершается ошибкой. Для разрешённого contract `Resolve<T>` до publication или после её снятия завершается lifecycle-ошибкой, а `TryResolve<T>` возвращает `false`.
- Повторная одновременная publication запрещена. Снятие выполняется generation-safe handle, поэтому устаревший owner не может очистить новую publication.
- Static state сбрасывается на `SubsystemRegistration`, включая Play Mode с отключённым Domain Reload.
- Через `G` разрешаются только Global contracts. Scene, Session, Player и Local services используют соответствующие scoped resolvers.

## ServiceScope semantics

- `IServiceResolver` предоставляет только состояние lifetime, `Resolve` и `TryResolve`; регистрировать зависимости может только владелец `ServiceScope`.
- Mutable `ServiceScope`, registration handles, transactions и scope registries являются assembly-internal infrastructure.
- Contract обязан быть interface. Регистрация concrete-типа завершается ошибкой.
- Duplicate contract внутри одного scope всегда запрещён.
- Shadowing parent contract запрещён по умолчанию. Он разрешается только явным `ServiceShadowingPolicy.Allow`; local service тогда имеет приоритет только внутри этого child scope.
- `UnityOwned` service при unregister только удаляется из resolver. Его Unity lifecycle остаётся у scene, prefab или bootstrap owner.
- `ScopeOwned` service получает cleanup ровно один раз. Он обязан реализовывать `IDisposable` либо получить явный cleanup callback. Один instance может предоставлять несколько interfaces внутри одного scope, но не может принадлежать двум scopes.
- Registration handle позволяет удалить динамический NetworkObject service до закрытия всего scope.
- Registration transaction откатывает все выполненные в ней регистрации в обратном порядке, если не был вызван `Commit`.
- Dispose parent scope сначала закрывает child scopes в обратном порядке, затем собственные registrations.
- После начала Dispose scope больше не разрешает Resolve, Register, создание child scope или новой transaction.

## Global scope integration

- `ProjectContext` создаёт Global `ServiceScope` в фазе Compose; его `Services` остаётся внутренним источником publication для `G`.
- `ProjectContext` остаётся публичным только как Unity `MonoBehaviour`, необходимый сцене Bootstrap; его lifecycle, scene composition и concrete service accessors являются assembly-internal. Внешние consumers используют `G` и scoped resolvers.
- `GlobalServiceContractPolicy` проверяет allowlist внутри `ServiceScope.Register` и повторно на публичной границе `G.Resolve/TryResolve`; child scopes policy не наследуют.
- Lifecycle-ошибки `G.Resolve<T>` содержат имя запрошенного contract; duplicate publication сообщает active и requested Bootstrap owners. Текущие generation/state/owner доступны через internal diagnostics только в Editor и Development Build.
- Global contracts регистрируются одной transaction после успешной scene service composition.
- Resolver публикуется в `G` только после успешного Initialize и transaction `Commit`.
- Bootstrap failure откатывает незавершённую transaction, затем закрывает Global scope.
- Обычный shutdown сохраняет `G` доступным для cleanup. `ProjectContext.Dispose` снимает publication непосредственно перед Global scope Dispose и закрывает оба lifetime ровно один раз.

## Session scope integration

- `NetworkSessionShutdownCoordinator` синхронно открывает Session child scope через `SessionScopeController`; события не участвуют в создании scope.
- `IGameMapSessionService` и `IGameplayNoiseService` регистрируются одной transaction как Unity-owned services.
- `IPlayerScopeRegistry` создаётся вместе с Session scope и является единственным владельцем Player child scopes.
- Ошибка регистрации откатывает partial Session scope и отменяет начало network session до запуска подключения.
- `SessionStarted` отправляется только после успешного commit.
- При shutdown Session scope закрывается после полной остановки NGO и до загрузки MainMenu.
- Перед Dispose Session scope registry принудительно закрывает все оставшиеся Player scopes в обратном порядке создания.
- `SessionStopped` отправляется только после закрытия scope и ровно один раз.

## Player scope integration

- `PlayerScopeRegistry` хранит scopes строго по `NetworkObjectId`; duplicate open завершается ошибкой и не заменяет активный scope.
- `PlayerScopeLifetime` создаёт Player child от Session в `OnNetworkSpawn` и хранит generation-safe registration handle до `OnNetworkDespawn`.
- Replicated contracts регистрируются в Player scope на server и clients одной transaction.
- Local child создаётся только при `IsClient && IsOwner`. Input, camera и presentation contracts регистрируются отдельной transaction и не видны через replicated resolver.
- Local resolver наследует replicated Player и Session services; обратный lookup из Player/Session в Local запрещён иерархией.
- Закрытие публикует `PlayerScopeClosing`, пока resolvers ещё активны, затем Dispose удаляет Local child и Player scope.

## Scene scope integration

- `SceneRuntimeScopeRegistry` создаёт отдельный `ServiceScope` для каждого runtime instance сцены; единственный ключ — `Scene.handle`.
- MainMenu scene scope создаётся как child Global scope. Lobby, Game и сцены из `IGameMapCatalog` создаются как children активного Session scope.
- Известная Map-сцена получает scope даже без `SceneRuntime`; это позволяет additive map instance иметь собственный lifetime до появления map-specific features.
- Каждый feature получает `SceneFeatureContext` с scene handle, scene kind и `IServiceResolver` соответствующего scene scope.
- `ISceneServiceRegistrar` разрешает регистрацию только во время feature install и не раскрывает mutable `ServiceScope`.
- Все feature registrations сцены входят в одну transaction: commit выполняется перед `Ready`, а registrar после этого закрывается.
- `LobbySceneFeature` регистрирует `ILobbyReadService` и `ILobbyCommandService`; `PauseSceneFeature` регистрирует `IPauseService` как Unity-owned contracts.
- Feature validation/install использует interface contracts через resolver и наследует доступ к parent services.
- Unload и install rollback сначала вызывают feature uninstall в обратном порядке, пока resolver ещё активен, и только затем закрывают scene `ServiceScope`.
- Ошибка install сначала выполняет reverse feature uninstall, затем rollback registration transaction и Dispose scene scope.
- После остановки NGO coordinator сначала удаляет все Session-owned scene scopes, затем закрывает Session scope, отправляет `SessionStopped` и загружает MainMenu.

## Динамические Session services

- `ISessionServiceRegistry` публикуется внутри Session scope и предоставляет только resolve плюс `ServicesChanged`.
- Session-owned `NetworkObject` регистрирует interface contracts атомарным batch через внутренний registrar; `ServiceScope` наружу не передаётся.
- Успешный batch публикует одно изменение после commit. Ошибка, включая duplicate contract, откатывает весь batch без уведомления consumers.
- `NetworkChatSession` хранит одну группу handles для `IChatReadService` и `IChatCommandService` и освобождает её в начале `OnNetworkDespawn`.
- Scene UI получает registry через parent lookup своего scene scope. Gameplay `NetworkBehaviour` разрешает Session contracts через `NetworkObjectServiceContext` с явным `NetworkManager`.
