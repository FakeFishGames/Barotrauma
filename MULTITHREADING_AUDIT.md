# Barotrauma Multithreading Audit

**Date**: 2026-03-03
**Scope**: Full codebase audit of threading model, synchronization mechanisms, and potential issues

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
