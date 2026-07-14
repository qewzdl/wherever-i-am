# G: service contracts and scope ownership

Статус: обязательный контракт перед реализацией `G`.

`G` должен хранить и выдавать сервисы только по интерфейсу. Concrete-типы остаются деталями bootstrap, scene composition и NGO spawn lifecycle.

## Иерархия scopes

```text
Global (ProjectContext: Ready -> Dispose)
└── Session (TryOpenSessionScope -> SessionStopped)
    ├── Scene[Scene.handle] (Install -> Uninstall/Dispose)
    │   └── Player[NetworkObjectId] (OnNetworkSpawn -> OnNetworkDespawn)
    └── Scene[другой Scene.handle]
```

- `Global` создаётся bootstrap-ом один раз и живёт до `ProjectContext.Dispose`.
- `Session` открывается только через `NetworkSessionShutdownCoordinator.TryOpenSessionScope`. Закрытие происходит ровно один раз на `SessionStopped`, после остановки NGO client/server.
- `Scene` принадлежит `SceneRuntimeScopeRegistry` и идентифицируется только `Scene.handle`. Game shell и additive Map имеют независимые scopes.
- `Player` принадлежит конкретному spawned player `NetworkObject`; `NetworkObjectId` допустим только внутри активной session.

## Ownership table

| Scope | Contract | Текущая реализация | Регистрация | Удаление | Multiplayer authority |
|---|---|---|---|---|---|
| Global | `IProjectSceneRegistry` | `ProjectSceneRegistry` | bootstrap Compose | global Dispose | read-only конфигурация на всех peers |
| Global | `IGameStateService` | `GameStateMachine` | bootstrap Compose | global Dispose | локальная проекция состояния; сетевые переходы инициирует server flow |
| Global | `IProjectSceneFlowService` | `ProjectSceneFlowService` | после Initialize | Shutdown перед global Dispose | network scene load запускает только server; offline load локальный |
| Global | `INetworkSessionService` | `NetworkSessionOrchestrator` | после Initialize | global Shutdown | принимает UI intent; не является источником server gameplay authority |
| Global, internal | `INetworkConnectionService` | `NetworkConnectionService` | bootstrap Compose | после `ShutdownAndWaitAsync` | владеет NGO start/stop; gameplay не обращается к нему напрямую |
| Global | `IUiErrorService` | `UiErrorManager` | bootstrap Compose | global Dispose | только local presentation |
| Global | `IAudioService` | `AudioManager` | bootstrap Compose | global Dispose | только local presentation |
| Global | `IMusicService` | `MusicManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | только local presentation |
| Global | `IUiSoundService` | `UiSoundManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | только local presentation |
| Global | `IGameplaySoundService` | `GameplaySoundManager` | не регистрируется отдельно; доступен через `IAudioService` | вместе с `IAudioService` | воспроизводит локальную проекцию подтверждённых gameplay events |
| Global, read-only | `IGameMapCatalog` | `GameMapCatalog` | bootstrap Compose | unregister при global Dispose; asset остаётся Unity-owned | одинаковая конфигурация должна быть доступна server и clients |
| Session | `IGameMapSessionService` | `GameMapService` | `SessionStarted` | `SessionStopped` | map selection/load подтверждает server; clients читают результат |
| Session | `IGameplayNoiseService` | `GameplayNoiseWorldService` | `SessionStarted` | `SessionStopped` | запись и поиск noise events разрешены только server |
| Session, NetworkObject | `IChatReadService`, `IChatCommandService` | `NetworkChatSession` | после `OnNetworkSpawn` | на `OnNetworkDespawn`, до закрытия session | client отправляет command, server валидирует и реплицирует сообщения |
| Scene: Lobby | `ILobbyReadService`, `ILobbyCommandService` | `NetworkLobbyService` | после успешного install Lobby scene feature | reverse uninstall Lobby scope | client отправляет intent, server владеет lobby state и start decision |
| Scene: Game shell | `IPauseService` | `GamePauseService` | после успешного install `PauseSceneFeature` | reverse uninstall Game scope | local-only pause UI; не останавливает server simulation |

## Не являются root-сервисами G

| Объект | Владелец | Причина |
|---|---|---|
| `ProjectContext` | Bootstrap scene | composition root, а не dependency gameplay-кода |
| `NetworkManager` | Bootstrap/NGO | внешняя инфраструктура; передаётся network services явно |
| `NetworkConnectionApprovalService` | Global composition | внутренняя часть network session, скрыта за facade |
| `NetworkSessionShutdownCoordinator` | Global composition | владелец Session scope, а не сервис для feature-кода |
| `SceneRuntimeScopeRegistry` | `ProjectContext` | владелец Scene scopes |
| `NetworkGameFlow`, objectives, `GameMapRoot` | соответствующий Scene scope | scene/network entities; связываются scene features или NGO lifecycle |
| player input, camera и player presentation | Player `NetworkObject` | принадлежат одному player и не должны попадать в Global/Session registry |

## Обязательные правила G

1. Регистрация выполняется по interface contract. На один contract допускается ровно один instance внутри конкретного scope; duplicate registration должна завершаться ошибкой.
2. Child scope может разрешать зависимости из parent scope. Parent scope не видит child services.
3. Session, Scene и Player services запрещено регистрировать в Global scope.
4. Scene scope key — только `Scene.handle`; имя и path сцены не гарантируют уникальный runtime instance.
5. Любая ошибка Compose/Initialize/Install откатывает только уже выполненные регистрации в обратном порядке.
6. Unregister выполняется до `Dispose`. Повторные Shutdown, Uninstall и Dispose должны быть idempotent.
7. NetworkObject service регистрируется не раньше `OnNetworkSpawn` и удаляется не позже `OnNetworkDespawn`.
8. Новый код не использует `Find*`, `Resources.Load`, `ProjectContext.Instance`, `AudioManager.Instance` или `NetworkManager.Singleton` как fallback service resolution.
9. Static `Instance` у существующих классов считается migration API и не входит в контракт `G`.
10. `G` не определяет network authority: каждый contract сохраняет server/client правила из ownership table.

## ServiceScope semantics

- `IServiceResolver` предоставляет только `Resolve` и `TryResolve`; регистрировать зависимости может только владелец `ServiceScope`.
- Contract обязан быть interface. Регистрация concrete-типа завершается ошибкой.
- Duplicate contract внутри одного scope всегда запрещён.
- Shadowing parent contract запрещён по умолчанию. Он разрешается только явным `ServiceShadowingPolicy.Allow`; local service тогда имеет приоритет только внутри этого child scope.
- `UnityOwned` service при unregister только удаляется из resolver. Его Unity lifecycle остаётся у scene, prefab или bootstrap owner.
- `ScopeOwned` service получает cleanup ровно один раз. Он обязан реализовывать `IDisposable` либо получить явный cleanup callback. Один instance может предоставлять несколько interfaces внутри одного scope, но не может принадлежать двум scopes.
- Registration handle позволяет удалить динамический NetworkObject service до закрытия всего scope.
- Registration transaction откатывает все выполненные в ней регистрации в обратном порядке, если не был вызван `Commit`.
- Dispose parent scope сначала закрывает child scopes в обратном порядке, затем собственные registrations.
- После начала Dispose scope больше не разрешает Resolve, Register, создание child scope или новой transaction.
