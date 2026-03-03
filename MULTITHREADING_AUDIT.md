# Barotrauma Multithreading Audit

**Date**: 2026-03-03
**Scope**: Full codebase audit of threading model, synchronization mechanisms, and performance bottlenecks on complex submarines

---

## Threading Model Overview

Barotrauma uses a **hybrid multi-threaded architecture**:

- **Main Thread**: All game logic, rendering, physics (by default), and UI
- **Background Threads**: Networking, audio streaming, light raycasting, VoIP capture, file I/O
- **Thread Communication**: `CrossThread` helper for worker-to-main communication; concurrent collections for queuing

The game logic is intentionally kept single-threaded to avoid complexity, while I/O-bound and compute-heavy subsystems are offloaded to dedicated background threads.

---

## Systems Running on Separate Threads

### 1. Network Layer (Lidgren)
- **File**: `Libraries/Lidgren.Network/NetPeer.cs:160`
- **Thread**: Dedicated `m_networkThread` (background thread)
- **Purpose**: UDP socket communication, message processing, fragmentation reassembly
- **Sync**: `lock()` on `m_connections`, named global `Mutex` (`"Global\\lidgrenSocketBind"`) for socket binding

### 2. Audio Streaming (SoundManager)
- **File**: `Barotrauma/BarotraumaClient/ClientSource/Sounds/SoundManager.cs:759`
- **Thread**: `updateChannelsThread`, spawned dynamically when streaming audio is detected
- **Purpose**: Streaming audio updates, channel fade-out and disposal
- **Sync**: Per-channel `lock(playingChannels[i])`, `ManualResetEvent` (`updateChannelsMre`) for wake-up signaling

### 3. Light Raycasting (LightManager)
- **File**: `Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs:96`
- **Thread**: Dedicated `rayCastThread` spawned at initialization
- **Purpose**: Off-thread raycasting for shadow/lighting calculations
- **Sync**: `ConcurrentQueue<RayCastTask>` for lock-free task queuing

### 4. VoIP Audio Capture
- **File**: `Barotrauma/BarotraumaClient/ClientSource/Networking/Voip/VoipCapture.cs:153`
- **Thread**: Dedicated `captureThread`
- **Purpose**: Microphone input capture from OpenAL
- **Sync**: `lock(buffers)` on shared buffer arrays

### 5. Game Loading
- **File**: `Barotrauma/BarotraumaClient/ClientSource/GameMain.cs:468`
- **Thread**: `initialLoadingThread` for asynchronous content loading
- **Purpose**: Prevent UI freeze during asset loading
- **Sync**: `CrossThread` helper for main-thread callbacks

### 6. Physics Simulation (Optional / Disabled)
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Screens/GameScreen.cs:72-78`
- **Thread**: Conditional (`#if RUN_PHYSICS_IN_SEPARATE_THREAD`) — currently disabled
- **Purpose**: Offload physics from main thread
- **Sync**: `lock(updateLock)` when enabled

