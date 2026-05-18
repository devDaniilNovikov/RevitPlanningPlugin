# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Revit add-in (plugin) that loads building contours from an external REST API, sends them to an AI generation service to produce apartment layout variants, and applies the selected variant back into the active Revit model — all without leaving the Revit environment. Version 2.0 adds МОПы (common areas), apartment type composition (квартирография), and a Mock API mode for development without a real backend.

## Build & Test Commands

The solution targets **.NET Framework 4.8** and uses Visual Studio 2022. Revit API DLLs (`RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll`) must be present at `$(ProgramFiles)\Autodesk\Revit 2024\` for the main project to build; the test project does **not** require them.

```bash
# Build the full solution
dotnet build RevitPlanningPlugin.sln

# Build the test project only (no Revit DLLs needed)
dotnet build tests/RevitPlanningPlugin.Tests/

# Run all tests
dotnet test tests/RevitPlanningPlugin.Tests/

# Run a single test class
dotnet test tests/RevitPlanningPlugin.Tests/ --filter "FullyQualifiedName~ContourValidatorTests"

# Run a single test method
dotnet test tests/RevitPlanningPlugin.Tests/ --filter "FullyQualifiedName~ContourValidatorTests.Validate_ValidRectangle_ReturnsValid"
```

The test project (`tests/RevitPlanningPlugin.Tests/`) includes source files from `Models/`, `Services/`, and `Infrastructure/` directly via `<Compile Include="..."/>` globs — no separate DLL reference. It explicitly excludes `Revit/`, `UI/`, `App.cs`, and `Commands/`, so tests run without a Revit installation.

## Architecture

Five layers, each with a strict dependency direction (downward only):

```
UI Layer          — WPF/MVVM: MainWindow.xaml + code-behind, ViewModels, Converters, Ribbon (App.cs, Commands/)
Application Layer — MainViewModel: full orchestration cycle (connect → load contour → generate → preview → apply)
Domain Layer      — Pure C# models: BuildingContour, LayoutVariant, GenerationParameters, RoomLayout, ContourSegment
Infrastructure    — PlanningApiClient / MockPlanningApiClient, DtoMapper, ConfigurationService, PluginLogger
Revit Adapter     — RevitCurveBuilder, RevitElementCreator + CreatedElementsTracker, SafeTransaction
```

**Entry point**: `App.cs` implements `IExternalApplication` — registers the Ribbon tab and button on Revit startup. `OpenPlanningGeneratorCommand` (`IExternalCommand`) opens `MainWindow` as a modal dialog, passing `ExternalCommandData` to `MainViewModel`.

**MainViewModel** (`UI/ViewModels/MainViewModel.cs`) is the orchestration hub. It holds references to `IPlanningApiClient`, `ConfigurationService`, `ContourValidator`, and `RevitElementCreator`. All async operations use `CancellationTokenSource` stored in `_cts`; cancellation is exposed via `CancelCommand`.

**API client selection** is controlled by `PluginSettings.UseMockApi`. `InitializeApiClient()` in the VM instantiates either `PlanningApiClient` (real HTTP) or `MockPlanningApiClient` (no network). Both implement `IPlanningApiClient`.

**Unit conversion boundary**: All domain coordinates are stored in **metres**. The `UnitConverter` service converts incoming API data (mm/cm/m/ft/in) to metres on load. The Revit Adapter layer (`RevitCurveBuilder`) converts metres → Revit internal units (feet) with `UnitConverter.MetersToFeet` when building `Curve`/`XYZ` objects.

**DTO isolation**: `DtoMapper` (`Services/Api/DtoMapper.cs`) is the single place that translates between API JSON DTOs (`Models/Api/ApiDtos.cs`) and domain models. If the external API contract changes, only `DtoMapper` should need updating.

**Element lifecycle**: `RevitElementCreator` owns two `CreatedElementsTracker` instances — `PreviewTracker` (detail lines, replaced on each variant switch) and `AppliedTracker` (room separators + rooms, replaced when a new variant is applied). Every Revit model mutation goes through `SafeTransaction.Execute` or `SafeTransaction.ExecuteGroup` to guarantee rollback on failure.

**MVVM infrastructure**: `ObservableObject` (base for ViewModels), `RelayCommand`, and `AsyncRelayCommand` live in `Infrastructure/MvvmBase.cs`. Cross-component messaging uses `EventAggregator.Instance.Publish<T>()` with typed event classes defined in the same file.

**PasswordBox binding**: WPF `PasswordBox` does not support standard data binding for security reasons. API Key and Bearer Token fields are handled via `PasswordChanged` event handlers in `MainWindow.xaml.cs` code-behind — this is intentional, not a bug.

**Settings persistence**: `ConfigurationService` saves to `%AppData%\RevitPlanningPlugin\settings.json`. API keys and Bearer tokens are encrypted/decrypted with Windows DPAPI (`DataProtectionScope.CurrentUser`). The `[JsonIgnore]` property `EffectiveBaseUrl` is computed from `BaseUrl` + `Environment` and never serialised.

## Key Conventions

- **Segment types supported**: `Line`, `Arc`, `Spline` (HermiteSpline in Revit), `Ellipse`, `NurbsSpline`. `RevitCurveBuilder` falls back to a straight `Line` if Revit rejects a curve — it logs a warning but does not throw.
- **Contour closure tolerance**: 1 mm (`ContourValidator.ClosureTolerance = 0.001`). Validated on load; generation is blocked if validation fails.
- **Contour orientation**: CCW (counter-clockwise) is canonical. CW produces a warning (`CW_ORIENTATION`), not an error.
- **Inner loops**: `BuildingContour.InnerLoops` represents interior cutouts (courtyards, shafts). The Mock client includes a "С внутренним двором" demo that exercises this path.
- **Generation history**: `BuildingContour.GenerationHistory` stores all past generation runs (keyed by `DateTime`). `ShowAllHistory` in the VM toggles the gallery between the latest run only (`Variants`) and all historical variants (`AllVariants`).
- **Mock data**: `MockPlanningApiClient` provides 6 demo contours (`demo-rect`, `demo-lshape`, `demo-polygon`, `demo-organic`, `demo-spline`, `demo-courtyard`) and generates realistic variants with MOP zones (central corridor strip) and apartments on both sides. Useful for all development that doesn't require a real API.
- **RoomType.CommonArea** marks МОП rooms (общие коридоры). `Lobby` = лифтовый холл, `Elevator` = шахта лифта. `LayoutVariant.MopRooms` filters these three types; `LayoutVariant.ResidentialRooms` filters `LivingRoom`/`Bedroom`/`Kitchen`.
- **Log location**: `%AppData%\RevitPlanningPlugin\Logs\plugin_YYYY-MM-DD.log` (NLog, 30-day rotation). API secrets are masked in logs.
