# Profile backup

Snapshots of your saved remapping profiles and app settings, mirrored here from
`%AppData%\WolverineRemapper\` so they can be restored on another PC.

## Restore on a new machine

1. Install/run the app once (this creates the `%AppData%\WolverineRemapper\` folders).
2. Close the app.
3. Copy the files from this folder into:
   - `*.json` profiles → `%AppData%\WolverineRemapper\Profiles\`
   - `settings.json` → `%AppData%\WolverineRemapper\`
4. Launch the app — your last-used profile auto-loads.

In PowerShell:

```powershell
Copy-Item ".\Throne and Liberty*.json" "$env:APPDATA\WolverineRemapper\Profiles\" -Force
Copy-Item ".\settings.json" "$env:APPDATA\WolverineRemapper\" -Force
```
