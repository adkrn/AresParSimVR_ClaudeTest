# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Quick Command Reference

```bash
# Build for Quest
# Unity Editor: File → Build Settings → Android → Build

# Deploy to Quest via ADB
adb install -r Builds/AresParSimVR.apk

# Monitor Quest logs
adb logcat -s Unity

# Clear Quest app data
adb shell pm clear com.YourCompany.AresParSimVR
```

## Project Overview

AresParSimVR is a Unity-based VR parachuting training simulator for professional military/aviation training. The project supports three application modes determined at startup by `MainManager`:
- **ControlManager**: Instructor control interface
- **Simulator**: Main VR training simulation for trainees
- **ViewManager**: Observation/monitoring interface

## Build and Development Commands

### Unity Project Configuration
- **Unity Version**: Unity 6000.1.2f1 (Unity 6.1) with Universal Render Pipeline
- **Target Platform**: Android (Meta Quest 2/3/Pro)
- **Build Target**: Android API Level 29+ (Android 10.0)
- **XR Plugin**: Meta XR SDK + Oculus XR Plugin
- **Rendering**: Universal Render Pipeline (URP)

### Building the Project
1. **Open Unity Hub** → Select project → Open with Unity 6.1
2. **Switch Platform**: File → Build Settings → Android → Switch Platform
3. **Configure XR**: Edit → Project Settings → XR Plug-in Management → Enable Oculus
4. **Build APK**: File → Build Settings → Build (outputs to `Builds/` folder)
5. **Deploy to Quest**: Build & Run with device connected via USB

### Key Package Dependencies
- **Meta XR SDK**: v74.0.3 - Quest hardware integration
- **Unity Netcode**: v2.4.3 - Multiplayer synchronization
- **Unity WebRTC**: v3.0.0-pre.8 - Audio/video streaming
- **XR Interaction Toolkit**: v3.1.1 - VR interaction system
- **Unity Addressables**: v2.4.6 - Asset management
- **Unity Timeline**: v1.8.7 - Sequence orchestration
- **Unity Movement**: file:../Assets/Unity-Movement-74.0.0 - Meta body tracking
- **Universal RP**: v17.1.0 - URP rendering pipeline
- **Post Processing**: v3.4.0 - Visual effects
- **Input System**: v1.14.0 - New Unity input handling

### Running Tests
- **Play Mode Tests**: Window → General → Test Runner → PlayMode → Run All
- **Edit Mode Tests**: Window → General → Test Runner → EditMode → Run All
- **Physics Testing**: Use test scenes in `Assets/Scenes/WIP/` folder:
  - `SunLightPhysics_Test.unity` - Physics testing
  - `ServerConnectTest001.unity` - Network testing
  - `CharacterOvrRigTest_*.unity` - VR character rig testing
  - `Test.unity` - General testing scene

### Running the Simulator
1. **Unity Editor**: Open scene → Press Play button (recommend opening `Main` or `Lobby` scene)
2. **Quest Device**: Install APK via ADB or SideQuest → Launch from Apps menu
3. **Hardware Mode**: Set `AresHardwareService.useHardware = true` in Inspector for ARES motion platform
4. **Simulation Mode**: Keep `useHardware = false` for keyboard/VR controller input
5. **Network Testing**: Use localhost or configure WebSocket server URL in `WS_DB_Client`

### Code Quality and Testing
- **Unity Code Linting**: Unity uses built-in code analysis via IDE integration
- **Format Check**: Ensure C# formatting follows Unity conventions (4 spaces, Allman braces)
- **Performance Profiling**: Window → Analysis → Profiler (monitor FPS, draw calls, memory)
- **VR Performance**: Use `VRFPSDisplay` component to monitor runtime FPS on device

## Architecture Overview

### Core State Management Flow

```
1. MainManager → Determines app type (Control/Sim/View)
2. DataManager → Loads CSV scenarios based on jumpType
3. StateManager_New → Executes timeline procedures (Note: StateManager_Refactored.cs exists but is not yet integrated)
4. Procedures → Complete based on CompleteCondition enum
```

