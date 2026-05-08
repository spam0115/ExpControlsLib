# .NET 10 Upgrade - Scenario Instructions

## Strategy

**Selected**: All-At-Once  
**Rationale**: 3 projects, all on .NET Framework 4.8, simple dependency structure (Demo_CS depends on two libraries). No package compatibility issues. Windows Forms modernization is a known, well-documented pattern. All projects can be upgraded together and validated as one atomic unit.

### Execution Constraints

- All 3 projects are upgraded simultaneously — no tier ordering or phased rollout
- Single atomic upgrade: update TFMs → fix API incompatibilities → build → validate
- Windows Forms support must be enabled for Demo_CS (use net10.0-windows)
- Must validate full solution build before running tests
- Build succeeds with 0 errors — not a retry loop, but a bounded single pass to fix all compilation errors

## Preferences

- **Flow Mode**: Automatic
- **Commit Strategy**: After Each Task
- **Source Branch**: csharp_conversion
- **Working Branch**: upgrade-to-NET10

## Key Decisions

- **Target Framework**: .NET 10.0 (LTS, EOL 2028-11-14)
- **WinForms Runtime**: net10.0-windows for Demo_CS (enables Windows Desktop support)
- **No Package Vulnerabilities**: Assessment found 0 NuGet packages, so no security fixes needed
- **Assessment Focus**: Windows Forms (97.6% of API issues)

## Custom Instructions

<!-- Add per-task overrides here if needed, e.g.:
For 03-fix-windows-forms-compatibility: [instruction]
-->
