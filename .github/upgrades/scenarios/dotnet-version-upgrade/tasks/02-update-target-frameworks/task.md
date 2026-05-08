# 02-update-target-frameworks: Update Target Frameworks

Update all project files (Demo_CS, ExpListLib, ExpTreeLib) to target net10.0. For the WinForms project (Demo_CS), target net10.0-windows to enable Windows Desktop support. Add/update TargetFramework properties and any conditional SDK imports.

**Key concerns**: Windows Forms requires desktop runtime; ExpListLib and ExpTreeLib may expose APIs used by Demo_CS, so namespace/API consistency will be important during compilation.

**Done when**:
- All 3 .csproj files specify net10.0 (or net10.0-windows for Demo_CS)
- Projects can be loaded in Visual Studio without errors
- No build-time import failures
