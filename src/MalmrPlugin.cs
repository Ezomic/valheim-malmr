using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Malmr
{
    /// <summary>
    /// Malmr. Once your Pickaxes skill is high enough for a metal, a swing at that metal's
    /// deposit takes a few more chunks along the vein than the ones you hit. Each metal opens
    /// at its own level, climbing with the biomes, and the number of extra chunks grows with
    /// the skill after that.
    ///
    /// Since 2026-09-26 a metal also waits for its biome's boss: the Elder for tin and copper,
    /// Bonemass for iron, Moder for silver, Fader for flametal and bloodgold, beaten at one star
    /// through Vandi. See Gate for the rule and Bosses for why only Vandi can answer it.
    ///
    /// Vein mining already exists and it is popular, and the reason is the same one Skaft
    /// answers for repair: a copper deposit is dozens of chunks and taking them one at a time
    /// is tedium, not difficulty. The popular version takes the whole deposit while you hold a
    /// key. That deletes the deposit as a job - one click and it is ore on the ground - and
    /// the key makes it a setting rather than something the character has learned. This one
    /// is narrower on every axis, and each narrowing is paid for somewhere:
    ///
    ///  - A few chunks a swing, never the deposit. Four at most by default, so a copper
    ///    deposit is still minutes of work, only fewer of them.
    ///  - Each extra chunk takes one of the blows the swing landed. A chunk that needs three
    ///    hits still needs three, so the swing count per deposit drops by the cap and no more.
    ///  - Each extra chunk wears the pickaxe what that blow cost by hand - the swing's wear
    ///    shared across the chunks it struck. The deposit costs the same pickaxe either way;
    ///    what vein mining buys is time.
    ///  - Metal by metal, on the skill you have earned. Tin opens first, gold last, and the
    ///    swings that level the skill are vanilla's own. A bonus from gear or an effect does not
    ///    open a metal early. The extra chunks do not train it by default, so a deposit mined
    ///    along the vein teaches less than one mined chunk by chunk.
    ///  - Buried chunks stay buried. Silver in particular is dug for, and a vein that reached
    ///    through the hillside would have deleted the digging.
    ///  - No key. It is on for a metal once you have earned it, on the pickaxe in your hand,
    ///    and there is nothing to hold.
    ///
    /// <b>Client-side, in the honest sense.</b> Every decision is made on the machine that
    /// swings, because that is the only machine that knows the swinger's skill - skills live in
    /// the player profile, not on any ZDO. What leaves the machine is the RPC a vanilla swing
    /// sends, once per extra chunk; the deposit's owner applies it the vanilla way and needs no
    /// mod. So a player without Malmr sees an identical world, only with other people mining
    /// faster - see RegisterWithCore for what that means for the gate. The one thing read from
    /// outside the machine is Vandi's kill count, which lives in the world's global keys and so
    /// is already on every client.
    ///
    /// There is deliberately no BepInProcess attribute. A dedicated server runs
    /// valheim_server.exe, and Core's gate only refuses on the server side of RPC_PeerInfo -
    /// so a mod whose settings a host imposes has to be allowed to load there.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. A hard dependency that is absent does not degrade - the plugin never
    // loads at all - and a mod should not need a second install for something it can do
    // without. Core is exactly that: what Malmr loses without it is the host's say, not the
    // mod. Soft still buys the load-order guarantee when Core is present, which is all that
    // registering with the gate needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // Hard, and the opposite argument. Vandi is not something Malmr can do without: the rule is
    // that a metal opens once its biome's boss has been beaten at one star, and the only record
    // of who beat what at how many stars is Vandi's. Soft would have left two bad choices when
    // it is missing - every metal with a boss shut forever, which is a mod that loads and does
    // nothing while its log says it is ready, or the boss half waved through, which is the
    // generous version this mod exists not to be. A plugin BepInEx refuses to load, with a line
    // naming the missing dependency, is the honest one of the three. Through a mod manager it
    // is still one install: the manifest lists Vandi, so it comes along.
    [BepInDependency(VandiGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class MalmrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.malmr";
        public const string PluginName = "Malmr";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        /// <summary>Vandi's plugin GUID. Required - see the attribute above and Bosses.</summary>
        private const string VandiGuid = "ezomic.valheim.vandi";

        internal static ManualLogSource Log;

        /// <summary>
        /// Whether Core answered at load. Worth keeping even when nothing reads it yet: the
        /// difference between gated and ungated is invisible to a player otherwise, and this
        /// is what a warning on spawn would be driven by.
        /// </summary>
        internal static bool CorePresent;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // Config first. Registering absorbs every entry the mod has bound, so anything
            // bound after this line is carried only because Core re-absorbs at manifest
            // time - and depending on the order of two lines in an Awake is not a thing
            // worth relying on.
            MalmrConfig.Bind(Config);

            TryRegisterWithCore();

            // One named type at a time, never the whole assembly, and each on its own so a
            // game update that renames one target costs that feature and not the rest.
            // PatchAll(Type) patches exactly the type it is handed and does not recurse into
            // nested ones, which is why the console hook is named on its own.
            _harmony = new Harmony(PluginGuid);
            Apply("swing", typeof(SwingPatch));
            Apply("deposit hit", typeof(DamagePatches));
            Apply("unlock message", typeof(Announce));
            Apply("console command", typeof(DevConsole.Hook));

            // The startup line every mod in the suite writes. It is how a log answers "which
            // build of what is actually loaded" without anyone guessing.
            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Once per world, the table of every deposit and what it counts as. Once a second, a
        /// look at which metals have just opened, for the message - see Opened for why a boss
        /// kill has to be looked for rather than heard. Cheap every other frame: a few compares
        /// and a return.
        /// </summary>
        private void Update()
        {
            Deposits.SurveyTick();
            Opened.Tick();
        }

        private void Apply(string what, Type patches)
        {
            try
            {
                _harmony.PatchAll(patches);
            }
            catch (Exception error)
            {
                Log.LogError("Could not apply the " + what + " patches, so that part of Malmr "
                    + "is off for this session. The rest is unaffected. " + error.Message);
            }
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Standing alone costs the host's say, not the mod. Without Core nothing swaps a host's
        /// unlock table into a joining client, so on a shared server the levels and the cap are
        /// whatever each player wrote in their own file - anyone can set every metal to 0 and
        /// the cap to 50 and have exactly the mod this one was written not to be. That is a
        /// real loss and it is the server owner's choice to accept, which is why this logs
        /// rather than refusing to run.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a method
        /// needs when it first compiles that method, so a Suite call sitting directly in Awake
        /// would drag Ezomic.Core in before the check above could prevent it - and the
        /// missing-assembly exception would land during plugin load, which is the exact
        /// failure this arrangement exists to avoid. Isolating it means the type is only ever
        /// resolved on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            // HostOnly, argued from the template's rule rather than picked by habit. Everyone is
            // for anything that registers a prefab or changes item data, because a client that
            // cannot resolve a prefab hash loses ZDOs silently. Malmr does neither: no prefab, no
            // item data, no ZDO key of its own, and no RPC vanilla does not already send. The
            // extra chunks travel as ordinary MineRock5/MineRock damage RPCs, applied by the
            // deposit's owner the vanilla way, so a client without the mod is genuinely
            // unaffected - it mines one chunk at a time and sees everyone else's ore land where
            // it always would. Everyone would refuse those players for nothing.
            //
            // The durability it writes is on the local player's own pickaxe, the field Attack
            // itself writes on every swing, and it never leaves that player's save.
            //
            // What HostOnly costs, said plainly: Core lets a HostOnly mod through in BOTH
            // directions, so a player carrying Malmr can join a Core server that does not have
            // it and vein-mine there on their own settings. It could not be stopped server-side
            // even if that were wanted - MineRock5.RPC_Damage has no sender check, so any
            // client can already send any hit to any chunk - and policing what a client runs is
            // Dyrr's job, not the gate's. What HostOnly does keep is the half that matters on a
            // server that has chosen it: a client that has Malmr is checked against the host
            // and gets the host's unlock table, cap and costs.
            //
            // The boss half does not change this argument, but it does change what HostOnly
            // means in practice. Vandi comes with Malmr, and Vandi registers Everyone - its star
            // roll and its boss credit run on whichever client owns a zone or a boss - so a
            // player carrying Malmr can only join a Core server that runs the same Vandi. That
            // is Vandi's requirement, stated by Vandi, and it is right: the kill count this mod
            // reads is only kept by a world where everybody runs the mod that keeps it.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.HostOnly);

            // Registering already absorbs the whole config file, so naming these is a formality.
            // It is worth writing anyway: these are the mod's balance, and saying out loud that
            // the host owns them is the point of putting Malmr on a server.
            Suite.Sync(MalmrConfig.Enabled, MalmrConfig.Unlocks, MalmrConfig.LevelsPerExtraChunk,
                       MalmrConfig.MaxExtraChunks, MalmrConfig.Deposits, MalmrConfig.LeaveBuried,
                       MalmrConfig.Bosses, MalmrConfig.BossKills,
                       MalmrConfig.DurabilityPerChunk, MalmrConfig.StaminaPerChunk,
                       MalmrConfig.ExtraChunksTrainSkill);

            // The message and the logging are the player's own. A host reaching across to turn
            // either on or off for somebody else's evening is not a thing anybody asked for.
            Suite.Local(MalmrConfig.AnnounceUnlocks, MalmrConfig.Verbose);
        }

        private void OnDestroy()
        {
            // UnpatchSelf, never UnpatchAll(). The argumentless one unpatches every mod in
            // the process, not just this one.
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
