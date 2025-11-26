/*
 * KillaDome.cs - Full COD-Style Rust Server Experience Plugin
 * 
 * Features:
 * - Lobby system with comprehensive UI (Play/Loadouts/Store/Stats tabs)
 * - Drag-and-drop loadout editor with image library
 * - Persistent weapon progression and attachment upgrades
 * - Custom VFX/SFX for bullets and attachments
 * - Store integration (Tebex-compatible)
 * - High performance, GC-friendly architecture
 * 
 * Version: 1.0.0
 * Author: KillaDome Dev Team
 */

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using System.IO;

namespace Oxide.Plugins
{
    [Info("KillaDome", "KillaDome", "1.0.0")]
    [Description("Full COD-style server experience with lobby, loadouts, and progression")]
    public class KillaDome : RustPlugin
    {
        #region Fields
        
        [PluginReference]
        private Plugin ImageLibrary;
        
        private DomeManager _domeManager;
        private LobbyUI _lobbyUI;
        private LoadoutEditor _loadoutEditor;
        private AttachmentSystem _attachmentSystem;
        private VFXManager _vfxManager;
        private SFXManager _sfxManager;
        private ForgeStationSystem _forgeStation;
        private BloodTokenEconomy _tokenEconomy;
        private SaveManager _saveManager;
        private AntiExploit _antiExploit;
        private TelemetrySystem _telemetry;
        
        private PluginConfig _config;
        private GunConfig _gunConfig;
        private OutfitConfig _outfitConfig;
        private Dictionary<ulong, PlayerSession> _activeSessions = new Dictionary<ulong, PlayerSession>();
        
        private const string PERMISSION_ADMIN = "killadome.admin";
        private const string PERMISSION_VIP = "killadome.vip";
        
        #endregion
        
        #region Gun & Image Configuration
        
        /// <summary>
        /// SIMPLIFIED GUN AND SKIN CONFIGURATION
        /// Now loaded from external JSON file for easy management!
        /// 
        /// File: 
        /// - oxide/data/KillaDome/Guns.json - All weapon definitions with skin lists
        /// 
        /// SIMPLIFIED FEATURES:
        /// - Each gun has a Cost and list of AvailableSkins (just skin IDs)
        /// - Base guns must be purchased (not free)
        /// - Skins are just IDs: "0" (default), or workshop IDs like "3611341751"
        /// - Skin pricing based on rarity tiers (Common/Rare/Epic/Legendary)
        /// 
        /// To reload changes: Use 'oxide.reload KillaDome' command
        /// </summary>
        public class GunConfig
        {
            [JsonProperty("Guns")]
            public Dictionary<string, GunDefinition> Guns { get; set; } = new Dictionary<string, GunDefinition>();
            
            [JsonProperty("SkinPricing")]
            public SkinPricing SkinPricing { get; set; } = new SkinPricing();
            
            // ===== HELPER METHODS =====
            
            public string GetGunImageUrl(string gunId)
            {
                return Guns.ContainsKey(gunId) ? Guns[gunId].ImageUrl : "";
            }
            
            public string[] GetAllGunIds()
            {
                return Guns.Keys.ToArray();
            }
            
            public List<string> GetSkinsForWeapon(string weaponId)
            {
                if (Guns.ContainsKey(weaponId))
                {
                    return Guns[weaponId].GetAllSkinIds();
                }
                return new List<string>();
            }
            
            public SkinDefinition GetSkinDefinition(string weaponId, string skinId)
            {
                if (Guns.ContainsKey(weaponId))
                {
                    return Guns[weaponId].GetSkin(skinId);
                }
                return null;
            }
            
            public List<SkinDefinition> GetAllSkinDefinitions(string weaponId)
            {
                if (Guns.ContainsKey(weaponId) && Guns[weaponId].Skins != null)
                {
                    return Guns[weaponId].Skins;
                }
                return new List<SkinDefinition>();
            }
            