### Key Singleton Patterns
```csharp
MainManager.Inst          // Application type management
DataManager.Inst          // CSV data and scenarios
StateManager_New.Inst     // Training flow control (Note: StateManager_Refactored.cs exists but is not yet integrated)
UIManager.Inst            // UI state and results
WS_DB_Client.Instance     // WebSocket communication
AresHardwareService.Inst  // Hardware communication service
NetworkManager.Inst       // Unity Netcode management
WeatherManager.Inst       // Weather system control
```

### Procedure Execution System

Procedures execute via `StateManager_New.CompleteTriggerAction()` based on CompleteCondition:
- **Time**: Timer-based completion
- **Animation**: Animation event triggers
- **SitDown/Stand**: Character state changes
- **Fall**: Free fall initiation
- **PullCord**: Parachute deployment trigger
- **Landing**: Ground collision detection
- **Point**: Waypoint arrival

### Physics Simulation

**PlayCharacter** implements realistic free-fall physics:
- Gravity constant: 9.80665 m/s²
- Air density: 1.225 kg/m³
- Drag coefficients for free-fall and parachute states
- Terminal velocity: ~85 m/s (free-fall), ~5 m/s (parachute)

**AresHardwareParagliderController** handles parachute control:
- ARES hardware integration via `AresHardwareService`
- Left/right pull inputs (0-1 range)
- Forward speed: 8-15 m/s (parachute deployed)
- Sink rate: 4.5-5.5 m/s (parachute deployed)

## Data Architecture

### CSV Configuration Files
Located in `Assets/StreamingAssets/Csvs/`:
- `CD_TimeLine.csv` - Training sequence definitions
- `CD_Procedure.csv` - Individual procedure details
- `CD_Evaluation.csv` - Scoring criteria
- `CD_Routes.csv` - Aircraft waypoint paths
- `CD_Weather.csv` - Weather presets
- `CD_Instruction.csv` - UI instructions
- `CD_SimInfo.csv` - Simulator configuration

### Data Loading Flow
1. `WS_DB_Client` receives scenario ID from instructor
2. `DataManager.ReceiveScenarioID()` loads scenario
3. CSV data filtered by `jumpType` (STANDARD, HIGH_FALL, etc.)
4. Timeline procedures loaded in order

## Network Architecture

### Communication Layers
- **Unity Netcode**: Multiplayer synchronization via `NetworkManager`
- **WebSocket**: Real-time database updates via `WS_DB_Client`
- **WebRTC**: Trainer-trainee video/audio via `WebRTCManager`
- **SSL Handling**: Custom certificate handlers in `CertificateHandlers.cs`

### ARES Hardware Integration
- **Hardware Service**: `AresHardwareService` - Hybrid communication layer with threading for feedback and direct calls for sending
- **API Wrapper**: `AresParachuteAPI` - DLL interface (`ARESParaSimDllMotionExternC.dll`)
- **Motion Control**: Roll (-180°~+180°), Yaw (0°~360°), Riser inputs (0-1 range in AresMotionData)
- **Event System**: Separate event API (`SetEvent`, `GetEvent`) for state synchronization
- **Data Flow**:
  - Send: Unity → `AresMotionData` → `SendMotionData()` → `ARESParaSIM_SetMotionControl()` → Hardware
  - Receive: Hardware → Thread loop → `ARESParaSIM__GetMotionControl()` → `AresFeedbackData` → Unity
- **Thread Management**: Background thread polls feedback data continuously while main thread sends commands

## Development Workflow Patterns

### Scene Management Strategy
The project uses a multi-scene architecture:
1. **Start Scene**: Initial app type selection (Control/Sim/View)
2. **Lobby Scene**: Training setup and participant synchronization
3. **Main Scene**: Active training simulation
4. Scene transitions managed by `MainManager` and `StateManager_New`

### Multiplayer Architecture
Three-layer networking approach:
- **Unity Netcode**: GameObject synchronization for avatars/objects
- **WebSocket (WS_DB_Client)**: Database operations and instructor commands
- **WebRTC**: Real-time audio/video between trainer and trainee

## Common Development Tasks

