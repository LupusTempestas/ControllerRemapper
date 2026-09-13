# Example profile

`Throne and Liberty.json` is a ready-made mapping for *Throne and Liberty* on a
Wolverine V3 Pro 8K. Use it as a starting point, or as a reference for the
profile file format.

## Install it

1. Run the app once (this creates the `%AppData%\WolverineRemapper\` folders).
2. Copy the `.json` file into `%AppData%\WolverineRemapper\Profiles\`.
3. Launch the app and pick it from the PROFILE dropdown.

In PowerShell:

```powershell
Copy-Item ".\Throne and Liberty.json" "$env:APPDATA\WolverineRemapper\Profiles\" -Force
```

Your own profiles live in that same folder. To back them up or move them to
another PC, copy the `.json` files from there.