### 7. Child Server Relay
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Networking/ChildServerRelay.cs:76-87`
- **Threads**: Dual threads — `readThread` and `writeThread` for IPC pipe communication
- **Sync**: `volatile StatusEnum status`, `ConcurrentQueue<byte[]>`, `ManualResetEvent`

### 8. Farseer Physics Contact Solver
- **File**: `Libraries/Farseer Physics Engine 3.5/Dynamics/Contacts/ContactSolver.cs:459-475`
- **Threads**: `ThreadPool.QueueUserWorkItem()` for parallel velocity constraint solving
- **Sync**: Custom spinlock via `Interlocked.CompareExchange()`

---

## Synchronization Mechanisms

| Mechanism | Usage | Files |
|-----------|-------|-------|
| `lock()` | Most common; protects shared mutable state | SoundManager, TaskPool, CoroutineManager, GameServer |
| `ReaderWriterLockSlim` | Multiple readers / exclusive writer | `BarotraumaCore/Utils/Threading.cs` (RAII wrappers `ReadLock`, `WriteLock`) |
| `ConcurrentQueue<T>` | Lock-free FIFO queuing | LightManager (raycast tasks), ChildServerRelay (messages), EOS TaskScheduler |
| `ConcurrentDictionary<T>` | Lock-free key-value store | `NamedEvent<T>` event handler registration |
| `ManualResetEvent` | Thread signaling / wake-up | SoundManager, VoipCapture, CrossThread, Lidgren |
| `volatile` | Lock-free visibility of simple status | ChildServerRelay (`status` field) |
| `Interlocked` | Atomic increment/compare-and-swap | Lidgren message recycling, Farseer contact solver |
| `Mutex` (named) | Cross-instance socket binding | `NetPeer.Internal.cs:119` |
| `CrossThread` helper | Queue work from worker thread to main thread | GameMain loading, general cross-thread callbacks |

---

## Custom Infrastructure

### `Threading.cs` — ReaderWriterLockSlim Wrappers
- **File**: `Libraries/BarotraumaLibs/BarotraumaCore/Utils/Threading.cs`
- Provides ref-struct wrappers `ReadLock` and `WriteLock` for RAII-style lock management
- Ensures locks are always released even on exceptions

### `CrossThread.cs` — Worker-to-Main Thread Dispatcher
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Utils/CrossThread.cs`
- Background threads can queue lambdas to run on the main thread
- Uses `ManualResetEvent` to optionally block the caller until work completes

### `TaskPool.cs` — Background Task Manager
- **File**: `Libraries/BarotraumaLibs/BarotraumaCore/Utils/TaskPool.cs`
- Wraps `System.Threading.Task` with lifecycle tracking and callback dispatch
- Uses `lock(taskActions)` to protect the task list

### `CustomTaskScheduler.cs` — EOS Main-Thread Scheduler
- **File**: `Libraries/BarotraumaLibs/EosInterfacePrivate/InterfaceImpl/Core/CustomTaskScheduler.cs`
- Forces all Epic Online Services SDK calls onto the main thread (EOS SDK is not thread-safe)
- Uses `ConcurrentQueue<Task>` internally

### `GameMain.MainThread`
- **File**: `Barotrauma/BarotraumaClient/ClientSource/GameMain.cs:81,294`
- Caches main thread reference at startup
- Used throughout codebase via `Thread.CurrentThread == GameMain.MainThread` checks

---

## Identified Issues and Concerns

### HIGH — VoipQueue Buffer Race Condition
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Networking/Voip/VoipQueue.cs:87-99`
- `EnqueueBuffer()` modifies `newestBufferInd`, `bufferLengths[]`, and `LatestBufferID` without any lock
- `RetrieveBuffer()` may read these concurrently, producing torn reads or stale data
- **Recommendation**: Add `lock` around the buffer index and length updates, or use `Interlocked` for the counter

### HIGH — Entity Creation Counter (Developer-Acknowledged)
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Map/Entity.cs`
- Contains:
  ```csharp
  #warning TODO: consider removing this mutex, entity creation probably shouldn't be multithreaded
  lock (creationCounterMutex)
  {
      CreationIndex = creationCounter;
      creationCounter++;
  }
  ```
- Suggests entity creation may sometimes happen off the main thread unexpectedly
- The `#warning` pragma indicates this is a known architectural concern
- **Recommendation**: Audit all entity creation call sites to ensure they happen on the main thread; if so, remove the lock

### MEDIUM — Farseer Contact Solver Spinlock
- **File**: `Libraries/Farseer Physics Engine 3.5/Dynamics/Contacts/ContactSolver.cs:459-475`
- Hand-rolled spinlock using `Interlocked.CompareExchange()` in `ThreadPool` workers
- Spinlocks can cause CPU starvation under contention and are sensitive to implementation correctness
- **Recommendation**: Replace with `SemaphoreSlim` or restructure to eliminate the shared mutable state being protected

