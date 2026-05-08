# .NET 10 Upgrade Plan

## Overview

**Target**: Upgrade ExpTreeLib solution from .NET Framework 4.8 to .NET 10.0  
**Scope**: 3 projects (1 WinForms app + 2 ClassLibraries), ~15K LOC with ~15.5% requiring modification

**Selected Strategy**: All-At-Once — All projects upgraded simultaneously in a single atomic operation.

**Rationale**: Small project count (3), simple dependency structure, straightforward Windows Forms modernization, no package compatibility issues. All projects can be upgraded together and validated as one unit.

## Tasks

### 01-framework-and-sdk-setup

Prepare the development environment and verify .NET 10 SDK is installed. Check for and update any global.json files that may constrain the SDK version.

**Done when**:
- .NET 10 SDK is validated as installed
- global.json (if present) is compatible with .NET 10
- Environment is ready for multi-project upgrade

---

### 02-update-target-frameworks

Update all project files (Demo_CS, ExpListLib, ExpTreeLib) to target net10.0. For the WinForms project (Demo_CS), target net10.0-windows to enable Windows Desktop support. Add/update TargetFramework properties and any conditional SDK imports.

**Key concerns**: Windows Forms requires desktop runtime; ExpListLib and ExpTreeLib may expose APIs used by Demo_CS, so namespace/API consistency will be important during compilation.

**Done when**:
- All 3 .csproj files specify net10.0 (or net10.0-windows for Demo_CS)
- Projects can be loaded in Visual Studio without errors
- No build-time import failures

---

### 03-fix-windows-forms-compatibility

Address Windows Forms API incompatibilities in Demo_CS and ExpListLib (854 Windows Forms issues identified in assessment). This includes updating control properties, event handlers, and drag-drop logic to use .NET 10–compatible APIs. Focus on the most frequent issues: ListView, TreeView, TreeNode, DragDropEffects, Keys, and Control.

**Key concerns**: Windows Forms underwent binary breaking changes between .NET Framework and .NET Core. Many property names and enum values differ slightly.

**Done when**:
- Demo_CS compiles without Windows Forms–related errors
- ExpListLib compiles without Windows Forms–related errors
- All TreeView/ListView/Node references resolve correctly
- No `CS0246` (type not found) errors for System.Windows.Forms types

---

### 04-resolve-system-api-compatibility

Fix remaining API incompatibilities in all projects. This includes:
- Configuration migration (System.Configuration → System.Configuration.ConfigurationManager NuGet package, or migrate to Microsoft.Extensions.Configuration)
- System.Drawing / GDI+ (if needed, add System.Drawing.Common NuGet)
- System.Management / WMI (if needed, add System.Management NuGet)
- Any source-incompatible APIs (50 identified in assessment)

**Done when**:
- No `CS0246` errors for any removed or moved types
- No `CS0117` errors for missing methods/properties
- All NuGet package references are resolved
- Solution builds with 0 compilation errors

---

### 05-compile-and-verify-build

Restore dependencies, compile the full solution, and fix any remaining compilation errors. Verify that all 3 projects build successfully as a single atomic unit.

**Done when**:
- `dotnet build` succeeds with 0 errors across all projects
- No warnings related to deprecated APIs (warnings are acceptable if they don't block build)
- Solution structure is intact (no broken project references)

---

### 06-run-unit-tests

Execute all unit tests to validate functional correctness of the upgraded code. Ensure Windows Forms behavior changes are tested if GUI test coverage exists.

**Done when**:
- All unit tests pass
- No test failures due to behavioral changes in .NET 10
- Test coverage confirms the upgrade maintains existing functionality

---

### 07-integration-validation

Perform smoke testing of the WinForms application and verify inter-project dependencies work correctly. Confirm that Demo_CS can load and use ExpTreeLib and ExpListLib without runtime errors.

**Done when**:
- Demo_CS application starts without crashes
- TreeView, ListView, and drag-drop features function as expected
- No runtime errors when calling methods from ExpListLib or ExpTreeLib
- Application UI renders correctly on .NET 10 runtime
