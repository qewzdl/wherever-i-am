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
| Session | `IGameMapSessionService` | `GameMapService` | synchronous Session transaction | после NGO stop, до `SessionStopped` | map selection/load подтверждает server; clients читают результат. Сюда же попадает выбранная в лобби сложность: `SelectDifficulty` вызывает только server, и `SelectedEnemyConfig` читают только серверные враги |
| Session | `IGameplayNoiseService` | `GameplayNoiseWorldService` | synchronous Session transaction | после NGO stop, до `SessionStopped` | запись и поиск noise events разрешены только server |
| Session | `ISessionServiceRegistry` | `SessionServiceRegistry` | synchronous Session transaction | вместе с Session scope | read/subscription API для динамических Session contracts; mutable scope не публикуется |
| Session | `IPlayerScopeRegistry` | `PlayerScopeRegistry` | synchronous Session transaction | `CloseAll` до Session Dispose | read-only registry Player scopes по `NetworkObjectId` на каждом peer |
| Session, NetworkObject | `IChatReadService`, `IChatCommandService` | `NetworkChatSession` | atomic batch на `OnNetworkSpawn` | registration handles на `OnNetworkDespawn`, до закрытия session | client вызывает command contract, server валидирует и реплицирует сообщения |
| Session, NetworkObject | `ISessionPhaseService` | `NetworkSessionPhaseService` | atomic registration через `NetworkObjectServiceContext` на `OnNetworkSpawn` | registration handle на `OnNetworkDespawn` | server публикует committed `ProjectSceneKind` через `NetworkVariable`; clients только читают фазу |
| Session, NetworkObject | `IMatchCompletionService` | `NetworkGameFlow` | atomic registration на `OnNetworkSpawn` | registration handle первым снимается на `OnNetworkDespawn` | контракт доступен на всех peers, но завершение матча принимает только server |
| Player | `IPlayerNetworkService`, `IPlayerActionGate`, `IReplicatedPlayerStateService`, `IReplicatedPlayerHidingStateService`, `IEnemyAttackReceiver` | `PlayerScopeLifetime`, `PlayerActionGate`, `PlayerNetwork`, `PlayerHidingController`, `PlayerEnemyAttackReceiver` | Player transaction на `OnNetworkSpawn` | Player scope на `OnNetworkDespawn` | `IPlayerActionGate` атомарно арбитрирует server-authoritative pickup, drag и hiding за O(1); replicated state читается на peers |
| Player/Local | `IPlayerHidingCommandService`, `ILocalPlayerInputService`, `ILocalPlayerCameraService`, `ILocalPlayerPresentationService` | `PlayerHidingController`, `PlayerInputHandler`, `CameraLook`, `PlayerUI` | Local transaction только для owner | вместе с Player scope | команды скрытия не раскрывают concrete controller; никогда не создаётся для remote player или dedicated server |
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
3. Каждый scope принимает только contracts из своей policy: `GlobalServiceContractPolicy`, `SessionContractPolicy`, `SceneContractPolicy`, `PlayerContractPolicy` или `LocalPlayerContractPolicy`. Cross-scope регистрация отклоняется до изменения scope.
4. Создание child scope требует явной policy; policy parent scope не наследуется автоматически.
5. Scene scope key — только `Scene.handle`; имя и path сцены не гарантируют уникальный runtime instance.
6. Любая ошибка Compose/Initialize/Install откатывает только уже выполненные регистрации в обратном порядке.
7. Unregister выполняется до `Dispose`. Повторные Shutdown, Uninstall и Dispose должны быть idempotent.
8. NetworkObject service регистрируется не раньше `OnNetworkSpawn` и удаляется не позже `OnNetworkDespawn`.
9. Новый код не использует `Find*`, `Resources.Load`, `ProjectContext.Instance`, `AudioManager.Instance` или `NetworkManager.Singleton` как fallback service resolution.
10. `G` является единственной глобальной runtime-точкой доступа; ambient `Instance` у runtime-компонентов запрещён.
11. `G` не определяет network authority: каждый contract сохраняет server/client правила из ownership table.

## Как добавить новый сервис

Регистрация начинается не с `G.Resolve`, а с выбора владельца lifetime. Один и тот же порядок используется для любого нового сервиса:

