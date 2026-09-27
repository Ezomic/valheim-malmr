# Changelog

## Unreleased

First version. Nothing in it has run in game yet.

Tap Left Alt with a pickaxe out to switch vein mining on, and tap it again to switch it off. A
small gold "Vein" just above the crosshair shows it is on. While it is on, a blow on any chunk of
a deposit you have opened does not break that chunk. The damage it would have done goes into a
bar on the deposit, a thin gold bar just under the crosshair with "Copper vein 45%" under it. At
100% the whole deposit breaks, buried chunks included, and every chunk drops its ore. 100% is the
current health of every chunk still standing, so chunks mined by hand in the meantime make the
total smaller. A deposit that grows back, like a vein Dvala restocks, starts with an empty bar.

The damage counted is what vanilla would have dealt that chunk: the deposit's resistances and
its tool tier apply, a pickaxe that is too weak gets the usual "too hard" and adds nothing, and
the normal damage number shows. Stamina, pickaxe wear and skill gain come from the swing as in
vanilla, so a deposit costs about the swings it would by hand. The bar is saved on the deposit
and survives a logout, and another player with vein mining on can finish it.

The key is `VeinToggleKey` and stays your own on a server. It only listens while a pickaxe is
out, and only a short tap counts, so Jafna's Left Alt on the hoe, Taum's Alt+E and Alt+Tab do not
switch it. `malmr vein on` and `malmr vein off` do the same from the console.

What vein mines, and from which Pickaxes level: stone 20, copper 30, iron 40, silver 50, the
Mistlands' giant brains 60, flametal 70, bloodgold 80. Nothing else. Tin is left out, and so is
obsidian, though one line in `Unlocks` adds either. The level is the one you earned. A skill
bonus from gear, food or another mod does not open a metal early. With vein mining on and the
metal still shut you mine the normal way, and once per deposit the top left of the screen says
what is missing, like "Iron veins need Pickaxes 40 and Bonemass beaten at one star through
Vandi".

Stone means the rocks and boulders that drop nothing but stone. A deposit with any ore in it is
that ore's vein or no vein, never a stone one, even though ore deposits drop stone too, so tin
cannot come in through any stone it drops. A rock that drops anything else beside its stone is not a stone
vein unless that is in `Unlocks` as well, and then the higher level of the two applies. A big
rock comes down a little at a time once its bar is full, so breaking it does not stall the game.

In the Mistlands only the giant brains and stone vein mine. Copper, iron or anything else found
there is mined by hand even when it is open to you, and the top left says so once per deposit.
The `Mistlands` setting lists what still vein mines there.

A metal also waits for the boss of its biome: Eikthyr for stone, the Elder for copper, Bonemass
for iron, Moder for silver and obsidian, the Queen for the brains, Fader for flametal. Bloodgold
is on Fader too, the pairing Vandi and Utangard use for the Deep North, until the Frozen King's
key has been read from a world load. With Vandi installed the boss has to be beaten at one star,
which is your second kill of a boss you summoned yourself. Without Vandi there are no stars, and
one kill by your character is enough, read from the kill record the game keeps on every
character. That record starts at the game's Call to Arms update, so a boss beaten before then has
to be beaten again, and it counts a boss spawned with devcommands like any other. Vandi is a soft
dependency, not in the manifest, and the README recommends it. `Bosses` and `BossKills` are in
the config, and `BossKills` 0 turns the boss half off.

The unlock message comes when a metal opens, on the level-up or on the boss kill, whichever is
last. It says what opened it, including which boss count applied, and it names the key. Once per
opening, and never for a metal that was already open when you logged in.

A deposit's metal comes from its drops: whatever its ore smelts into at any station in the game.
That lets an ore from another mod work without a new build once its metal is named in `Unlocks`.
The `Deposits` setting overrides it per deposit, and `Names` sets what the screen calls a
deposit, which is how the brains are called giant brains. On world load the log lists every
deposit and what it counted as, what the ones that are not veins drop, and every boss in the
world with the key it sets on death.

The `malmr` console command prints your level, whether vein mining is on, which boss count
applies, the Mistlands line, and for each metal its level, its boss, your kills of it, whether
it is open and what is missing, followed by the deposit list. `malmr progress` shows the bar of
the nearest deposit within 10 metres, or of the nearest one with a given name, or further when
given a distance, with its biome and whether the Mistlands rule leaves it to the hand. No
devcommands needed.

The bar is kept on the deposit, and only whoever owns a deposit may change it, so a blow is sent
to the owner the way a normal pickaxe hit is and the owner fills the bar and breaks the deposit.
That owner can be any player or the server, so Malmr is registered Everyone with Core: it has to
be on the server and on every client. So does Vandi when it is used.