### MEDIUM — Lidgren Fragment Counter Not Thread-Safe
- **File**: `Libraries/Lidgren.Network/NetPeer.Fragmentation.cs:28`
- Comment: `// @TODO: not thread safe; but in practice probably not an issue`
- Fragment group counter reset is unprotected; could produce duplicate fragment group IDs under concurrent sends
- **Recommendation**: Use `Interlocked.Increment` or protect with the existing network thread lock

### MEDIUM — Lidgren NetConnection Thread Safety Question
- **File**: `Libraries/Lidgren.Network/NetConnection.cs`
- Comment: `// TODO: do we need to make this more thread safe?`
- Specific fields and access patterns are ambiguous; needs investigation
- **Recommendation**: Audit which fields are accessed from both the network thread and the main thread

### LOW — Physics Thread Currently Disabled
- **File**: `Barotrauma/BarotraumaShared/SharedSource/Screens/GameScreen.cs:72-78`
- `RUN_PHYSICS_IN_SEPARATE_THREAD` preprocessor flag exists but is not defined
- The synchronization for this path (`lock(updateLock)`) may not cover all shared physics state
- **Recommendation**: If this feature is ever re-enabled, conduct a thorough race-condition review before shipping

---

## Risk Summary Table

| System | Thread | Sync Mechanism | Risk |
|--------|--------|----------------|------|
| Network (Lidgren) | Dedicated | `lock()`, named `Mutex` | Medium |
| Audio Streaming | Conditional | Per-channel `lock()`, `ManualResetEvent` | Low |
| Light Raycasting | Dedicated | `ConcurrentQueue` | Low |
| VoIP Capture | Dedicated | `lock(buffers)` | Medium |
| Game Loading | Conditional | `CrossThread` | Low |
| Physics (disabled) | N/A | `lock(updateLock)` | Low (disabled) |
| Child Server Relay | Dual | `ConcurrentQueue`, `volatile` | Low |
| Farseer Contact Solver | Thread pool | `Interlocked` spinlock | **Medium-High** |
| Entity Creation Counter | Main thread | `lock(creationCounterMutex)` | **High** (design smell) |
| VoIP Queue Buffers | Mixed | None | **High** |

---

## Recommendations Summary

1. **Fix VoipQueue race condition** — add locking or atomic operations in `EnqueueBuffer()`/`RetrieveBuffer()`
2. **Resolve Entity creation thread-safety warning** — enforce main-thread-only creation or document why the lock is genuinely needed
3. **Replace Farseer spinlock** with a standard synchronization primitive
4. **Fix Lidgren fragment counter** with `Interlocked.Increment`
5. **Document NetConnection thread safety** — audit and resolve the TODO
6. **Do not enable physics thread** without a full thread-safety review first

---

## Key Files Reference

| Purpose | File |
|---------|------|
| RAII lock wrappers | `Libraries/BarotraumaLibs/BarotraumaCore/Utils/Threading.cs` |
| Worker-to-main dispatch | `Barotrauma/BarotraumaShared/SharedSource/Utils/CrossThread.cs` |
| Background task manager | `Libraries/BarotraumaLibs/BarotraumaCore/Utils/TaskPool.cs` |
| EOS main-thread scheduler | `Libraries/BarotraumaLibs/EosInterfacePrivate/InterfaceImpl/Core/CustomTaskScheduler.cs` |
| Network thread | `Libraries/Lidgren.Network/NetPeer.cs` |
| Network loop | `Libraries/Lidgren.Network/NetPeer.Internal.cs` |
| Audio streaming thread | `Barotrauma/BarotraumaClient/ClientSource/Sounds/SoundManager.cs` |
| VoIP capture thread | `Barotrauma/BarotraumaClient/ClientSource/Networking/Voip/VoipCapture.cs` |
| VoIP queue (race issue) | `Barotrauma/BarotraumaShared/SharedSource/Networking/Voip/VoipQueue.cs` |
| Light raycast thread | `Barotrauma/BarotraumaClient/ClientSource/Map/Lights/LightManager.cs` |
| Entity counter (TODO) | `Barotrauma/BarotraumaShared/SharedSource/Map/Entity.cs` |
| Farseer spinlock | `Libraries/Farseer Physics Engine 3.5/Dynamics/Contacts/ContactSolver.cs` |
| Server relay threads | `Barotrauma/BarotraumaShared/SharedSource/Networking/ChildServerRelay.cs` |

