# DogePet — Tiny Desktop Shiba (Doge Coin style)

A cute little always-on-top Shiba Inu that lives on your desktop.

- Draggable
- Click anywhere on it to give pats (happy animation + happiness boost)
- Slowly gets a bit sadder if ignored (visual feedback coming)
- Right-click for menu (reset position, exit)

## Building & Running

### Requirements
- .NET 8 SDK (or newer)

### Build from terminal

```powershell
cd C:\Users\david\DesktopPet
dotnet build -c Release
```

The executable will be in:
`bin\Release\net8.0-windows\win-x64\publish\DogePet.exe` (after publish)

Or simply:

```powershell
dotnet run
```

### Single-file publish (recommended for sharing)

```powershell
dotnet publish -c Release -p:PublishSingleFile=true --self-contained false
```

## Adding / Replacing Sprites

Put your own 48–64 px PNGs (with transparent background preferred) in the `Sprites/` folder:

- `idle.png`
- `blink.png`
- `happy.png`
- `petted.png`
- `walk_right.png`

The app resizes them to the target pet size automatically.

## Controls

- **Left drag** — move the pet anywhere
- **Left click** — pat the Shiba (boosts happiness)
- **Right click** — context menu (reset position, exit)

## Future ideas
- Walking around the screen
- More animations (sleep, eat coin, zoomies)
- Persistent happiness across runs
- "Feed Doge coin" mini interaction

Made with ❤️ for the Doge community (and all Shiba lovers).
