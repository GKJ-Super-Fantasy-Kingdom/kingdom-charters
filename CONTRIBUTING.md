# Contributing to Kingdom Charters

## Report a bug

Open a [GitHub issue](https://github.com/GKJ-Super-Fantasy-Kingdom/kingdom-charters/issues) with the mod, game, and BepInEx versions, your kingdom and day, and the steps to reproduce the problem. Include the relevant log excerpt. For interface or input issues, include the UI scale and whether you used a controller or keyboard and mouse.

## Submit a change

Fork the repository, create a branch, and follow the build and packaging instructions below. Use the assemblies from your own game installation and BepInEx profile as build references. Keep game binaries, save files, and local build output out of commits.

Describe the player-visible change in your pull request. List the checks you ran and, where relevant, which controller actions and UI scales you tested.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) for each commit. Start the subject with a type, an optional scope, and a short description. Use an exclamation mark before the colon for a breaking change.

Examples include `feat(ui): show controller prompts`, `fix(save): restore the current day`, and `chore: update build tooling`.

Accepted types are `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`. Use the same format for pull request titles so GitHub can use the title as the merge or squash commit subject.

## Building and Testing

Build against your installed game and a BepInEx 5 profile, then package and test the Release DLL.

```powershell
dotnet build KingdomCharters.csproj -c Release
python package.py
python tests/smoke.py
```

The ZIP is written under `dist/`. For other installations, set the MSBuild properties `GameDir` and `ProfileDir`, and pass `--game` and `--profile` to the smoke test. The profile must contain the BepInEx core assemblies. The offline harness uses the installed game’s embedded Mono without launching a player.

For companion regression checks, run `python tests/smoke.py --compat-dlls PATH` with the unpacked [ExtraStartingCarrier](https://thunderstore.io/c/super-fantasy-kingdom/p/ownlyme/ExtraStartingCarrier/) 1.0.0, [CheatMenu](https://thunderstore.io/c/super-fantasy-kingdom/p/ILM/CheatMenu/) 1.0.3, [Daily Caravan](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/DailyCaravan/) 0.1.1 and [Caravan Commissions](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/CaravanCommissions/) 0.1.2 DLLs in one directory. These mods are optional for gameplay.
