# Malmr

Once your Pickaxes skill is high enough for a metal, a swing at that metal's deposit also takes
a few of the chunks next to the one you hit. Each metal opens at its own level.

*Malmr* is Old Norse for ore, and for metal. The mod is about both: you mine the ore, and it
unlocks by the metal it smelts into.

## Why

A copper deposit comes apart in a lot of chunks, and taking them off one swing at a time is the
part of mining nobody enjoys. Vein mining mods exist for that. The popular one takes the whole
rock in one go while you hold a key. That is quick, but it turns a deposit into a click, and the
key means you had it from the moment you installed it.

Malmr is narrower on purpose. A swing takes a handful of extra chunks, never the deposit. You
earn it per metal on the Pickaxes skill you already have, so tin opens early and bloodgold late.
And the extra chunks cost the pickaxe what mining them by hand would have cost, so mining gets
faster without getting cheaper.

## What it does

- A pickaxe swing at a deposit of an unlocked metal takes extra chunks beside the ones it hit,
  walking outward through chunks that touch, nearest first.
- One extra chunk at the unlock level, one more every 10 levels after it, and never more than
  4 extra chunks in one swing.
- Each extra chunk takes one of the blows your swing landed on the chunks you hit. A chunk that
  needs three hits by hand still needs three.
- Each extra chunk wears the pickaxe as much as that blow would have by hand. When the pickaxe
  hits zero the swing stops taking chunks.
- A chunk that falls by itself because the one under it broke does not use up one of the four.
- Chunks still under the ground are left alone. You dig down to a silver vein in vanilla and
  you still do.
- When a level-up opens a metal, the game tells you in the middle of the screen.
- There is no key. It works on the pickaxe in your hand once the metal is unlocked.
- No new items, prefabs or saved values. A world played with Malmr is an ordinary world.

## The unlock table

| Metal | Pickaxes level |
| --- | --- |
| Tin | 10 |
| Copper | 20 |
| Iron | 30 |
| Silver | 40 |
| Flametal | 60 |
| Bloodgold (`Gold` in the config) | 70 |
| Any other ore a furnace takes | 50 |

The levels follow the biomes. By the time a metal opens you have mined it by hand for a while.
Copper at 20 means one extra chunk at 20, two at 30, three at 40 and four from 50 on.

The level that counts is the one you earned, the big number on the skills page. A bonus on top
of it from gear, food or another mod makes each blow harder, as it always does, but it does not
open a metal early. The message arrives on the level-up that opens the metal, not before.

Iron covers anything whose drop smelts into iron, so muddy scrap piles count. Obsidian is not on
the list because nothing smelts it, so obsidian rocks stay vanilla unless you add them.

## What it costs

Pickaxe durability. A swing wears the pickaxe once, however many chunks it hits, and a swing
at a broken deposit often hits two or three. So one blow costs a share of a swing, and each
extra chunk pays exactly that share. A deposit wears the pickaxe the same amount whichever way
you mine it. What you save is time.

Stamina is not charged by default. At a full blow's stamina per chunk, a swing that hits one
chunk and takes four more would cost five swings of stamina and you would stop to rest every
two or three swings. `StaminaPerChunk` is there if you want it.

The extra chunks do not raise Pickaxes. The skill still counts your swings, so a deposit mined
along the vein teaches you less than one mined chunk by chunk. `ExtraChunksTrainSkill` turns
that around if you prefer it.

## How it knows what a deposit is

It reads the drops. Every deposit has a drop table, and every smelter and furnace in the game
lists what it takes in and what comes out. A deposit that drops copper ore is a copper deposit
because copper ore smelts into copper. The same goes for an ore another mod adds, as long as a
furnace smelts it. That ore gets the `*` level from the table until you give it its own.

A furnace here is a station that makes one of the metals in the table, or burns the same fuel
as one that does. That covers the smelter, the blast furnace and a mod's own coal forge. The
charcoal kiln, the windmill and the eitr refinery are not furnaces, so nothing that only goes
into them counts as ore.

When a world loads, the log gets one block listing every deposit in it, what it counted as and
why, and any metal in the table that nothing matched. If a deposit comes out wrong, name it in
`Deposits` and that wins over the drops.

In the console, `malmr` prints your Pickaxes level, what each metal gives you at it, and the
same deposit list. No devcommands needed.

## Installing

Needs BepInEx. Nothing else. Through a mod manager it is one install. By hand, put `Malmr.dll`
in `BepInEx/plugins/Malmr/`.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

## Settings

The file is `BepInEx/config/ezomic.valheim.malmr.cfg`. Every setting has a comment above it
with the reasoning, so the file explains itself. The ones worth knowing about:

- `Unlocks` is the table above, as one line: `Copper:20, Tin:10, ...`. Add a metal, change a
  level, or set one to -1 to switch it off.
- `MaxExtraChunks` and `LevelsPerExtraChunk` are the cap and how fast you grow into it.
- `Deposits` overrides what a deposit counts as, by its prefab name.
- `LeaveBuried` keeps the digging.
- `DurabilityPerChunk`, `StaminaPerChunk` and `ExtraChunksTrainSkill` are the costs.

Changing a default in a new version does nothing on a machine that has already run the mod.
BepInEx writes every entry on first run and the saved value wins.

## Multiplayer

**The host needs it for its settings to count. Nobody else needs it.**

Your skill is saved with your character, so only your own machine knows it. Malmr decides the
extra chunks there and sends each one to the deposit as an ordinary pickaxe hit, the same
message a swing sends. Whoever owns the deposit handles it the vanilla way and needs no mod.
A player without Malmr is let in and mines one chunk at a time.

If [Core](https://github.com/Ezomic/valheim-core) is installed, Malmr registers with its version
gate and the host's unlock table, cap and costs apply to everyone connected who has Malmr, in
memory only. Your own config file comes back the moment you disconnect. Without Core the mod
still runs, and each player plays by their own file.

A player with Malmr can also join a server that does not have it, and will vein mine there on
their own settings. The server has no way to tell those hits apart from normal ones.

## What has not been tested

None of it has run in game yet. The deposit list, whether the Ashlands flametal and the Deep
North bloodgold deposits are recognised, how many chunks a swing takes in practice, and
anything with a second player are all still to be seen.

## Bugs and ideas

Both go to the site. [longhouse.thijssensoftware.nl/bugs](https://longhouse.thijssensoftware.nl/bugs)
is for anything broken, and [longhouse.thijssensoftware.nl/ideas](https://longhouse.thijssensoftware.nl/ideas)
is for what a mod should do next. You can vote on other people's ideas there as well.

Signing in takes a Steam or Discord account. I work from that list, so the votes decide what
I pick up next.

## Licence

MIT. See `LICENSE`.
