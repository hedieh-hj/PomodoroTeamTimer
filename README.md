# Pomodoro Team Timer

A synchronized Pomodoro timer for teams. One person hosts a room, teammates join with a six-digit code, and everyone sees the same authoritative timer in real time.

![.NET](https://img.shields.io/badge/.NET-7.0-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows-0078D4) ![License](https://img.shields.io/badge/license-MIT-green)

## Download and run (no installation required)

1. Download `PomodoroTeamTimer-win-x64.zip` from the [latest release](https://github.com/hedieh-hj/PomodoroTeamTimer/releases/latest).
2. Extract the ZIP file.
3. Run `PomodoroTeamTimer.Tray.exe`.
4. If Windows Firewall asks for permission, allow access on **Private networks**.

The portable package includes the .NET runtime. Users do not need to install .NET, a database, or any other dependency. The tray menu displays the LAN address that teammates should open in their browsers.

> Windows SmartScreen may warn about an unsigned executable. Choose **More info → Run anyway** if you downloaded it from this repository's Releases page.

## Features

- Create a focus room with a six-digit join code
- Join a room with a display name
- Server-authoritative timer synchronized over SignalR
- Host controls for start, pause, resume, reset, end, and skip break
- Automatic focus/break cycles
- Online/offline presence using heartbeats
- Browser notifications and an audible alert at phase changes
- Session history and daily statistics stored locally
- Responsive Persian RTL interface
- Installable browser PWA
- Windows System Tray host
- Runs entirely on a local network without internet access

## Running from source

Requirements: .NET 7 SDK on the host computer.

```powershell
dotnet run --project PomodoroTeamTimer.Server
```

Open `http://localhost:5080` on the host. Teammates can join at `http://HOST-IP:5080`.

To run the development tray launcher:

```powershell
dotnet build PomodoroTeamTimer.sln
.\PomodoroTeamTimer.Tray\bin\Debug\net7.0-windows\PomodoroTeamTimer.Tray.exe
```

## Building the portable package

Run:

```powershell
.\scripts\publish-windows.ps1
```

The script creates `artifacts/PomodoroTeamTimer-win-x64.zip`. The package is self-contained and can be copied to any 64-bit Windows 10/11 computer.

## Architecture

The ASP.NET Core server serves both the web UI and a SignalR hub. Each phase's end time is stored on the server, so page refreshes, client sleep, and short network delays do not cause timer drift. State and history are persisted atomically as local JSON under `Server/App_Data`; no external database or cloud service is required.

The portable tray launcher starts the bundled self-contained server, opens the app in the default browser, exposes the LAN address, and shuts the server down when the tray app exits.

## Network and security notes

- The server listens on port `5080` on all network interfaces.
- Use the app only on trusted private networks.
- Room host privileges are protected by a random host token stored in the host browser.
- Session data remains on the host computer.

## License

MIT
