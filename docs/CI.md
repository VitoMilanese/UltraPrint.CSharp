# Continuous integration

A permanent Windows GitHub Actions workflow (`.github/workflows/ci.yml`) is used so future incremental commits do not need temporary workflow commits or branch-history rewrites.

The workflow runs on pull requests and on pushes to `main` and performs:

1. `dotnet restore UltraPrint.sln`
2. `dotnet build UltraPrint.sln -c Release --no-restore`
3. `dotnet run --project tests/UltraPrint.CompatibilityTests/UltraPrint.CompatibilityTests.csproj -c Release --no-build`

The compatibility executable covers recovered `.ly` load/save invariants, record insert/duplicate/delete, database binding/state, operator DB discovery, VBScript preprocessing/dispatch, `Carta`, new-layout creation, inline PropertyGrid text editing, and the recovered `Mainform` facade contract.

Hardware/card-printer/TWAIN/SmartCard behavior still requires physical/runtime side-by-side verification and is not represented as complete merely because the managed solution builds.
