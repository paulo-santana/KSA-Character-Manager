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

To give your characters idle variants on the ground, list them in your asset XML, right in the character's `CharacterGroundAnimations`, as `GltfFile`s like the game's own clips:

```xml
<CharacterGroundAnimations Id="MyGroundAnimations">
    <AnimIdle Id="MyAnimIdleGlb"/>
    <CharacterManager.AnimIdleAction Id="MyAnimIdleAGlb"/>
    <CharacterManager.AnimIdleAction Id="MyAnimIdleBGlb"/>
    ...
</CharacterGroundAnimations>
```

The `CharacterManager.` prefix marks a line only this mod reads; the game skips it, so your mod still loads without Character Manager. A standing character plays a random one after 10 to 20 seconds of idle, then goes back to `AnimIdle`.

To have crew in every new game, add `[[crew]]` entries:

```toml
[[crew]]
name = "Jebediah Kerman"
character = "KerbalOrange"       # a character your mod defines
```

A mod can only describe its own characters and give crew its own characters, so two mods can't fight over one.

The name list has one name per line, optionally with a pool in front: `common-m Bob`. A line without a pool is in the pool `common`, and `#` starts a comment. Without `pools`, a character uses every pool except `veteran`. As in KSP, names from pools whose name starts with `special` are drawn 1 time in 20.

[KSP Characters](https://github.com/paulo-santana/KSA-KSP-Characters) is a complete example.

## Building

`dotnet build -c Release` writes `mod/CharacterManager/CharacterManager.dll`. It expects the game in `~/Games/Ahwoo/Kitten Space Agency` and StarMap in `~/Games/starmap`; pass `-p:GameDir=...` and `-p:StarMapDir=...` to use other paths.

## License

MIT, see `LICENSE`. This is a fan project, not affiliated with or endorsed by RocketWerkz or Ahwoo.