### Adding New Training Procedures
1. Define procedure in `CD_Procedure.csv` with appropriate CompleteCondition
2. Add timeline entry in `CD_TimeLine.csv` with procedure reference
3. If new CompleteCondition needed, add case to `StateManager_New.CompleteTriggerAction()`
4. Implement behavior in relevant component (PlayCharacter, CameraController, etc.)

### Modifying Physics Parameters
- **Free-fall drag**: `PlayCharacter.dragCoefficient` (default 0.005)
- **Terminal velocity**: `PlayCharacter.maxFallSpeed` (default 85 m/s)
- **Parachute drag**: `PlayCharacter.parachuteDragCoefficient` (default 1.0)
- **Control gains**: `AresHardwareParagliderController` fwdSpeedGain/sinkRateGain

### Working with Routes
- Define waypoints in `CD_Routes.csv`
- `RouteManager` instantiates route GameObjects
- `AirPlane` follows waypoints sequentially via `MoveToNextPoint()`

### Evaluation and Scoring
- Criteria defined in `CD_Evaluation.csv`
- Results tracked: `UIManager.AddResult(evaluation, isSuccess)`
- Final calculation: `StateManager_New.OnEnd()`

### Debugging Hardware Connection
1. Check COM port in `AresHardwareService.comPort` (0 = COM1, 1 = COM2, etc.)
2. Verify DLL presence: `Assets/Plugins/ARESParaSimDllMotionExternC.dll`
3. Monitor connection status via `AresHardwareService.IsConnected`
4. Check logs for hardware communication errors - look for "[AresHardware]" prefixed messages
5. Enable `debugMode` in `AresHardwareService` for detailed logging (shows `LogSendData` and `LogFeedbackData` outputs)
6. Thread monitoring: Background thread handles feedback polling while main thread sends motion commands
7. Event API: Use `SetEvent()` for state changes (FreeFall, Deploy, Landing), separate from motion control
8. Auto-reconnect: Configure `autoReconnect`, `reconnectInterval`, and `maxReconnectAttempts` in Inspector

## Performance Considerations

### Optimization Patterns
- Component caching to avoid `FindAnyObjectByType` calls
- Conditional updates to reduce per-frame overhead
- Non-allocating JSON serialization for network messages
- Coroutine pooling for repeated operations

### Known Performance Areas
- `PlayCharacter` physics calculations (every FixedUpdate)
- `AresHardwareParagliderController` control updates (configurable send interval)
- Large terrain rendering (use LOD and occlusion culling)

### Recent Refactoring Notes
- **AresHardwareService**: Hybrid approach - threading for feedback polling, direct API calls for motion sending
- **API Structures**: Renamed to `ARES_PARASIM_MOTION_DATA` and `ARES_PARASIM_FEEDBACK_DATA` (simplified names)
- **Event Handling**: Separated into dedicated API functions (`ARESParaSIM_SetEvent`, `ARESParaSIM_GetEvent`)
- **VRFPSDisplay/VRFPSManager**: FPS monitoring system for Quest performance tracking (target: 72-90 FPS)
- **StateManager_Refactored.cs**: Improved state management (exists but not integrated - use StateManager_New.cs)

## Common Issues and Solutions

### Quest Performance Issues
- **Low FPS**: Check VRFPSDisplay for performance monitoring, target 72-90 FPS
- **Build Issues**: Ensure Android platform is selected and XR Plugin Management has Oculus enabled
- **Motion Sickness**: Reduce camera shake effects in `CameraController`, adjust fade transitions

### Network Issues
- **WebSocket Connection Failed**: Check server URL in `WS_DB_Client`, verify SSL certificates
- **WebRTC Not Working**: Ensure proper signaling server configuration, check firewall settings
- **Multiplayer Sync Issues**: Verify NetworkManager settings, check NetworkObject components

### Data Loading Issues
- **CSV Not Found**: Ensure CSV files are in `Assets/StreamingAssets/Csvs/` folder
- **Scenario Loading Failed**: Check `jumpType` in scenario data matches CSV entries
- **Timeline Errors**: Verify procedure IDs in `CD_TimeLine.csv` match `CD_Procedure.csv`

