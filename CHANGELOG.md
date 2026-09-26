# Changelog

## Unreleased

First version. Nothing in it has run in game yet.

A pickaxe swing at a deposit takes extra chunks along the vein once your Pickaxes skill has
reached that metal's level: tin at 10, copper at 20, iron at 30, silver at 40, flametal at 60,
bloodgold at 70, and any other ore a furnace takes at 50. One extra chunk at the unlock level,
one more every 10 levels, never more than 4 in a swing. The table, the step and the cap are all
in the config. The level is the one you earned. A skill bonus from gear, food or another mod
does not open a metal early.

Each extra chunk takes one of the blows your swing landed on the chunks you hit, so a chunk
that needs three hits by hand still needs three. It wears the pickaxe as much as that blow
would have by hand: a swing wears it once however many chunks it hits, so each chunk is a
share of a swing. The swing stops taking chunks when the pickaxe reaches zero. A chunk that
falls by itself when the one under it breaks does not count toward the four. Stamina is free
by default and the extra chunks do not raise Pickaxes, both configurable. Chunks with their
middle under the ground are skipped, so buried veins still have to be dug out.

A deposit's metal comes from its drops: whatever its ore smelts into at any station in the
game. That lets an ore from another mod work without a new build. The `*` level only takes
drops that go into a furnace, meaning a station that makes one of the named metals or burns
the same fuel as one, so nothing that only goes into a kiln or the eitr refinery counts as
ore. The `Deposits` setting overrides it per deposit. On world load the log lists every
deposit and what it counted as, and the `malmr` console command prints the same list with
your current level.

A metal also waits for the boss of its biome, beaten at one star through Vandi, which is your
second kill of it. That is the Elder for tin and copper, Bonemass for iron, Moder for silver
and Fader for flametal. Bloodgold is on Fader too, the pairing Vandi and Utangard use for the
Deep North, until the Deep North's own boss has a key Vandi counts. Only kills Vandi credits to
you count, so a boss you summoned yourself. `Bosses` and `BossKills` are in the config, and
`BossKills` 0 turns the boss half off. Vandi is a hard dependency and the manifest lists it.

The unlock message comes when a metal opens, on the level-up or on the boss kill, whichever is
last. Once per opening, and never for a metal that was already open when you logged in. The
`malmr` command shows each metal's boss, your kills of it and which of the two you are missing.
On world load the log lists every boss in the world with the key it sets on death, and says so
if a boss in `Bosses` can never be met.

The extra hits are decided on the swinging player's machine, because only that machine knows
the skill, and sent to the deposit as ordinary pickaxe hits. The owner needs no mod. Malmr is
registered HostOnly with Core, so players without it can still join a server that has it. A
player with it also has Vandi, which every player on a server needs.
