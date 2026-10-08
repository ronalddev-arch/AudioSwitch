# Contributing to AudioSwitch

Thanks for helping. AudioSwitch is a small project maintained by one person (the Project Owner), so a short issue first is the quickest way to find out whether a change fits before you spend time on it.

## Build and test

You need Windows and the .NET 10 SDK.

```powershell
dotnet build AudioSwitch.slnx                 # must finish with 0 warnings
dotnet test tests/AudioSwitch.Core.Tests      # pure logic, no audio devices needed
```

### Code layout
- **`src/AudioSwitch.Core`**: everything except the UI (no WinForms).
  - `Audio/`: the Windows side. Enumerating endpoints, default-device events, setting the default device (`PolicyConfig`, undocumented COM) and disconnecting Bluetooth devices.
  - `Sonar/`: the SteelSeries side. All knowledge of Sonar's unofficial API (routes, JSON fields, event names) is in `SonarProtocol.cs`; the rest of the code goes through `ISonarClient`. `SonarEventStream` is the WebSocket listener.
  - `Watching/`: what is usable right now. `AudioWatcher` debounces Windows and Sonar events, rebuilds an `AvailabilitySnapshot` and diffs it into device changes; `DeviceFilter` holds the ignored devices.
  - `Routing/`: what to do about it. `RoutingEngine` is pure and holds the switching rules (each decision carries a reason that is logged); `RoutingExecutor` applies a plan; `AudioSwitchService` orchestrates one change at a time.
  - `Settings/`: `settings.json` (schema-versioned, `Migrate()`), `state.json` (recently used outputs), the first-run setup logic and the device picker's suggestions.
- **`src/AudioSwitch.App`**: the WinForms tray app. `TrayApplicationContext` is the composition root; the popup (`SwitchPromptForm`), toast, Settings, first-run, device picker and About windows; `UpdateChecker` (Velopack).
- **`tests/AudioSwitch.Core.Tests`**: xUnit tests of the pure logic. `RoutingEngineTests` plays out each switching rule as one scenario; `Fixtures/` holds real Sonar API responses.
- **`tools/`**: `publish.ps1` (your own build, see below), `pack.ps1` (release packaging), `make-icon.ps1` (regenerates `app.ico`).

Threading: NAudio's device callbacks run on an audio worker thread and must not call back into Windows audio, so they only queue a trigger for the watcher. Service events are marshalled to the UI thread.

Running the app changes your real audio routing (it may re-route Sonar or disconnect a Bluetooth device on startup). Turn the volume down before trying a change live.

### Run your own build
`powershell -File tools/publish.ps1 -Start` installs a framework-dependent build (needs the .NET 10 Desktop Runtime) to `%LocalAppData%\Programs\AudioSwitch\AudioSwitch.exe`, stopping and restarting a running copy. Such a build doesn't update itself; About says so. It is a separate copy from one installed with Setup.exe (`%LocalAppData%\AudioSwitch`); both use the same settings in `%AppData%\AudioSwitch`.

## Releases (Project Owner)
Releases are made with [Velopack](https://velopack.io) (the `vpk` CLI is a repo-local dotnet tool in `.config/dotnet-tools.json`). `RepositoryUrl` in `Directory.Build.props` points at https://github.com/ronalddev-arch/AudioSwitch; installed copies look for updates in its GitHub Releases. A build without it reports updates as "not configured".
1. Bump `<Version>` in `Directory.Build.props` (semantic version; updates only go up). Re-check the installed SteelSeries GG and Sonar versions and update `VerifiedGgVersion` / `VerifiedSonarVersion` there.
2. `powershell -File tools/pack.ps1`. This makes a self-contained win-x64 build and packs it into `artifacts\releases`: `AudioSwitch-win-Setup.exe`, the full (and, from the second release on, delta) `.nupkg` update packages and `releases.win.json`. It first downloads the latest published release to build the delta (`-NoDeltas` skips that).
3. Upload everything in that folder as a GitHub release:
   ```powershell
   dotnet vpk upload github -o artifacts\releases --repoUrl https://github.com/ronalddev-arch/AudioSwitch --token <github-token> --publish --tag v<version>
   ```
   Installed copies find it at their next check. Don't run the produced Setup.exe on a machine whose audio you care about: it installs and starts AudioSwitch.

## Code style

- **0 warnings.** Nullable reference types are on; keep the build clean.
- **Routing decisions live in `AudioSwitch.Core` and are covered by tests.** A change to a rule needs a test in `tests/AudioSwitch.Core.Tests` (usually a scenario in `RoutingEngineTests`) and, if users notice it, an update to the README.
- **Sonar protocol knowledge goes in `SonarProtocol.cs`** (routes, JSON field names, event names). The rest of the code goes through `ISonarClient`.
- **Match devices by name fragment, never by endpoint id.** Endpoint ids change when USB devices re-enumerate.
- **Settings are schema-versioned.** A new settings field means bumping `AudioSwitchSettings.CurrentSchemaVersion`, adding a step to `Migrate()` and a test to `SettingsTests`. Never overwrite a user's `settings.json` wholesale.
- Popups and toasts must never take focus (they are shown without activation so they don't interrupt games).
- Follow the existing formatting and naming; there is no separate formatter configuration.

## Branches and pull requests

- Contributions target the **`main`** branch. Branch from `main`, and open your pull request against `main`.
- Accepted pull requests are applied by the Project Owner rather than merged with GitHub's merge button: the pull request is closed, and the change appears in the next commit on `main`, which names you as co-author.
- Keep a pull request to one change, and describe what you tested (unit tests, and live testing with which devices and which SteelSeries GG / Sonar versions).
- The pull request template has a short checklist: tests pass, 0 warnings, CLA signed.

## Contributor License Agreement (CLA)

Before a pull request can be merged, every contributor must accept the [Individual Contributor License Agreement](CLA.md). You keep the copyright in your work; the CLA gives the Project Owner a license to use and distribute it (including a patent license), and confirms that you have the right to contribute it.

Signing works through a bot on the pull request:
1. When you open your first pull request, the CLA bot posts a comment and the `CLA Assistant` check fails.
2. Read [CLA.md](CLA.md), then reply on the pull request with exactly:

   ```
   I have read the CLA Document and I hereby sign the CLA
   ```
3. The bot records your GitHub user name, the date and the pull request in a signatures file in the repository, and the check turns green. You only sign once; later pull requests pass automatically.
4. If the check doesn't update, comment `recheck`.

**Contributing for an employer?** There is no separate corporate CLA for now. The individual CLA (section 4) requires you to have your employer's permission, or a waiver of their rights, before you contribute work they may own. If your employer wants a corporate agreement, open an issue and the Project Owner will sort it out.

## Reporting bugs

Open an issue with the steps to reproduce, your Windows, SteelSeries GG and Sonar versions, and the relevant part of the log from `%AppData%\AudioSwitch\logs\`. The log contains your audio device names, so check it before attaching it.

## License

By contributing, you agree that your contributions are licensed as described in [CLA.md](CLA.md), and the project is distributed under the [Apache License 2.0](LICENSE).
