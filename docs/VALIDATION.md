# Validation scope — v0.4

## Verified without game input

- Windows process-memory read/write and readback, read-only guards, null-address guards and stat range validation in an isolated test process.
- Restoration of all six native global flags, preservation of unrelated actor bits, restoration of clock scale, preservation of later external changes, and rejection of stale actor/clock object ownership in a separate test process.
- Overlay hidden startup, no taskbar entry, borderless layout, F1-message show/hide, Escape hide, retained toggle state while hidden, close-to-tray versus explicit exit, and positioning on negative monitor coordinates and small windows.

## Verified on the supported local build during development

- Character ownership and stat/skill module identification.
- Reversible HP, FP and stamina restoration.
- Reversible roundtrips of native feature flags and actor invulnerability/no-death bits.
- A game-clock freeze with unchanged packed time for 350 ms, followed by restoration of the original scale and observed clock progression.
- Isolated emulation of the supported executable's getters confirmed the invulnerability/no-death branches, cooldown bypass and full-ultimate getter behavior. The emulator and extracted game instruction bytes are not redistributed.
- Preliminary manual feedback indicated that the v0.3 feature toggles appeared to work. This is not a comprehensive gameplay certification.

## Not established

- End-to-end physical F1 operation and composition over the running game's window for v0.4. The attempted automated desktop check was interrupted before completion.
- Exclusive fullscreen overlay rendering.
- Every character, boss phase, scripted death, item-consumption path or mission transition.
- Universal compatibility with all executables carrying the same version number.
- Account safety, anti-cheat compatibility, successful official reward validation, or policy/legal approval.

The source retains conservative type/ownership/value checks and initial-off defaults. These checks reduce erroneous writes but do not establish the untested claims above.
