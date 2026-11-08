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

3. Start the game through StarMap (0.5.2 or later on KSA 2026.11.10.5801).

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

Animations the game has no slot for, and RCS puff positions, go in a `CharacterManager.xml` next to your `mod.toml`. It is written the way the game patches an asset: each block names one of your mod's own assets by `Id` with `Modify="Patch"`, and lists sit in a container. The clips themselves are ordinary `GltfFile`s in your asset XML.

```xml
<Assets>
    <CharacterGroundAnimations Id="MyGroundAnimations" Modify="Patch">
        <CharacterManager.AnimIdleActions>
            <GltfFile Ref="MyAnimIdleAGlb"/>
            <GltfFile Ref="MyAnimIdleBGlb"/>
        </CharacterManager.AnimIdleActions>
        <CharacterManager.AnimLadderIdle Ref="MyAnimLadderIdleGlb"/>
        <CharacterManager.AnimIdleRcs Ref="MyAnimIdleRcsGlb"/>
        <CharacterManager.AnimRcsOn Ref="MyAnimRcsOnGlb"/>
    </CharacterGroundAnimations>
    <CharacterMMUAnimations Id="MyMMUAnimations" Modify="Patch">
        <CharacterManager.MMUArmExtendAnim Ref="MyArmsExtendGlb"/>
    </CharacterMMUAnimations>
    <CharacterAttachment Id="MyJetpackAttachment" Modify="Patch">
        <CharacterManager.RcsPuffs>
            <RcsPuff Nozzle="TranslateRight1" X="-0.22" Y="-0.17" Z="-0.46" DX="0" DY="-1" DZ="0"/>
        </CharacterManager.RcsPuffs>
    </CharacterAttachment>
</Assets>
```

**Don't list this file in `mod.toml`'s `assets`.** The game doesn't know the `CharacterManager.` fields, and since KSA 2026.11.10.5801 it drops a whole asset that holds a field it doesn't know. Kept out of `assets`, the game never reads the file, so your mod loads the same with or without Character Manager.

In `CharacterGroundAnimations`:

| Field | How many | Plays |
| --- | --- | --- |
| `CharacterManager.AnimIdleActions` | a list of `GltfFile`s | an idle variant now and then while standing |
| `CharacterManager.AnimLadderIdle` | one | while holding still on a ladder |
| `CharacterManager.AnimLadderDown` | one | while climbing down a ladder |
| `CharacterManager.Anim<slot>Rcs` | one per slot | instead of the slot while RCS is on; for `AnimIdle`, `AnimWalk`, `AnimMoonWalk`, `AnimMoonRun`, `AnimJump`, `AnimJumpLand`, `AnimTumble` |
| `CharacterManager.AnimRcsOn` | one | once, when RCS is turned on while standing |
| `CharacterManager.AnimRcsOff` | one | once, when RCS is turned off while standing |

In `CharacterMMUAnimations`, `CharacterManager.MMUArmExtendAnim` is the jetpack's arms coming out when RCS is turned on. The game has only `MMUArmRetractAnim` and plays it backwards. The extend clip uses the same skeleton.

In the jetpack's `CharacterAttachment`, `CharacterManager.RcsPuffs` lists one `RcsPuff` per nozzle of the game's EVA jetpack (`KittenBackPackSubPart` in `Core/PartGameData.xml`, e.g. `TranslateRight1`).
- The coordinates are in that part's frame: metres, the feet at the origin, +X forward, +Y right, -Z up.
- `X`, `Y`, `Z` are where the puff starts. `DX`, `DY`, `DZ`, optional, are the direction it blows.
- Only the puffs, trails and exhaust light move; each nozzle still pushes from where the game puts it.

How they play:
- **Idle variants:** a standing character plays a random one after 10 to 20 seconds of idle, then goes back to `AnimIdle`.
- **Ladder:** a character with either ladder clip also climbs smoothly. The game freezes its ladder clip between physics steps, and Character Manager lifts that freeze while it climbs.
- **RCS toggles:** these play only when the character stands still, as in KSP. While moving, the stance just switches.

[KSP Characters](https://github.com/paulo-santana/KSA-KSP-Characters) is a complete example.

## Building

`dotnet build -c Release` writes `mod/CharacterManager/CharacterManager.dll`. It expects the game in `~/Games/Ahwoo/Kitten Space Agency` and StarMap in `~/Games/starmap`; pass `-p:GameDir=...` and `-p:StarMapDir=...` to use other paths.

## License

MIT, see `LICENSE`. This is a fan project, not affiliated with or endorsed by RocketWerkz or Ahwoo.
