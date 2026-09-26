# Changelog

## Unreleased

First version. Nothing in it has run in game yet.

A pickaxe swing at a deposit takes extra chunks along the vein once your Pickaxes skill has
reached that metal's level: tin at 10, copper at 20, iron at 30, silver at 40, flametal at 60,
gold at 70, and any other ore a smelter takes at 50. One extra chunk at the unlock level, one
more every 10 levels, never more than 4 in a swing. The table, the step and the cap are all in
the config.

Each extra chunk takes the same blow as the chunk you hit and wears the pickaxe as much as a
swing. The swing stops taking chunks when the pickaxe reaches zero. Stamina is free by default
and the extra chunks do not raise Pickaxes, both configurable. Chunks with their middle under
the ground are skipped, so buried veins still have to be dug out.

A deposit's metal comes from its drops: whatever its ore smelts into at any station in the
game. That lets an ore from another mod work without a new build. The `Deposits` setting
overrides it per deposit. On world load the log lists every deposit and what it counted as,
and the `malmr` console command prints the same list with your current level.

The extra hits are decided on the swinging player's machine, because only that machine knows
the skill, and sent to the deposit as ordinary pickaxe hits. The owner needs no mod. Malmr is
registered HostOnly with Core, so players without it can still join a server that has it.
