# CGv2 — Activity Ledger

Portable, no-admin Windows tool. Shows Boot/Shutdown and 11–13 h lock activity
of the last 10 days as a copyable (TSV) table. Runs from the tray, logs lock
events going forward (no admin = no historical locks).

## Build (from Linux or Windows)
    dotnet publish src/CGv2.App/CGv2.App.csproj -c Release -r win-x64 \
      --self-contained true -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true

Output: a single portable `CGv2.exe`. Copy to the Windows PC, run, optionally
enable Autostart from the tray menu. Data (`lock-log.json`) is stored next to
the exe; never committed.

## Test (Linux)
    dotnet test
