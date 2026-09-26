# Changelog

## Unreleased

First version. Nothing in it has run in game yet.

Tap Left Alt with a pickaxe out to switch vein mining on, and tap it again to switch it off. A
small tag under the crosshair shows it is on. While it is on, a blow on any chunk of a deposit
whose metal you have opened does not break that chunk. The damage it would have done goes into a
bar on the deposit, shown under the crosshair as "Copper vein 45%". At 100% the whole deposit
breaks, buried chunks included, and every chunk drops its ore. 100% is the current health of
every chunk still standing, so chunks mined by hand in the meantime make the total smaller.

The damage counted is what vanilla would have dealt that chunk: the deposit's resistances and
its tool tier apply, a pickaxe that is too weak gets the usual "too hard" and adds nothing, and
the normal damage number shows. Stamina, pickaxe wear and skill gain come from the swing as in
vanilla, so a deposit costs about the swings it would by hand. The bar is saved on the deposit
and survives a logout, and another player with vein mining on can finish it.

The key is `VeinToggleKey` and stays your own on a server. It only listens while a pickaxe is
out, and only a short tap counts, so Jafna's Left Alt on the hoe, Taum's Alt+E and Alt+Tab do not
switch it. `malmr vein on` and `malmr vein off` do the same from the console.

Metals open late: tin at Pickaxes 40, copper 50, iron 60, silver 70, flametal 80, bloodgold 90,
and any other ore a furnace takes at 90. The level is the one you earned. A skill bonus from gear,
food or another mod does not open a metal early. With vein mining on and the metal still shut you
mine the normal way, and once per deposit the top left of the screen says what is missing, like
"Iron veins need Pickaxes 60 and Bonemass beaten at one star".

A metal also waits for the boss of its biome, beaten at one star through Vandi, which is your
second kill of it. That is the Elder for tin and copper, Bonemass for iron, Moder for silver and
Fader for flametal. Bloodgold is on Fader too, the pairing Vandi and Utangard use for the Deep
North, until the Deep North's own boss has a key Vandi counts. Only kills Vandi credits to you
count, so a boss you summoned yourself. `Bosses` and `BossKills` are in the config, and
`BossKills` 0 turns the boss half off. Vandi is a hard dependency and the manifest lists it.

The unlock message comes when a metal opens, on the level-up or on the boss kill, whichever is
last, and it names the key. Once per opening, and never for a metal that was already open when
you logged in.

A deposit's metal comes from its drops: whatever its ore smelts into at any station in the game.
That lets an ore from another mod work without a new build. The `*` level only takes drops that
go into a furnace, meaning a station that makes one of the named metals or burns the same fuel as
one, so nothing that only goes into a kiln or the eitr refinery counts as ore. The `Deposits`
setting overrides it per deposit. On world load the log lists every deposit and what it counted
as, and every boss in the world with the key it sets on death.

The `malmr` console command prints your level, whether vein mining is on, and for each metal its
level, its boss, your kills of it, whether it is open and what is missing, followed by the deposit
list. `malmr progress` shows the bar of the nearest deposit within 10 metres. No devcommands
needed.

The bar is kept on the deposit, and only whoever owns a deposit may change it, so a blow is sent
to the owner the way a normal pickaxe hit is and the owner fills the bar and breaks the deposit.
That owner can be any player or the server, so Malmr is registered Everyone with Core: every
player and the server need it. Vandi asks the same.
