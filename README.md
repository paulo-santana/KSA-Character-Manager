# Character Manager for Kitten Space Agency

Lets you choose which character mods your crew roster draws from, and gives each character the names its mod provides.

**It does nothing on its own.** It manages the characters that other mods add, such as [KSP Characters](https://github.com/paulo-santana/KSA-KSP-Characters). With only the game's own kittens installed, there is nothing to choose between.

It works with any character mod. A mod doesn't need to know about this one: every character the game has loaded shows up. A mod can also come with a name list (see below), and then its characters are named from it.

## What it does

Open **View > Mods > Characters** in game.

- **Roster Character Models:** one checkbox per mod with characters, including the game's own kittens (`Core`). New recruits are picked only from the mods that are ticked. Untick `Core` and new crew are only characters from your mods.
- **Names:** a recruit whose character comes from a mod with a name list gets a name from that list, e.g. "Bob Kerman" for a Kerbal from KSP Characters. Other recruits keep the game's kitten names.
- **Fixed crew:** a mod can name crew that every new game starts with, e.g. Jebediah, Bill, Bob and Valentina Kerman from KSP Characters, while that mod is ticked. They join right after the game's own three, in place of random recruits. In a Testing start the first two are seated in the starting vehicles.
- **Idle variety:** a crew member standing still on the ground plays one of their character's idle variants now and then, e.g. KSP's idle animations for the Kerbals of KSP Characters. The game has this only for seated crew and the jetpack.
- **Ladders:** a character can have its own clips for holding still on a ladder and for climbing down; the game has one climbing clip for both directions and freezes it whenever the position doesn't change.
- **RCS stance:** while the jetpack's RCS is on, a character can stand, walk and jump with its own clips, and play a toggle animation when RCS is turned on or off while standing. Its jetpack's arms can come out with their own clip, in step with the body; the game plays its one arm clip backwards.
- **Jetpacks:** a character with its own jetpack can put the RCS puffs at that jetpack's thrusters. Only the visuals move; the thrust stays the game's. Untick "RCS puffs from the character's own jetpack" to see the game's positions.
- **Current Game:** "Regenerate unused recruits" replaces the crew in the current game who have never flown, aren't seated and aren't on EVA, using the options above. Click it twice to confirm.

The game's three starting crew, Hunter, Banjo and Polaris, are left as they are.

The options are saved for the whole install, in `Documents/My Games/Kitten Space Agency/CharacterManager.json`. They apply to crew recruited from then on: a new game, more recruits, or a regenerate. Crew that already exist keep their character and name.

## Install

Character Manager is a code mod, so it needs the [StarMap](https://github.com/StarMapLoader/StarMap) mod loader.

1. Copy the `CharacterManager` folder into `Documents/My Games/Kitten Space Agency/mods/`.
2. Add these lines to `Documents/My Games/Kitten Space Agency/manifest.toml`:

   ```toml
   [[mods]]
   id = "CharacterManager"
   enabled = true
   ```

3. Start the game through StarMap.

## For character mod authors: `characters.toml`

Put a `characters.toml` next to your `mod.toml` to give your characters display names and a name list. The game ignores the file, so your mod still works without Character Manager.

```toml
[[character]]
id = "Kerbal"                    # a character your mod defines
name = "Kerbal (white suit)"     # shown in the Characters window
names = "names/kerbal_names.txt" # name list, relative to your mod folder
pools = ["common-m", "special-m"]  # optional: which pools of the list this character uses
surname = "Kerman"               # optional: appended to every name
```

To have specific crew definitions in every new game, add `[[crew]]` entries:

```toml
[[crew]]
name = "Jebediah Kerman"
character = "KerbalOrange"       # a character your mod defines
```

A mod can only describe its own characters, and its fixed crew (`[[crew]]`) can only use its own characters, so two mods can't fight over one.

The name list has one name per line, optionally with a pool in front: `common-m Bob`. A line without a pool is in the pool `common`, and `#` starts a comment. Without `pools`, a character uses every pool except `veteran`. As in KSP, names from pools whose name starts with `special` are drawn 1 time in 20.

### Animation extensions

Animations the game has no slot for go in your asset XML, inside the character's `CharacterGroundAnimations`, each pointing at a `GltfFile` like the game's own clips:

| Tag | Where | How many | Plays |
| --- | --- | --- | --- |
| `CharacterManager.AnimIdleAction` | after `AnimIdle` | any | an idle variant now and then while standing |
| `CharacterManager.AnimLadderIdle` | after `AnimLadder` | one | while holding still on a ladder |
| `CharacterManager.AnimLadderDown` | after `AnimLadder` | one | while climbing down a ladder |
| `CharacterManager.Anim<slot>Rcs` | after the slot | one per slot | instead of the slot while RCS is on; for `AnimIdle`, `AnimWalk`, `AnimMoonWalk`, `AnimMoonRun`, `AnimJump`, `AnimJumpLand`, `AnimTumble` |
| `CharacterManager.AnimRcsOn` | after `AnimIdle` | one | once, when RCS is turned on while standing |
| `CharacterManager.AnimRcsOff` | after `AnimIdle` | one | once, when RCS is turned off while standing |

Two more go elsewhere:

| Tag | Where | How many | Does |
| --- | --- | --- | --- |
| `CharacterManager.MMUArmExtendAnim` | in `CharacterMMUAnimations`, after `MMUArmRetractAnim` | one | the jetpack's arms coming out when RCS is turned on (the game plays `MMUArmRetractAnim` backwards); it uses the same skeleton |
| `CharacterManager.RcsPuff` | in the jetpack's `CharacterAttachment` | one per nozzle | moves that nozzle's RCS puff (see below) |

Idle variants on the ground:

```xml
<CharacterGroundAnimations Id="MyGroundAnimations">
    <AnimIdle Id="MyAnimIdleGlb"/>
    <CharacterManager.AnimIdleAction Id="MyAnimIdleAGlb"/>
    <CharacterManager.AnimIdleAction Id="MyAnimIdleBGlb"/>
    ...
</CharacterGroundAnimations>
```

The `CharacterManager.` prefix marks a line only this mod reads; the game skips it, so your mod still loads without Character Manager. Each tag points at its `GltfFile` with `Id`, as the game's own tags do today, or with `Ref`, the game's coming form for references. A standing character plays a random one after 10 to 20 seconds of idle, then goes back to `AnimIdle`.

Ladder clips go right after `AnimLadder`, one of each at most:

```xml
    <AnimLadder Id="MyAnimLadderUpGlb"/>
    <CharacterManager.AnimLadderIdle Id="MyAnimLadderIdleGlb"/>   <!-- holding still -->
    <CharacterManager.AnimLadderDown Id="MyAnimLadderDownGlb"/>   <!-- climbing down -->
```

A character with either one also climbs smoothly: the game freezes its ladder clip between physics steps, and Character Manager lifts that freeze while it climbs.

RCS clips go right after the slot they stand in for, and the toggles after `AnimIdle`:

```xml
    <AnimIdle Id="MyAnimIdleGlb"/>
    <CharacterManager.AnimIdleRcs Id="MyAnimIdleRcsGlb"/>   <!-- standing with RCS on -->
    <CharacterManager.AnimRcsOn Id="MyAnimRcsOnGlb"/>
    <CharacterManager.AnimRcsOff Id="MyAnimRcsOffGlb"/>
```

A toggle plays only when the character stands still, as in KSP; while moving, the stance just switches.

RCS puffs are given per nozzle of the game's EVA jetpack (`KittenBackPackSubPart` in `Core/PartGameData.xml`, e.g. `TranslateRight1`), in that part's frame: metres, the feet at the origin, +X forward, +Y right, -Z up. `X`, `Y`, `Z` are where the puff starts; `DX`, `DY`, `DZ`, optional, the direction it blows:

```xml
<CharacterAttachment Id="MyJetpackAttachment">
    ...
    <CharacterManager.RcsPuff Nozzle="TranslateRight1" X="-0.22" Y="-0.17" Z="-0.46" DX="0" DY="-1" DZ="0"/>
</CharacterAttachment>
```

Only the puffs, trails and exhaust light move; each nozzle still pushes from where the game puts it.

[KSP Characters](https://github.com/paulo-santana/KSA-KSP-Characters) is a complete example.

## Building

`dotnet build -c Release` writes `mod/CharacterManager/CharacterManager.dll`. It expects the game in `~/Games/Ahwoo/Kitten Space Agency` and StarMap in `~/Games/starmap`; pass `-p:GameDir=...` and `-p:StarMapDir=...` to use other paths.

## License

MIT, see `LICENSE`. This is a fan project, not affiliated with or endorsed by RocketWerkz or Ahwoo.
