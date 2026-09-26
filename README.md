# Malmr

Tap Left Alt with a pickaxe out and vein mining is on. Every blow you land on an ore deposit
then fills a bar instead of breaking the chunk you hit. When the bar reaches 100%, the whole
deposit breaks at once and all of its ore drops, including the chunks that were still under the
ground. Each metal opens at its own Pickaxes level, late, and once you have beaten the boss of
its biome.

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

It also opens late. Copper opens at Pickaxes 30 and bloodgold at 80, so you have mined each
metal by hand for a while before this changes anything. A metal also waits for the boss of the
biome it comes from. With [Vandi](https://github.com/Ezomic/valheim-vandi) installed that means
the boss beaten at one star, and without it the boss beaten once.

## How it works

- Tap Left Alt with a pickaxe in your hands. A small gold "Vein" appears just above the
  crosshair. Tap it again to switch it off. It stays on until you do, across swings, tool
  changes and deposits.
- Swing at any chunk of a deposit that is open to you. The chunk does not break. The damage your
  blow would have done to it goes into the bar instead, and the usual damage number still floats
  up.
- The bar sits just under the crosshair while you look at the deposit or have just hit it. It is
  a thin gold bar with the name and the percentage under it, like "Copper vein 45%".
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
  Pickaxes 40 and Bonemass beaten at one star through Vandi".
- A whole rock that has not cracked yet, like a copper deposit you have not hit before, breaks
  into its chunks the normal way first. The bar starts on the cracked deposit.
- When a metal opens, the game tells you in the middle of the screen, once, says what opened it
  and names the key. That happens on the level-up or on the boss kill, whichever came last.

Each swing costs stamina, wears the pickaxe and trains Pickaxes exactly as it does in vanilla,
whether its damage goes into a chunk or into the bar. By hand, the last blow on a chunk wastes
whatever it does past that chunk's health, and the bar wastes nothing. By hand, a chunk whose
support you broke falls for free, and the bar charges for every chunk. So some deposits take a
few swings more than by hand and some a few less.

## The unlock table

| What | Pickaxes level | Boss |
| --- | --- | --- |
| Copper | 30 | The Elder |
| Iron | 40 | Bonemass |
| Silver | 50 | Moder |
| Giant brains (`Eitr` in the config) | 60 | The Queen |
| Flametal | 70 | Fader |
| Bloodgold (`Gold` in the config) | 80 | Fader |

Only these vein mine. Anything else is mined by hand.

Tin is not on the list. It comes in small rocks and there is no deposit of it worth a bar.
`Tin:20` in `Unlocks` would add it.

Obsidian is not on the list either, because nothing smelts it. `Obsidian:50` in `Unlocks` adds it,
at silver's level since it is the same biome, and it already has Moder as its boss.

The giant brains in the Mistlands are in the table as `Eitr`. A brain drops soft tissue and the
eitr refinery turns that into refined eitr, which is how Malmr recognises a brain. On screen they
are called giant brains.

The level that counts is the one you earned, the big number on the skills page. A bonus on top of
it from gear, food or another mod makes each blow harder, as it always does, but it does not open
a metal early.

Iron covers anything whose drop smelts into iron, so muddy scrap piles count.

## The Mistlands

In the Mistlands only the giant brains vein mine. Copper, iron or any other ore you find there is
mined the normal way, even when that metal is open to you. With vein mining on, the top left of
the screen says so once per deposit and no bar shows.

It goes by the biome the deposit stands in, the one the map shows at that spot. The `Mistlands`
line in the config says which entries still vein mine there.

## The boss

With Vandi installed, the boss has to be beaten at one star. Vandi brings a boss back one star
harder every time you kill it again, so the one-star fight is your second kill of it, and Malmr
waits for that. Only kills Vandi counts are counted, which means a boss you summoned yourself at
its altar. Helping a friend with their Bonemass does not count for you, the same as in Vandi.
Vandi counts kills rather than stars, so if a server turns off Vandi's harder bosses the second
kill still counts. Vandi keeps the count in the world, so a character that moves to a new world
starts there without its boss kills.

Without Vandi there are no stars, so one kill is enough. That is the game's own record of your
kills, the one it keeps on your character. Every player who landed a hit on the boss gets the
kill, not only the one who struck last. Because it is kept on the character, a boss you killed in
another world counts too.

The unlock message, the top left message and the `malmr` command all say which of the two applies:
"at one star through Vandi", or "beaten by you" without it.

Bloodgold waits for Fader, the same as flametal, because Vandi and Utangard both give the Deep
North to Fader. The Deep North does have a boss of its own in 1.0, and its key has not been read
yet. The log lists every boss in the world with its key when a world loads, so once somebody has
loaded a 1.0 world the Frozen King can go into Vandi's `BossBiomes` and into `Bosses` here.

If `Bosses` names a boss nothing in the world sets, or one Vandi does not count, its metals can
never open. The log says so on world load and so does the `malmr` command.

## How it knows what a deposit is

It reads the drops. Every deposit has a drop table, and every smelter and furnace in the game
lists what it takes in and what comes out. A deposit that drops copper ore is a copper deposit
because copper ore smelts into copper. The same goes for an ore another mod adds, as long as you
name its metal in `Unlocks` and some station makes it.

When a world loads, the log gets one block listing every deposit in it, what it counted as and
why, and any metal in the table that nothing matched. If a deposit comes out wrong, name it in
`Deposits` and that wins over the drops.

## Console

No devcommands needed for any of these.

- `malmr` prints your Pickaxes level, whether vein mining is on, which boss count applies, the
  Mistlands line, each metal's level and boss, your kills of that boss, whether the metal is
  open and what is still missing, and the deposit list.
- `malmr vein on` and `malmr vein off` do what tapping the key does.
- `malmr progress` shows the bar of the nearest deposit within 10 metres: the metal, whether it
  is open to you here, its biome, the percent, the damage stored against the total, and how
  many chunks are still standing. `malmr progress rock4_copper_frac` looks for the nearest one
  of that name instead.

## Installing

Malmr needs BepInEx and nothing else. Put `Malmr.dll` in `BepInEx/plugins/Malmr/`, or install it
with a mod manager.

I recommend [Vandi](https://github.com/Ezomic/valheim-vandi) alongside it. Vandi makes bosses you
kill again come back with stars, and with Vandi installed Malmr asks for the one-star kill rather
than a plain one. That second fight is what I think the vein should cost. Vandi is not pulled in
by a mod manager, so install it yourself if you want it.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

## Settings

The file is `BepInEx/config/ezomic.valheim.malmr.cfg`. Every setting has a comment above it with
the reasoning, so the file explains itself. The ones worth knowing about:

- `VeinToggleKey` is the key, Left Alt by default. It only listens while a pickaxe is out, so
  Jafna's Left Alt on the hoe is left alone. A tap is a short press and release, so Alt+Tab and
  Taum's Alt+E on a boar do not switch it.
- `Unlocks` is the table above, as one line: `Copper:30, Iron:40, ...`. Add a metal, change a
  level, or set one to -1 to switch it off.
- `Bosses` is the boss column of the table, as one line: `Copper:defeated_gdking, ...`. A metal
  left out needs no boss.
- `BossKills` is how many kills of that boss Vandi has to have counted. 2 is the one-star kill.
  Without Vandi it is not used, and one kill is enough. 0 turns the boss half off either way.
- `Mistlands` says which entries still vein mine in the Mistlands. `Eitr` by default, the brains.
- `Names` is what the screen calls a deposit of an entry. `Eitr:Giant brain` by default.
- `Deposits` overrides what a deposit counts as, by its prefab name.

Changing a default in a new version does nothing on a machine that has already run the mod.
BepInEx writes every entry on first run and the saved value wins.

## Multiplayer

**Malmr has to be on the server and on every client.** If you use Vandi, the same goes for
Vandi.

Your skill and the game's record of your kills are saved with your character, so only your own
machine knows whether a metal is open for you. That part is decided where you swing. The bar is
saved on the deposit, and in Valheim only the player or server that owns an object may change
it. So your blow is sent to whoever owns that deposit, the same way a normal pickaxe hit is, and
their Malmr adds it to the bar and breaks the deposit when it is full. The owner can be any
player near the rock, or the server. A machine without Malmr cannot do that, and a blow sent to
it is simply lost.

Vandi needs to be everywhere for its own reasons. It counts a boss kill on whichever machine
owned the boss when it died, and it keeps the count in the world. If some machines have it and
some do not, kills go uncounted and players on the same server end up under different rules.

With [Core](https://github.com/Ezomic/valheim-core) installed, Malmr registers with its version
gate, which keeps out anyone who does not have the same Malmr. The host's unlock table, bosses,
Mistlands line and deposit list apply to everyone connected, in memory only. Your own config
file comes back the moment you disconnect. Your key stays your own. Without Core the mod still
runs, but nothing checks that everybody has it and each player plays by their own file.

## What has not been tested

None of it has run in game yet. That covers the bar filling, the whole deposit breaking and where
its ore lands, the key, the marker and the bar's look, the bar surviving a logout, and two players
on one rock. The deposit list, whether the giant brains, the Ashlands flametal and the Deep North
bloodgold deposits are recognised, the Mistlands rule, and both ways of counting the boss also
still have to be seen in a real session.

## Bugs and ideas

Both go to the site. [longhouse.thijssensoftware.nl/bugs](https://longhouse.thijssensoftware.nl/bugs)
is for anything broken, and [longhouse.thijssensoftware.nl/ideas](https://longhouse.thijssensoftware.nl/ideas)
is for what a mod should do next. You can vote on other people's ideas there as well.

Signing in takes a Steam or Discord account. I work from that list, so the votes decide what
I pick up next.

## Licence

MIT. See `LICENSE`.
