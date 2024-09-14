using BepInEx;
using BepInEx.Configuration;
using R2API.Utils;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;

namespace GotSaleStarred
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    public class GotSaleStarred : BaseUnityPlugin
    {
        public static GotSaleStarred instance;

        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "GiGaGon";
        public const string PluginName = "GotSaleStarred";
        public const string PluginVersion = "1.0.0";

        public static bool legendaryOpened = false;

        internal class ModConfig
        {
            public static ConfigEntry<bool> toggleMod;
            public static ConfigEntry<string> saleStaredMessage;
            public static ConfigEntry<string> guaranteedLegendaryStages;
            public static ConfigEntry<string> smallChestList;
            public static ConfigEntry<string> largeChestList;
            public static ConfigEntry<string> legendaryChestList;
            public static string[] split_guaranteed_legendary_stages;
            public static string[] split_small_chest_list;
            public static string[] split_large_chest_list;
            public static string[] split_legendary_chest_list;

            public static void InitConfig(ConfigFile config)
            {
                toggleMod =                 config.Bind("General", "Toggle Mod",                  true,                                                                                                   "Set to false to disable all mod functionality");
                saleStaredMessage =         config.Bind("General", "Sale Stared Message",         "<color=#ffff00>{player_name}</color> got <color=#00ff00>Sale Star</color>'d",                          "Message that gets sent in chat when a person gets Sale Stared. {player_name} is replaced with the name of the player");
                guaranteedLegendaryStages = config.Bind("General", "Guaranteed Legendary Stages", "rootjungle,dampcavesimple",                                                                            "Comma seperated, no spaces list of all the stages with guaranteed legendary chests");
                smallChestList =            config.Bind("General", "Small Chest List",            "CategoryChestDamage,CategoryChestHealing,CategoryChestUtility,Chest1,Chest1Stealthed,EquipmentBarrel", "Comma seperated, no spaces list of all the small chest names that trigger the message");
                largeChestList =            config.Bind("General", "Large Chest List",            "CategoryChest2Damage,CategoryChest2Healing,CategoryChest2Utility,Chest2,CasinoChest",                  "Comma seperated, no spaces list of all the large chest names that trigger the message");
                legendaryChestList =        config.Bind("General", "Legendary Chest List",        "GoldChest",                                                                                            "Comma seperated, no spaces list of all the legendary chest names that trigger the message");
            }

            public static void Reload()
            {
                instance.Config.Reload();
                split_guaranteed_legendary_stages = guaranteedLegendaryStages.Value.Split(',');
                split_small_chest_list            = smallChestList.Value.Split(',');
                split_large_chest_list            = largeChestList.Value.Split(',');
                split_legendary_chest_list        = legendaryChestList.Value.Split(',');
            }
        }

        public void Awake()
        {
            instance = this;

            ModConfig.InitConfig(Config);

            On.RoR2.PurchaseInteraction.OnInteractionBegin += HookOnInteractionBegin;
            On.RoR2.Run.OnServerSceneChanged += HookOnServerSceneChanged;
        }

        public void OnDestroy()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin -= HookOnInteractionBegin;
            On.RoR2.Run.OnServerSceneChanged -= HookOnServerSceneChanged;
        }

        internal void HookOnServerSceneChanged(On.RoR2.Run.orig_OnServerSceneChanged orig, Run self, string sceneName)
        {
            legendaryOpened = false;
            orig(self, sceneName);
        }

        internal void HookOnInteractionBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            // This runs before orig since that consumes the Sale Stars, but that means some of the checks need to be duplicated

            ModConfig.Reload();
            if (!ModConfig.toggleMod.Value) { orig(self, activator); return; }

            // Server/host side only
            if (!NetworkServer.active) { orig(self, activator); return; }

            // Check from orig
            if (!self.CanBeAffordedByInteractor(activator))
            {
                orig(self, activator);
                return;
            }

            var interactable_name = self.name;
            if (interactable_name == null) { orig(self, activator); return; }
            interactable_name = interactable_name.Replace("(Clone)", "");

            // Check if the legendary was opened
            if (!legendaryOpened && ModConfig.split_legendary_chest_list.Contains(interactable_name))
            {
                legendaryOpened = true;
                orig(self, activator); return;
            }

            var player_body = activator.GetComponent<CharacterBody>();
            if (player_body == null) { orig(self, activator); return; }

            // Check from orig
            if (
                player_body.inventory == null
                || player_body.inventory.GetItemCount(DLC2Content.Items.LowerPricedChests) < 1
                || self == null
                || !self.saleStarCompatible)
            { orig(self, activator); return; }

            var player_name = player_body.GetUserName();
            if (player_name == null) { orig(self, activator); return; }

            // legendary condition
            if (
                !legendaryOpened
                && SceneManager.GetActiveScene() != null
                && SceneManager.GetActiveScene().name != null
                && ModConfig.split_guaranteed_legendary_stages.Contains(SceneManager.GetActiveScene().name)
                && !ModConfig.split_legendary_chest_list.Contains(interactable_name))
            {
                SaleStared(player_name);
                orig(self, activator); return;
            }

            var purchase_interactions = FindObjectsOfType<PurchaseInteraction>().Where(x => x != null && x.available).Select(x => x.name.Replace("(Clone)", "")).ToList();

            // Rusted key check
            if (
                player_body.inventory.GetItemCount(RoR2Content.Items.TreasureCache) > 0
                && ModConfig.split_small_chest_list.Contains(interactable_name)
                && purchase_interactions.Contains("Lockbox"))
            {
                SaleStared(player_name);
                orig(self, activator); return;
            }
            // Small chest check
            if (
                ModConfig.split_small_chest_list.Contains(interactable_name)
                && purchase_interactions.Any(x => ModConfig.split_large_chest_list.Contains(x))
            )
            {
                SaleStared(player_name);
                orig(self, activator); return;
            }

            orig(self, activator);
        }

        internal void SaleStared(string player_name)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = ModConfig.saleStaredMessage.Value.Replace("{player_name}", player_name) });
        }
    }
}