1. Создать узкий interface contract, например `IInventoryReadService`.
2. Выбрать ровно один owner: Global, Session, конкретный Scene kind, Player или Local Player.
3. Добавить `typeof(IInventoryReadService)` в соответствующий список `ServiceContractCatalog`. Это единственная таблица разрешённых регистраций в коде.
4. Зарегистрировать contract только в lifecycle-точке владельца.
5. Передать consumer-у interface через `Construct`, `SceneFeatureContext.Services`, Player resolver или `NetworkObjectServiceContext`.
6. Добавить policy/lifecycle test. Для обязательного динамического Session contract также обновить `SessionServiceReadinessPolicy`.

Если шаг 3 пропущен или выбран неправильный owner, регистрация fail-closed завершится ошибкой с именем contract и scope.

### Scene service

Scene-сервис регистрируется внутри `SceneRuntimeFeature.InstallFeature`:

```csharp
protected override bool InstallFeature(SceneFeatureContext context)
{
    inventoryService.Construct(context.Services.Resolve<INetworkSessionService>());
    context.Register<IInventoryReadService>(inventoryService);

    inventoryUi.Construct(context.Services.Resolve<IInventoryReadService>());
    return true;
}
```

`context.Register` работает только во время общей scene transaction. Ошибка любого feature откатит все регистрации, а unload сначала выполнит reverse uninstall и только затем закроет scene scope.

### Dynamic Session NetworkObject service

Обязательный сетевой сервис регистрируется на каждом peer в `OnNetworkSpawn`:

```csharp
private IDisposable serviceRegistration;

public override void OnNetworkSpawn()
{
    if (!NetworkObjectServiceContext.TryRegisterRequiredSessionServices(
            this,
            registration =>
            {
                registration.Register<IInventoryReadService>(this);
                registration.Register<IInventoryCommandService>(this);
            },
            out serviceRegistration))
    {
        enabled = false;
    }
}

public override void OnNetworkDespawn()
{
    serviceRegistration?.Dispose();
    serviceRegistration = null;
}
```

Batch атомарен: либо зарегистрированы все contracts, либо ни одного. Для необязательного сервиса используется `TryRegisterSessionServices`, который возвращает причину ошибки без автоматического coordinated shutdown.

### Player и Local Player services

`PlayerScopeLifetime` открывает оба scope одним вызовом `NetworkObjectServiceContext.TryOpenRequiredPlayerScope`. Replicated contracts регистрируются на server и clients; Local contracts — только для `IsLocalPlayer`. Registration handle хранится до `OnNetworkDespawn`; ошибка transaction запускает coordinated shutdown.

### Global service

Global contracts намеренно добавляются реже и явно: reference/создание в `ProjectContext`, validation, `Construct`, регистрация в `RegisterGlobalServiceContracts`, shutdown/dispose и строка в `ServiceContractCatalog.Global`. `G` публикует новый contract только после успешного bootstrap commit.

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
- `CreateChild` требует явную registration policy. Session, каждый вид Scene, replicated Player и Local Player используют независимые allowlists из ownership table.
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
- `GlobalServiceContractPolicy` проверяет allowlist внутри `ServiceScope.Register` и повторно на публичной границе `G.Resolve/TryResolve`; каждый child получает собственную policy явно в composition point.
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
- Pickup, drag и hiding получают занятость через `IPlayerActionGate`. Gate хранит одно действие и owner-token, поэтому server requests коммитятся атомарно без обхода `SpawnedObjects`.
- `PickupItem` и `DraggableObject` получают server gate через `ConnectedClients[clientId].PlayerObject`; отсутствие player object или gate приводит к отказу действия.
- Local child создаётся только при `IsClient && IsOwner`. Input, camera и presentation contracts регистрируются отдельной transaction и не видны через replicated resolver.
- Local resolver наследует replicated Player и Session services; обратный lookup из Player/Session в Local запрещён иерархией.
- Закрытие публикует `PlayerScopeClosing`, пока resolvers ещё активны, затем Dispose удаляет Local child и Player scope.

## Scene scope integration

