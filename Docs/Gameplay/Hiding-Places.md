# Hiding places

`HidingPlaceInteractable` расширяет общую систему `InteractableObject`. Один
runtime-компонент подходит для шкафа, кровати, сундука и других укрытий:
разница задаётся точками и анимацией на prefab.

## Как добавить новое укрытие

1. На корневой объект prefab добавьте `NetworkObject`, collider и
   `HidingPlaceInteractable`.
2. Создайте `HidingPlaceData` через
   `Create -> Wherever I Am -> Items -> Hiding Place data` и назначьте его в
   поле `Data`.
3. Создайте дочерние transforms:
   - `Hiding Point` — положение игрока внутри;
   - `Exit Point` — основное безопасное положение после выхода;
   - дополнительные точки назначьте в `Fallback Exit Points`;
   - при необходимости `Interaction Anchor` — центр серверной проверки
     дистанции.
4. Collider укрытия должен находиться на interaction layer игрока. В
   `HidingPlaceData` настройте маски препятствий для line of sight и
   безопасного выхода.
5. Для анимации добавьте `HidingPlacePresentation`, назначьте `Animator` и
   bool-параметр `IsOccupied`.
6. Если prefab создаётся динамически, зарегистрируйте его в NGO Network
   Prefabs. Для scene-placed объекта достаточно корректного `NetworkObject`
   в сетевой сцене.

Игрок входит и выходит той же кнопкой `Interact`. Вход запрещён во время
pickup/drag. Одно укрытие допускает одного игрока.

## Multiplayer lifecycle

- Клиент отправляет только намерение войти.
- Сервер проверяет владельца PlayerObject, дистанцию, line of sight,
  доступность игрока, занятость укрытия и отсутствие активного pickup/drag.
- Сервер атомарно назначает occupant и публикует replicated hiding state.
- При выходе сервер проверяет capsule игрока через `Physics.CheckCapsule` и
  выбирает первую свободную exit-точку. Пока свободной точки нет, игрок
  остаётся внутри.
- Скрытый игрок не двигается, при настройке теряет colliders/renderers и не
  считается допустимой целью врага.
- Runtime-despawn укрытия сначала выводит игрока в безопасную точку. Unload
  сцены и shutdown сессии выполняют cleanup без gameplay-телепорта.
- Exit, disconnect, despawn игрока и cleanup укрытия освобождают occupant
  идемпотентно.
- `IReplicatedPlayerHidingStateService` доступен только в Player scope.
