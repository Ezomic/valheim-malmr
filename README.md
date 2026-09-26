# Malmr

Tap Left Alt with a pickaxe out and vein mining is on. Every blow you land on an ore deposit
then fills a bar instead of breaking the chunk you hit. When the bar reaches 100%, the whole
deposit breaks at once and all of its ore drops, including the chunks that were still under the
ground. Each metal opens at its own Pickaxes level, late, and once you have beaten the boss of
its biome at one star.

*Malmr* is Old Norse for ore, and for metal. The mod is about both: you mine the ore, and it
unlocks by the metal it smelts into.

## Why

A copper deposit comes apart in a lot of chunks, and chasing them one at a time is the part of
mining nobody enjoys. You walk around the rock, dig after the bits that sank into the ground,
and pick up the ore from five places. Vein mining mods exist for that. The popular one breaks
the whole rock on one hit while you hold a key. That turns a deposit into a click, and you have
it from the moment you install it.

Malmr is slower than that on purpose. The bar only fills as fast as your swings would have
broken the rock by hand, so a deposit still costs you about as many swings, as much stamina and
as much pickaxe wear. You save the walking between chunks and the digging after the buried ones.

It also opens late. Tin opens at Pickaxes 40 and bloodgold at 90, so you have mined every metal
by hand for a long while before this changes anything. A metal also waits for the boss of the
biome it comes from, beaten at one star through [Vandi](https://github.com/Ezomic/valheim-vandi).
Vandi brings a boss back one star harder every time you kill it again, so the one-star fight is
your second kill of that boss. Your first kill opens the next biome as it always did.

## How it works

- Tap Left Alt with a pickaxe in your hands. A small "Vein mining" tag appears under the
  crosshair. Tap it again to switch it off. It stays on until you do, across swings, tool
  changes and deposits.
- Swing at any chunk of a deposit whose metal is open to you. The chunk does not break. The
  damage your blow would have done to it goes into the bar instead, and the usual damage number
  still floats up.
- The bar sits under the crosshair while you look at the deposit or have just hit it, and reads
  like "Copper vein 45%".
- 100% is the health of every chunk still standing, buried ones included. When the bar gets
  there, the deposit breaks. The chunks go from the top down in a fraction of a second, each
  drops its ore where it was, and ore from under the ground pops up to the surface.
- The bar is saved on the deposit. Walk away, switch tools or log out, and it is still there when
  you come back. A friend with vein mining on can finish it for you.
- If somebody mines chunks off the same deposit by hand in the meantime, the total shrinks and
  the percentage goes up with it.
- If your pickaxe is too weak for the rock, you get the game's own "too hard" and the bar does
  not move.
- With vein mining on and a metal you have not opened yet, you mine the normal way. Once per
  deposit the top left of the screen tells you what is missing, for example "Iron veins need
  Pickaxes 60 and Bonemass beaten at one star".
- A whole rock that has not cracked yet, like a copper deposit you have not hit before, breaks
  into its chunks the normal way first. The bar starts on the cracked deposit.
- When a metal opens, the game tells you in the middle of the screen, once, and names the key.
  That happens on the level-up or on the boss kill, whichever came last.

Each swing costs stamina, wears the pickaxe and trains Pickaxes exactly as it does in vanilla,
whether its damage goes into a chunk or into the bar. By hand, the last blow
on a chunk wastes whatever it does past that chunk's health, and the bar wastes nothing. By hand,
a chunk whose support you broke falls for free, and the bar charges for every chunk. So some
deposits take a few swings more than by hand and some a few less.

## The unlock table

| Metal | Pickaxes level | Boss, killed twice |
| --- | --- | --- |
| Tin | 40 | The Elder |
| Copper | 50 | The Elder |
| Iron | 60 | Bonemass |
| Silver | 70 | Moder |
| Flametal | 80 | Fader |
| Bloodgold (`Gold` in the config) | 90 | Fader |
| Any other ore a furnace takes | 90 | none |

The level that counts is the one you earned, the big number on the skills page. A bonus on top of
it from gear, food or another mod makes each blow harder, as it always does, but it does not open
a metal early.

Iron covers anything whose drop smelts into iron, so muddy scrap piles count. Obsidian is not on
the list because nothing smelts it. You can add it with `Obsidian:70` in `Unlocks`, and it already
has Moder as its boss.

An ore from another mod that nobody named in the table falls under the last row. It sits at 90
because Malmr has no way to know which biome that ore belongs to, and last is the one place where
it can never open before the vanilla metals. Give it its own line in `Unlocks` if it belongs
earlier.

## The boss

Only kills Vandi counts are counted here, which means a boss you summoned yourself at its altar.
Helping a friend with their Bonemass does not count for you, the same as in Vandi. Your first
kill is the boss as the game ships it and your second is the boss at one star. Malmr waits for
that second kill. Vandi counts kills rather than stars, so if a server turns off Vandi's harder
bosses the second kill still counts.

Vandi keeps the count in the world, not on your character. A character that moves to a new world
starts there without its boss kills, and its veins in that world wait for them again.

Bloodgold waits for Fader, the same as flametal, because Vandi and Utangard both give the Deep
North to Fader and a boss Vandi does not count can never be met. The Deep North does have a boss
of its own in 1.0. Once its defeat key has been read in game it can go into Vandi's `BossBiomes`
and into `Bosses` here. The log lists every boss in the world with its key when a world loads.

If `Bosses` names a boss Vandi does not count, or one nothing in the world ever sets, its metals
can never open. The log says so on world load and so does the `malmr` command.

## How it knows what a deposit is

It reads the drops. Every deposit has a drop table, and every smelter and furnace in the game
lists what it takes in and what comes out. A deposit that drops copper ore is a copper deposit
because copper ore smelts into copper. The same goes for an ore another mod adds, as long as a
furnace smelts it.

A furnace here is a station that makes one of the metals in the table, or burns the same fuel as
one that does. That covers the smelter, the blast furnace and a mod's own coal forge. The
charcoal kiln, the windmill and the eitr refinery are not furnaces, so nothing that only goes into
them counts as ore.

When a world loads, the log gets one block listing every deposit in it, what it counted as and
why, and any metal in the table that nothing matched. If a deposit comes out wrong, name it in
`Deposits` and that wins over the drops.

## Console

No devcommands needed for any of these.

- `malmr` prints your Pickaxes level, whether vein mining is on, each metal's level and boss,
  your kills of that boss, whether the metal is open and what is still missing, and the deposit
  list.
- `malmr vein on` and `malmr vein off` do what tapping the key does.
- `malmr progress` shows the bar of the nearest deposit within 10 metres: the metal, the percent,
  the damage stored against the total, and how many chunks are still standing.

## Installing

Needs BepInEx and Vandi. Through a mod manager it is one install, because Vandi comes along. By
hand, put `Malmr.dll` in `BepInEx/plugins/Malmr/` and install Vandi as well. Without Vandi,
BepInEx does not load Malmr at all and says why in its log.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

## Settings

The file is `BepInEx/config/ezomic.valheim.malmr.cfg`. Every setting has a comment above it with
the reasoning, so the file explains itself. The ones worth knowing about:

- `VeinToggleKey` is the key, Left Alt by default. It only listens while a pickaxe is out, so
  Jafna's Left Alt on the hoe is left alone. A tap is a short press and release, so Alt+Tab and
  Taum's Alt+E on a boar do not switch it.
- `Unlocks` is the table above, as one line: `Tin:40, Copper:50, ...`. Add a metal, change a
  level, or set one to -1 to switch it off.
- `Bosses` is the boss column of the table, as one line: `Copper:defeated_gdking, ...`. A metal
  left out needs no boss.
- `BossKills` is how many kills of that boss you need. 2 is the one-star kill. 0 turns the boss
  half off and every metal opens on the level alone.
- `Deposits` overrides what a deposit counts as, by its prefab name.

Changing a default in a new version does nothing on a machine that has already run the mod.
BepInEx writes every entry on first run and the saved value wins.

## Multiplayer

**Everyone needs it, the server too.**

Your skill is saved with your character, so only your own machine knows whether a metal is open
for you. That part is decided where you swing. The bar is saved on the deposit, and in Valheim
only the player or server that owns an object may change it. So your blow is sent to whoever
owns that deposit, the same way a normal pickaxe hit is, and their Malmr adds it to the bar and
breaks the deposit when it is full. The owner can be any player near the rock, or the server. A
machine without Malmr cannot do that, and a blow sent to it is simply lost.

With [Core](https://github.com/Ezomic/valheim-core) installed, Malmr registers with its version
gate, which keeps out anyone who does not have the same Malmr. The host's unlock table, bosses
and deposit list apply to everyone connected, in memory only. Your own config file comes back the
moment you disconnect. Your key stays your own. Without Core the mod still runs, but nothing
checks that everybody has it and each player plays by their own file.

Vandi needs everyone to have it as well, so on a server that runs both, nothing extra is asked of
anyone.

## What has not been tested

None of it has run in game yet. That covers the bar filling, the whole deposit breaking and where
its ore lands, the key and the marker, the bar surviving a logout, and two players on one rock.
The deposit list, whether the Ashlands flametal and the Deep North bloodgold deposits are
recognised, and the boss half also still have to be seen in a real session.

## Bugs and ideas

Both go to the site. [longhouse.thijssensoftware.nl/bugs](https://longhouse.thijssensoftware.nl/bugs)
is for anything broken, and [longhouse.thijssensoftware.nl/ideas](https://longhouse.thijssensoftware.nl/ideas)
is for what a mod should do next. You can vote on other people's ideas there as well.

Signing in takes a Steam or Discord account. I work from that list, so the votes decide what
I pick up next.

## Licence

MIT. See `LICENSE`.