---

## Performance Bottleneck Analysis — Why Complex Submarines Drop FPS

### The Core Problem: Everything Runs Sequentially on One Thread

The main update loop in `GameScreen.Update()` is a **fully sequential pipeline**. On a complex submarine this becomes the FPS killer because every subsystem runs one after the other with no parallelism:

```
GameScreen.Update() — MAIN THREAD ONLY
  ├── PhysicsBody.List foreach → Update() for every body
  ├── GameSession.Update()
  ├── Character.UpdateAll()      → all NPCs/players, AI, pathfinding
  ├── StatusEffect.UpdateAll()
  ├── MapEntity.UpdateAll()
  │     ├── Hull.HullList foreach → wave sim per hull
  │     ├── Structure.WallList foreach
  │     ├── Gap.GapList.OrderBy(Rand) → gaps in random order (ALLOCATES every frame)
  │     ├── Powered.UpdatePower() → electrical grid solver
  │     └── Item.ItemList foreach → every item component
  ├── Character.UpdateAnimAll()  → all ragdoll animations
  ├── Ragdoll.UpdateAll()
  ├── Submarine.Loaded foreach → sub.Update()
  └── GameMain.World.Step()     → Farseer physics (single step)
```

There is **zero `Parallel.For` or `Task.Run`** anywhere in this game-logic pipeline. The only parallelism is inside Farseer's contact constraint solver (internal library code).

---

### Bottleneck 1 — Hull Water Simulation: O(hulls × width)

**File**: `BarotraumaShared/SharedSource/Map/Hull.cs:881`

Each hull runs a discrete wave simulation every frame. The cost scales with hull width:
- Wave resolution: 1 point per 32px (`WaveWidth = 32`)
- Operations per hull per frame: ~7 × (width/32) — two spread passes of left/right propagation
- A 1024px hull = 33 wave points ≈ 231 operations
- A large submarine with 100 hulls averaging 512px wide = ~3,600 operations/frame just for wave math

All hulls are updated sequentially in `MapEntity.UpdateAll()` with no skipping unless `WaterVolume == 0` and waves have fully settled.

**These are independent per-hull — a perfect candidate for `Parallel.For`.**

---

### Bottleneck 2 — Gap Shuffle Allocates Garbage Every Frame

**File**: `BarotraumaShared/SharedSource/Map/MapEntity.cs:665`

```csharp
foreach (Gap gap in Gap.GapList.OrderBy(g => Rand.Int(int.MaxValue)))
{
    gap.Update(deltaTime, cam);
}
```

`LINQ .OrderBy()` allocates a new sorted array every single frame. On a large submarine with 200+ gaps, this is a heap allocation + GC pressure every tick. The comment explains the intent (avoid water always draining through the first gap), but the implementation is expensive. A pre-shuffled list or a frame-seeded Fisher-Yates shuffle would avoid allocation.

---

### Bottleneck 3 — Electrical Grid Solver: O(devices)

**File**: `BarotraumaShared/SharedSource/Items/Components/Power/Powered.cs:455`

`Powered.UpdatePower()` runs every frame and:
1. `UpdateGrids()` — BFS-traverses the entire connection graph to rebuild or patch grids
2. Iterates all `poweredList` entries to compute load/supply per device
3. Resolves power output in priority stages (reactors → relays → batteries)

All sequential. On a submarine with 150+ powered devices (lights, pumps, engines, terminals, reactors, batteries, junction boxes) this is a sizeable serial scan every tick.

