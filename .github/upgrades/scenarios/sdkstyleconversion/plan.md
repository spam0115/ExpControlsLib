# SDK Style Conversion Plan

## Objective
Convert three .NET Framework 4.8 projects from legacy MSBuild format to modern SDK-style format while preserving:
- Target framework (net48)
- All dependencies
- Build behavior and output

## Pre-Conversion Status
- **Solution**: C:\s\ExpTreeLib\ExpTreeLib.sln
- **Projects**: 3
- **Current format**: Legacy MSBuild (non-SDK-style)
- **packages.config**: Not found (all projects use assembly references)

## Project Conversion Order
(Based on topological dependency analysis)

| Order | Project Name  | Path                                      | Type    | Status  | Notes |
|------:|---------------|-------------------------------------------|---------|---------|-------|
| 1     | ExpTreeLib    | ExpTreeLib\ExpTreeLib.csproj              | Library | Done    | Converted; fixed wildcard version (3.0.2.* → 3.0.2.0) |
| 2     | ExpListLib    | ExpListLib2\ExpListLib.csproj             | Library | Done    | Converted; builds successfully |
| 3     | Demo_CS       | Demo_CS\Demo_CS.csproj                    | WinExe  | Done    | Converted; builds successfully |

## Conversion Approach
1. Convert ExpTreeLib first (no dependencies)
2. Convert ExpListLib (depends on ExpTreeLib)
3. Convert Demo_CS (depends on ExpListLib)
4. Verify each project builds successfully before proceeding to the next

## Known Issues to Monitor
- No packages.config files to migrate (clean scenario)
- Demo_CS is a Windows Forms application (may require special handling post-conversion)
- All assembly references are framework assemblies (System, System.Data, System.Windows.Forms, etc.)

## Success Criteria
✓ All projects converted to SDK-style format  
✓ All projects build successfully  
✓ Target frameworks unchanged (remain net48)  
✓ All assembly references preserved  
✓ No .csproj/.vbproj files in legacy format  