- `SceneRuntimeScopeRegistry` создаёт отдельный `ServiceScope` для каждого runtime instance сцены; единственный ключ — `Scene.handle`.
- MainMenu scene scope создаётся как child Global scope. Lobby, Game и сцены из `IGameMapCatalog` создаются как children активного Session scope.
- Известная Map-сцена получает scope даже без `SceneRuntime`; это позволяет additive map instance иметь собственный lifetime до появления map-specific features.
- Каждый feature получает `SceneFeatureContext` с scene handle, scene kind и `IServiceResolver` соответствующего scene scope.
- `SceneFeatureContext.Register<T>` разрешает регистрацию только во время feature install и не раскрывает mutable `ServiceScope`.
- Все feature registrations сцены входят в одну transaction: commit выполняется перед `Ready`, а registrar после этого закрывается.
- `LobbySceneFeature` регистрирует `ILobbyReadService` и `ILobbyCommandService`; `PauseSceneFeature` регистрирует `IPauseService` как Unity-owned contracts.
- Feature validation/install использует interface contracts через resolver и наследует доступ к parent services.
- Unload и install rollback сначала вызывают feature uninstall в обратном порядке, пока resolver ещё активен, и только затем закрывают scene `ServiceScope`.
- Ошибка install сначала выполняет reverse feature uninstall, затем rollback registration transaction и Dispose scene scope.
- После остановки NGO coordinator сначала удаляет все Session-owned scene scopes, затем закрывает Session scope, отправляет `SessionStopped` и загружает MainMenu.

## Post-load commit

- Сетевой переход сцены завершается в порядке: validate handlers → execute server actions → publish authoritative Session phase → validate dynamic Session contracts → commit `GameState` → `SceneLoadCompleted`.
- `IProjectSceneFlowServerActionHandler` возвращает `ProjectSceneActionResult`; успешное действие может передать rollback callback для созданных им NGO objects.
- Ошибка следующего action или dynamic contract validation выполняет rollback уже завершённых actions в обратном порядке, переводит игру в `GameState.Error` и отправляет `SceneLoadFailed` в coordinated session shutdown.
- Lobby считается готовым только при наличии `IChatReadService` и `IChatCommandService`. Game дополнительно требует `IMatchCompletionService`.
- `SpawnPlayers` подтверждает не только NGO spawn каждого player object, но и создание соответствующего Player scope по `NetworkObjectId`.
- `AppRuntime` не применяет scene state, пока `IProjectSceneFlowService` держит активную operation. Dedicated client дополнительно ждёт совпадения replicated Session phase и локальной dynamic readiness; только после этого коммитит `NetworkSessionState` и `GameState`.
- `NetworkGameFlowSceneStarter` не переводит матч в Playing до commit `GameState.InGame`, поэтому server simulation не стартует между map readiness и player action completion.

## Динамические Session services

- `ISessionServiceRegistry` публикуется внутри Session scope и предоставляет только resolve плюс `ServicesChanged`.
- Session-owned `NetworkObject` регистрирует interface contracts атомарным batch через `NetworkObjectServiceContext`; `ServiceScope` наружу не передаётся.
- Успешный batch публикует одно изменение после commit. Ошибка, включая duplicate contract, откатывает весь batch без уведомления consumers.
- `NetworkChatSession` хранит одну группу handles для `IChatReadService` и `IChatCommandService` и освобождает её в начале `OnNetworkDespawn`.
- `NetworkSessionPhaseService` отдельно публикует внутренний `ISessionPhaseService`, поэтому синхронизация session phase не зависит от наличия чата.
- `NetworkGameFlow` публикует `IMatchCompletionService` на spawn и снимает handle до остального despawn cleanup; duplicate flow отклоняется Session scope.
- Scene UI получает registry через parent lookup своего scene scope. Gameplay `NetworkBehaviour` разрешает Session contracts через `NetworkObjectServiceContext` с явным `NetworkManager`; перебор `SpawnedObjectsList` как service discovery запрещён.
- Удаление dynamic registration физически удаляет её из registration order; частый spawn/despawn не увеличивает память Session scope.

## Lifecycle hardening

- Readiness проверяет не только наличие interface registration: Unity object должен быть жив, `Behaviour` активен, `NetworkBehaviour` spawned, а сервис с `ISessionServiceReadiness` обязан подтвердить собственную готовность.
- Все state/scene lifecycle events вызывают subscribers изолированно. Исключение одного subscriber логируется, но не отменяет уже выполненный commit и не блокирует остальных subscribers.
- Shutdown timeout запускает ограниченный immediate retry. До подтверждённых `OnClientStopped`/`OnServerStopped` Session, Player и Scene scopes остаются открытыми; после исчерпания попыток cleanup остаётся fail-closed и может быть повторён.
- `IServiceResolver.IsDisposed` становится `true` уже при входе в `Disposing`; Resolve/Register после этого запрещены. Весь scoped resolver API привязан к Unity main thread, на котором создан корневой scope.