## Key Architectural Patterns

### Singleton Management
Most managers use a singleton pattern with lazy initialization:
```csharp
public static DataManager Inst => _inst ??= FindObjectOfType<DataManager>();
```

### CSV-Driven Configuration
Training scenarios are entirely data-driven through CSV files in `StreamingAssets/Csvs/`. This allows non-programmers to modify training sequences without code changes.

### State Machine Architecture
`StateManager_New` implements a procedure-based state machine where each state has:
- Entry conditions
- Execution logic
- Completion conditions (Time, Animation, User Action, Physics Event)
- Exit transitions

### Network Architecture Layers
1. **Netcode Layer**: Unity multiplayer synchronization (NetworkManager)
2. **WebSocket Layer**: Database and monitoring updates (WS_DB_Client)
3. **WebRTC Layer**: Real-time video/audio (WebRTCManager)
4. **Hardware Layer**: ARES motion platform (AresHardwareService)

## Critical Files and Classes

### Entry Points
- `Assets/Scenes/Start.unity`: Application launcher scene
- `Assets/Scenes/Lobby.unity`: Pre-training lobby scene
- `Assets/Scenes/Main.unity`: Main training simulation scene

### State Management
- `StateManager_New.cs`: Main training flow controller (production)
- `StateManager_Refactored.cs`: Improved version (not yet integrated)
- `DataManager.cs`: Scenario and CSV data loading
- `MainManager.cs`: Application type determination and initialization

### Hardware Integration
- `AresHardwareService.cs`: Hardware communication service (hybrid threading model - feedback polling in thread, command sending direct)
- `AresParachuteAPI.cs`: P/Invoke DLL wrapper with `ARES_PARASIM_MOTION_DATA` and `ARES_PARASIM_FEEDBACK_DATA` structures
- `AresHardwareParagliderController.cs`: Parachute physics control
- **Key API Functions**:
  - `ARESParaSIM_SetMotionControl()`: Send motion commands
  - `ARESParaSIM__GetMotionControl()`: Receive feedback (polled in thread)
  - `ARESParaSIM_SetEvent()`: Set training events (FreeFall, Deploy, Landing)
  - `ARESParaSIM_GetEvent()`: Get current event state

### Player & Physics
- `PlayCharacter.cs`: Core physics simulation and movement
- `CameraController.cs`: VR camera management and perspective switching
- `AirPlane.cs`: Aircraft movement along waypoints

### Video Recording
- `FFMPEGRecorder.cs`: Training session recording via FFMPEG
- Output location: `Assets/StreamingAssets/RecordVideos/`
- Recording format: MP4 with timestamp in filename

## Important Implementation Notes

### Threading Considerations
- **AresHardwareService**: Uses hybrid threading model - background thread for feedback polling, main thread for sending commands
- **UnityMainThreadDispatcher**: Required for executing Unity API calls from background threads
- **WebSocket Communication**: Async/await pattern with proper exception handling

### Data Flow Critical Path
1. **Scenario Selection**: Instructor → WebSocket → `DataManager.ReceiveScenarioID()`
2. **Data Loading**: CSV files filtered by `jumpType` → Timeline/Procedure lists created
3. **Training Execution**: `StateManager_New` iterates through procedures → `CompleteTriggerAction()` checks completion
4. **Hardware Sync**: Motion data → `AresHardwareService.SendMotionData()` → DLL → Hardware
5. **Results Collection**: `UIManager.AddResult()` → Final evaluation on training end

### Known Refactoring in Progress
- **StateManager_Refactored.cs**: Exists but not integrated - continue using `StateManager_New.cs` for production
- **UI System Migration**: Some UI components still use legacy patterns in `Backup/` folder

### Critical Dependencies
- **ARES Hardware DLL**: `ARESParaSimDllMotionExternC.dll` must be present in `Assets/Plugins/`
- **CSV Files**: All training data depends on CSV files in `Assets/StreamingAssets/Csvs/`
- **WebSocket Server**: Required for instructor control and database operations
- **Meta Quest SDK**: Version 74.0.3 specifically required for body tracking features