**Grids are independent of each other — cross-grid work could run in parallel.**

---

### Bottleneck 4 — Item Update Loop: O(items)

**File**: `BarotraumaShared/SharedSource/Map/MapEntity.cs:680`

```csharp
foreach (Item item in Item.ItemList)
{
    item.Update(deltaTime, cam);
}
```

Every item in the world (including all items inside the submarine) is updated sequentially. A complex submarine easily has 500–2000 items (wires, components, containers, weapons, etc.). Each `item.Update()` runs all active `ItemComponent.Update()` methods on that item.

Many item components are stateless or read-only relative to other items — but the shared `Item.ItemList` and component state make naive parallelization risky without analysis.

---

### Bottleneck 5 — Physics: Disabled Parallel Thread

**File**: `BarotraumaShared/SharedSource/Screens/GameScreen.cs:1,300-311`

```csharp
//#define RUN_PHYSICS_IN_SEPARATE_THREAD   // ← COMMENTED OUT
```

The physics thread infrastructure exists but is disabled. When enabled, `World.Step()` would run on a background thread while game logic runs in the `lock(updateLock)` block. However the synchronization is incomplete:

- **Inside Farseer**: `ContactSolver` uses `ThreadPool` / `Parallel.For` with spinlocks on `_velocities[]` for constraint solving — this internal parallelism works
- **Between game logic and physics**: `PhysicsBody.SimPosition`, `LinearVelocity`, `FarseerBody.Position` etc. are read directly without locks in the main thread loop (`GameScreen.cs:142-151`). If the physics thread writes these while the main thread reads them, data races occur

This is why the thread was disabled — enabling it requires fencing all physics state reads in game logic.

---

### Bottleneck 6 — Character + AI: O(characters)

**File**: `BarotraumaShared/SharedSource/Characters/Character.cs:3318`

`Character.UpdateAll()` iterates every character sequentially. Each character tick runs:
- AI controller update (pathfinding decisions, behavior tree)
- Animation controller update
- Health/affliction updates
- Inventory updates

On servers with many bots the AI cost accumulates. Pathfinding via `IndoorsSteeringManager` issues raycasts into the physics world — these are potentially the heaviest per-character calls.

---

### Summary Table — Main Thread Bottlenecks

| System | Location | Scales With | Parallelizable? | Notes |
|--------|----------|-------------|-----------------|-------|
| Hull wave sim | `Hull.cs:881` | # hulls × width | **Yes** — hulls are independent | ~100-300 hulls on large subs |
| Gap update shuffle | `MapEntity.cs:665` | # gaps | Yes, but simpler fix | LINQ alloc every frame |
| Power grid solver | `Powered.cs:455` | # powered devices | **Partially** — grids are independent | ~150+ devices typical |
| Item update loop | `MapEntity.cs:680` | # items | Risky — shared state | ~500-2000 items |
| Farseer physics | `GameScreen.cs:303` | # contacts | **Already parallel** internally | Thread exists but disabled |
| Character/AI | `Character.cs:3318` | # characters | Partially — AI is mostly independent | Raycasts touch physics world |
| PhysicsBody loop | `GameScreen.cs:142` | # physics bodies | Partially — draw update only | Reads physics state unsafely |

### Lowest-Hanging Fruit (Safest to Parallelize)

1. **Hull wave simulation** — no cross-hull writes except through `ConnectedGaps` (which only touch edge wave points); could use double-buffering and `Parallel.For`
2. **Gap random ordering** — replace `LINQ .OrderBy()` with a pre-allocated shuffled list using Fisher-Yates; eliminates the per-frame heap allocation entirely
3. **Power grids** — each `GridInfo` is independent; updating multiple grids in parallel is safe as long as no device appears in two grids (it can't by definition)
4. **Re-enable physics thread** — requires adding `Volatile.Read` / `Interlocked` fences on `FarseerBody.Position` and `LinearVelocity` getters in `PhysicsBody.cs`
