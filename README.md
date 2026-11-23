# Kingdom Charters

At hero selection, press **Play** to choose a charter. Inspect the terms, then choose **Start Run**. Back returns to the same choice. Standard Run is focused each time.

| Choice | Terms |
| --- | --- |
| Standard Run | Use the usual rules. |
| Merchant Charter | The first scheduled caravan tries to arrive one day early. Its gate must already be open and the guest slot free. If the early visit cannot take place, the normal appointment stands. Each accepted caravan item costs one extra coin. |
| Forester’s Charter | Gain 3 wood with starting supplies. Pay the next 2 stone credited to your stores after those supplies. |
| Mason’s Charter | Gain 2 stone with starting supplies. Pay the next 3 wood credited to your stores after those supplies. |

The outstanding material debt appears in the lower-left corner. The charter travels with the run save, including its normal backup. Existing saves open under standard rules. Challenge runs use their own rules. With Daily Caravan loaded, Merchant remains visible with an explanation and cannot be selected. If you enable Daily Caravan during an existing Merchant run, its early visit and item fee are suspended while that mod is loaded.

## Local import and controller playtest

1. In Thunderstore Mod Manager, select **Super Fantasy Kingdom** and open **Test - Mods**. Use **Settings → Import local mod**, choose `KingdomCharters-0.1.0.zip`, and enable the package and its listed dependencies. Coordinate the game session with anyone using this PC before choosing **Start modded**. The manager profile and the in-game save slot are separate. Use an agreed test save for new runs.
2. At hero selection, choose **Play** using only the controller. Check that Standard Run has focus and that moving through the list exposes both sides of every deal. Open Forester, keep Confirm held, then release. The review should stay open. Back should return to Forester. A fresh Confirm on **Start Run** should start it once.
3. Repeat the picker using only keyboard, then mouse. Switch devices mid-review and check focus, binding hints and glyphs. Arrow keys, Enter and Escape are keyboard fallbacks.
4. Compare Forester and Mason with Standard in the same kingdom. Starting supplies should differ by the listed gift. Collect part of the debt, reach a normal save checkpoint, and resume. Check both the remaining debt and stock. Finish the debt and verify the next delivery increases stock normally.
5. For Merchant, test with Daily Caravan disabled and the gate unlocked. A standard day-16 appointment can move to day 15 if the slot is free. Check for a duplicate on day 16. Browse, decline, try with zero coins, then buy with one coin. Only the accepted item should cost a coin. Enable Daily Caravan for a separate launch and verify the picker explains the unavailable choice.
6. Start a challenge after a chartered run. Check that it skips the picker, uses normal challenge resources and prices, and that returning to a new normal run focuses Standard.

## Building locally

Use the installed game assemblies and a Thunderstore profile containing BepInEx 5. Run `dotnet build KingdomCharters.csproj -c Release`, then `python package.py`. Override `GameDir` and `ProfileDir` with MSBuild properties for other installations. The ZIP is written to `dist/`.

Run `python tests/smoke.py` on 64-bit Windows to load the packaged DLL in the installed game’s embedded Mono runtime, exercise charter and storage rules, and install and remove the Harmony patches. This offline harness runs without a Unity scene or controller. Mono can print unresolved Unity engine internal-call diagnostics in this mode. The harness returns its assertion result as the exit code.

## Contributing and license

Bug reports and pull requests are welcome. See the [contribution guide](CONTRIBUTING.md) for local testing and reporting instructions.

The original mod code is available under the [MIT License](LICENSE). Super Fantasy Kingdom, its game assets, and third-party dependencies retain their respective licenses and ownership.