            // ===== DEFAULT CONFIGURATION =====
            public static GunConfig CreateDefault()
            {
                var config = new GunConfig();
                
                config.Guns = new Dictionary<string, GunDefinition>
                {
                    ["ak47"] = new GunDefinition
                    {
                        Id = "ak47",
                        DisplayName = "AK-47",
                        RustItemShortname = "rifle.ak",
                        ImageUrl = "https://i.imgur.com/YourAK47Image.png",
                        Cost = 500,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultAK47.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "3602286295", DisplayName = "Tempered AK47", ImageUrl = "https://i.imgur.com/TemperedAK.png", Cost = 600, Rarity = "Epic", Tag = "HOT" },
                            new SkinDefinition { SkinId = "3102802323", DisplayName = "Glory AK47", ImageUrl = "https://i.imgur.com/GloryAK.png", Cost = 800, Rarity = "Legendary", Tag = "NEW" },
                            new SkinDefinition { SkinId = "2854463727", DisplayName = "Alien Red", ImageUrl = "https://i.imgur.com/AlienRedAK.png", Cost = 400, Rarity = "Rare", Tag = "" }
                        }
                    },
                    ["lr300"] = new GunDefinition
                    {
                        Id = "lr300",
                        DisplayName = "LR-300",
                        RustItemShortname = "rifle.lr300",
                        ImageUrl = "https://i.imgur.com/YourLR300Image.png",
                        Cost = 500,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultLR.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "2561668054", DisplayName = "Gold LR300", ImageUrl = "https://i.imgur.com/GoldLR.png", Cost = 800, Rarity = "Legendary", Tag = "POPULAR" }
                        }
                    },
                    ["m249"] = new GunDefinition
                    {
                        Id = "m249",
                        DisplayName = "M249",
                        RustItemShortname = "lmg.m249",
                        ImageUrl = "https://i.imgur.com/YourM249Image.png",
                        Cost = 600,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultM249.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "2854146553", DisplayName = "Chrome M249", ImageUrl = "https://i.imgur.com/ChromeM249.png", Cost = 600, Rarity = "Epic", Tag = "" }
                        }
                    },
                    ["mp5"] = new GunDefinition
                    {
                        Id = "mp5",
                        DisplayName = "MP5A4",
                        RustItemShortname = "smg.mp5",
                        ImageUrl = "https://i.imgur.com/YourMP5Image.png",
                        Cost = 400,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultMP5.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "2561668055", DisplayName = "Tactical MP5", ImageUrl = "https://i.imgur.com/TacticalMP5.png", Cost = 400, Rarity = "Rare", Tag = "" }
                        }
                    },
                    ["thompson"] = new GunDefinition
                    {
                        Id = "thompson",
                        DisplayName = "Thompson",
                        RustItemShortname = "smg.thompson",
                        ImageUrl = "https://i.imgur.com/YourThompsonImage.png",
                        Cost = 400,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultThompson.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "2561668056", DisplayName = "Dragon Thompson", ImageUrl = "https://i.imgur.com/DragonThompson.png", Cost = 600, Rarity = "Epic", Tag = "NEW" }
                        }
                    },
                    ["python"] = new GunDefinition
                    {
                        Id = "python",
                        DisplayName = "Python Revolver",
                        RustItemShortname = "pistol.python",
                        ImageUrl = "https://i.imgur.com/YourPythonImage.png",
                        Cost = 300,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultPython.png", Cost = 0, Rarity = "Common", Tag = "" },
                            new SkinDefinition { SkinId = "2561668057", DisplayName = "Black Python", ImageUrl = "https://i.imgur.com/BlackPython.png", Cost = 400, Rarity = "Rare", Tag = "" }
                        }
                    },
                    ["bolt"] = new GunDefinition
                    {
                        Id = "bolt",
                        DisplayName = "Bolt Action Rifle",
                        RustItemShortname = "rifle.bolt",
                        ImageUrl = "https://i.imgur.com/YourBoltImage.png",
                        Cost = 550,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultBolt.png", Cost = 0, Rarity = "Common", Tag = "" }
                        }
                    },
                    ["sarpistol"] = new GunDefinition
                    {
                        Id = "sarpistol",
                        DisplayName = "Semi-Auto Pistol",
                        RustItemShortname = "pistol.semiauto",
                        ImageUrl = "https://i.imgur.com/YourSARImage.png",
                        Cost = 250,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultSAR.png", Cost = 0, Rarity = "Common", Tag = "" }
                        }
                    },
                    ["custom"] = new GunDefinition
                    {
                        Id = "custom",
                        DisplayName = "Custom SMG",
                        RustItemShortname = "smg.2",
                        ImageUrl = "https://i.imgur.com/YourCustomImage.png",
                        Cost = 350,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultCustom.png", Cost = 0, Rarity = "Common", Tag = "" }
                        }
                    },
                    ["m39"] = new GunDefinition
                    {
                        Id = "m39",
                        DisplayName = "M39 Rifle",
                        RustItemShortname = "rifle.m39",
                        ImageUrl = "https://i.imgur.com/YourM39Image.png",
                        Cost = 450,
                        DefaultSkinId = "0",
                        Skins = new List<SkinDefinition>
                        {
                            new SkinDefinition { SkinId = "0", DisplayName = "Default", ImageUrl = "https://i.imgur.com/DefaultM39.png", Cost = 0, Rarity = "Common", Tag = "" }
                        }
                    }
                };
                
                config.SkinPricing = new SkinPricing
                {
                    CommonCost = 250,
                    RareCost = 400,
                    EpicCost = 600,
                    LegendaryCost = 800
                };
                
                return config;
            }
        }
        
        public class GunDefinition
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string RustItemShortname { get; set; }
            public string ImageUrl { get; set; }
            public int Cost { get; set; } = 500; // Cost to purchase the base gun
            public string DefaultSkinId { get; set; } = "0"; // Default Rust skin ID (0 = no skin)
            
            // Full skin definitions with individual image, price, rarity per skin
            [JsonProperty("Skins")]
            public List<SkinDefinition> Skins { get; set; } = new List<SkinDefinition>();
            
            // Legacy support - kept for backward compatibility with old config files
            // Legacy skin IDs are included in GetAllSkinIds() but won't have full metadata
            [JsonProperty("AvailableSkins")]
            public List<string> AvailableSkins { get; set; } = new List<string>();
            
            // Helper to get all skin IDs (combines Skins and legacy AvailableSkins)
            public List<string> GetAllSkinIds()
            {
                var ids = new List<string>();
                if (Skins != null)
                {
                    foreach (var skin in Skins)
                    {
                        ids.Add(skin.SkinId);
                    }
                }
                // Also include legacy AvailableSkins that aren't in Skins list
                if (AvailableSkins != null)
                {
                    foreach (var skinId in AvailableSkins)
                    {
                        if (!ids.Contains(skinId))
                            ids.Add(skinId);
                    }
                }
                return ids;
            }
            
            // Get skin definition by ID (returns null for legacy skins without definitions)
            public SkinDefinition GetSkin(string skinId)
            {
                if (Skins != null)
                {
                    return Skins.FirstOrDefault(s => s.SkinId == skinId);
                }
                return null;
            }
        }
        
        /// <summary>
        /// Individual skin definition with image, price, and rarity
        /// </summary>
        public class SkinDefinition
        {
            [JsonProperty("SkinId")]
            public string SkinId { get; set; } = "0";
            
            [JsonProperty("DisplayName")]
            public string DisplayName { get; set; } = "Default";
            
            [JsonProperty("ImageUrl")]
            public string ImageUrl { get; set; } = "";
            
            [JsonProperty("Cost")]
            public int Cost { get; set; } = 0;
            
            [JsonProperty("Rarity")]
            public string Rarity { get; set; } = "Common"; // Common, Rare, Epic, Legendary
            
            [JsonProperty("Tag")]
            public string Tag { get; set; } = ""; // NEW, HOT, POPULAR, etc.
        }
        
        public class SkinPricing
        {
            [JsonProperty("CommonCost")]
            public int CommonCost { get; set; } = 250;
            
            [JsonProperty("RareCost")]
            public int RareCost { get; set; } = 400;
            
            [JsonProperty("EpicCost")]
            public int EpicCost { get; set; } = 600;
            
            [JsonProperty("LegendaryCost")]
            public int LegendaryCost { get; set; } = 800;
            
            /// <summary>
            /// Get default cost for a rarity tier. Used as fallback when skin doesn't have explicit cost.
            /// </summary>
            public int GetCostForRarity(string rarity)
            {
                switch (rarity?.ToLower())
                {
                    case "legendary": return LegendaryCost;
                    case "epic": return EpicCost;
                    case "rare": return RareCost;
                    case "common":
                    default: return CommonCost;
                }
            }
            
            /// <summary>
            /// Get cost for a skin ID using heuristics. Used when skin definition is not available.
            /// </summary>
            public int GetCostForSkinId(string skinId)
            {
                // Simple heuristic: if skin ID is "0", it's free (default)
                if (skinId == "0") return 0;
                
                // Workshop IDs starting with 3 are typically higher quality
                if (skinId.StartsWith("3") && skinId.Length > 8)
                    return LegendaryCost;
                
                // You can customize this logic or add a mapping
                return RareCost;
            }
        }
        
        // ===== OUTFIT/ARMOR CONFIGURATION =====
        /// <summary>
        /// Armor/Outfit configuration now loaded from external JSON file
        /// File: oxide/data/KillaDome/Armor.json
        /// Edit the JSON file to add/remove armor pieces without touching code!
        /// </summary>
        public class OutfitConfig
        {
            [JsonProperty("Armors")]
            public List<ArmorItem> Armors { get; set; } = new List<ArmorItem>();
            
            public ArmorItem[] GetArmorsBySlot(string slot)
            {
                return Armors.Where(a => a.Slot == slot).ToArray();
            }
            
            // ===== DEFAULT CONFIGURATION =====
            public static OutfitConfig CreateDefault()
            {
                var config = new OutfitConfig();
                config.Armors = new List<ArmorItem>
                {
                    // Head Armor
                    new ArmorItem
                    {
                        Name = "Metal Facemask",
                        ItemShortname = "metal.facemask",
                        Slot = "head",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/mVY2Uav.png",
                        Cost = 300,
                        Rarity = "Common"
                    },
                    new ArmorItem
                    {
                        Name = "Coffee Can Helmet",
                        ItemShortname = "coffeecan.helmet",
                        Slot = "head",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourCoffeeCanImage.png",
                        Cost = 250,
                        Rarity = "Common"
                    },
                    // Chest Armor
                    new ArmorItem
                    {
                        Name = "Metal Chest Plate",
                        ItemShortname = "metal.plate.torso",
                        Slot = "chest",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourMetalChestImage.png",
                        Cost = 400,
                        Rarity = "Rare"
                    },
                    new ArmorItem
                    {
                        Name = "Road Sign Jacket",
                        ItemShortname = "roadsign.jacket",
                        Slot = "chest",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourRoadSignImage.png",
                        Cost = 300,
                        Rarity = "Common"
                    },
                    // Legs Armor
                    new ArmorItem
                    {
                        Name = "Heavy Plate Pants",
                        ItemShortname = "heavy.plate.pants",
                        Slot = "legs",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourHeavyPantsImage.png",
                        Cost = 400,
                        Rarity = "Rare"
                    },
                    new ArmorItem
                    {
                        Name = "Road Sign Kilt",
                        ItemShortname = "roadsign.kilt",
                        Slot = "legs",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourRoadSignKiltImage.png",
                        Cost = 300,
                        Rarity = "Common"
                    },
                    // Hands/Gloves
                    new ArmorItem
                    {
                        Name = "Tactical Gloves",
                        ItemShortname = "tactical.gloves",
                        Slot = "hands",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourTacticalGlovesImage.png",
                        Cost = 200,
                        Rarity = "Common"
                    },
                    // Feet/Boots
                    new ArmorItem
                    {
                        Name = "Heavy Plate Boots",
                        ItemShortname = "shoes.boots",
                        Slot = "feet",
                        SkinId = "0",
                        ImageUrl = "https://i.imgur.com/YourBootsImage.png",
                        Cost = 250,
                        Rarity = "Common"
                    }
                };
                
                return config;
            }
        }
        
        public class ArmorItem
        {
            public string Name { get; set; }
            public string DisplayName => Name; // Alias for consistency
            public string ItemShortname { get; set; }
            public string Slot { get; set; } // "head", "chest", "legs", "hands", "feet"
            public string SkinId { get; set; } = "0";
            public string ImageUrl { get; set; }
            public int Cost { get; set; } = 300;
            public string Rarity { get; set; } = "Common";
            public string Tag { get; set; } = "";
        }
        
        #endregion
        
        #region Configuration
        
        internal class PluginConfig
        {
            [JsonProperty("Lobby Spawn Position")]
            public Vector3 LobbySpawnPosition { get; set; } = new Vector3(0, 100, 0);
            
            [JsonProperty("Arena Spawn Positions")]
            public List<Vector3> ArenaSpawnPositions { get; set; } = new List<Vector3>
            {
                new Vector3(0, 100, 500)
            };
            
            [JsonProperty("Starting Blood Tokens")]
            public int StartingTokens { get; set; } = 500;
            
            [JsonProperty("Tokens Per Kill")]
            public int TokensPerKill { get; set; } = 10;
            
            [JsonProperty("Admin Daily Tokens")]
            public int AdminDailyTokens { get; set; } = 10000;
            
            [JsonProperty("Daily Token Refill Enabled")]
            public bool DailyRefillEnabled { get; set; } = true;
            

            
            [JsonProperty("UI Update Throttle MS")]
            public int UIUpdateThrottleMS { get; set; } = 100;
            
            [JsonProperty("Auto Save Interval Seconds")]
            public float AutoSaveInterval { get; set; } = 300f;
            
            [JsonProperty("Enable Debug Logging")]
            public bool EnableDebugLogging { get; set; } = false;
        }
        
        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("Configuration file is corrupt. Loading defaults...");
                LoadDefaultConfig();
            }
            SaveConfig();
        }
        
        protected override void SaveConfig() => Config.WriteObject(_config, true);
        
        // ===== LOAD EXTERNAL DATA CONFIGURATIONS =====
        
        private GunConfig LoadGunConfig()
        {
            string dataDirectory = Path.Combine(Interface.Oxide.DataDirectory, "KillaDome");
            string filePath = Path.Combine(dataDirectory, "Guns.json");
            
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
            
            if (!File.Exists(filePath))
            {
                Puts("Guns.json not found. Creating default configuration...");
                var defaultConfig = GunConfig.CreateDefault();
                File.WriteAllText(filePath, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
                return defaultConfig;
            }
            
            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonConvert.DeserializeObject<GunConfig>(json);
                if (config == null || config.Guns == null || config.SkinPricing == null)
                {
                    PrintWarning("Guns.json is invalid. Using defaults...");
                    return GunConfig.CreateDefault();
                }
                
                // Count total skins across all guns
                int totalSkins = 0;
                foreach (var gun in config.Guns.Values)
                {
                    if (gun.AvailableSkins != null)
                        totalSkins += gun.AvailableSkins.Count;
                }
                
                Puts($"Loaded {config.Guns.Count} guns with {totalSkins} total skins from Guns.json");
                return config;
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load Guns.json: {ex.Message}. Using defaults...");
                return GunConfig.CreateDefault();
            }
        }
        
        private OutfitConfig LoadOutfitConfig()
        {
            string dataDirectory = Path.Combine(Interface.Oxide.DataDirectory, "KillaDome");
            string filePath = Path.Combine(dataDirectory, "Armor.json");
            
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
            
            if (!File.Exists(filePath))
            {
                Puts("Armor.json not found. Creating default configuration...");
                var defaultConfig = OutfitConfig.CreateDefault();
                File.WriteAllText(filePath, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
                return defaultConfig;
            }
            
            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonConvert.DeserializeObject<OutfitConfig>(json);
                if (config == null || config.Armors == null)
                {
                    PrintWarning("Armor.json is invalid. Using defaults...");
                    return OutfitConfig.CreateDefault();
                }
                Puts($"Loaded {config.Armors.Count} armor pieces from Armor.json");
                return config;
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load Armor.json: {ex.Message}. Using defaults...");
                return OutfitConfig.CreateDefault();
            }
        }
        
        #endregion
        
        #region Oxide Hooks
        
        private void Init()
        {
            permission.RegisterPermission(PERMISSION_ADMIN, this);
            permission.RegisterPermission(PERMISSION_VIP, this);
            
            // Load configurations from JSON files
            _gunConfig = LoadGunConfig();
            _outfitConfig = LoadOutfitConfig();
            
            // Initialize all systems
            _saveManager = new SaveManager(this, _config);
            _antiExploit = new AntiExploit(this);
            _tokenEconomy = new BloodTokenEconomy(this, _config);
            _attachmentSystem = new AttachmentSystem(this, _config);
            _vfxManager = new VFXManager(this);
            _sfxManager = new SFXManager(this);
            _forgeStation = new ForgeStationSystem(this, _config, _tokenEconomy, _attachmentSystem);
            _loadoutEditor = new LoadoutEditor(this, _attachmentSystem);
            _lobbyUI = new LobbyUI(this, _loadoutEditor, _forgeStation, _tokenEconomy);
            _domeManager = new DomeManager(this, _config);
            _telemetry = new TelemetrySystem(this);
            
            LogDebug("KillaDome initialized successfully");
        }
        
        private void OnServerInitialized()
        {
            timer.Every(_config.AutoSaveInterval, () => AutoSaveAllPlayers());
            LogDebug("Auto-save timer started");
            
            // Load images after server is ready
            timer.Once(5f, () => LoadImages());
        }
        
        private void LoadImages()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded)
            {
                PrintWarning("ImageLibrary not loaded. Images will not display. Please install ImageLibrary plugin.");
                return;
            }
            
            int skinCount = 0;
            
            // Load gun images and individual skin images
            foreach (var gun in _gunConfig.Guns.Values)
            {
                if (!string.IsNullOrEmpty(gun.ImageUrl))
                {
                    ImageLibrary.Call("AddImage", gun.ImageUrl, gun.ImageUrl);
                }
                
                // Load individual skin images
                if (gun.Skins != null)
                {
                    foreach (var skin in gun.Skins)
                    {
                        if (!string.IsNullOrEmpty(skin.ImageUrl))
                        {
                            // Use a unique key combining gun ID and skin ID
                            string imageKey = $"{gun.Id}_skin_{skin.SkinId}";
                            ImageLibrary.Call("AddImage", skin.ImageUrl, imageKey);
                            skinCount++;
                        }
                    }
                }
            }
            
            // Load armor images
            foreach (var armor in _outfitConfig.Armors)
            {
                if (!string.IsNullOrEmpty(armor.ImageUrl))
                {
                    ImageLibrary.Call("AddImage", armor.ImageUrl, armor.ImageUrl);
                }
            }
            
            Puts($"Loaded {_gunConfig.Guns.Count} gun images, {skinCount} skin images, and {_outfitConfig.Armors.Count} armor images into ImageLibrary");
        }
        
        private void Unload()
        {
            // Clean up all UI
            foreach (var player in BasePlayer.activePlayerList)
            {
                _lobbyUI?.DestroyUI(player);
            }
            
            // Save all player data
            foreach (var session in _activeSessions.Values)
            {
                _saveManager?.SavePlayerProfile(session.Profile);
            }
            
            _activeSessions.Clear();
            
            LogDebug("KillaDome unloaded and cleaned up");
        }
        
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || _saveManager == null || _lobbyUI == null) return;
            
            NextTick(() =>
            {
                if (player == null || !player.IsConnected) return;
                
                var profile = _saveManager.LoadPlayerProfile(player.userID);
                
                // Check for admin daily token refill
                if (_config.DailyRefillEnabled && permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
                {
                    // Check if 24 hours have passed since last refill
                    TimeSpan timeSinceRefill = DateTime.UtcNow - profile.LastDailyRefill;
                    
                    if (timeSinceRefill.TotalHours >= 24)
                    {
                        profile.Tokens = _config.AdminDailyTokens;
                        profile.LastDailyRefill = DateTime.UtcNow;
                        _saveManager.SavePlayerProfile(profile);
                        
                        SendReply(player, $"<color=#00ff00>✓ Admin Daily Tokens:</color> You received {_config.AdminDailyTokens} Blood Tokens!");
                        LogDebug($"Admin {player.displayName} received daily refill of {_config.AdminDailyTokens} tokens");
                    }
                }
                
                var session = new PlayerSession(player, profile);
                _activeSessions[player.userID] = session;
                
                // Teleport to lobby
                TeleportToLobby(player);
                
                // Show lobby UI
                timer.Once(1f, () =>
                {
                    if (player != null && player.IsConnected)
                    {
                        _lobbyUI.ShowLobbyUI(player);
                    }
                });
                
                LogDebug($"Player {player.displayName} ({player.userID}) connected");
            });
        }
        
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            
            _lobbyUI?.DestroyUI(player);
            
            if (_activeSessions.TryGetValue(player.userID, out var session))
            {
                _saveManager?.SavePlayerProfile(session.Profile);
                _activeSessions.Remove(player.userID);
            }
            
            LogDebug($"Player {player.displayName} disconnected: {reason}");
        }
        
        private void OnEntityDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null || _tokenEconomy == null || _telemetry == null) return;
            
            var attacker = info?.InitiatorPlayer;
            if (attacker != null && attacker != victim && attacker.IsConnected)
            {
                // Award tokens for kill
                _tokenEconomy.AwardTokens(attacker.userID, _config.TokensPerKill);
                
                // Track telemetry
                _telemetry.RecordKill(attacker.userID, victim.userID);
                
                LogDebug($"{attacker.displayName} killed {victim.displayName}");
            }
            
            // Respawn victim in lobby after delay
            timer.Once(3f, () =>
            {
                if (victim != null && victim.IsConnected)
                {
                    TeleportToLobby(victim);
                    victim.Respawn();
                }
            });
        }
        
        #endregion
        
        #region Helper Methods
        
        private void TeleportToLobby(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            player.Teleport(_config.LobbySpawnPosition);
        }
        
        private void TeleportToArena(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            
            // Select random spawn point from configured arena spawns
            Vector3 spawnPos;
            if (_config.ArenaSpawnPositions != null && _config.ArenaSpawnPositions.Count > 0)
            {
                spawnPos = _config.ArenaSpawnPositions[UnityEngine.Random.Range(0, _config.ArenaSpawnPositions.Count)];
            }
            else
            {
                // Fallback to default if no spawns configured
                spawnPos = new Vector3(0, 100, 500);
            }
            
            player.Teleport(spawnPos);
            
            // Apply loadout when entering arena
            ApplyLoadout(player);
        }
        
        private void ApplyLoadout(BasePlayer player)
        {
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            var loadout = session.Profile.Loadouts[0];
            
            // Strip existing items
            player.inventory.Strip();
            
            // Give primary weapon
            GiveWeapon(player, loadout.Primary, loadout.PrimaryAttachments, loadout.Skins);
            
            // Give secondary weapon
            GiveWeapon(player, loadout.Secondary, loadout.SecondaryAttachments, loadout.Skins);
            
            // Give equipped armor/outfit
            GiveArmor(player, loadout);
            
            LogDebug($"Applied loadout to {player.displayName}");
        }
        
        private void GiveWeapon(BasePlayer player, string weaponName, Dictionary<string, string> attachments, Dictionary<string, string> skins)
        {
            if (string.IsNullOrEmpty(weaponName)) return;
            
            // Get weapon info from centralized config
            string itemName = "rifle.ak"; // Default fallback
            if (_gunConfig?.Guns != null && _gunConfig.Guns.TryGetValue(weaponName, out var gunDef))
            {
                itemName = gunDef.RustItemShortname;
            }
            
            var item = ItemManager.CreateByName(itemName, 1);
            if (item == null)
            {
                LogDebug($"Failed to create weapon: {itemName}");
                return;
            }
            
            // Apply skin if exists
            if (skins != null && skins.TryGetValue(weaponName, out string skinId))
            {
                if (ulong.TryParse(skinId, out ulong skin))
                {
                    item.skin = skin;
                    item.MarkDirty(); // Mark for network update
                }
            }
            
            // Apply attachments if exists
            if (attachments != null && attachments.Count > 0)
            {
                var heldEntity = item.GetHeldEntity() as BaseProjectile;
                if (heldEntity != null && item.contents != null)
                {
                    foreach (var attachmentEntry in attachments)
                    {
                        string attachmentId = attachmentEntry.Value;
                        if (!string.IsNullOrEmpty(attachmentId))
                        {
                            var attachmentItem = ItemManager.CreateByName(attachmentId, 1);
                            if (attachmentItem != null)
                            {
                                // Add attachment to weapon's content container
                                if (!attachmentItem.MoveToContainer(item.contents))
                                {
                                    attachmentItem.Remove(); // Clean up if can't add
                                }
                            }
                        }
                    }
                }
            }
            
            // Give item to player
            if (!player.inventory.GiveItem(item))
            {
                LogDebug($"Failed to give weapon {itemName} to {player.displayName} - inventory full?");
                item.Remove(); // Clean up item if can't give
                return;
            }
            
            // If item was given to belt, ensure visual update
            var heldItem = item.GetHeldEntity();
            if (heldItem != null)
            {
                heldItem.skinID = item.skin;
                heldItem.SendNetworkUpdate();
            }
            
            // Give ammo
            string ammoType = weaponName == "pistol" ? "ammo.pistol" : "ammo.rifle";
            var ammo = ItemManager.CreateByName(ammoType, 250);
            if (ammo != null)
            {
                player.inventory.GiveItem(ammo);
            }
        }
        
        private void GiveArmor(BasePlayer player, Loadout loadout)
        {
            if (player == null || loadout == null) return;
            
            // Give head armor
            if (!string.IsNullOrEmpty(loadout.ArmorHead))
            {
                GiveArmorPiece(player, loadout.ArmorHead);
            }
            
            // Give chest armor
            if (!string.IsNullOrEmpty(loadout.ArmorChest))
            {
                GiveArmorPiece(player, loadout.ArmorChest);
            }
            
            // Give legs armor
            if (!string.IsNullOrEmpty(loadout.ArmorLegs))
            {
                GiveArmorPiece(player, loadout.ArmorLegs);
            }
            
            // Give hands armor
            if (!string.IsNullOrEmpty(loadout.ArmorHands))
            {
                GiveArmorPiece(player, loadout.ArmorHands);
            }
            
            // Give feet armor
            if (!string.IsNullOrEmpty(loadout.ArmorFeet))
            {
                GiveArmorPiece(player, loadout.ArmorFeet);
            }
            
            LogDebug($"Applied armor to {player.displayName}");
        }
        
        private void GiveArmorPiece(BasePlayer player, string armorShortname)
        {
            if (string.IsNullOrEmpty(armorShortname)) return;
            
            var item = ItemManager.CreateByName(armorShortname, 1);
            if (item == null)
            {
                LogDebug($"Failed to create armor item: {armorShortname}");
                return;
            }
            
            // Try to move to wear container (for clothing/armor)
            if (!item.MoveToContainer(player.inventory.containerWear))
            {
                // If wear container is full or item can't be worn, give to main inventory
                player.inventory.GiveItem(item);
            }
            
            LogDebug($"Gave armor piece {armorShortname} to {player.displayName}");
        }
        
        private void AutoSaveAllPlayers()
        {
            if (_saveManager == null) return;
            
            int saved = 0;
            foreach (var session in _activeSessions.Values)
            {
                if (session?.Profile != null)
                {
                    _saveManager.SavePlayerProfile(session.Profile);
                    saved++;
                }
            }
            LogDebug($"Auto-saved {saved} player profiles");
        }
        
        private void LogDebug(string message)
        {
            if (_config?.EnableDebugLogging == true)
            {
                Puts($"[DEBUG] {message}");
            }
        }
        
        internal PlayerSession GetSession(ulong steamId)
        {
            _activeSessions.TryGetValue(steamId, out var session);
            return session;
        }
        
        #endregion
        
        #region Console Commands
        
        [ConsoleCommand("kd.open")]
        private void CmdOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(arg, "You don't have permission to use this command");
                return;
            }
            
            _lobbyUI.ShowLobbyUI(player);
            SendReply(arg, "Lobby UI opened");
        }
        
        [ConsoleCommand("kd.start")]
        private void CmdStart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(arg, "You don't have permission to use this command");
                return;
            }
            
            _domeManager.StartMatch();
            SendReply(arg, "Match started");
        }
        
        [ConsoleCommand("kd.giveskin")]
        private void CmdGiveSkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(2))
            {
                SendReply(arg, "Usage: kd.giveskin <steamid> <skinid>");
                return;
            }
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(arg, "You don't have permission to use this command");
                return;
            }
            
            if (!ulong.TryParse(arg.Args[0], out ulong targetId))
            {
                SendReply(arg, "Invalid Steam ID");
                return;
            }
            
            string skinId = arg.Args[1];
            
            if (_activeSessions.TryGetValue(targetId, out var session))
            {
                session.Profile.OwnedSkins.Add(skinId);
                _saveManager.SavePlayerProfile(session.Profile);
                SendReply(arg, $"Granted skin {skinId} to player {targetId}");
            }
            else
            {
                SendReply(arg, "Player not found or not online");
            }
        }
        
        [ConsoleCommand("kd.resetprogress")]
        private void CmdResetProgress(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1))
            {
                SendReply(arg, "Usage: kd.resetprogress <steamid>");
                return;
            }
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(arg, "You don't have permission to use this command");
                return;
            }
            
            if (!ulong.TryParse(arg.Args[0], out ulong targetId))
            {
                SendReply(arg, "Invalid Steam ID");
                return;
            }
            
            var newProfile = new PlayerProfile(targetId, _config.StartingTokens);
            _saveManager.SavePlayerProfile(newProfile);
            
            if (_activeSessions.TryGetValue(targetId, out var session))
            {
                session.Profile = newProfile;
            }
            
            SendReply(arg, $"Reset progress for player {targetId}");
        }
        
        #endregion
        
        #region Chat Commands
        
        [ChatCommand("kd")]
        private void CmdKD(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                SendReply(player, "KillaDome Commands:\n" +
                    "/kd open - Open lobby UI\n" +
                    "/kd stats - View your stats\n" +
                    "/kd help - Show this help");
                return;
            }
            
            switch (args[0].ToLower())
            {
                case "open":
                    _lobbyUI.ShowLobbyUI(player);
                    SendReply(player, "Lobby UI opened");
                    break;
                    
                case "stats":
                    if (_activeSessions.TryGetValue(player.userID, out var session))
                    {
                        SendReply(player, $"Blood Tokens: {session.Profile.Tokens}\n" +
                            $"VIP Status: {(session.Profile.IsVIP ? "Active" : "Inactive")}");
                    }
                    break;
                    
                case "help":
                    SendReply(player, "KillaDome - Full COD Experience\n" +
                        "Use /kd open to access the lobby");
                    break;
                    
                default:
                    SendReply(player, "Unknown command. Use /kd help");
                    break;
            }
        }
        
        #endregion
        
        #region UI Console Commands
        
        [ConsoleCommand("killadome.close")]
        private void CmdUIClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            _lobbyUI.DestroyUI(player);
        }
        
        [ConsoleCommand("killadome.tab")]
        private void CmdUITab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string tab = arg.Args[0].ToLower();
            _lobbyUI.ShowLobbyUIWithTab(player, tab);
            
            LogDebug($"Player {player.displayName} opened tab: {tab}");
        }
        
        [ConsoleCommand("killadome.joinqueue")]
        private void CmdUIJoinQueue(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            _domeManager.AddToQueue(player.userID);
            SendReply(player, "You have joined the queue!");
        }
        
        [ConsoleCommand("killadome.weapon.prev")]
        private void CmdWeaponPrev(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string slot = arg.Args[0]; // "primary" or "secondary"
            CycleWeapon(player, slot, -1);
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.weapon.next")]
        private void CmdWeaponNext(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string slot = arg.Args[0]; // "primary" or "secondary"
            CycleWeapon(player, slot, 1);
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.purchase")]
        private void CmdPurchase(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(2)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string itemId = arg.Args[0];
            if (!int.TryParse(arg.Args[1], out int cost))
            {
                SendReply(player, "Invalid cost");
                return;
            }
            
            var session = GetSession(player.userID);
            if (session == null)
            {
                // Create session if it doesn't exist
                var profile = _saveManager.LoadPlayerProfile(player.userID);
                session = new PlayerSession(player, profile);
                _activeSessions[player.userID] = session;
            }
            
            if (session.Profile.Tokens < cost)
            {
                SendReply(player, $"Insufficient tokens! You need {cost} but only have {session.Profile.Tokens}.");
                return;
            }
            
            if (_tokenEconomy.PurchaseItem(player.userID, itemId, cost))
            {
                SendReply(player, $"Successfully purchased {itemId}!");
                _saveManager.SavePlayerProfile(session.Profile);
                _lobbyUI.ShowLobbyUIWithTab(player, "store");
            }
            else
            {
                SendReply(player, "Purchase failed!");
            }
        }
        
        [ConsoleCommand("killadome.purchase.armor")]
        private void CmdPurchaseArmor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(2)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string itemShortname = arg.Args[0];
            if (!int.TryParse(arg.Args[1], out int cost))
            {
                SendReply(player, "Invalid cost");
                return;
            }
            
            var session = GetSession(player.userID);
            if (session == null)
            {
                // Create session if it doesn't exist
                var profile = _saveManager.LoadPlayerProfile(player.userID);
                session = new PlayerSession(player, profile);
                _activeSessions[player.userID] = session;
            }
            
            // Check if already owned
            if (session.Profile.OwnedArmor.Contains(itemShortname))
            {
                SendReply(player, "You already own this armor piece!");
                return;
            }
            
            if (session.Profile.Tokens < cost)
            {
                SendReply(player, $"Insufficient tokens! You need {cost} but only have {session.Profile.Tokens}.");
                return;
            }
            
            // Deduct cost and add armor
            session.Profile.Tokens -= cost;
            session.Profile.OwnedArmor.Add(itemShortname);
            
            // Find armor name for message
            var armor = _outfitConfig.Armors.FirstOrDefault(a => a.ItemShortname == itemShortname);
            string armorName = armor != null ? armor.DisplayName : itemShortname;
            
            SendReply(player, $"Successfully purchased {armorName}!");
            _saveManager.SavePlayerProfile(session.Profile);
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.buygun")]
        private void CmdBuyGun(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string gunId = arg.Args[0];
            
            // Validate gun exists in config
            if (!_gunConfig.Guns.ContainsKey(gunId))
            {
                SendReply(player, "Invalid gun!");
                return;
            }
            
            var gun = _gunConfig.Guns[gunId];
            
            var session = GetSession(player.userID);
            if (session == null)
            {
                // Create session if it doesn't exist
                var profile = _saveManager.LoadPlayerProfile(player.userID);
                session = new PlayerSession(player, profile);
                _activeSessions[player.userID] = session;
            }
            
            // Check if already owned
            if (session.Profile.OwnedGuns.Contains(gunId))
            {
                SendReply(player, "You already own this gun!");
                return;
            }
            
            if (session.Profile.Tokens < gun.Cost)
            {
                SendReply(player, $"Insufficient tokens! You need {gun.Cost} but only have {session.Profile.Tokens}.");
                return;
            }
            
            // Deduct cost and add gun
            session.Profile.Tokens -= gun.Cost;
            session.Profile.OwnedGuns.Add(gunId);
            
            SendReply(player, $"Successfully purchased {gun.DisplayName}!");
            _saveManager.SavePlayerProfile(session.Profile);
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.applyskin")]
        private void CmdApplySkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(2)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string weapon = arg.Args[0]; // "primary" or "secondary"
            string skinId = arg.Args[1];
            
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            // Check if player owns the skin
            if (!session.Profile.OwnedSkins.Contains(skinId))
            {
                SendReply(player, "You don't own this skin!");
                return;
            }
            
            var loadout = session.Profile.Loadouts[0];
            string weaponName = weapon == "primary" ? loadout.Primary : loadout.Secondary;
            
            // Apply skin to weapon
            loadout.Skins[weaponName] = skinId;
            _saveManager.SavePlayerProfile(session.Profile);
            
            SendReply(player, $"Skin applied to {weaponName}!");
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.applyattachment")]
        private void CmdApplyAttachment(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(3)) return;
            
            if (!_antiExploit.CheckRateLimit(player.userID))
            {
                SendReply(player, "Please slow down!");
                return;
            }
            
            string weapon = arg.Args[0]; // "primary" or "secondary"
            string attachmentSlot = arg.Args[1]; // "optic", "barrel", "magazine", "grip"
            string attachmentId = arg.Args[2];
            
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            // Check if player owns the attachment (stored in OwnedSkins list for simplicity)
            if (!session.Profile.OwnedSkins.Contains(attachmentId))
            {
                SendReply(player, "You don't own this attachment!");
                return;
            }
            
            var loadout = session.Profile.Loadouts[0];
            var attachments = weapon == "primary" ? loadout.PrimaryAttachments : loadout.SecondaryAttachments;
            
            // Apply attachment
            attachments[attachmentSlot] = attachmentId;
            _saveManager.SavePlayerProfile(session.Profile);
            
            SendReply(player, $"Attachment applied!");
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        #endregion
        
        #region Helper Methods
        
        private void CycleWeapon(BasePlayer player, string slot, int direction)
        {
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            var loadout = session.Profile.Loadouts[0];
            
            // Only allow cycling through OWNED guns
            var ownedGuns = session.Profile.OwnedGuns;
            var ownedGunsSet = new HashSet<string>(ownedGuns ?? new List<string>());
            
            if (ownedGunsSet.Count == 0)
            {
                player.ChatMessage("<color=#FF4444>You don't own any guns! Purchase weapons in the Store.</color>");
                return;
            }
            
            // Get weapon in the OTHER slot to exclude it (prevent duplicate weapons)
            string otherSlotWeapon = slot == "primary" ? loadout.Secondary : loadout.Primary;
            
            // Filter to only guns that exist in config, are owned, and NOT in other slot
            string[] availableWeapons = _gunConfig.GetAllGunIds()
                .Where(g => ownedGunsSet.Contains(g) && g != otherSlotWeapon)
                .ToArray();
            
            if (availableWeapons.Length == 0)
            {
                player.ChatMessage("<color=#FF4444>You don't own any guns! Purchase weapons in the Store.</color>");
                return;
            }
            
            string currentWeapon = slot == "primary" ? loadout.Primary : loadout.Secondary;
            int currentIndex = Array.IndexOf(availableWeapons, currentWeapon);
            
            if (currentIndex == -1) currentIndex = 0;
            
            int newIndex = (currentIndex + direction + availableWeapons.Length) % availableWeapons.Length;
            string newWeapon = availableWeapons[newIndex];
            
            if (slot == "primary")
            {
                loadout.Primary = newWeapon;
            }
            else
            {
                loadout.Secondary = newWeapon;
            }
            
            _saveManager.SavePlayerProfile(session.Profile);
            LogDebug($"Player {player.displayName} changed {slot} weapon to {newWeapon}");
        }
        
        [ConsoleCommand("killadome.attachcat")]
        private void CmdAttachmentCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string category = arg.Args[0].ToLower(); // "scopes", "silencers", "underbarrel"
            if (category != "scopes" && category != "silencers" && category != "underbarrel") return;
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            session.SelectedAttachmentCategory = category;
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.editweapon")]
        private void CmdEditWeapon(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string slot = arg.Args[0].ToLower(); // "primary" or "secondary"
            if (slot != "primary" && slot != "secondary") return;
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            session.EditingWeaponSlot = slot;
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.storecat")]
        private void CmdStoreCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string category = arg.Args[0].ToLower(); // "guns", "skins", or "outfits"
            if (category != "guns" && category != "skins" && category != "outfits") return;
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            session.SelectedStoreCategory = category;
            // Reset page when switching categories
            session.GunsStorePage = 0;
            session.SkinsStorePage = 0;
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.storepage")]
        private void CmdStorePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string direction = arg.Args[0].ToLower(); // "next" or "prev"
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            string category = session.SelectedStoreCategory ?? "guns";
            
            if (category == "guns")
            {
                // Calculate max pages to prevent overflow
                int itemsPerPage = 6;
                int totalItems = _gunConfig.Guns.Count;
                int maxPage = (int)Math.Ceiling((double)totalItems / itemsPerPage) - 1;
                
                if (direction == "next" && session.GunsStorePage < maxPage)
                    session.GunsStorePage++;
                else if (direction == "prev" && session.GunsStorePage > 0)
                    session.GunsStorePage--;
            }
            else if (category == "skins")
            {
                // Calculate max pages for selected gun's available skins
                if (string.IsNullOrEmpty(session.SelectedGunForSkins))
                    session.SelectedGunForSkins = _gunConfig.GetAllGunIds().FirstOrDefault() ?? "";
                
                int itemsPerPage = 12;
                var availableSkins = _gunConfig.GetSkinsForWeapon(session.SelectedGunForSkins);
                int totalItems = availableSkins.Count;
                int maxPage = Math.Max(0, (int)Math.Ceiling((double)totalItems / itemsPerPage) - 1);
                
                if (direction == "next" && session.SkinsStorePage < maxPage)
                    session.SkinsStorePage++;
                else if (direction == "prev" && session.SkinsStorePage > 0)
                    session.SkinsStorePage--;
            }
            
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.selectgunforskins")]
        private void CmdSelectGunForSkins(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string gunId = arg.Args[0];
            var session = GetSession(player.userID);
            if (session == null) return;
            
            // Validate gun exists
            if (!_gunConfig.Guns.ContainsKey(gunId))
            {
                player.ChatMessage("Invalid gun selected.");
                return;
            }
            
            session.SelectedGunForSkins = gunId;
            session.SkinsStorePage = 0; // Reset to first page when changing guns
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.buyskin")]
        private void CmdBuySkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(2)) return;
            
            string gunId = arg.Args[0];
            string skinId = arg.Args[1];
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            // Validate gun exists
            if (!_gunConfig.Guns.ContainsKey(gunId))
            {
                player.ChatMessage("Invalid gun.");
                return;
            }
            
            var gun = _gunConfig.Guns[gunId];
            
            // Validate skin is available for this gun
            if (!gun.AvailableSkins.Contains(skinId))
            {
                player.ChatMessage("This skin is not available for this gun.");
                return;
            }
            
            // Check if player already owns this skin
            if (session.Profile.OwnedSkins.Contains(skinId))
            {
                player.ChatMessage("You already own this skin!");
                return;
            }
            
            // Default skin is always free
            if (skinId == "0")
            {
                session.Profile.OwnedSkins.Add(skinId);
                _saveManager.SavePlayerProfile(session.Profile);
                player.ChatMessage("Default skin equipped!");
                _lobbyUI.ShowLobbyUIWithTab(player, "store");
                return;
            }
            
            // Check if player owns the gun
            if (!session.Profile.OwnedGuns.Contains(gunId))
            {
                player.ChatMessage($"You must own the {gun.DisplayName} before buying skins for it!");
                return;
            }
            
            // Determine skin cost (TODO: implement rarity tiers)
            int skinCost = _gunConfig.SkinPricing.RareCost;
            
            // Check affordability
            if (session.Profile.Tokens < skinCost)
            {
                player.ChatMessage($"Not enough Blood Tokens! Need {skinCost}, have {session.Profile.Tokens}.");
                return;
            }
            
            // Purchase skin
            session.Profile.Tokens -= skinCost;
            session.Profile.OwnedSkins.Add(skinId);
            _saveManager.SavePlayerProfile(session.Profile);
            
            player.ChatMessage($"✓ Purchased skin {skinId} for {gun.DisplayName}! ({skinCost} tokens)");
            _telemetry.RecordPurchase(player.userID, $"skin_{skinId}", skinCost);
            
            _lobbyUI.ShowLobbyUIWithTab(player, "store");
        }
        
        [ConsoleCommand("killadome.loadouttab")]
        private void CmdLoadoutTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string tab = arg.Args[0].ToLower(); // "weapons" or "outfit"
            if (tab != "weapons" && tab != "outfit") return;
            
            var session = GetSession(player.userID);
            if (session == null) return;
            
            session.SelectedLoadoutTab = tab;
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.armor.next")]
        private void CmdArmorNext(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string slot = arg.Args[0].ToLower(); // "head", "chest", "legs", "hands", "feet"
            
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            var loadout = session.Profile.Loadouts[0];
            
            // Get owned armor for this slot
            var ownedArmor = _outfitConfig.Armors
                .Where(a => a.Slot == slot && session.Profile.OwnedArmor.Contains(a.ItemShortname))
                .ToArray();
            
            if (ownedArmor.Length == 0) return;
            
            // Get current armor shortname
            string currentArmorShortname = slot switch
            {
                "head" => loadout.ArmorHead,
                "chest" => loadout.ArmorChest,
                "legs" => loadout.ArmorLegs,
                "hands" => loadout.ArmorHands,
                "feet" => loadout.ArmorFeet,
                _ => null
            };
            
            // Find current index and move to next
            int currentIndex = Array.FindIndex(ownedArmor, a => a.ItemShortname == currentArmorShortname);
            if (currentIndex == -1) currentIndex = 0;
            
            int nextIndex = (currentIndex + 1) % ownedArmor.Length;
            string nextArmor = ownedArmor[nextIndex].ItemShortname;
            
            // Update loadout
            switch (slot)
            {
                case "head": loadout.ArmorHead = nextArmor; break;
                case "chest": loadout.ArmorChest = nextArmor; break;
                case "legs": loadout.ArmorLegs = nextArmor; break;
                case "hands": loadout.ArmorHands = nextArmor; break;
                case "feet": loadout.ArmorFeet = nextArmor; break;
            }
            
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ConsoleCommand("killadome.armor.prev")]
        private void CmdArmorPrev(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs(1)) return;
            
            string slot = arg.Args[0].ToLower(); // "head", "chest", "legs", "hands", "feet"
            
            var session = GetSession(player.userID);
            if (session == null || session.Profile.Loadouts.Count == 0) return;
            
            var loadout = session.Profile.Loadouts[0];
            
            // Get owned armor for this slot
            var ownedArmor = _outfitConfig.Armors
                .Where(a => a.Slot == slot && session.Profile.OwnedArmor.Contains(a.ItemShortname))
                .ToArray();
            
            if (ownedArmor.Length == 0) return;
            
            // Get current armor shortname
            string currentArmorShortname = slot switch
            {
                "head" => loadout.ArmorHead,
                "chest" => loadout.ArmorChest,
                "legs" => loadout.ArmorLegs,
                "hands" => loadout.ArmorHands,
                "feet" => loadout.ArmorFeet,
                _ => null
            };
            
            // Find current index and move to prev
            int currentIndex = Array.FindIndex(ownedArmor, a => a.ItemShortname == currentArmorShortname);
            if (currentIndex == -1) currentIndex = 0;
            
            int prevIndex = (currentIndex - 1 + ownedArmor.Length) % ownedArmor.Length;
            string prevArmor = ownedArmor[prevIndex].ItemShortname;
            
            // Update loadout
            switch (slot)
            {
                case "head": loadout.ArmorHead = prevArmor; break;
                case "chest": loadout.ArmorChest = prevArmor; break;
                case "legs": loadout.ArmorLegs = prevArmor; break;
                case "hands": loadout.ArmorHands = prevArmor; break;
                case "feet": loadout.ArmorFeet = prevArmor; break;
            }
            
            _lobbyUI.ShowLobbyUIWithTab(player, "loadouts");
        }
        
        [ChatCommand("kdlobby")]
        private void CmdSetLobbySpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(player, "<color=#FF0000>Error:</color> You don't have permission to use this command!");
                return;
            }
            
            if (args.Length == 0 || args[0].ToLower() != "set")
            {
                SendReply(player, "<color=#00FF00>Lobby Spawn:</color> Usage: /kdlobby set");
                return;
            }
            
            // Set lobby spawn to player's current position
            _config.LobbySpawnPosition = player.transform.position;
            SaveConfig();
            
            SendReply(player, $"<color=#00FF00>Lobby Spawn:</color> Set to {player.transform.position}");
            LogDebug($"Admin {player.displayName} set lobby spawn to {player.transform.position}");
        }
        
        [ChatCommand("kdspawn")]
        private void CmdSetArenaSpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(player, "<color=#FF0000>Error:</color> You don't have permission to use this command!");
                return;
            }
            
            if (args.Length < 2 || args[0].ToLower() != "set")
            {
                SendReply(player, "<color=#00FF00>Arena Spawn:</color> Usage: /kdspawn set <number>");
                SendReply(player, "Example: /kdspawn set 1");
                return;
            }
            
            if (!int.TryParse(args[1], out int spawnIndex) || spawnIndex < 1)
            {
                SendReply(player, "<color=#FF0000>Error:</color> Spawn number must be a positive integer!");
                return;
            }
            
            // Ensure list exists
            if (_config.ArenaSpawnPositions == null)
            {
                _config.ArenaSpawnPositions = new List<Vector3>();
            }
            
            // Expand list if needed
            while (_config.ArenaSpawnPositions.Count < spawnIndex)
            {
                _config.ArenaSpawnPositions.Add(new Vector3(0, 100, 500));
            }
            
            // Set spawn point (spawnIndex - 1 because 0-indexed)
            _config.ArenaSpawnPositions[spawnIndex - 1] = player.transform.position;
            SaveConfig();
            
            SendReply(player, $"<color=#00FF00>Arena Spawn #{spawnIndex}:</color> Set to {player.transform.position}");
            LogDebug($"Admin {player.displayName} set arena spawn #{spawnIndex} to {player.transform.position}");
        }
        
        [ChatCommand("kdspawns")]
        private void CmdListSpawns(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN))
            {
                SendReply(player, "<color=#FF0000>Error:</color> You don't have permission to use this command!");
                return;
            }
            
            SendReply(player, "<color=#00FF00>=== Spawn Points ===</color>");
            SendReply(player, $"<color=#FFFF00>Lobby:</color> {_config.LobbySpawnPosition}");
            
            if (_config.ArenaSpawnPositions != null && _config.ArenaSpawnPositions.Count > 0)
            {
                SendReply(player, $"<color=#FFFF00>Arena Spawns:</color> {_config.ArenaSpawnPositions.Count} configured");
                for (int i = 0; i < _config.ArenaSpawnPositions.Count; i++)
                {
                    SendReply(player, $"  #{i + 1}: {_config.ArenaSpawnPositions[i]}");
                }
            }
            else
            {
                SendReply(player, "<color=#FF8A00>Arena Spawns:</color> None configured (using default)");
            }
        }
        
        [ChatCommand("dice")]
        private void CmdDiceGame(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            var session = GetSession(player.userID);
            if (session == null)
            {
                SendReply(player, "Session not found! Please rejoin.");
                return;
            }
            
            // Check if player is in cooldown
            if (session.LastDiceGame != default && (DateTime.UtcNow - session.LastDiceGame).TotalSeconds < 30)
            {
                int remaining = 30 - (int)(DateTime.UtcNow - session.LastDiceGame).TotalSeconds;
                SendReply(player, $"<color=#FF8A00>Dice Game:</color> Wait {remaining}s before playing again!");
                return;
            }
            
            if (args.Length == 0)
            {
                SendReply(player, "<color=#FF8A00>Dice Game:</color> Roll the dice! Win 2x your bet!");
                SendReply(player, "Usage: /dice <bet> (10-100 tokens)");
                SendReply(player, $"Your tokens: <color=#FF8A00>{session.Profile.Tokens}</color>");
                return;
            }
            
            if (!int.TryParse(args[0], out int bet))
            {
                SendReply(player, "<color=#FF8A00>Dice Game:</color> Invalid bet amount!");
                return;
            }
            
            if (bet < 10 || bet > 100)
            {
                SendReply(player, "<color=#FF8A00>Dice Game:</color> Bet must be between 10-100 tokens!");
                return;
            }
            
            if (session.Profile.Tokens < bet)
            {
                SendReply(player, $"<color=#FF8A00>Dice Game:</color> Not enough tokens! You have {session.Profile.Tokens}");
                return;
            }
            
            // Deduct bet
            session.Profile.Tokens -= bet;
            session.LastDiceGame = DateTime.UtcNow;
            
            // Roll dice (1-6 for player, 1-6 for house)
            int playerRoll = UnityEngine.Random.Range(1, 7);
            int houseRoll = UnityEngine.Random.Range(1, 7);
            
            SendReply(player, $"<color=#FF8A00>╔═══════════════════╗</color>");
            SendReply(player, $"<color=#FF8A00>║</color>   DICE GAME    <color=#FF8A00>║</color>");
            SendReply(player, $"<color=#FF8A00>╚═══════════════════╝</color>");
            SendReply(player, $"Your roll: <color=#4CFF4C>[{playerRoll}]</color>");
            SendReply(player, $"House roll: <color=#FF4C4C>[{houseRoll}]</color>");
            
            if (playerRoll > houseRoll)
            {
                int winnings = bet * 2;
                session.Profile.Tokens += winnings;
                SendReply(player, $"<color=#4CFF4C>★ YOU WIN! ★</color> +{winnings} tokens");
                SendReply(player, $"Balance: <color=#FF8A00>{session.Profile.Tokens}</color> tokens");
                Effect.server.Run("assets/prefabs/deployable/vendingmachine/effects/buy.prefab", player.transform.position);
            }
            else if (playerRoll < houseRoll)
            {
                SendReply(player, $"<color=#FF4C4C>✖ YOU LOSE!</color> -{bet} tokens");
                SendReply(player, $"Balance: <color=#FF8A00>{session.Profile.Tokens}</color> tokens");
                Effect.server.Run("assets/prefabs/deployable/vendingmachine/effects/deny.prefab", player.transform.position);
            }
            else
            {
                // Tie - return bet
                session.Profile.Tokens += bet;
                SendReply(player, $"<color=#FFD700>═ TIE! ═</color> Bet returned");
                SendReply(player, $"Balance: <color=#FF8A00>{session.Profile.Tokens}</color> tokens");
            }
            
            _saveManager.SavePlayerProfile(session.Profile);
        }
        
        [ChatCommand("tokengame")]
        private void CmdTokenGameHelp(BasePlayer player, string command, string[] args)
        {
            SendReply(player, "<color=#FF8A00>╔═══════════════════════════╗</color>");
            SendReply(player, "<color=#FF8A00>║</color>  BLOOD TOKEN GAMES     <color=#FF8A00>║</color>");
            SendReply(player, "<color=#FF8A00>╚═══════════════════════════╝</color>");
            SendReply(player, "");
            SendReply(player, "<color=#4CFF4C>/dice <bet></color> - Roll dice vs house");
            SendReply(player, "  • Bet: 10-100 tokens");
            SendReply(player, "  • Win: 2x your bet");
            SendReply(player, "  • Cooldown: 30 seconds");
            SendReply(player, "");
            SendReply(player, "<color=#FFD700>More games coming soon!</color>");
        }
        
        #endregion
        
        #region Data Models
        
        internal class PlayerSession
        {
            public BasePlayer Player { get; set; }
            public PlayerProfile Profile { get; set; }
            public DateTime LastAction { get; set; }
            public string SelectedItem { get; set; }
            public bool IsInMatch { get; set; }
            public string EditingWeaponSlot { get; set; } // "primary" or "secondary"
            public string SelectedAttachmentCategory { get; set; } // "scopes", "silencers", "underbarrel"
            public string SelectedStoreCategory { get; set; } // "guns", "skins", or "outfits"
            public int GunsStorePage { get; set; } // Current page for gun store
            public int SkinsStorePage { get; set; } // Current page for skins store
            public string SelectedGunForSkins { get; set; } // Which gun's skins to show in skin store
            public DateTime LastDiceGame { get; set; } // Cooldown for dice game
            public string SelectedLoadoutTab { get; set; } // "weapons" or "outfit"
            
            internal PlayerSession(BasePlayer player, PlayerProfile profile)
            {
                Player = player;
                Profile = profile;
                LastAction = DateTime.UtcNow;
                EditingWeaponSlot = "primary"; // Default to editing primary
                SelectedAttachmentCategory = "scopes"; // Default to scopes tab
                SelectedStoreCategory = "guns"; // Default to guns store
                GunsStorePage = 0; // Start at first page
                SkinsStorePage = 0; // Start at first page
                SelectedGunForSkins = ""; // Will default to first gun when opening skin store
                SelectedLoadoutTab = "weapons"; // Default to weapons tab
            }
        }
        
        public class PlayerProfile
        {
            public ulong SteamID { get; set; }
            public List<Loadout> Loadouts { get; set; }

            public List<string> OwnedSkins { get; set; }
            public List<string> OwnedGuns { get; set; } // List of owned gun IDs
            public List<string> OwnedArmor { get; set; } // List of owned armor shortnames
            public int Tokens { get; set; }
            public bool IsVIP { get; set; }
            public DateTime LastUpdated { get; set; }
            public int TotalKills { get; set; }
            public int TotalDeaths { get; set; }
            public int MatchesPlayed { get; set; }
            public DateTime LastDailyRefill { get; set; }
            
            public PlayerProfile()
            {
                Loadouts = new List<Loadout>();

                OwnedSkins = new List<string>();
                OwnedGuns = new List<string>();
                OwnedArmor = new List<string>();
            }
            
            public PlayerProfile(ulong steamId, int startingTokens) : this()
            {
                SteamID = steamId;
                Tokens = startingTokens;
                LastUpdated = DateTime.UtcNow;
                
                // Create empty default loadout - players must purchase guns first
                Loadouts.Add(new Loadout
                {
                    Name = "Default",
                    Primary = "",  // Empty - no default weapon
                    Secondary = "", // Empty - no default weapon
                    PrimaryAttachments = new Dictionary<string, string>(),
                    SecondaryAttachments = new Dictionary<string, string>(),
                    Skins = new Dictionary<string, string>()
                });
            }
        }
        
        public class Loadout
        {
            public string Name { get; set; }
            public string Primary { get; set; }
            public string Secondary { get; set; }
            public Dictionary<string, string> PrimaryAttachments { get; set; }
            public Dictionary<string, string> SecondaryAttachments { get; set; }
            public Dictionary<string, string> Skins { get; set; }
            public string Lethal { get; set; }
            public string Tactical { get; set; }
            public List<string> Perks { get; set; }
            // Outfit/Armor slots
            public string ArmorHead { get; set; }
            public string ArmorChest { get; set; }
            public string ArmorLegs { get; set; }
            public string ArmorHands { get; set; }
            public string ArmorFeet { get; set; }
            
            public Loadout()
            {
                PrimaryAttachments = new Dictionary<string, string>();
                SecondaryAttachments = new Dictionary<string, string>();
                Skins = new Dictionary<string, string>();
                Perks = new List<string>();
            }
        }
        
        #endregion
        
        #region Module: DomeManager
        
        internal class DomeManager
        {
            private KillaDome _plugin;
            private PluginConfig _config;
            private Match _currentMatch;
            private List<ulong> _matchQueue = new List<ulong>();
            
            internal DomeManager(KillaDome plugin, PluginConfig config)
            {
                _plugin = plugin;
                _config = config;
            }
            
            public void StartMatch()
            {
                if (_currentMatch != null && _currentMatch.IsActive)
                {
                    _plugin.PrintWarning("Match already in progress");
                    return;
                }
                
                _currentMatch = new Match
                {
                    MatchId = Guid.NewGuid().ToString(),
                    StartTime = DateTime.UtcNow,
                    IsActive = true
                };
                
                // Teleport queued players to arena
                foreach (var steamId in _matchQueue)
                {
                    var player = BasePlayer.FindByID(steamId);
                    if (player != null && player.IsConnected)
                    {
                        _plugin.TeleportToArena(player);
                        
                        var session = _plugin.GetSession(steamId);
                        if (session != null)
                        {
                            session.IsInMatch = true;
                        }
                    }
                }
                
                _plugin.Puts($"Match {_currentMatch.MatchId} started with {_matchQueue.Count} players");
                _matchQueue.Clear();
            }
            
            public void EndMatch()
            {
                if (_currentMatch == null || !_currentMatch.IsActive)
                {
                    return;
                }
                
                _currentMatch.IsActive = false;
                _currentMatch.EndTime = DateTime.UtcNow;
                
                // Return players to lobby
                foreach (var player in BasePlayer.activePlayerList)
                {
                    var session = _plugin.GetSession(player.userID);
                    if (session != null && session.IsInMatch)
                    {
                        _plugin.TeleportToLobby(player);
                        session.IsInMatch = false;
                    }
                }
                
                _plugin.Puts($"Match {_currentMatch.MatchId} ended");
            }
            
            public void AddToQueue(ulong steamId)
            {
                if (!_matchQueue.Contains(steamId))
                {
                    _matchQueue.Add(steamId);
                }
            }
            
            public void RemoveFromQueue(ulong steamId)
            {
                _matchQueue.Remove(steamId);
            }
        }
        
        internal class Match
        {
            public string MatchId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public bool IsActive { get; set; }
            public List<ulong> Participants { get; set; } = new List<ulong>();
        }
        
        #endregion
        
        
        #region Module: LobbyUI
        
        internal class LobbyUI
        {
            private KillaDome _plugin;
            private LoadoutEditor _loadoutEditor;
            private ForgeStationSystem _forgeStation;
            private BloodTokenEconomy _tokenEconomy;
            
            private const string UI_MAIN = "KillaDome.Main";
            private const string UI_TAB_CONTAINER = "KillaDome.TabContainer";
            
            internal LobbyUI(KillaDome plugin, LoadoutEditor loadoutEditor, ForgeStationSystem forgeStation, BloodTokenEconomy tokenEconomy)
            {
                _plugin = plugin;
                _loadoutEditor = loadoutEditor;
                _forgeStation = forgeStation;
                _tokenEconomy = tokenEconomy;
            }
            
            public void ShowLobbyUI(BasePlayer player)
            {
                ShowLobbyUIWithTab(player, "play");
            }
            
            public void ShowLobbyUIWithTab(BasePlayer player, string tab)
            {
                DestroyUI(player);
                
                var container = new CuiElementContainer();
                
                // Main background - FULL SCREEN
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.02 0.02 0.04 0.98" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, "Overlay", UI_MAIN);
                
                // Title Bar - Sleek header with gradient effect (reduced to 6% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.95" },
                    RectTransform = { AnchorMin = "0 0.94", AnchorMax = "1 1" }
                }, UI_MAIN, "TitleBar");
                
                // Title gradient accent
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.5 0 0.4" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.03" }
                }, "TitleBar");
                
                // Title text with glow effect
                container.Add(new CuiLabel
                {
                    Text = { Text = "⚔ KILLADOME ⚔", FontSize = 28, Align = TextAnchor.MiddleCenter, Color = "1 0.6 0.1 1" },
                    RectTransform = { AnchorMin = "0.3 0", AnchorMax = "0.7 1" }
                }, "TitleBar");
                
                // Tab buttons - full width navigation bar (6% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.05 0.05 0.08 0.9" },
                    RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 0.94" }
                }, UI_MAIN, "TabBar");
                
                AddTabButtonFullscreen(container, "TabBar", "PLAY", 0, tab == "play", "killadome.tab play");
                AddTabButtonFullscreen(container, "TabBar", "LOADOUTS", 1, tab == "loadouts", "killadome.tab loadouts");
                AddTabButtonFullscreen(container, "TabBar", "STORE", 2, tab == "store", "killadome.tab store");
                AddTabButtonFullscreen(container, "TabBar", "STATS", 3, tab == "stats", "killadome.tab stats");
                AddTabButtonFullscreen(container, "TabBar", "SETTINGS", 4, tab == "settings", "killadome.tab settings");
                
                // Close button - top right corner
                container.Add(new CuiButton
                {
                    Button = { Color = "0.8 0.2 0.2 1", Command = "killadome.close" },
                    RectTransform = { AnchorMin = "0.96 0.94", AnchorMax = "1 1" },
                    Text = { Text = "✕", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, UI_MAIN);
                
                // Tab content container - FULL WIDTH with minimal padding (88% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.95" },
                    RectTransform = { AnchorMin = "0.01 0.01", AnchorMax = "0.99 0.87" }
                }, UI_MAIN, UI_TAB_CONTAINER);
                
                // Show appropriate tab content
                switch (tab.ToLower())
                {
                    case "play":
                        ShowPlayTab(container, player);
                        break;
                    case "loadouts":
                        ShowLoadoutsTab(container, player);
                        break;
                    case "store":
                        ShowStoreTab(container, player);
                        break;
                    case "stats":
                        ShowStatsTab(container, player);
                        break;
                    case "settings":
                        ShowSettingsTab(container, player);
                        break;
                    default:
                        ShowPlayTab(container, player);
                        break;
                }
                
                CuiHelper.AddUi(player, container);
            }
            
            private void AddTabButtonFullscreen(CuiElementContainer container, string parent, string text, int index, bool isActive, string command)
            {
                float width = 0.18f;
                float spacing = 0.01f;
                float startX = 0.02f;
                float minX = startX + (width + spacing) * index;
                float maxX = minX + width;
                
                string bgColor = isActive ? "0.25 0.55 0.35 0.95" : "0.12 0.12 0.15 0.9";
                string textColor = isActive ? "1 1 1 1" : "0.7 0.7 0.7 1";
                
                container.Add(new CuiButton
                {
                    Button = { Color = bgColor, Command = command },
                    RectTransform = { AnchorMin = $"{minX} 0.1", AnchorMax = $"{maxX} 0.9" },
                    Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = textColor }
                }, parent);
            }
            
            private void AddTabButton(CuiElementContainer container, string parent, string text, int index, BasePlayer player, string command)
            {
                float width = 0.15f;
                float spacing = 0.02f;
                float startX = 0.1f;
                float minX = startX + (width + spacing) * index;
                float maxX = minX + width;
                
                container.Add(new CuiButton
                {
                    Button = { Color = "0.3 0.3 0.3 1", Command = command },
                    RectTransform = { AnchorMin = $"{minX} 0.80", AnchorMax = $"{maxX} 0.86" },
                    Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter }
                }, parent);
            }
            
            private void ShowPlayTab(CuiElementContainer container, BasePlayer player)
            {
                // Main play section header
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.9" },
                    RectTransform = { AnchorMin = "0.02 0.85", AnchorMax = "0.98 0.98" }
                }, UI_TAB_CONTAINER, "PlayHeader");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "━━━ READY FOR BATTLE? ━━━", FontSize = 32, Align = TextAnchor.MiddleCenter, Color = "1 0.7 0.2 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "PlayHeader");
                
                // Main content area
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.15 0.25", AnchorMax = "0.85 0.80" }
                }, UI_TAB_CONTAINER, "PlayContent");
                
                // Decorative border
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.3 0.8 0.4 0.5" },
                    RectTransform = { AnchorMin = "0 0.98", AnchorMax = "1 1" }
                }, "PlayContent");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.3 0.8 0.4 0.5" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.02" }
                }, "PlayContent");
                
                // Join Queue button - large and centered
                container.Add(new CuiButton
                {
                    Button = { Color = "0.2 0.7 0.3 0.95", Command = "killadome.joinqueue" },
                    RectTransform = { AnchorMin = "0.25 0.50", AnchorMax = "0.75 0.75" },
                    Text = { Text = "⚔ JOIN QUEUE ⚔", FontSize = 28, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, "PlayContent");
                
                // Status indicator
                container.Add(new CuiLabel
                {
                    Text = { Text = "Click to enter the battlefield", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1" },
                    RectTransform = { AnchorMin = "0.1 0.38", AnchorMax = "0.9 0.48" }
                }, "PlayContent");
                
                // Stats preview
                var session = _plugin.GetSession(player.userID);
                if (session != null)
                {
                    // Stats panel
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1 0.08 0.05 0.9" },
                        RectTransform = { AnchorMin = "0.25 0.08", AnchorMax = "0.75 0.32" }
                    }, "PlayContent", "StatsPreview");
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "◆ YOUR STATS ◆", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 0.8 0.2 1" },
                        RectTransform = { AnchorMin = "0 0.70", AnchorMax = "1 0.95" }
                    }, "StatsPreview");
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"Blood Tokens: {session.Profile.Tokens}", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 0.9 0.6 1" },
                        RectTransform = { AnchorMin = "0 0.40", AnchorMax = "1 0.65" }
                    }, "StatsPreview");
                    
                    float kd = session.Profile.TotalDeaths > 0 ? (float)session.Profile.TotalKills / session.Profile.TotalDeaths : session.Profile.TotalKills;
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"K/D: {kd:F2}  |  Kills: {session.Profile.TotalKills}  |  Deaths: {session.Profile.TotalDeaths}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" },
                        RectTransform = { AnchorMin = "0 0.10", AnchorMax = "1 0.35" }
                    }, "StatsPreview");
                }
                
                // Footer with tips
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.8" },
                    RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.98 0.10" }
                }, UI_TAB_CONTAINER, "PlayFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "💡 TIP: Customize your loadout in the LOADOUTS tab before entering battle!", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.7 0.8 0.9 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "PlayFooter");
            }
            
            private void ShowLoadoutsTab(CuiElementContainer container, BasePlayer player)
            {
                var session = _plugin.GetSession(player.userID);
                if (session == null)
                {
                    var profile = _plugin._saveManager.LoadPlayerProfile(player.userID);
                    session = new PlayerSession(player, profile);
                    _plugin._activeSessions[player.userID] = session;
                }
                
                if (session.Profile.Loadouts.Count == 0)
                {
                    // Create empty loadout - no default weapons
                    session.Profile.Loadouts.Add(new Loadout
                    {
                        Name = "Default",
                        Primary = "",
                        Secondary = "",
                        PrimaryAttachments = new Dictionary<string, string>(),
                        SecondaryAttachments = new Dictionary<string, string>(),
                        Skins = new Dictionary<string, string>()
                    });
                }
                
                var loadout = session.Profile.Loadouts[0];
                string editingSlot = session.EditingWeaponSlot ?? "primary";
                string currentWeapon = editingSlot == "primary" ? loadout.Primary : loadout.Secondary;
                
                // Get list of owned guns
                var ownedGuns = session.Profile.OwnedGuns ?? new List<string>();
                bool hasOwnedGuns = ownedGuns.Count > 0;
                
                // Validate current weapon is owned, otherwise clear it
                if (!string.IsNullOrEmpty(currentWeapon) && !ownedGuns.Contains(currentWeapon))
                {
                    // Current weapon not owned, set to first owned gun or empty
                    currentWeapon = ownedGuns.FirstOrDefault() ?? "";
                    if (editingSlot == "primary")
                        loadout.Primary = currentWeapon;
                    else
                        loadout.Secondary = currentWeapon;
                }
                
                // Prevent duplicate weapons in both slots
                if (!string.IsNullOrEmpty(loadout.Primary) && !string.IsNullOrEmpty(loadout.Secondary) 
                    && loadout.Primary == loadout.Secondary)
                {
                    // Find another owned gun for secondary, or clear it
                    string alternativeGun = ownedGuns.FirstOrDefault(g => g != loadout.Primary) ?? "";
                    loadout.Secondary = alternativeGun;
                    if (editingSlot == "secondary")
                        currentWeapon = alternativeGun;
                }
                
                // === HEADER === (6% height - full width)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.95" },
                    RectTransform = { AnchorMin = "0.01 0.93", AnchorMax = "0.99 0.99" }
                }, UI_TAB_CONTAINER, "LoadoutHeader");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.6 0.2 0.6" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.04" }
                }, "LoadoutHeader");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "━━━ LOADOUT EDITOR ━━━", FontSize = 22, Align = TextAnchor.MiddleCenter, Color = "1 0.9 0.7 1" },
                    RectTransform = { AnchorMin = "0 0.04", AnchorMax = "1 1" }
                }, "LoadoutHeader");
                
                // === SUB-TABS === (Weapons vs Outfit Editor - 5% height)
                string selectedLoadoutTab = session.SelectedLoadoutTab ?? "weapons";
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.01 0.87", AnchorMax = "0.99 0.92" }
                }, UI_TAB_CONTAINER, "LoadoutSubTabs");
                
                // Weapons Tab
                bool isWeaponsActive = selectedLoadoutTab == "weapons";
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.loadouttab weapons", Color = isWeaponsActive ? "0.2 0.6 0.3 0.9" : "0.1 0.3 0.15 0.9" },
                    Text = { Text = "⚔ WEAPONS", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = isWeaponsActive ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.35 0.9" }
                }, "LoadoutSubTabs");
                
                // Outfit Tab
                bool isOutfitActive = selectedLoadoutTab == "outfit";
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.loadouttab outfit", Color = isOutfitActive ? "0.2 0.6 0.3 0.9" : "0.1 0.3 0.15 0.9" },
                    Text = { Text = "👕 OUTFIT", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = isOutfitActive ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.37 0.1", AnchorMax = "0.70 0.9" }
                }, "LoadoutSubTabs");
                
                // Show appropriate content based on selected tab
                if (selectedLoadoutTab == "outfit")
                {
                    ShowOutfitEditorContent(container, player, session, loadout);
                }
                else
                {
                    ShowWeaponsEditorContent(container, player, session, loadout, editingSlot, currentWeapon);
                }
            }
            
            private void ShowWeaponsEditorContent(CuiElementContainer container, BasePlayer player, PlayerSession session, Loadout loadout, string editingSlot, string currentWeapon)
            {
                // Check if player owns any guns
                var ownedGuns = session.Profile.OwnedGuns ?? new List<string>();
                bool hasOwnedGuns = ownedGuns.Count > 0;
                
                // === WEAPON SELECTION === (16% height, adjusted for sub-tabs)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.66", AnchorMax = "0.95 0.82" }
                }, UI_TAB_CONTAINER, "WeaponSelection");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "SELECT WEAPONS", FontSize = 11, Align = TextAnchor.UpperLeft, Color = "0.8 0.8 0.8 1" },
                    RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.30 1" }
                }, "WeaponSelection");
                
                // Show warning if no guns owned
                if (!hasOwnedGuns)
                {
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.5 0.2 0.1 0.9" },
                        RectTransform = { AnchorMin = "0.1 0.2", AnchorMax = "0.9 0.8" }
                    }, "WeaponSelection", "NoGunsWarning");
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "⚠ NO WEAPONS OWNED!", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 0.8 0.5 1" },
                        RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.9" }
                    }, "NoGunsWarning");
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "Visit the STORE tab to purchase weapons", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                        RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.45" }
                    }, "NoGunsWarning");
                    
                    return; // Don't show weapon slots if no guns owned
                }
                
                // PRIMARY WEAPON
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.1 0.3 0.15 0.95" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.49 0.88" }
                }, "WeaponSelection", "PrimaryBox");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.6 0.2 0.4" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" }
                }, "PrimaryBox");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "PRIMARY WEAPON", FontSize = 10, Align = TextAnchor.UpperCenter, Color = "1 0.8 0.5 1" },
                    RectTransform = { AnchorMin = "0.05 0.88", AnchorMax = "0.95 0.98" }
                }, "PrimaryBox");
                
                // Get primary weapon - check if owned
                string primaryGun = loadout.Primary;
                bool primaryOwned = !string.IsNullOrEmpty(primaryGun) && ownedGuns.Contains(primaryGun);
                
                if (primaryOwned)
                {
                    // Weapon image - using centralized config
                    string primaryImageUrl = _plugin._gunConfig.GetGunImageUrl(loadout.Primary);
                    container.Add(new CuiElement
                    {
                        Parent = "PrimaryBox",
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)_plugin.ImageLibrary?.Call("GetImage", primaryImageUrl) },
                            new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" }
                        }
                    });
                }
                
                // Get display name from config
                string primaryDisplayName = primaryOwned && _plugin._gunConfig.Guns.ContainsKey(loadout.Primary) 
                    ? _plugin._gunConfig.Guns[loadout.Primary].DisplayName 
                    : "NO WEAPON";
                
                // Add background for better visibility
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.20", AnchorMax = "0.95 0.30" }
                }, "PrimaryBox", "PrimaryNameBg");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = primaryDisplayName, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = primaryOwned ? "1 0.9 0.5 1" : "0.6 0.4 0.4 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "PrimaryNameBg");
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.weapon.prev primary", Color = "0.2 0.6 0.3 0.9" },
                    Text = { Text = "< PREV", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.47 0.22" }
                }, "PrimaryBox");
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.weapon.next primary", Color = "0.2 0.6 0.3 0.9" },
                    Text = { Text = "NEXT >", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.53 0.06", AnchorMax = "0.95 0.22" }
                }, "PrimaryBox");
                
                // SECONDARY WEAPON
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.1 0.3 0.15 0.95" },
                    RectTransform = { AnchorMin = "0.51 0.05", AnchorMax = "0.98 0.88" }
                }, "WeaponSelection", "SecondaryBox");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.5 0.7 1.0 0.4" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" }
                }, "SecondaryBox");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "SECONDARY WEAPON", FontSize = 10, Align = TextAnchor.UpperCenter, Color = "0.7 0.9 1.0 1" },
                    RectTransform = { AnchorMin = "0.05 0.88", AnchorMax = "0.95 0.98" }
                }, "SecondaryBox");
                
                // Get secondary weapon - check if owned
                string secondaryGun = loadout.Secondary;
                bool secondaryOwned = !string.IsNullOrEmpty(secondaryGun) && ownedGuns.Contains(secondaryGun);
                
                if (secondaryOwned)
                {
                    // Weapon image - using centralized config
                    string secondaryImageUrl = _plugin._gunConfig.GetGunImageUrl(loadout.Secondary);
                    container.Add(new CuiElement
                    {
                        Parent = "SecondaryBox",
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)_plugin.ImageLibrary?.Call("GetImage", secondaryImageUrl) },
                            new CuiRectTransformComponent { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" }
                        }
                    });
                }
                
                // Get display name from config
                string secondaryDisplayName = secondaryOwned && _plugin._gunConfig.Guns.ContainsKey(loadout.Secondary) 
                    ? _plugin._gunConfig.Guns[loadout.Secondary].DisplayName 
                    : "NO WEAPON";
                
                // Add background for better visibility
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.20", AnchorMax = "0.95 0.30" }
                }, "SecondaryBox", "SecondaryNameBg");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = secondaryDisplayName, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = secondaryOwned ? "0.7 0.9 1.0 1" : "0.6 0.4 0.4 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "SecondaryNameBg");
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.weapon.prev secondary", Color = "0.5 0.3 0.8 0.9" },
                    Text = { Text = "< PREV", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.47 0.22" }
                }, "SecondaryBox");
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.weapon.next secondary", Color = "0.5 0.3 0.8 0.9" },
                    Text = { Text = "NEXT >", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.53 0.06", AnchorMax = "0.95 0.22" }
                }, "SecondaryBox");
                
                // === EDITOR SECTION === (58% height, adjusted)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.95 0.64" }
                }, UI_TAB_CONTAINER, "EditorArea");
                
                // Show different content based on whether current weapon is owned
                bool currentWeaponOwned = !string.IsNullOrEmpty(currentWeapon) && ownedGuns.Contains(currentWeapon);
                string displayWeaponName = currentWeaponOwned ? currentWeapon.ToUpper() : "NO WEAPON";
                container.Add(new CuiLabel
                {
                    Text = { Text = $"CUSTOMIZE: {displayWeaponName}", FontSize = 12, Align = TextAnchor.UpperLeft, Color = currentWeaponOwned ? "0.8 0.8 0.8 1" : "0.6 0.4 0.4 1" },
                    RectTransform = { AnchorMin = "0.02 0.96", AnchorMax = "0.50 1" }
                }, "EditorArea");
                
                bool isPrimaryActive = editingSlot == "primary";
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.editweapon primary", Color = isPrimaryActive ? "0.2 0.6 0.3 0.9" : "0.1 0.3 0.15 0.9" },
                    Text = { Text = "PRIMARY", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = isPrimaryActive ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.72 0.96", AnchorMax = "0.84 1" }
                }, "EditorArea");
                
                container.Add(new CuiButton
                {
                    Button = { Command = "killadome.editweapon secondary", Color = !isPrimaryActive ? "0.5 0.3 0.8 0.9" : "0.1 0.3 0.15 0.9" },
                    Text = { Text = "SECONDARY", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = !isPrimaryActive ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.85 0.96", AnchorMax = "0.98 1" }
                }, "EditorArea");
                
                // LEFT PANEL - SKINS (with scrolling)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.10 0.10 0.14 0.95" },
                    RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.49 0.93" }
                }, "EditorArea", "SkinsPanel");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.15 0.10 0.20 0.9" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                }, "SkinsPanel");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "WEAPON SKINS", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.9 0.8 1.0 1" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                }, "SkinsPanel");
                
                // Scrollable content area for skins
                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.94" }
                }, "SkinsPanel", "SkinsScroll");
                
                // Skins list - Now using centralized configuration!
                var availableSkins = _plugin._gunConfig.GetSkinsForWeapon(currentWeapon);
                
                for (int i = 0; i < availableSkins.Count; i++)
                {
                    var skinId = availableSkins[i];
                    float yMin = 0.96f - ((i + 1) * 0.20f);
                    float yMax = yMin + 0.18f;
                    
                    bool isOwned = session.Profile.OwnedSkins.Contains(skinId) || skinId == "0"; // Default skin is always owned
                    bool isEquipped = loadout.Skins.TryGetValue(currentWeapon, out string equippedSkin) && equippedSkin == skinId;
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = isOwned ? "0.14 0.14 0.18 1" : "0.10 0.10 0.14 0.5" },
                        RectTransform = { AnchorMin = $"0.03 {yMin}", AnchorMax = $"0.97 {yMax}" }
                    }, "SkinsScroll", $"SkinCard_{i}");
                    
                    if (isEquipped)
                    {
                        container.Add(new CuiPanel
                        {
                            Image = { Color = "0.6 0.4 1.0 0.6" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" }
                        }, $"SkinCard_{i}");
                    }
                    
                    // Skin image - using workshop skin ID for display
                    string skinDisplayName = skinId == "0" ? "Default" : $"Skin {skinId}";
                    string imageKey = $"{currentWeapon}_skin_{skinId}"; // ImageLibrary key format
                    
                    container.Add(new CuiElement
                    {
                        Parent = $"SkinCard_{i}",
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)_plugin.ImageLibrary?.Call("GetImage", imageKey) },
                            new CuiRectTransformComponent { AnchorMin = "0.05 0.05", AnchorMax = "0.35 0.45" }
                        }
                    });
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = skinDisplayName, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.40 0.70", AnchorMax = "0.95 0.90" }
                    }, $"SkinCard_{i}");
                    
                    string statusText = isEquipped ? "EQUIPPED" : (isOwned ? "OWNED" : "LOCKED");
                    string statusColor = isEquipped ? "0.4 1.0 0.4" : (isOwned ? "0.3 0.8 0.4" : "1.0 0.4 0.4");
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = statusText, FontSize = 8, Align = TextAnchor.MiddleLeft, Color = $"{statusColor} 1" },
                        RectTransform = { AnchorMin = "0.40 0.50", AnchorMax = "0.70 0.68" }
                    }, $"SkinCard_{i}");
                    
                    if (isEquipped)
                    {
                        container.Add(new CuiButton
                        {
                            Button = { Command = "", Color = "0.2 0.6 0.2 0.5" },
                            Text = { Text = "EQUIPPED", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.8 1 0.8 1" },
                            RectTransform = { AnchorMin = "0.40 0.10", AnchorMax = "0.95 0.40" }
                        }, $"SkinCard_{i}");
                    }
                    else if (isOwned)
                    {
                        container.Add(new CuiButton
                        {
                            Button = { Command = $"killadome.applyskin {editingSlot} {skinId}", Color = "0.2 0.6 0.3 0.9" },
                            Text = { Text = "EQUIP", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0.40 0.10", AnchorMax = "0.95 0.40" }
                        }, $"SkinCard_{i}");
                    }
                    else
                    {
                        container.Add(new CuiButton
                        {
                            Button = { Command = "", Color = "0.15 0.15 0.15 0.5" },
                            Text = { Text = "LOCKED", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.5 1" },
                            RectTransform = { AnchorMin = "0.40 0.10", AnchorMax = "0.95 0.40" }
                        }, $"SkinCard_{i}");
                    }
                }
                
                // RIGHT PANEL - ATTACHMENTS (with scrolling)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.10 0.10 0.14 0.95" },
                    RectTransform = { AnchorMin = "0.51 0.02", AnchorMax = "0.98 0.93" }
                }, "EditorArea", "AttachmentsPanel");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.10 0.15 0.20 0.9" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                }, "AttachmentsPanel");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "WEAPON ATTACHMENTS", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.7 1.0 1.0 1" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                }, "AttachmentsPanel");
                
                // Category tabs
                string selectedCategory = session.SelectedAttachmentCategory ?? "scopes";
                string[] categories = { "SCOPES", "BARREL", "UNDERBARREL" };
                
                for (int categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
                {
                    float xMin = 0.03f + (categoryIndex * 0.323f);
                    float xMax = xMin + 0.31f;
                    
                    bool isSelected = categories[categoryIndex].ToLower() == selectedCategory || 
                                     (categories[categoryIndex] == "BARREL" && selectedCategory == "silencers");
                    
                    string categoryCommand = categories[categoryIndex] == "BARREL" ? "silencers" : categories[categoryIndex].ToLower();
                    
                    container.Add(new CuiButton
                    {
                        Button = { Command = $"killadome.attachcat {categoryCommand}", Color = isSelected ? "0.2 0.6 0.3 0.9" : "0.1 0.3 0.15 0.9" },
                        Text = { Text = categories[categoryIndex], FontSize = 9, Align = TextAnchor.MiddleCenter, Color = isSelected ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                        RectTransform = { AnchorMin = $"{xMin} 0.91", AnchorMax = $"{xMax} 0.95" }
                    }, "AttachmentsPanel");
                }
                
                // Scrollable content area for attachments
                container.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.89" }
                }, "AttachmentsPanel", "AttachmentsScroll");
                
                // Attachments list
                var allAttachments = new[]
                {
                    new { Name = "Small Scope", Id = "weapon.mod.small.scope", ImageId = "small_scope", Category = "scopes" },
                    new { Name = "8x Scope", Id = "weapon.mod.8x.scope", ImageId = "8x_scope", Category = "scopes" },
                    new { Name = "Holo Sight", Id = "weapon.mod.holosight", ImageId = "holo_sight", Category = "underbarrel" },
                    new { Name = "Laser Sight", Id = "weapon.mod.lasersight", ImageId = "laser_sight", Category = "underbarrel" },
                    new { Name = "Soda Can Silencer", Id = "weapon.mod.sodacansilencer", ImageId = "sodacan_silencer", Category = "silencers" },
                    new { Name = "Silencer", Id = "weapon.mod.silencer", ImageId = "silencer", Category = "silencers" },
                    new { Name = "Muzzle Brake", Id = "weapon.mod.muzzlebrake", ImageId = "muzzle_brake", Category = "silencers" },
                    new { Name = "Muzzle Boost", Id = "weapon.mod.muzzleboost", ImageId = "muzzle_boost", Category = "silencers" },
                };
                
                // Only show owned attachments
                var availableAttachments = allAttachments
                    .Where(a => a.Category == selectedCategory && session.Profile.OwnedSkins.Contains(a.Id))
                    .ToArray();
                
                // Show helpful message if no attachments owned in this category
                if (availableAttachments.Length == 0)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "No attachments owned in this category.\nPurchase attachments in the Store Tab.", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.6 0.6 0.6 1" },
                        RectTransform = { AnchorMin = "0.1 0.35", AnchorMax = "0.9 0.65" }
                    }, "AttachmentsScroll");
                }
                
                for (int i = 0; i < availableAttachments.Length; i++)
                {
                    var att = availableAttachments[i];
                    float yMin = 0.96f - ((i + 1) * 0.20f);
                    float yMax = yMin + 0.18f;
                    
                    var attachments = editingSlot == "primary" ? loadout.PrimaryAttachments : loadout.SecondaryAttachments;
                    bool isEquipped = attachments.ContainsValue(att.Id);
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.14 0.14 0.18 1" },
                        RectTransform = { AnchorMin = $"0.03 {yMin}", AnchorMax = $"0.97 {yMax}" }
                    }, "AttachmentsScroll", $"AttCard_{i}");
                    
                    if (isEquipped)
                    {
                        container.Add(new CuiPanel
                        {
                            Image = { Color = "0.4 0.8 1.0 0.6" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" }
                        }, $"AttCard_{i}");
                    }
                    
                    // Attachment image
                    container.Add(new CuiElement
                    {
                        Parent = $"AttCard_{i}",
                        Components =
                        {
                            new CuiRawImageComponent { Png = (string)_plugin.ImageLibrary?.Call("GetImage", att.ImageId) },
                            new CuiRectTransformComponent { AnchorMin = "0.05 0.05", AnchorMax = "0.35 0.45" }
                        }
                    });
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = att.Name, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.40 0.70", AnchorMax = "0.95 0.90" }
                    }, $"AttCard_{i}");
                    
                    string statusText = isEquipped ? "EQUIPPED" : "OWNED";
                    string statusColor = isEquipped ? "0.4 1.0 0.4" : "0.3 0.8 0.4";
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = statusText, FontSize = 8, Align = TextAnchor.MiddleLeft, Color = $"{statusColor} 1" },
                        RectTransform = { AnchorMin = "0.40 0.50", AnchorMax = "0.70 0.68" }
                    }, $"AttCard_{i}");
                    
                    if (isEquipped)
                    {
                        container.Add(new CuiButton
                        {
                            Button = { Command = "", Color = "0.2 0.6 0.2 0.5" },
                            Text = { Text = "EQUIPPED", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.8 1 0.8 1" },
                            RectTransform = { AnchorMin = "0.40 0.10", AnchorMax = "0.95 0.40" }
                        }, $"AttCard_{i}");
                    }
                    else
                    {
                        container.Add(new CuiButton
                        {
                            Button = { Command = $"killadome.applyattachment {editingSlot} {att.Category} {att.Id}", Color = "0.2 0.6 0.3 0.9" },
                            Text = { Text = "EQUIP", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0.40 0.10", AnchorMax = "0.95 0.40" }
                        }, $"AttCard_{i}");
                    }
                }
                
                // === FOOTER === (6% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.01", AnchorMax = "0.95 0.06" }
                }, UI_TAB_CONTAINER, "LoadoutFooter");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.6 0.2 0.6" },
                    RectTransform = { AnchorMin = "0 0.90", AnchorMax = "1 1" }
                }, "LoadoutFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "Select weapons | Customize with skins and attachments | Purchase items in Store Tab", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.90" }
                }, "LoadoutFooter");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.6 0.2 0.4" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.05" }
                }, "LoadoutFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "VIC", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.8 0.9 1.0 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.95 0.85" }
                }, "LoadoutFooter");
            }
            
            private void ShowOutfitEditorContent(CuiElementContainer container, BasePlayer player, PlayerSession session, Loadout loadout)
            {
                // === OUTFIT CUSTOMIZATION SECTION === (76% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.06", AnchorMax = "0.95 0.82" }
                }, UI_TAB_CONTAINER, "OutfitEditorArea");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "CUSTOMIZE YOUR OUTFIT", FontSize = 14, Align = TextAnchor.UpperCenter, Color = "0.9 0.8 1.0 1" },
                    RectTransform = { AnchorMin = "0.02 0.96", AnchorMax = "0.98 1" }
                }, "OutfitEditorArea");
                
                // 5 Armor Slots in vertical tower (5 columns)
                string[] armorSlots = { "head", "chest", "legs", "hands", "feet" };
                string[] slotLabels = { "HEAD", "CHEST", "LEGS", "HANDS", "FEET" };
                string[] slotColors = { "1.0 0.3 0.3", "1.0 0.8 0.2", "0.3 0.6 1.0", "0.7 0.4 1.0", "0.4 1.0 0.5" };
                
                for (int i = 0; i < armorSlots.Length; i++)
                {
                    string slot = armorSlots[i];
                    string slotLabel = slotLabels[i];
                    string slotColor = slotColors[i];
                    
                    float xMin = 0.02f + (i * 0.195f);
                    float xMax = xMin + 0.185f;
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.10 0.10 0.14 0.95" },
                        RectTransform = { AnchorMin = $"{xMin} 0.02", AnchorMax = $"{xMax} 0.93" }
                    }, "OutfitEditorArea", $"ArmorSlot_{slot}");
                    
                    // Colored accent bar
                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"{slotColor} 0.6" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0.02 1" }
                    }, $"ArmorSlot_{slot}");
                    
                    // Slot label
                    container.Add(new CuiLabel
                    {
                        Text = { Text = slotLabel, FontSize = 11, Align = TextAnchor.UpperCenter, Color = $"{slotColor} 1" },
                        RectTransform = { AnchorMin = "0.05 0.93", AnchorMax = "0.95 0.99" }
                    }, $"ArmorSlot_{slot}");
                    
                    // Get owned armor for this slot
                    var ownedArmor = _plugin._outfitConfig.Armors
                        .Where(a => a.Slot == slot && session.Profile.OwnedArmor.Contains(a.ItemShortname))
                        .ToArray();
                    
                    // Get current armor shortname from loadout
                    string currentArmorShortname = slot switch
                    {
                        "head" => loadout.ArmorHead,
                        "chest" => loadout.ArmorChest,
                        "legs" => loadout.ArmorLegs,
                        "hands" => loadout.ArmorHands,
                        "feet" => loadout.ArmorFeet,
                        _ => null
                    };
                    
                    if (ownedArmor.Length == 0)
                    {
                        // No armor owned in this slot
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"No {slot}\narmor owned", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.5 1" },
                            RectTransform = { AnchorMin = "0.05 0.40", AnchorMax = "0.95 0.60" }
                        }, $"ArmorSlot_{slot}");
                        
                        container.Add(new CuiLabel
                        {
                            Text = { Text = "Purchase in\nOutfit Store", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "0.4 0.7 0.4 1" },
                            RectTransform = { AnchorMin = "0.05 0.25", AnchorMax = "0.95 0.38" }
                        }, $"ArmorSlot_{slot}");
                    }
                    else
                    {
                        // Find current armor in owned list
                        var currentArmor = ownedArmor.FirstOrDefault(a => a.ItemShortname == currentArmorShortname);
                        if (currentArmor == null) currentArmor = ownedArmor[0]; // Default to first owned
                        
                        // Armor image
                        container.Add(new CuiElement
                        {
                            Parent = $"ArmorSlot_{slot}",
                            Components =
                            {
                                new CuiRawImageComponent { Png = (string)_plugin.ImageLibrary?.Call("GetImage", currentArmor.ImageUrl) },
                                new CuiRectTransformComponent { AnchorMin = "0.15 0.45", AnchorMax = "0.85 0.85" }
                            }
                        });
                        
                        // Armor name
                        container.Add(new CuiPanel
                        {
                            Image = { Color = "0.08 0.08 0.12 0.9" },
                            RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.42" }
                        }, $"ArmorSlot_{slot}", $"ArmorNameBg_{slot}");
                        
                        container.Add(new CuiLabel
                        {
                            Text = { Text = currentArmor.DisplayName, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }, $"ArmorNameBg_{slot}");
                        
                        // Status indicator
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"EQUIPPED", FontSize = 7, Align = TextAnchor.MiddleCenter, Color = "0.4 1.0 0.4 1" },
                            RectTransform = { AnchorMin = "0.05 0.22", AnchorMax = "0.95 0.28" }
                        }, $"ArmorSlot_{slot}");
                        
                        // PREV button
                        container.Add(new CuiButton
                        {
                            Button = { Command = $"killadome.armor.prev {slot}", Color = "0.2 0.5 0.7 0.9" },
                            Text = { Text = "<", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.47 0.18" }
                        }, $"ArmorSlot_{slot}");
                        
                        // NEXT button
                        container.Add(new CuiButton
                        {
                            Button = { Command = $"killadome.armor.next {slot}", Color = "0.2 0.5 0.7 0.9" },
                            Text = { Text = ">", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0.53 0.05", AnchorMax = "0.95 0.18" }
                        }, $"ArmorSlot_{slot}");
                        
                        // Item count indicator
                        int currentIndex = Array.IndexOf(ownedArmor, currentArmor);
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"{currentIndex + 1}/{ownedArmor.Length}", FontSize = 7, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1" },
                            RectTransform = { AnchorMin = "0.05 0.01", AnchorMax = "0.95 0.04" }
                        }, $"ArmorSlot_{slot}");
                    }
                }
                
                // === FOOTER === (4% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.01", AnchorMax = "0.95 0.04" }
                }, UI_TAB_CONTAINER, "OutfitFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "Cycle through owned armor | Purchase more items in Outfit Store", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }, "OutfitFooter");
            }
            
            private void ShowStoreTab(CuiElementContainer container, BasePlayer player)
            {
                var session = _plugin.GetSession(player.userID);
                if (session == null)
                {
                    // Create session if it doesn't exist
                    var profile = _plugin._saveManager.LoadPlayerProfile(player.userID);
                    session = new PlayerSession(player, profile);
                    _plugin._activeSessions[player.userID] = session;
                }
                
                string selectedCategory = session.SelectedStoreCategory ?? "guns";
                
                // ===== HEADER SECTION - FULL WIDTH ===== (10% height)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.10 0.95" },
                    RectTransform = { AnchorMin = "0.01 0.88", AnchorMax = "0.99 0.99" }
                }, UI_TAB_CONTAINER, "StoreHeader");
                
                // Top accent line (cyan glow)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.2 0.8 1.0 0.6" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                }, "StoreHeader");
                
                // Title with shadow effect
                container.Add(new CuiLabel
                {
                    Text = { Text = "━━━  S T O R E  ━━━", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.95" }
                }, "StoreHeader");
                
                // Token balance with icon and glow effect
                string tokenPanelName = "TokenPanel";
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.08 0.02 0.9" },
                    RectTransform = { AnchorMin = "0.80 0.15", AnchorMax = "0.98 0.85" }
                }, "StoreHeader", tokenPanelName);
                
                // Token icon (using unicode symbol)
                container.Add(new CuiLabel
                {
                    Text = { Text = "◆", FontSize = 22, Align = TextAnchor.MiddleRight, Color = "1 0.8 0 1" },
                    RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.35 1" }
                }, tokenPanelName);
                
                // Token amount
                container.Add(new CuiLabel
                {
                    Text = { Text = $"{session.Profile.Tokens}", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 0.9 0.7 1" },
                    RectTransform = { AnchorMin = "0.35 0", AnchorMax = "0.95 1" }
                }, tokenPanelName);
                
                // ===== SUB-TAB BUTTONS ===== (5% height - full width)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.05 0.05 0.08 0.9" },
                    RectTransform = { AnchorMin = "0.01 0.82", AnchorMax = "0.99 0.87" }
                }, UI_TAB_CONTAINER, "StoreSubTabs");
                
                // Gun Store Sub-Tab
                bool isGunsSelected = selectedCategory == "guns";
                container.Add(new CuiButton
                {
                    Button = { Color = isGunsSelected ? "0.2 0.55 0.3 0.95" : "0.1 0.25 0.15 0.9", Command = "killadome.storecat guns" },
                    Text = { Text = "⚔ GUN STORE", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = isGunsSelected ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.01 0.1", AnchorMax = "0.24 0.9" }
                }, "StoreSubTabs");
                
                // Skins Store Sub-Tab
                bool isSkinsSelected = selectedCategory == "skins";
                container.Add(new CuiButton
                {
                    Button = { Color = isSkinsSelected ? "0.2 0.55 0.3 0.95" : "0.1 0.25 0.15 0.9", Command = "killadome.storecat skins" },
                    Text = { Text = "🎨 SKINS STORE", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = isSkinsSelected ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.26 0.1", AnchorMax = "0.49 0.9" }
                }, "StoreSubTabs");
                
                // Outfit Store Sub-Tab
                bool isOutfitsSelected = selectedCategory == "outfits";
                container.Add(new CuiButton
                {
                    Button = { Color = isOutfitsSelected ? "0.2 0.55 0.3 0.95" : "0.1 0.25 0.15 0.9", Command = "killadome.storecat outfits" },
                    Text = { Text = "👕 OUTFIT STORE", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = isOutfitsSelected ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.51 0.1", AnchorMax = "0.74 0.9" }
                }, "StoreSubTabs");
                
                // Display selected category content
                if (selectedCategory == "guns")
                {
                    ShowGunStoreContent(container, session, player);
                }
                else if (selectedCategory == "skins")
                {
                    ShowSkinsStoreContent(container, session, player);
                }
                else if (selectedCategory == "outfits")
                {
                    ShowOutfitStoreContent(container, session, player);
                }
            }
            
            private void ShowGunStoreContent(CuiElementContainer container, PlayerSession session, BasePlayer player)
            {
                // Load base guns from centralized config
                var baseGuns = _plugin._gunConfig.Guns.Select(g => new
                {
                    Name = g.Value.DisplayName,
                    Cost = g.Value.Cost,
                    Id = g.Value.Id,
                    ImageId = g.Value.ImageUrl,
                    Shortname = g.Value.RustItemShortname
                }).ToArray();
                
                // Pagination for base guns (8 per page for better fullscreen use)
                int itemsPerPage = 8;
                int currentPage = session.GunsStorePage;
                int totalPages = (int)Math.Ceiling((double)baseGuns.Length / itemsPerPage);
                var pagedGuns = baseGuns.Skip(currentPage * itemsPerPage).Take(itemsPerPage).ToArray();
                
                // ===== BASE GUNS SECTION (LEFT SIDE) - FULL WIDTH =====
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.49 0.80" }
                }, UI_TAB_CONTAINER, "BaseGunsSection");
                
                // Section header
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.08 0.15 0.95" },
                    RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
                }, "BaseGunsSection");
                
                // Section Title
                container.Add(new CuiLabel
                {
                    Text = { Text = "⚔  B A S E   G U N S", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.9 0.8 1.0 1" },
                    RectTransform = { AnchorMin = "0.05 0.94", AnchorMax = "0.70 1" }
                }, "BaseGunsSection");
                
                // Page indicator
                if (totalPages > 1)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"{currentPage + 1}/{totalPages}", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8 0.8 0.8 1" },
                        RectTransform = { AnchorMin = "0.70 0.94", AnchorMax = "0.95 1" }
                    }, "BaseGunsSection");
                }
                
                // Smaller gun cards
                float cardHeight = 0.14f;
                float cardSpacing = 0.01f;
                float startY = 0.92f;
                
                for (int i = 0; i < pagedGuns.Length; i++)
                {
                    var item = pagedGuns[i];
                    float yMax = startY - (i * (cardHeight + cardSpacing));
                    float yMin = yMax - cardHeight;
                    
                    string cardName = $"BaseGunCard_{i}";
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.1 0.3 0.15 0.95" },
                        RectTransform = { AnchorMin = $"0.02 {yMin}", AnchorMax = $"0.98 {yMax}" }
                    }, "BaseGunsSection", cardName);
                    
                    // Preview box (MADE EVEN BIGGER)
                    string previewName = $"StorePreviewBaseGun_{i}";
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.08 0.08 0.12 1" },
                        RectTransform = { AnchorMin = "0.05 0.10", AnchorMax = "0.45 0.90" }
                    }, cardName, previewName);
                    
                    // Try to add image
                    if (_plugin.ImageLibrary != null && _plugin.ImageLibrary.IsLoaded)
                    {
                        string imageId = (string)_plugin.ImageLibrary.Call("GetImage", item.ImageId);
                        if (!string.IsNullOrEmpty(imageId))
                        {
                            container.Add(new CuiElement
                            {
                                Parent = previewName,
                                Components =
                                {
                                    new CuiRawImageComponent { Png = imageId },
                                    new CuiRectTransformComponent { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" }
                                }
                            });
                        }
                    }
                    
                    // Name
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.Name, FontSize = 10, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.48 0.65", AnchorMax = "0.95 0.85" }
                    }, cardName);
                    
                    // Shortname
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.Shortname, FontSize = 8, Align = TextAnchor.UpperLeft, Color = "0.6 0.6 0.6 1" },
                        RectTransform = { AnchorMin = "0.48 0.48", AnchorMax = "0.95 0.65" }
                    }, cardName);
                    
                    // Cost with coin icon ABOVE button
                    bool canAfford = session.Profile.Tokens >= item.Cost;
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"◆ {item.Cost}", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = canAfford ? "1 0.8 0 1" : "0.6 0.3 0.3 1" },
                        RectTransform = { AnchorMin = "0.48 0.30", AnchorMax = "0.95 0.45" }
                    }, cardName);
                    
                    // Purchase button (below price)
                    container.Add(new CuiButton
                    {
                        Button = { Color = canAfford ? "0.2 0.6 0.2 0.9" : "0.3 0.3 0.3 0.5", Command = canAfford ? $"killadome.buygun {item.Id}" : "" },
                        Text = { Text = "BUY", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = canAfford ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.48 0.10", AnchorMax = "0.95 0.28" }
                    }, cardName);
                }
                
                // Pagination buttons
                if (totalPages > 1)
                {
                    bool canGoPrev = currentPage > 0;
                    container.Add(new CuiButton
                    {
                        Button = { Color = canGoPrev ? "0.2 0.6 0.3 0.9" : "0.3 0.3 0.3 0.5", Command = canGoPrev ? "killadome.storepage prev" : "" },
                        Text = { Text = "◀ PREV", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = canGoPrev ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.02 0.01", AnchorMax = "0.32 0.05" }
                    }, "BaseGunsSection");
                    
                    bool canGoNext = currentPage < totalPages - 1;
                    container.Add(new CuiButton
                    {
                        Button = { Color = canGoNext ? "0.2 0.6 0.3 0.9" : "0.3 0.3 0.3 0.5", Command = canGoNext ? "killadome.storepage next" : "" },
                        Text = { Text = "NEXT ▶", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = canGoNext ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.68 0.01", AnchorMax = "0.98 0.05" }
                    }, "BaseGunsSection");
                }
                
                // ===== ATTACHMENTS SECTION (RIGHT SIDE) - FULL WIDTH =====
                var attachments = new[]
                {
                    new { Name = "Small Scope", Cost = 250, Id = "weapon.mod.small.scope", ImageId = "small_scope", Category = "Optics" },
                    new { Name = "8x Scope", Cost = 400, Id = "weapon.mod.8x.scope", ImageId = "8x_scope", Category = "Optics" },
                    new { Name = "Holo Sight", Cost = 300, Id = "weapon.mod.holosight", ImageId = "holo_sight", Category = "Optics" },
                    new { Name = "Laser Sight", Cost = 250, Id = "weapon.mod.lasersight", ImageId = "laser_sight", Category = "Tactical" },
                    new { Name = "Soda Can Silencer", Cost = 150, Id = "weapon.mod.sodacansilencer", ImageId = "sodacan_silencer", Category = "Barrel" },
                    new { Name = "Oil Filter Silencer", Cost = 200, Id = "weapon.mod.oilfiltersilencer", ImageId = "oilfilter_silencer", Category = "Barrel" },
                    new { Name = "Silencer", Cost = 400, Id = "weapon.mod.silencer", ImageId = "silencer", Category = "Barrel" },
                    new { Name = "Muzzle Brake", Cost = 300, Id = "weapon.mod.muzzlebrake", ImageId = "muzzle_brake", Category = "Barrel" },
                    new { Name = "Muzzle Boost", Cost = 350, Id = "weapon.mod.muzzleboost", ImageId = "muzzle_boost", Category = "Barrel" }
                };
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.51 0.02", AnchorMax = "0.99 0.80" }
                }, UI_TAB_CONTAINER, "AttachmentsSection");
                
                // Section header
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.12 0.15 0.95" },
                    RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
                }, "AttachmentsSection");
                
                // Section Title
                container.Add(new CuiLabel
                {
                    Text = { Text = "⚙  A T T A C H M E N T S", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.7 1.0 1.0 1" },
                    RectTransform = { AnchorMin = "0.02 0.95", AnchorMax = "0.98 1" }
                }, "AttachmentsSection");
                
                // 3-column grid layout
                int itemsPerRow = 3;
                float cardWidth = 0.305f;
                float cardHeightAtt = 0.28f;
                float spacingX = 0.015f;
                float spacingY = 0.015f;
                float startX = 0.015f;
                float startYAtt = 0.92f;
                
                for (int i = 0; i < attachments.Length; i++)
                {
                    var item = attachments[i];
                    int row = i / itemsPerRow;
                    int col = i % itemsPerRow;
                    
                    float xMin = startX + (col * (cardWidth + spacingX));
                    float xMax = xMin + cardWidth;
                    float yMax = startYAtt - (row * (cardHeightAtt + spacingY));
                    float yMin = yMax - cardHeightAtt;
                    
                    string cardName = $"AttCard_{i}";
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.08 0.10 0.12 0.95" },
                        RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                    }, "AttachmentsSection", cardName);
                    
                    // Preview box
                    string previewName = $"StorePreviewAtt_{i}";
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.06 0.06 0.08 1" },
                        RectTransform = { AnchorMin = "0.1 0.38", AnchorMax = "0.9 0.88" }
                    }, cardName, previewName);
                    
                    // Try to add image
                    if (_plugin.ImageLibrary != null && _plugin.ImageLibrary.IsLoaded)
                    {
                        string imageId = (string)_plugin.ImageLibrary.Call("GetImage", item.ImageId);
                        if (!string.IsNullOrEmpty(imageId))
                        {
                            container.Add(new CuiElement
                            {
                                Parent = previewName,
                                Components =
                                {
                                    new CuiRawImageComponent { Png = imageId },
                                    new CuiRectTransformComponent { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" }
                                }
                            });
                        }
                    }
                    
                    // Category tag
                    string categoryColor = item.Category == "Optics" ? "0.5 0.7 1.0" : 
                                          item.Category == "Tactical" ? "1.0 0.5 0.5" : "0.5 1.0 0.7";
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.Category.ToUpper(), FontSize = 6, Align = TextAnchor.MiddleCenter, Color = $"{categoryColor} 0.8" },
                        RectTransform = { AnchorMin = "0.05 0.85", AnchorMax = "0.45 0.95" }
                    }, cardName);
                    
                    // Item name
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.Name, FontSize = 7, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.05 0.25", AnchorMax = "0.95 0.38" }
                    }, cardName);
                    
                    // Price
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "◆", FontSize = 9, Align = TextAnchor.MiddleRight, Color = "1 0.8 0 1" },
                        RectTransform = { AnchorMin = "0.20 0.12", AnchorMax = "0.40 0.23" }
                    }, cardName);
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"{item.Cost}", FontSize = 8, Align = TextAnchor.MiddleLeft, Color = "1 0.9 0.7 1" },
                        RectTransform = { AnchorMin = "0.40 0.12", AnchorMax = "0.70 0.23" }
                    }, cardName);
                    
                    // Buy button
                    bool canAfford = session.Profile.Tokens >= item.Cost;
                    string btnColor = canAfford ? "0.2 0.6 0.3" : "0.25 0.25 0.25";
                    string btnText = canAfford ? "BUY" : "🔒";
                    
                    container.Add(new CuiButton
                    {
                        Button = { Color = $"{btnColor} 0.9", Command = canAfford ? $"killadome.purchase {item.Id} {item.Cost}" : "" },
                        Text = { Text = btnText, FontSize = 8, Align = TextAnchor.MiddleCenter, Color = canAfford ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.15 0.02", AnchorMax = "0.85 0.10" }
                    }, cardName);
                }
            }
            
            private void ShowSkinsStoreContent(CuiElementContainer container, PlayerSession session, BasePlayer player)
            {
                // Get all available guns
                var allGuns = _plugin._gunConfig.GetAllGunIds();
                if (allGuns.Length == 0)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "No guns configured", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.99 0.82" }
                    }, UI_TAB_CONTAINER);
                    return;
                }
                
                // Default to first gun if none selected
                if (string.IsNullOrEmpty(session.SelectedGunForSkins))
                {
                    session.SelectedGunForSkins = allGuns[0];
                }
                
                // Validate selected gun still exists
                if (!_plugin._gunConfig.Guns.ContainsKey(session.SelectedGunForSkins))
                {
                    session.SelectedGunForSkins = allGuns[0];
                }
                
                var selectedGun = _plugin._gunConfig.Guns[session.SelectedGunForSkins];
                var skinDefinitions = _plugin._gunConfig.GetAllSkinDefinitions(session.SelectedGunForSkins);
                
                // ===== MAIN CONTAINER - FULL WIDTH =====
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.99 0.82" }
                }, UI_TAB_CONTAINER, "SkinsStoreSection");
                
                // ===== GUN SELECTOR ROW =====
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.08 0.15 0.95" },
                    RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 0.99" }
                }, "SkinsStoreSection", "GunSelectorBar");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "SELECT GUN:", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.9 0.8 1.0 1" },
                    RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.10 1" }
                }, "GunSelectorBar");
                
                // Gun buttons (horizontally arranged)
                float buttonWidth = 0.10f;
                float buttonSpacing = 0.005f;
                float startX = 0.11f;
                
                for (int i = 0; i < Math.Min(allGuns.Length, 8); i++)
                {
                    string gunId = allGuns[i];
                    var gun = _plugin._gunConfig.Guns[gunId];
                    bool isSelected = gunId == session.SelectedGunForSkins;
                    
                    float xMin = startX + (i * (buttonWidth + buttonSpacing));
                    float xMax = xMin + buttonWidth;
                    
                    string btnColor = isSelected ? "0.25 0.6 0.35" : "0.1 0.25 0.15";
                    
                    container.Add(new CuiButton
                    {
                        Button = { Color = $"{btnColor} 0.95", Command = $"killadome.selectgunforskins {gunId}" },
                        Text = { Text = gun.DisplayName, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = isSelected ? "1 1 1 1" : "0.7 0.7 0.7 1" },
                        RectTransform = { AnchorMin = $"{xMin} 0.1", AnchorMax = $"{xMax} 0.9" }
                    }, "GunSelectorBar");
                }
                
                // ===== SELECTED GUN DISPLAY =====
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.25 0.12 0.9" },
                    RectTransform = { AnchorMin = "0 0.85", AnchorMax = "1 0.91" }
                }, "SkinsStoreSection", "SelectedGunBar");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = $"🎨  Skins for: {selectedGun.DisplayName}  ({skinDefinitions.Count} available)", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1.0 0.95 0.85 1" },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }, "SelectedGunBar");
                
                // ===== SKINS GRID - FULL WIDTH =====
                int itemsPerPage = 15; // 5 columns × 3 rows
                int currentPage = session.SkinsStorePage;
                int totalPages = Math.Max(1, (int)Math.Ceiling((double)skinDefinitions.Count / itemsPerPage));
                var pagedSkins = skinDefinitions.Skip(currentPage * itemsPerPage).Take(itemsPerPage).ToList();
                
                // Page indicator
                if (totalPages > 1)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"Page {currentPage + 1}/{totalPages}", FontSize = 10, Align = TextAnchor.MiddleRight, Color = "0.8 0.8 0.8 1" },
                        RectTransform = { AnchorMin = "0.85 0.85", AnchorMax = "0.99 0.91" }
                    }, "SkinsStoreSection");
                }
                
                // Grid layout: 5 columns × 3 rows for better fullscreen use
                int itemsPerRow = 5;
                float cardWidth = 0.185f;
                float cardHeight = 0.26f;
                float spacingX = 0.01f;
                float spacingY = 0.01f;
                float startX2 = 0.015f;
                float startY = 0.82f;
                
                for (int i = 0; i < pagedSkins.Count; i++)
                {
                    var skinDef = pagedSkins[i];
                    int row = i / itemsPerRow;
                    int col = i % itemsPerRow;
                    
                    float xMin = startX2 + (col * (cardWidth + spacingX));
                    float xMax = xMin + cardWidth;
                    float yMax = startY - (row * (cardHeight + spacingY));
                    float yMin = yMax - cardHeight;
                    
                    string cardName = $"SkinCard_{i}";
                    
                    // Check ownership
                    bool isOwned = session.Profile.OwnedSkins.Contains(skinDef.SkinId) || skinDef.SkinId == "0";
                    bool isDefault = skinDef.SkinId == "0";
                    
                    // Get rarity color
                    string rarityColor = GetRarityColor(skinDef.Rarity);
                    string cardBgColor = isOwned ? "0.12 0.15 0.12 0.95" : "0.08 0.08 0.10 0.95";
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = cardBgColor },
                        RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                    }, "SkinsStoreSection", cardName);
                    
                    // Rarity border accent (top)
                    container.Add(new CuiPanel
                    {
                        Image = { Color = $"{rarityColor} 0.8" },
                        RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" }
                    }, cardName);
                    
                    // Tag badge (NEW, HOT, POPULAR)
                    if (!string.IsNullOrEmpty(skinDef.Tag))
                    {
                        string tagColor = skinDef.Tag == "NEW" ? "0.2 0.8 0.3" : 
                                         skinDef.Tag == "HOT" ? "1.0 0.4 0.2" : 
                                         skinDef.Tag == "POPULAR" ? "0.3 0.6 1.0" : "0.5 0.5 0.5";
                        container.Add(new CuiPanel
                        {
                            Image = { Color = $"{tagColor} 0.9" },
                            RectTransform = { AnchorMin = "0.70 0.88", AnchorMax = "0.98 0.95" }
                        }, cardName, $"{cardName}_tagbg");
                        
                        container.Add(new CuiLabel
                        {
                            Text = { Text = skinDef.Tag, FontSize = 6, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }, $"{cardName}_tagbg");
                    }
                    
                    // Skin preview image
                    string imageKey = $"{session.SelectedGunForSkins}_skin_{skinDef.SkinId}";
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.06 0.06 0.08 1" },
                        RectTransform = { AnchorMin = "0.05 0.42", AnchorMax = "0.95 0.88" }
                    }, cardName, $"{cardName}_preview");
                    
                    if (_plugin.ImageLibrary != null && _plugin.ImageLibrary.IsLoaded)
                    {
                        string imgData = (string)_plugin.ImageLibrary.Call("GetImage", imageKey);
                        if (!string.IsNullOrEmpty(imgData))
                        {
                            container.Add(new CuiElement
                            {
                                Parent = $"{cardName}_preview",
                                Components =
                                {
                                    new CuiRawImageComponent { Png = imgData },
                                    new CuiRectTransformComponent { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
                                }
                            });
                        }
                    }
                    
                    // Skin name
                    container.Add(new CuiLabel
                    {
                        Text = { Text = skinDef.DisplayName, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0.02 0.32", AnchorMax = "0.98 0.42" }
                    }, cardName);
                    
                    // Rarity label with color
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"★ {skinDef.Rarity}", FontSize = 7, Align = TextAnchor.MiddleCenter, Color = $"{rarityColor} 1" },
                        RectTransform = { AnchorMin = "0.02 0.24", AnchorMax = "0.98 0.32" }
                    }, cardName);
                    
                    // Status badge or price/buy button
                    if (isOwned)
                    {
                        container.Add(new CuiPanel
                        {
                            Image = { Color = "0.15 0.4 0.2 0.9" },
                            RectTransform = { AnchorMin = "0.15 0.03", AnchorMax = "0.85 0.22" }
                        }, cardName, $"{cardName}_owned");
                        
                        container.Add(new CuiLabel
                        {
                            Text = { Text = "✓ OWNED", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.4 1.0 0.5 1" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }, $"{cardName}_owned");
                    }
                    else
                    {
                        bool canAfford = session.Profile.Tokens >= skinDef.Cost;
                        string priceColor = canAfford ? "1 0.85 0.3" : "0.6 0.35 0.35";
                        
                        // Price display
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"◆ {skinDef.Cost}", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = $"{priceColor} 1" },
                            RectTransform = { AnchorMin = "0.05 0.14", AnchorMax = "0.95 0.24" }
                        }, cardName);
                        
                        // Buy button
                        string btnColor = canAfford ? "0.2 0.6 0.3" : "0.25 0.25 0.25";
                        string btnText = canAfford ? "BUY" : "🔒";
                        
                        container.Add(new CuiButton
                        {
                            Button = { Color = $"{btnColor} 0.95", Command = canAfford ? $"killadome.buyskin {session.SelectedGunForSkins} {skinDef.SkinId}" : "" },
                            Text = { Text = btnText, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = canAfford ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                            RectTransform = { AnchorMin = "0.15 0.03", AnchorMax = "0.85 0.13" }
                        }, cardName);
                    }
                }
                
                // Pagination buttons
                if (totalPages > 1)
                {
                    bool canGoPrev = currentPage > 0;
                    container.Add(new CuiButton
                    {
                        Button = { Color = canGoPrev ? "0.2 0.5 0.3 0.9" : "0.2 0.2 0.2 0.5", Command = canGoPrev ? "killadome.storepage skins prev" : "" },
                        Text = { Text = "◀ PREV", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = canGoPrev ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.01 0.01", AnchorMax = "0.15 0.05" }
                    }, "SkinsStoreSection");
                    
                    bool canGoNext = currentPage < totalPages - 1;
                    container.Add(new CuiButton
                    {
                        Button = { Color = canGoNext ? "0.2 0.5 0.3 0.9" : "0.2 0.2 0.2 0.5", Command = canGoNext ? "killadome.storepage skins next" : "" },
                        Text = { Text = "NEXT ▶", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = canGoNext ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.85 0.01", AnchorMax = "0.99 0.05" }
                    }, "SkinsStoreSection");
                }
            }
            
            /// <summary>
            /// Get UI color string for rarity tier (RGB format for Rust UI)
            /// </summary>
            private string GetRarityColor(string rarity)
            {
                switch (rarity?.ToLower())
                {
                    case "legendary": return "1.0 0.6 0.1";
                    case "epic": return "0.7 0.3 1.0";
                    case "rare": return "0.3 0.6 1.0";
                    case "common":
                    default: return "0.6 0.6 0.6";
                }
            }
            
            private void ShowOutfitStoreContent(CuiElementContainer container, PlayerSession session, BasePlayer player)
            {
                // ===== OUTFIT STORE SECTION ===== (Full width)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.99 0.82" }
                }, UI_TAB_CONTAINER, "OutfitStoreSection");
                
                // Section header bar
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.18 0.08 0.12 0.95" },
                    RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
                }, "OutfitStoreSection");
                
                // Section Title with icon
                container.Add(new CuiLabel
                {
                    Text = { Text = "👕  A R M O R   S T O R E", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1.0 0.8 0.9 1" },
                    RectTransform = { AnchorMin = "0.05 0.96", AnchorMax = "0.95 1" }
                }, "OutfitStoreSection");
                
                // Decorative corner accents
                container.Add(new CuiPanel
                {
                    Image = { Color = "1.0 0.4 0.6 0.3" },
                    RectTransform = { AnchorMin = "0 0.96", AnchorMax = "0.02 1" }
                }, "OutfitStoreSection");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1.0 0.4 0.6 0.3" },
                    RectTransform = { AnchorMin = "0.98 0.96", AnchorMax = "1 1" }
                }, "OutfitStoreSection");
                
                // Display armor items in a 4-column grid
                var armors = _plugin._outfitConfig.Armors;
                int cols = 4;
                int rows = 3;
                int itemsPerPage = cols * rows; // 12 items
                float cardWidth = 0.22f;
                float cardHeight = 0.28f;
                float spacingX = 0.02f;
                float spacingY = 0.02f;
                float startX = 0.05f;
                float startY = 0.92f;
                
                for (int i = 0; i < Math.Min(armors.Count, itemsPerPage); i++)
                {
                    var armor = armors[i];
                    int col = i % cols;
                    int row = i / cols;
                    
                    float xMin = startX + (col * (cardWidth + spacingX));
                    float yMax = startY - (row * (cardHeight + spacingY));
                    float xMax = xMin + cardWidth;
                    float yMin = yMax - cardHeight;
                    
                    string cardName = $"ArmorCard_{i}";
                    
                    // Card background
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.12 0.08 0.15 0.95" },
                        RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                    }, "OutfitStoreSection", cardName);
                    
                    // Armor image
                    if (_plugin.ImageLibrary != null && _plugin.ImageLibrary.IsLoaded)
                    {
                        string imageId = (string)_plugin.ImageLibrary.Call("GetImage", armor.ImageUrl);
                        if (!string.IsNullOrEmpty(imageId))
                        {
                            container.Add(new CuiElement
                            {
                                Parent = cardName,
                                Components =
                                {
                                    new CuiRawImageComponent { Png = imageId },
                                    new CuiRectTransformComponent { AnchorMin = "0.1 0.40", AnchorMax = "0.9 0.90" }
                                }
                            });
                        }
                    }
                    
                    // Armor name
                    container.Add(new CuiLabel
                    {
                        Text = { Text = armor.Name, FontSize = 8, Align = TextAnchor.UpperCenter, Color = "1 0.9 0.95 1" },
                        RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.38" }
                    }, cardName);
                    
                    // Slot label
                    string slotColor = armor.Slot == "head" ? "0.8 0.5 1.0" :
                                      armor.Slot == "chest" ? "0.5 0.8 1.0" :
                                      armor.Slot == "legs" ? "1.0 0.7 0.5" :
                                      armor.Slot == "hands" ? "0.7 1.0 0.5" : "1.0 0.5 0.5";
                    container.Add(new CuiLabel
                    {
                        Text = { Text = armor.Slot.ToUpper(), FontSize = 6, Align = TextAnchor.MiddleCenter, Color = $"{slotColor} 1" },
                        RectTransform = { AnchorMin = "0.05 0.22", AnchorMax = "0.95 0.30" }
                    }, cardName);
                    
                    // Rarity
                    string rarityColor = armor.Rarity == "Epic" ? "0.6 0.3 1.0" : 
                                        armor.Rarity == "Legendary" ? "1.0 0.6 0.2" :
                                        armor.Rarity == "Rare" ? "0.3 0.7 1.0" : "0.5 0.5 0.5";
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"★ {armor.Rarity}", FontSize = 7, Align = TextAnchor.MiddleCenter, Color = $"{rarityColor} 1" },
                        RectTransform = { AnchorMin = "0.05 0.14", AnchorMax = "0.95 0.22" }
                    }, cardName);
                    
                    // Price
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "◆", FontSize = 8, Align = TextAnchor.MiddleRight, Color = "1 0.8 0 1" },
                        RectTransform = { AnchorMin = "0.25 0.06", AnchorMax = "0.45 0.14" }
                    }, cardName);
                    
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"{armor.Cost}", FontSize = 7, Align = TextAnchor.MiddleLeft, Color = "1 0.9 0.7 1" },
                        RectTransform = { AnchorMin = "0.45 0.06", AnchorMax = "0.75 0.14" }
                    }, cardName);
                    
                    // Buy button
                    bool canAfford = session.Profile.Tokens >= armor.Cost;
                    string btnColor = canAfford ? "0.2 0.6 0.3" : "0.3 0.3 0.3";
                    string btnText = canAfford ? "BUY" : "🔒";
                    
                    container.Add(new CuiButton
                    {
                        Button = { Color = $"{btnColor} 0.9", Command = canAfford ? $"killadome.purchase.armor {armor.ItemShortname} {armor.Cost}" : "" },
                        Text = { Text = btnText, FontSize = 7, Align = TextAnchor.MiddleCenter, Color = canAfford ? "1 1 1 1" : "0.5 0.5 0.5 1" },
                        RectTransform = { AnchorMin = "0.15 0.01", AnchorMax = "0.85 0.05" }
                    }, cardName);
                }
            }
            
            private void ShowStatsTab(CuiElementContainer container, BasePlayer player)
            {
                // Header panel
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.95" },
                    RectTransform = { AnchorMin = "0.01 0.88", AnchorMax = "0.99 0.99" }
                }, UI_TAB_CONTAINER, "StatsHeader");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "1 0.7 0.2 0.5" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.04" }
                }, "StatsHeader");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "━━━ YOUR STATS ━━━", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "1 0.85 0.4 1" },
                    RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 1" }
                }, "StatsHeader");
                
                // Main stats container
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.15 0.10", AnchorMax = "0.85 0.85" }
                }, UI_TAB_CONTAINER, "StatsContent");
                
                var session = _plugin.GetSession(player.userID);
                if (session != null)
                {
                    var profile = session.Profile;
                    float kd = profile.TotalDeaths > 0 ? (float)profile.TotalKills / profile.TotalDeaths : profile.TotalKills;
                    
                    // Stats grid - larger cards
                    var statsData = new[] {
                        new { Label = "KILLS", Value = profile.TotalKills.ToString(), Color = "0.3 0.8 0.4" },
                        new { Label = "DEATHS", Value = profile.TotalDeaths.ToString(), Color = "0.8 0.3 0.3" },
                        new { Label = "K/D RATIO", Value = kd.ToString("F2"), Color = "0.4 0.7 1.0" },
                        new { Label = "BLOOD TOKENS", Value = profile.Tokens.ToString(), Color = "1.0 0.7 0.2" },
                        new { Label = "MATCHES PLAYED", Value = profile.MatchesPlayed.ToString(), Color = "0.7 0.5 1.0" },
                        new { Label = "VIP STATUS", Value = profile.IsVIP ? "ACTIVE" : "INACTIVE", Color = profile.IsVIP ? "0.3 1.0 0.5" : "0.5 0.5 0.5" }
                    };
                    
                    int cols = 3;
                    float cardWidth = 0.30f;
                    float cardHeight = 0.35f;
                    float spacingX = 0.025f;
                    float spacingY = 0.05f;
                    float startX = 0.03f;
                    float startY = 0.90f;
                    
                    for (int i = 0; i < statsData.Length; i++)
                    {
                        int row = i / cols;
                        int col = i % cols;
                        
                        float xMin = startX + (col * (cardWidth + spacingX));
                        float xMax = xMin + cardWidth;
                        float yMax = startY - (row * (cardHeight + spacingY));
                        float yMin = yMax - cardHeight;
                        
                        string cardName = $"StatCard_{i}";
                        
                        container.Add(new CuiPanel
                        {
                            Image = { Color = "0.08 0.08 0.12 0.95" },
                            RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                        }, "StatsContent", cardName);
                        
                        // Top accent
                        container.Add(new CuiPanel
                        {
                            Image = { Color = $"{statsData[i].Color} 0.8" },
                            RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
                        }, cardName);
                        
                        // Label
                        container.Add(new CuiLabel
                        {
                            Text = { Text = statsData[i].Label, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1" },
                            RectTransform = { AnchorMin = "0 0.60", AnchorMax = "1 0.85" }
                        }, cardName);
                        
                        // Value
                        container.Add(new CuiLabel
                        {
                            Text = { Text = statsData[i].Value, FontSize = 28, Align = TextAnchor.MiddleCenter, Color = $"{statsData[i].Color} 1" },
                            RectTransform = { AnchorMin = "0 0.15", AnchorMax = "1 0.60" }
                        }, cardName);
                    }
                }
                else
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "No stats available", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.6 0.6 0.6 1" },
                        RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = "0.9 0.6" }
                    }, "StatsContent");
                }
                
                // Footer
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.8" },
                    RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.99 0.08" }
                }, UI_TAB_CONTAINER, "StatsFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "📊 Stats are updated in real-time as you play", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.7 0.8 0.9 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "StatsFooter");
            }
            
            private void ShowSettingsTab(CuiElementContainer container, BasePlayer player)
            {
                // Header panel
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.08 0.08 0.12 0.95" },
                    RectTransform = { AnchorMin = "0.01 0.88", AnchorMax = "0.99 0.99" }
                }, UI_TAB_CONTAINER, "SettingsHeader");
                
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.5 0.5 0.8 0.5" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.04" }
                }, "SettingsHeader");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "━━━ SETTINGS ━━━", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 1.0 1" },
                    RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 1" }
                }, "SettingsHeader");
                
                // Settings container
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.04 0.04 0.06 0.9" },
                    RectTransform = { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" }
                }, UI_TAB_CONTAINER, "SettingsContent");
                
                // Section title
                container.Add(new CuiLabel
                {
                    Text = { Text = "⚙ Plugin Configuration", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 1.0 1" },
                    RectTransform = { AnchorMin = "0.05 0.85", AnchorMax = "0.95 0.95" }
                }, "SettingsContent");
                
                var settingsData = new[] {
                    new { Label = "UI Update Throttle", Value = "100ms", Icon = "⏱" },
                    new { Label = "Auto-Save Interval", Value = "5 minutes", Icon = "💾" },
                    new { Label = "Debug Logging", Value = "Disabled", Icon = "🔧" },
                    new { Label = "Plugin Version", Value = "1.0.0", Icon = "📦" }
                };
                
                float settingHeight = 0.12f;
                float startY = 0.78f;
                
                for (int i = 0; i < settingsData.Length; i++)
                {
                    float yMax = startY - (i * (settingHeight + 0.02f));
                    float yMin = yMax - settingHeight;
                    
                    string rowName = $"SettingRow_{i}";
                    
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.08 0.08 0.12 0.9" },
                        RectTransform = { AnchorMin = $"0.05 {yMin}", AnchorMax = $"0.95 {yMax}" }
                    }, "SettingsContent", rowName);
                    
                    // Icon
                    container.Add(new CuiLabel
                    {
                        Text = { Text = settingsData[i].Icon, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.7 0.8 1.0 1" },
                        RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.12 1" }
                    }, rowName);
                    
                    // Label
                    container.Add(new CuiLabel
                    {
                        Text = { Text = settingsData[i].Label, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                        RectTransform = { AnchorMin = "0.14 0", AnchorMax = "0.6 1" }
                    }, rowName);
                    
                    // Value
                    container.Add(new CuiLabel
                    {
                        Text = { Text = settingsData[i].Value, FontSize = 14, Align = TextAnchor.MiddleRight, Color = "0.6 0.8 0.6 1" },
                        RectTransform = { AnchorMin = "0.6 0", AnchorMax = "0.95 1" }
                    }, rowName);
                }
                
                // Config file info
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.10 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.20" }
                }, "SettingsContent", "ConfigInfo");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "📁 Configuration Files:", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.8 0.8 0.8 1" },
                    RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.98 0.95" }
                }, "ConfigInfo");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "• oxide/config/KillaDome.json - Main configuration\n• oxide/data/KillaDome/Guns.json - Weapon & skin definitions\n• oxide/data/KillaDome/Armor.json - Armor definitions", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.6 0.7 0.8 1" },
                    RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.97 0.60" }
                }, "ConfigInfo");
                
                // Footer
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.08 0.8" },
                    RectTransform = { AnchorMin = "0.01 0.02", AnchorMax = "0.99 0.08" }
                }, UI_TAB_CONTAINER, "SettingsFooter");
                
                container.Add(new CuiLabel
                {
                    Text = { Text = "💡 Reload plugin with 'oxide.reload KillaDome' after changing config files", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.7 0.8 0.9 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "SettingsFooter");
            }
            
            public void DestroyUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UI_MAIN);
            }
        }
        
        #endregion
        
        #region Module: LoadoutEditor
        
        internal class LoadoutEditor
        {
            private KillaDome _plugin;
            private AttachmentSystem _attachmentSystem;
            private Dictionary<ulong, string> _selectedItems = new Dictionary<ulong, string>();
            
            internal LoadoutEditor(KillaDome plugin, AttachmentSystem attachmentSystem)
            {
                _plugin = plugin;
                _attachmentSystem = attachmentSystem;
            }
            
            public void SelectItem(ulong steamId, string itemId)
            {
                _selectedItems[steamId] = itemId;
            }
            
            public string GetSelectedItem(ulong steamId)
            {
                _selectedItems.TryGetValue(steamId, out string itemId);
                return itemId;
            }
            
            public void ClearSelection(ulong steamId)
            {
                _selectedItems.Remove(steamId);
            }
            
            public bool TryEquipItem(ulong steamId, string slotId, string itemId)
            {
                var session = _plugin.GetSession(steamId);
                if (session == null || session.Profile.Loadouts.Count == 0)
                {
                    return false;
                }
                
                var loadout = session.Profile.Loadouts[0]; // Current loadout
                
                // Validate and equip based on slot type
                if (slotId.StartsWith("primary_att_"))
                {
                    string attachmentSlot = slotId.Replace("primary_att_", "");
                    loadout.PrimaryAttachments[attachmentSlot] = itemId;
                    return true;
                }
                
                return false;
            }
        }
        
        #endregion
        
        #region Module: AttachmentSystem
        
        internal class AttachmentSystem
        {
            private KillaDome _plugin;
            private PluginConfig _config;
            private Dictionary<string, AttachmentDefinition> _attachments;
            
            internal AttachmentSystem(KillaDome plugin, PluginConfig config)
            {
                _plugin = plugin;
                _config = config;
                InitializeAttachments();
            }
            
            private void InitializeAttachments()
            {
                _attachments = new Dictionary<string, AttachmentDefinition>
                {
                    ["silencer"] = new AttachmentDefinition
                    {
                        Id = "silencer",
                        Name = "Silencer",
                        Slot = "barrel",
                        VFXTag = "silencer_smoke",
                        SFXTag = "silencer_sound"
                    },
                    ["extended_mag"] = new AttachmentDefinition
                    {
                        Id = "extended_mag",
                        Name = "Extended Magazine",
                        Slot = "mag",
                        VFXTag = "extended_mag_visual",
                        SFXTag = "mag_sound"
                    },
                    ["reflex"] = new AttachmentDefinition
                    {
                        Id = "reflex",
                        Name = "Reflex Sight",
                        Slot = "optic",
                        VFXTag = "reflex_glow",
                        SFXTag = "optic_sound"
                    }
                };
            }
            
            internal AttachmentDefinition GetAttachment(string attachmentId)
            {
                _attachments.TryGetValue(attachmentId, out var attachment);
                return attachment;
            }
        }
        
        internal class AttachmentDefinition
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Slot { get; set; }
            public string VFXTag { get; set; }
            public string SFXTag { get; set; }
        }
        
        #endregion
        
        #region Module: VFXManager
        
        internal class VFXManager
        {
            private KillaDome _plugin;
            
            internal VFXManager(KillaDome plugin)
            {
                _plugin = plugin;
            }
            
            public void PlayVFX(BasePlayer player, string vfxTag, Vector3 position)
            {
                // This would trigger client-side VFX
                player.SendConsoleCommand($"killadome.vfx {vfxTag} {position.x} {position.y} {position.z}");
            }
        }
        
        #endregion
        
        #region Module: SFXManager
        
        internal class SFXManager
        {
            private KillaDome _plugin;
            
            internal SFXManager(KillaDome plugin)
            {
                _plugin = plugin;
            }
            
            public void PlaySFX(BasePlayer player, string sfxTag)
            {
                // This would trigger client-side SFX
                player.SendConsoleCommand($"killadome.sfx {sfxTag}");
            }
        }
        
        #endregion
        
        #region Module: ForgeStationSystem
        
        internal class ForgeStationSystem
        {
            private KillaDome _plugin;
            private PluginConfig _config;
            private BloodTokenEconomy _economy;
            private AttachmentSystem _attachmentSystem;
            
            internal ForgeStationSystem(KillaDome plugin, PluginConfig config, BloodTokenEconomy economy, 
                AttachmentSystem attachmentSystem)
            {
                _plugin = plugin;
                _config = config;
                _economy = economy;
                _attachmentSystem = attachmentSystem;
            }
            
            public bool UpgradeAttachment(ulong steamId, string attachmentId)
            {
                // Attachments are now cosmetic only (VFX/SFX tags)
                // No progression or upgrading needed
                return false;
            }
        }
        
        #endregion
        
        #region Module: BloodTokenEconomy
        
        internal class BloodTokenEconomy
        {
            private KillaDome _plugin;
            private PluginConfig _config;
            
            internal BloodTokenEconomy(KillaDome plugin, PluginConfig config)
            {
                _plugin = plugin;
                _config = config;
            }
            
            public void AwardTokens(ulong steamId, int amount)
            {
                var session = _plugin.GetSession(steamId);
                if (session == null) return;
                
                session.Profile.Tokens += amount;
                _plugin.LogDebug($"Awarded {amount} tokens to {steamId}. New balance: {session.Profile.Tokens}");
            }
            
            public bool SpendTokens(ulong steamId, int amount)
            {
                var session = _plugin.GetSession(steamId);
                if (session == null || session.Profile.Tokens < amount)
                {
                    return false;
                }
                
                session.Profile.Tokens -= amount;
                return true;
            }
            
            public int GetBalance(ulong steamId)
            {
                var session = _plugin.GetSession(steamId);
                return session?.Profile.Tokens ?? 0;
            }
            
            public bool PurchaseItem(ulong steamId, string itemId, int cost)
            {
                if (!SpendTokens(steamId, cost))
                {
                    return false;
                }
                
                var session = _plugin.GetSession(steamId);
                if (session == null) return false;
                
                session.Profile.OwnedSkins.Add(itemId);
                _plugin.LogDebug($"Player {steamId} purchased {itemId} for {cost} tokens");
                
                return true;
            }
        }
        
        #endregion
        
        #region Module: SaveManager
        
        internal class SaveManager
        {
            private KillaDome _plugin;
            private PluginConfig _config;
            private string _dataDirectory;
            
            internal SaveManager(KillaDome plugin, PluginConfig config)
            {
                _plugin = plugin;
                _config = config;
                _dataDirectory = Path.Combine(Interface.Oxide.DataDirectory, "KillaDome");
                
                if (!Directory.Exists(_dataDirectory))
                {
                    Directory.CreateDirectory(_dataDirectory);
                }
            }
            
            public PlayerProfile LoadPlayerProfile(ulong steamId)
            {
                string filePath = Path.Combine(_dataDirectory, $"{steamId}.json");
                
                if (!File.Exists(filePath))
                {
                    return new PlayerProfile(steamId, _config.StartingTokens);
                }
                
                try
                {
                    string json = File.ReadAllText(filePath);
                    var profile = JsonConvert.DeserializeObject<PlayerProfile>(json);
                    _plugin.LogDebug($"Loaded profile for {steamId}");
                    return profile ?? new PlayerProfile(steamId, _config.StartingTokens);
                }
                catch (Exception ex)
                {
                    _plugin.PrintError($"Failed to load profile for {steamId}: {ex.Message}");
                    return new PlayerProfile(steamId, _config.StartingTokens);
                }
            }
            
            public void SavePlayerProfile(PlayerProfile profile)
            {
                if (profile == null) return;
                
                profile.LastUpdated = DateTime.UtcNow;
                
                string filePath = Path.Combine(_dataDirectory, $"{profile.SteamID}.json");
                string tempPath = filePath + ".tmp";
                
                try
                {
                    string json = JsonConvert.SerializeObject(profile, Formatting.Indented);
                    File.WriteAllText(tempPath, json);
                    
                    // Atomic swap
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    File.Move(tempPath, filePath);
                    
                    _plugin.LogDebug($"Saved profile for {profile.SteamID}");
                }
                catch (Exception ex)
                {
                    _plugin.PrintError($"Failed to save profile for {profile.SteamID}: {ex.Message}");
                }
            }
        }
        
        #endregion
        
        #region Module: AntiExploit
        
        internal class AntiExploit
        {
            private KillaDome _plugin;
            private Dictionary<ulong, RateLimiter> _rateLimiters = new Dictionary<ulong, RateLimiter>();
            
            internal AntiExploit(KillaDome plugin)
            {
                _plugin = plugin;
            }
            
            public bool CheckRateLimit(ulong steamId, int maxActionsPerSecond = 5)
            {
                if (!_rateLimiters.TryGetValue(steamId, out var limiter))
                {
                    limiter = new RateLimiter(maxActionsPerSecond);
                    _rateLimiters[steamId] = limiter;
                }
                
                return limiter.AllowAction();
            }
            
            public bool ValidateAction(ulong steamId, string action)
            {
                if (!CheckRateLimit(steamId))
                {
                    _plugin.PrintWarning($"Rate limit exceeded for {steamId} on action {action}");
                    return false;
                }
                
                return true;
            }
        }
        
        internal class RateLimiter
        {
            private int _maxActions;
            private Queue<DateTime> _actions = new Queue<DateTime>();
            
            internal RateLimiter(int maxActionsPerSecond)
            {
                _maxActions = maxActionsPerSecond;
            }
            
            public bool AllowAction()
            {
                var now = DateTime.UtcNow;
                var cutoff = now.AddSeconds(-1);
                
                // Remove old actions
                while (_actions.Count > 0 && _actions.Peek() < cutoff)
                {
                    _actions.Dequeue();
                }
                
                if (_actions.Count >= _maxActions)
                {
                    return false;
                }
                
                _actions.Enqueue(now);
                return true;
            }
        }
        
        #endregion
        
        #region Module: TelemetrySystem
        
        internal class TelemetrySystem
        {
            private KillaDome _plugin;
            private Dictionary<string, int> _eventCounts = new Dictionary<string, int>();
            
            internal TelemetrySystem(KillaDome plugin)
            {
                _plugin = plugin;
            }
            
            public void RecordKill(ulong attackerId, ulong victimId)
            {
                IncrementEvent("kills");
                
                var session = _plugin.GetSession(attackerId);
                if (session != null)
                {
                    session.Profile.TotalKills++;
                }
                
                var victimSession = _plugin.GetSession(victimId);
                if (victimSession != null)
                {
                    victimSession.Profile.TotalDeaths++;
                }
            }
            
            public void RecordPurchase(ulong steamId, string itemId, int cost)
            {
                IncrementEvent("purchases");
                _plugin.LogDebug($"Telemetry: Purchase - {steamId}, {itemId}, {cost}");
            }
            
            private void IncrementEvent(string eventName)
            {
                if (!_eventCounts.ContainsKey(eventName))
                {
                    _eventCounts[eventName] = 0;
                }
                _eventCounts[eventName]++;
            }
            
            public Dictionary<string, int> GetStats()
            {
                return new Dictionary<string, int>(_eventCounts);
            }
        }
        
        #endregion
    }
}