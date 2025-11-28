using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using Rust;
using Newtonsoft.Json;

// NecroZombies - scarecrow-based zombies & hellhounds with BO-style drip waves, spawn sets,
// hop-like movement, fire/flies/gore VFX, and CUI wave/stage banners.
namespace Oxide.Plugins
{
    [Info("NecroZombies", "belisario-afk", "3.2.0")]
    [Description("Spawns Necro Zombie, Runner, Brute, and Hellhound variants with Black Ops style waves")]
    public class NecroZombies : RustPlugin
    {
        #region Configuration

        private ConfigData _config;

        private class ZombieProfile
        {
            public string ProfileName = "default";

            public float Health = 100f;
            public float Speed = 6.5f;

            public string MeleeShortname = "knife.bone";
            public ulong MeleeSkinId = 3612162757;

            // Primary clothing item
            public string ClothingShortname = "halloween.mummysuit";
            public ulong ClothingSkinId = 0;
            
            // Additional clothing items for complex outfits
            public string HeadwearShortname = "";
            public long HeadwearSkinId = 0;
            
            public string ShirtShortname = "";
            public long ShirtSkinId = 0;
            
            public string PantsShortname = "";
            public long PantsSkinId = 0;

            public string DisplayName = "Necro Zombie";

            public bool AlwaysOnFire = true;
            
            // Prefab to use (scarecrow or zombie)
            public string PrefabType = "scarecrow";  // "scarecrow" or "zombie"
            
            // Brute's explosive ability
            public bool ExplodeOnProximity = false;
            public float ExplosionProximity = 2.5f;  // Distance to trigger explosion
        }

        private class WaveSettings
        {
            // Total zombies per wave - Black Ops style: start small, grow steadily
            public int BaseCount = 6;                 // Wave 1: 6 zombies
            public int CountPerWaveIncrease = 2;      // +2 per wave (Wave 2: 8, Wave 3: 10, etc.)

            // Stat scaling per wave
            public float HealthMultiplierPerWave = 0.1f;
            public float SpeedMultiplierPerWave = 0.03f;  // Slower speed increase

            public int MaxWaves = 0;              // 0 = endless

            public int MaxActiveZombies = 24;     // Lower cap for early waves

            // Wave progression logic - Black Ops style: ALL zombies must die
            public float RequiredKillRatioToAdvance = 1.0f;  // 100% - last zombie must die
            public float WaveStartDelay = 8f;                 // Longer break between waves
            public float WaveTimeoutSeconds = 0f;             // 0 = no timeout, must kill all

            // Drip spawning inside a wave - slower spawning for early waves
            public int GroupSize = 2;                         // Spawn 2 at a time
            public float SpawnIntervalSeconds = 4.0f;         // 4 seconds between spawns
            public float SpawnIntervalMinSeconds = 1.5f;      // Minimum interval
            public float SpawnIntervalPerWaveMultiplier = 0.92f; // Gets faster each wave

            // CUI wave banner settings
            public bool EnableWaveBanner = true;
            public float BannerDuration = 3.0f;       // seconds visible
            public string BannerTitleColor = "1 0.1 0.1 1"; // RGBA (bright red)
            public string BannerSubColor = "1 1 1 0.8";     // RGBA (white)
            public int BannerTitleSize = 32;
            public int BannerSubSize = 18;
        }

        private class ConfigData
        {
            public float ZombieHealth = 100f;
            public float ZombieSpeed = 6.5f;
            public string MeleeShortname = "knife.bone";
            public ulong MeleeSkinId = 3612162757;
            public string ClothingShortname = "halloween.mummysuit";
            public ulong ClothingSkinId = 0;
            public string ZombieName = "Necro Zombie";

            public Dictionary<string, ZombieProfile> Profiles = new Dictionary<string, ZombieProfile>();
            public WaveSettings Waves = new WaveSettings();

            public Dictionary<string, List<Vector3>> SpawnSets = new Dictionary<string, List<Vector3>>();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();

            _config.Profiles["default"] = new ZombieProfile
            {
                ProfileName = "default",
                Health = _config.ZombieHealth,
                Speed = _config.ZombieSpeed,
                MeleeShortname = _config.MeleeShortname,
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = _config.ClothingShortname,
                ClothingSkinId = _config.ClothingSkinId,
                DisplayName = _config.ZombieName,
                AlwaysOnFire = true,
                PrefabType = "scarecrow"
            };

            // Necro Runner - Fast scarecrow with hoodie outfit
            _config.Profiles["runner"] = new ZombieProfile
            {
                ProfileName = "runner",
                Health = 75f,
                Speed = 10f,
                MeleeShortname = "knife.bone",
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = "",  // No primary clothing
                ClothingSkinId = 0,
                HeadwearShortname = "mask.balaclava",
                HeadwearSkinId = 539536877,
                ShirtShortname = "hoodie",
                ShirtSkinId = 10052,
                PantsShortname = "pants",
                PantsSkinId = 1883629284,
                DisplayName = "Necro Runner",
                AlwaysOnFire = false,  // No fire, stealthy look
                PrefabType = "scarecrow"  // Use scarecrow for reliable brain/AI
            };

            // Necro Brute - Explosive head, no melee, wellipets + jumpsuit + mummy
            _config.Profiles["brute"] = new ZombieProfile
            {
                ProfileName = "brute",
                Health = 300f,
                Speed = 4f,
                MeleeShortname = "",  // No melee weapon
                MeleeSkinId = 0,
                ClothingShortname = "halloween.mummysuit",
                ClothingSkinId = 0,
                HeadwearShortname = "hat.wolf",  // Wellipets hat
                HeadwearSkinId = (long)-507248640,
                ShirtShortname = "jumpsuit.suit",
                ShirtSkinId = (long)-97459906,
                PantsShortname = "",
                PantsSkinId = 0,
                DisplayName = "Necro Brute",
                AlwaysOnFire = true,
                PrefabType = "scarecrow",
                ExplodeOnProximity = true,
                ExplosionProximity = 2.5f
            };

            _config.Profiles["burner"] = new ZombieProfile
            {
                ProfileName = "burner",
                Health = 120f,
                Speed = 7.5f,
                MeleeShortname = "knife.bone",
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = "halloween.mummysuit",
                ClothingSkinId = 0,
                DisplayName = "Necro Burner",
                AlwaysOnFire = true,
                PrefabType = "scarecrow"
            };

            _config.Profiles["stalker"] = new ZombieProfile
            {
                ProfileName = "stalker",
                Health = 125f,
                Speed = 8.0f,
                MeleeShortname = "knife.bone",
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = "halloween.mummysuit",
                ClothingSkinId = 0,
                DisplayName = "Necro Stalker",
                AlwaysOnFire = true,
                PrefabType = "scarecrow"
            };

            // Hellhound profile (used for wolves)
            _config.Profiles["hellhound"] = new ZombieProfile
            {
                ProfileName = "hellhound",
                Health = 150f,
                Speed = 9.0f,
                MeleeShortname = "",
                MeleeSkinId = 0,
                ClothingShortname = "",
                ClothingSkinId = 0,
                DisplayName = "Hellhound",
                AlwaysOnFire = true
            };

            _config.SpawnSets = new Dictionary<string, List<Vector3>>();

            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<ConfigData>();
                if (_config == null)
                {
                    PrintWarning("Config was null, creating new default config.");
                    LoadDefaultConfig();
                }
                else
                {
                    if (_config.Profiles == null)
                        _config.Profiles = new Dictionary<string, ZombieProfile>();

                    if (!_config.Profiles.ContainsKey("default"))
                    {
                        _config.Profiles["default"] = new ZombieProfile
                        {
                            ProfileName = "default",
                            Health = _config.ZombieHealth,
                            Speed = _config.ZombieSpeed,
                            MeleeShortname = _config.MeleeShortname,
                            MeleeSkinId = _config.MeleeSkinId,
                            ClothingShortname = _config.ClothingShortname,
                            ClothingSkinId = _config.ClothingSkinId,
                            DisplayName = _config.ZombieName,
                            AlwaysOnFire = true
                        };
                    }

                    foreach (var kvp in _config.Profiles)
                    {
                        var p = kvp.Value;
                        if (string.IsNullOrEmpty(p.ProfileName)) p.ProfileName = kvp.Key;
                        if (string.IsNullOrEmpty(p.MeleeShortname)) p.MeleeShortname = _config.MeleeShortname;
                        if (string.IsNullOrEmpty(p.ClothingShortname)) p.ClothingShortname = _config.ClothingShortname;
                        if (string.IsNullOrEmpty(p.DisplayName)) p.DisplayName = _config.ZombieName;
                        if (p.Health <= 0f) p.Health = _config.ZombieHealth;
                        if (p.Speed <= 0f) p.Speed = _config.ZombieSpeed;
                    }

                    if (_config.SpawnSets == null)
                        _config.SpawnSets = new Dictionary<string, List<Vector3>>();
                }
            }
            catch (Exception e)
            {
                PrintWarning($"Config file invalid, creating new default config. Error: {e.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        #endregion

        #region Constants & State

        private const string ScarecrowPrefab = "assets/prefabs/npc/scarecrow/scarecrow_dungeonnoroam.prefab";
        private const string ZombiePrefab = "assets/rust.ai/agents/zombie/zombie.prefab";
        private const string WolfPrefab = "assets/rust.ai/agents/wolf/wolf.prefab";
        
        // Explosive prefab for Brute
        private const string BeancanPrefab = "assets/prefabs/weapons/beancan grenade/grenade.beancan.deployed.prefab";
        private const string ExplosionEffect = "assets/prefabs/weapons/beancan grenade/effects/beancan_grenade_explosion.prefab";

        private const float RaycastMaxDistance = 200f;

        // VFX effects
        private const string BurnEffectPrefab = "assets/bundled/prefabs/fx/fire/fire_v3.prefab";
        private const string BloodSlashEffect = "assets/bundled/prefabs/fx/impacts/slash/blood14slash.prefab";
        // Blood splatter decal for hellhound red appearance
        private const string BloodSplatterDecal = "assets/bundled/prefabs/fx/decals/blood/decal_blood_splatter_01.prefab";
        // Disabled flies effects - they cause server lag when running continuously
        // private const string FliesMediumEffect = "assets/bundled/prefabs/fx/animals/flies/flies_medium.prefab";
        // private const string FliesLoopEffect = "assets/bundled/prefabs/fx/animals/flies/flies_looping.prefab";
        private const string EatCeleryEffect = "assets/bundled/prefabs/fx/gestures/eat_celery.prefab";
        private const string DrinkVomitEffect = "assets/bundled/prefabs/fx/gestures/drink_vomit.prefab";

        // CUI IDs
        private const string WaveBannerPanel = "NecroWaveBanner.Panel";
        private const string WaveBannerTitle = "NecroWaveBanner.Title";
        private const string WaveBannerSubtitle = "NecroWaveBanner.Subtitle";
        private const string WaveHudPanel = "NecroWaveHud.Panel";
        private const string WaveHudText = "NecroWaveHud.Text";

        private readonly HashSet<BaseEntity> _activeZombies = new HashSet<BaseEntity>();
        private readonly HashSet<BaseEntity> _hellhoundsOnFire = new HashSet<BaseEntity>();

        // Wave state
        private bool _waveModeActive;
        private int _currentWave;
        private Vector3 _fallbackCenter;
        private string _waveProfileName = "default";
        private string _waveSpawnSetName = null;

        private readonly HashSet<BaseEntity> _currentWaveZombies = new HashSet<BaseEntity>();
        private int _currentWaveTotalToSpawn;
        private int _currentWaveSpawned;
        private int _currentWaveInitialCount;
        private float _currentWaveStartTime;
        private float _currentWaveSpawnInterval;

        private Timer _waveSpawnTimer;
        private Timer _waveCheckTimer;
        private Timer _hopTimer;
        private Timer _hellhoundTimer;
        private Timer _waveHudTimer;
        private Timer _bruteTimer;
        private Timer _zombieTargetTimer;  // Keep zombies focused on players

        private bool _loggedTypeOnce;

        #endregion

        #region Hooks

        private void Init()
        {
            Puts($"[NecroZombies] Using scarecrow prefab: {ScarecrowPrefab}");
            Puts($"[NecroZombies] Using zombie prefab: {ZombiePrefab}");
            _hopTimer = timer.Every(1f, HopTick);
            _hellhoundTimer = timer.Every(0.5f, HellhoundTick);
            _bruteTimer = timer.Every(0.3f, BruteTick);  // Check brute proximity every 0.3s
            _zombieTargetTimer = timer.Every(0.5f, ZombieTargetTick);  // Keep zombies focused on players - run frequently like hellhounds
        }

        private void Unload()
        {
            _hopTimer?.Destroy();
            _hopTimer = null;
            
            _hellhoundTimer?.Destroy();
            _hellhoundTimer = null;
            
            _bruteTimer?.Destroy();
            _bruteTimer = null;
            
            _zombieTargetTimer?.Destroy();
            _zombieTargetTimer = null;
            
            _waveHudTimer?.Destroy();
            _waveHudTimer = null;

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = null;

            _waveCheckTimer?.Destroy();
            _waveCheckTimer = null;

            DestroyWaveBannerForAll();
            DestroyWaveHudForAll();
            KillAllZombiesInternal();
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var be = entity as BaseEntity;
            if (be == null)
                return;

            if (_activeZombies.Contains(be))
                _activeZombies.Remove(be);

            if (_currentWaveZombies.Contains(be))
                _currentWaveZombies.Remove(be);
            
            if (_hellhoundsOnFire.Contains(be))
                _hellhoundsOnFire.Remove(be);
                
            if (_explosiveBrutes.Contains(be))
                _explosiveBrutes.Remove(be);
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return;

            var victim = entity as BasePlayer;
            if (victim == null)
                return;

            var attackerEntity = info.Initiator as BaseEntity;
            if (attackerEntity == null || !_activeZombies.Contains(attackerEntity))
                return;

            if (!string.IsNullOrEmpty(BloodSlashEffect))
            {
                Vector3 hitPos = info.HitPositionWorld;
                if (hitPos == Vector3.zero)
                    hitPos = victim.transform.position + Vector3.up * 1.2f;

                Effect.server.Run(BloodSlashEffect, hitPos, Vector3.up, null);
            }
        }

        #endregion

        #region Chat Commands

        [ChatCommand("necro")]
        private void CmdNecro(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#ff5555>You must be an admin to use this command.</color>");
                return;
            }

            int amount = 1;
            string profile = "default";

            if (args.Length >= 1)
            {
                if (!int.TryParse(args[0], out amount) || amount < 1)
                {
                    SendReply(player, "<color=#ff5555>Usage:</color> /necro [amount] [profile]");
                    return;
                }
            }

            if (args.Length >= 2)
                profile = args[1];

            if (!TryGetLookPoint(player, out var hitPos))
            {
                SendReply(player, "<color=#ff5555>Could not find a valid spawn point in your line of sight.</color>");
                return;
            }

            int spawned = SpawnHordeInternal(hitPos, amount, profile, trackForWave: false);
            SendReply(player, $"<color=#55ff55>Spawned {spawned} Necro zombie(s) using profile '{profile}'.</color>");
        }

        [ChatCommand("necro_killall")]
        private void CmdNecroKillAll(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#ff5555>You must be an admin to use this command.</color>");
                return;
            }

            int killed = KillAllZombiesInternal();
            SendReply(player, $"<color=#ff5555>Killed {killed} active Necro zombie(s).</color>");
        }

        // /necro_wave start <profile> [spawnSetName]
        [ChatCommand("necro_wave")]
        private void CmdNecroWave(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#ff5555>You must be an admin to use this command.</color>");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, "<color=#ffcc55>Usage:</color> /necro_wave start <profile> [spawnSetName] OR /necro_wave stop");
                return;
            }

            string action = args[0].ToLower();

            if (action == "stop")
            {
                StopWaveModeInternal();
                SendReply(player, "<color=#ff5555>Stopped Necro wave mode.</color>");
                return;
            }

            if (action == "start")
            {
                string profile = "default";
                string setName = null;

                if (args.Length >= 2)
                    profile = args[1];

                if (args.Length >= 3)
                    setName = args[2].ToLower();

                if (!TryGetLookPoint(player, out var center))
                {
                    SendReply(player, "<color=#ff5555>Could not find a valid center point from your view.</color>");
                    return;
                }

                if (StartWaveModeInternal(center, profile, setName))
                {
                    string setMsg = string.IsNullOrEmpty(setName) ? "no spawn set (single center)" : $"spawn set '{setName}'";
                    SendReply(player, $"<color=#55ff55>Started Necro drip-wave mode with profile '{profile}', {setMsg}.</color>");
                }
                else
                {
                    SendReply(player, "<color=#ff5555>Failed to start wave mode. Check server console for details.</color>");
                }

                return;
            }

            SendReply(player, "<color=#ffcc55>Usage:</color> /necro_wave start <profile> [spawnSetName] OR /necro_wave stop");
        }

        // /zspawnadd <setName>
        [ChatCommand("zspawnadd")]
        private void CmdZSpawnAdd(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#ff5555>You must be an admin to use this command.</color>");
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, "<color=#ffcc55>Usage:</color> /zspawnadd <setName>");
                return;
            }

            string setName = args[0].ToLower();

            if (!TryGetLookPoint(player, out var pos))
            {
                SendReply(player, "<color=#ff5555>Could not find a valid spawn point from your view.</color>");
                return;
            }

            if (!_config.SpawnSets.ContainsKey(setName))
                _config.SpawnSets[setName] = new List<Vector3>();

            _config.SpawnSets[setName].Add(pos);
            SaveConfig();

            SendReply(player, $"<color=#55ff55>Added spawn point to set '{setName}' at {pos}.</color>");
        }

        // /zspawndebug
        [ChatCommand("zspawndebug")]
        private void CmdZSpawnDebug(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#ff5555>You must be an admin to use this command.</color>");
                return;
            }

            if (_config.SpawnSets == null || _config.SpawnSets.Count == 0)
            {
                SendReply(player, "<color=#ffcc55>No spawn sets configured. Use /zspawnadd <setname> to add spawn points.</color>");
                return;
            }

            SendReply(player, "<color=#00ffff>===== Spawn Sets =====</color>");
            foreach (var kvp in _config.SpawnSets)
            {
                SendReply(player, $"<color=#55ff55>Set '{kvp.Key}':</color> {kvp.Value.Count} point(s)");
                Puts($"[NecroZombies] Set '{kvp.Key}': {kvp.Value.Count} point(s)");
                int idx = 0;
                foreach (var p in kvp.Value)
                {
                    SendReply(player, $"  <color=#aaaaaa>[{idx}]</color> {p.x:F1}, {p.y:F1}, {p.z:F1}");
                    Puts($"    [{idx++}] {p}");
                }
            }
            
            // Show current wave mode status
            if (_waveModeActive)
            {
                SendReply(player, $"<color=#ffff00>Wave mode active:</color> Using spawn set '{_waveSpawnSetName ?? "none (fallback center)"}'");
            }
        }

        #endregion

        #region Core Spawn / Hop Logic

        private bool TryGetLookPoint(BasePlayer player, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            if (player == null || !player.IsValid())
                return false;

            Ray ray = player.eyes.HeadRay();
            RaycastHit hit;

            int mask = Layers.Mask.World | Layers.Mask.Terrain | Layers.Mask.Deployed | Layers.Mask.Default;
            if (Physics.Raycast(ray, out hit, RaycastMaxDistance, mask))
            {
                hitPoint = hit.point;
                return true;
            }

            return false;
        }

        private ZombieProfile GetProfile(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
                profileName = "default";

            ZombieProfile profile;
            if (_config.Profiles != null && _config.Profiles.TryGetValue(profileName, out profile))
                return profile;

            if (_config.Profiles != null && _config.Profiles.TryGetValue("default", out profile))
                return profile;

            return new ZombieProfile
            {
                ProfileName = profileName,
                Health = _config.ZombieHealth,
                Speed = _config.ZombieSpeed,
                MeleeShortname = _config.MeleeShortname,
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = _config.ClothingShortname,
                ClothingSkinId = _config.ClothingSkinId,
                DisplayName = _config.ZombieName,
                AlwaysOnFire = true
            };
        }
        
        /// <summary>
        /// Find the nearest player to a given position within maxRange
        /// </summary>
        private BasePlayer FindNearestPlayer(Vector3 position, float maxRange = 100f)
        {
            BasePlayer nearestPlayer = null;
            float nearestDist = maxRange;
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.IsDead() || player.IsSleeping())
                    continue;
                    
                float dist = Vector3.Distance(player.transform.position, position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestPlayer = player;
                }
            }
            
            return nearestPlayer;
        }
        
        // Track brutes for proximity explosion check
        private HashSet<BaseEntity> _explosiveBrutes = new HashSet<BaseEntity>();

        private bool SpawnNecroZombie(Vector3 position, ZombieProfile profile, bool trackForWave)
        {
            var waves = _config.Waves;

            if (_activeZombies.Count >= waves.MaxActiveZombies)
                return false;

            // Choose prefab based on profile type
            string prefabPath = ScarecrowPrefab;
            if (profile.PrefabType == "zombie")
                prefabPath = ZombiePrefab;

            BaseEntity entity = GameManager.server.CreateEntity(prefabPath, position, Quaternion.identity, true);
            if (entity == null)
            {
                PrintWarning($"[NecroZombies] Failed to create entity. Prefab path: {prefabPath}");
                return false;
            }

            entity.Spawn();
            _activeZombies.Add(entity);

            if (trackForWave)
                _currentWaveZombies.Add(entity);
                
            // Track brutes for explosion proximity check
            if (profile.ExplodeOnProximity)
                _explosiveBrutes.Add(entity);

            if (!_loggedTypeOnce)
            {
                _loggedTypeOnce = true;
                Puts($"[NecroZombies] Spawned entity type: {entity.GetType().FullName} ({profile.ProfileName})");
            }

            if (profile.AlwaysOnFire)
            {
                entity.SetFlag(BaseEntity.Flags.OnFire, true);
                entity.SendNetworkUpdate();
            }

            var npc = entity as NPCPlayer;
            if (npc != null)
            {
                npc.startHealth = profile.Health;
                npc.health = profile.Health;
                npc.InitializeHealth(profile.Health, profile.Health);

                ConfigureNpcMovement(npc, profile);
                ConfigureNpcLoadout(npc, profile);

                if (!string.IsNullOrEmpty(profile.DisplayName))
                    npc.displayName = profile.DisplayName;
                
                // IMMEDIATE targeting - find nearest player and chase
                var nearestPlayer = FindNearestPlayer(position, 500f);
                if (nearestPlayer != null && npc.NavAgent != null)
                {
                    // Set destination to player
                    if (npc.NavAgent.isOnNavMesh)
                    {
                        npc.NavAgent.SetDestination(nearestPlayer.transform.position);
                        npc.NavAgent.isStopped = false;
                    }
                    else
                    {
                        // Try to warp to navmesh first
                        UnityEngine.AI.NavMeshHit hit;
                        if (UnityEngine.AI.NavMesh.SamplePosition(position, out hit, 10f, -1))
                        {
                            npc.NavAgent.Warp(hit.position);
                            // Only set destination if warp succeeded and we're on navmesh
                            if (npc.NavAgent.isOnNavMesh)
                            {
                                npc.NavAgent.SetDestination(nearestPlayer.transform.position);
                                npc.NavAgent.isStopped = false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        private bool SpawnHellhound(Vector3 position, ZombieProfile profile, bool trackForWave)
        {
            var waves = _config.Waves;

            if (_activeZombies.Count >= waves.MaxActiveZombies)
                return false;

            BaseEntity entity = GameManager.server.CreateEntity(WolfPrefab, position, Quaternion.identity, true);
            if (entity == null)
            {
                PrintWarning($"[NecroZombies] Failed to create hellhound entity. Prefab path: {WolfPrefab}");
                return false;
            }

            entity.Spawn();
            _activeZombies.Add(entity);

            if (trackForWave)
                _currentWaveZombies.Add(entity);

            if (!_loggedTypeOnce)
            {
                _loggedTypeOnce = true;
                Puts($"[NecroZombies] Spawned entity type: {entity.GetType().FullName} (hellhound)");
            }

            // Configure wolf as hostile hellhound - NEVER run away, always chase players
            var wolf = entity as BaseNpc;
            if (wolf != null)
            {
                // Set health
                wolf.startHealth = profile.Health;
                wolf.health = profile.Health;
                wolf.InitializeHealth(profile.Health, profile.Health);
                
                // Make wolf aggressive - NEVER afraid, ALWAYS attack
                wolf.SetFact(BaseNpc.Facts.IsAggro, 1);
                wolf.SetFact(BaseNpc.Facts.HasEnemy, 1);
                wolf.SetFact(BaseNpc.Facts.IsAfraid, 0);
                
                // Find nearest player anywhere on map and set as target
                var nearestPlayer = FindNearestPlayer(position, 500f);
                if (nearestPlayer != null)
                {
                    wolf.AttackTarget = nearestPlayer;
                    wolf.SetFact(BaseNpc.Facts.HasEnemy, 1);
                }
                
                // Maximum aggression range - wolves will chase across the map
                wolf.Stats.VisionRange = 200f;
                wolf.Stats.AggressionRange = 200f;
                wolf.Stats.DeaggroRange = 500f;  // Never deaggro
            }

            // Store reference for continuous blood effect and aggression maintenance
            _hellhoundsOnFire.Add(entity);

            // Apply blood splatter decal at multiple heights to cover the whole wolf in red
            if (!string.IsNullOrEmpty(BloodSplatterDecal))
            {
                // Low (legs/ground level)
                Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.1f, Vector3.up, null);
                // Mid (body)
                Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.4f, Vector3.up, null);
                // Upper body
                Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.7f, Vector3.up, null);
                // Head
                Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.9f, Vector3.up, null);
            }

            return true;
        }

        private void ConfigureNpcMovement(NPCPlayer npc, ZombieProfile profile)
        {
            if (npc == null)
                return;

            if (npc.NavAgent != null)
            {
                npc.NavAgent.speed = profile.Speed;
                npc.NavAgent.acceleration = Math.Max(npc.NavAgent.acceleration, profile.Speed * 3f);
                npc.NavAgent.stoppingDistance = 0.5f;
            }
        }

        private void ConfigureNpcLoadout(NPCPlayer npc, ZombieProfile profile)
        {
            if (npc == null || npc.inventory == null)
                return;

            try
            {
                npc.inventory.Strip();
            }
            catch (Exception e)
            {
                PrintWarning($"[NecroZombies] Error stripping NPC inventory: {e}");
            }

            // Add melee weapon (if configured)
            if (!string.IsNullOrEmpty(profile.MeleeShortname))
            {
                Item melee = ItemManager.CreateByName(profile.MeleeShortname, 1, profile.MeleeSkinId);
                if (melee != null)
                {
                    melee.condition = melee.maxCondition;
                    melee.MoveToContainer(npc.inventory.containerBelt);
                    npc.UpdateActiveItem(melee.uid);
                }
                else
                {
                    PrintWarning($"[NecroZombies] Failed to create melee item: '{profile.MeleeShortname}'");
                }
            }

            // Add primary clothing (mummy suit, etc.)
            if (!string.IsNullOrEmpty(profile.ClothingShortname))
            {
                Item clothing = ItemManager.CreateByName(profile.ClothingShortname, 1, profile.ClothingSkinId);
                if (clothing != null)
                {
                    clothing.condition = clothing.maxCondition;
                    clothing.MoveToContainer(npc.inventory.containerWear);
                }
                else
                {
                    PrintWarning($"[NecroZombies] Failed to create clothing item: '{profile.ClothingShortname}'");
                }
            }
            
            // Add headwear (balaclava, wellipets hat, etc.)
            if (!string.IsNullOrEmpty(profile.HeadwearShortname))
            {
                ulong skinId = profile.HeadwearSkinId >= 0 ? (ulong)profile.HeadwearSkinId : (ulong)(-profile.HeadwearSkinId);
                Item headwear = ItemManager.CreateByName(profile.HeadwearShortname, 1, skinId);
                if (headwear != null)
                {
                    headwear.condition = headwear.maxCondition;
                    headwear.MoveToContainer(npc.inventory.containerWear);
                }
            }
            
            // Add shirt (hoodie, jumpsuit, etc.)
            if (!string.IsNullOrEmpty(profile.ShirtShortname))
            {
                ulong skinId = profile.ShirtSkinId >= 0 ? (ulong)profile.ShirtSkinId : (ulong)(-profile.ShirtSkinId);
                Item shirt = ItemManager.CreateByName(profile.ShirtShortname, 1, skinId);
                if (shirt != null)
                {
                    shirt.condition = shirt.maxCondition;
                    shirt.MoveToContainer(npc.inventory.containerWear);
                }
            }
            
            // Add pants
            if (!string.IsNullOrEmpty(profile.PantsShortname))
            {
                ulong skinId = profile.PantsSkinId >= 0 ? (ulong)profile.PantsSkinId : (ulong)(-profile.PantsSkinId);
                Item pants = ItemManager.CreateByName(profile.PantsShortname, 1, skinId);
                if (pants != null)
                {
                    pants.condition = pants.maxCondition;
                    pants.MoveToContainer(npc.inventory.containerWear);
                }
            }

            npc.inventory.ServerUpdate(0f);
        }

        private int SpawnHordeInternal(Vector3 center, int amount, string profileName, bool trackForWave)
        {
            var profile = GetProfile(profileName);
            int spawned = 0;

            for (int i = 0; i < amount; i++)
            {
                if (_activeZombies.Count >= _config.Waves.MaxActiveZombies)
                    break;

                Vector2 circle = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(2f, 3f);
                Vector3 spawnPos = center + new Vector3(circle.x, 0f, circle.y);

                if (SpawnNecroZombie(spawnPos, profile, trackForWave))
                    spawned++;
            }

            return spawned;
        }

        private int KillAllZombiesInternal()
        {
            int count = 0;
            var snapshot = new List<BaseEntity>(_activeZombies);
            foreach (var be in snapshot)
            {
                if (be != null && !be.IsDestroyed)
                {
                    be.Kill();
                    count++;
                }
            }

            _activeZombies.Clear();
            _currentWaveZombies.Clear();
            _hellhoundsOnFire.Clear();
            _currentWaveSpawned = 0;
            _currentWaveTotalToSpawn = 0;
            return count;
        }

        private void HopTick()
        {
            if (_activeZombies.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;

            const float hopCooldown = 4f;
            const float maxLungeDistance = 8f;
            const float hopHeight = 0.6f;
            const float playerSearchRange = 30f;
            const float minFlatDistance = 4f;

            var snapshot = new List<BaseEntity>(_activeZombies);
            foreach (var be in snapshot)
            {
                if (be == null || be.IsDestroyed)
                    continue;

                var npc = be as NPCPlayer;
                if (npc == null)
                    continue;

                Vector3 npcPos = npc.transform.position;

                BasePlayer target = null;
                float bestDist = float.MaxValue;

                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null || player.IsDead() || player.IsSleeping())
                        continue;

                    float dist = Vector3.Distance(player.transform.position, npcPos);
                    if (dist < bestDist && dist <= playerSearchRange)
                    {
                        bestDist = dist;
                        target = player;
                    }
                }

                if (target == null)
                    continue;

                if (bestDist < minFlatDistance)
                {
                    if (!string.IsNullOrEmpty(EatCeleryEffect) && UnityEngine.Random.Range(0f, 1f) < 0.2f)
                    {
                        Effect.server.Run(EatCeleryEffect, npcPos + Vector3.up * 1.4f, Vector3.up, null);
                    }

                    if (!string.IsNullOrEmpty(DrinkVomitEffect) && UnityEngine.Random.Range(0f, 1f) < 0.05f)
                    {
                        Effect.server.Run(DrinkVomitEffect, npcPos + Vector3.up * 1.4f, Vector3.up, null);
                    }

                    continue;
                }

                uint uid = npc.net != null ? (uint)(npc.net.ID.Value & 0xFFFFFFFF) : 0u;
                int bucket = (int)(now + uid) % (int)hopCooldown;
                if (bucket != 0)
                    continue;

                Vector3 to = target.transform.position;
                Vector3 dir = (to - npcPos);
                dir.y = 0f;
                float distFlat = dir.magnitude;
                if (distFlat < 0.1f)
                    continue;

                dir /= distFlat;

                float lungeDist = Mathf.Min(maxLungeDistance, distFlat * 0.75f);
                Vector3 candidate = npcPos + dir * lungeDist + Vector3.up * hopHeight;

                RaycastHit hit;
                if (Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out hit, 10f,
                    Layers.Mask.World | Layers.Mask.Terrain))
                {
                    candidate = hit.point + Vector3.up * 0.1f;
                }

                npc.MovePosition(candidate);
                npc.TransformChanged();
                npc.SendNetworkUpdateImmediate();
            }
        }
        
        private void HellhoundTick()
        {
            if (_hellhoundsOnFire.Count == 0)
                return;
            
            var toRemove = new List<BaseEntity>();
            
            foreach (var entity in _hellhoundsOnFire)
            {
                if (entity == null || entity.IsDestroyed)
                {
                    toRemove.Add(entity);
                    continue;
                }
                
                // Apply blood splatter at multiple heights for full red coverage
                if (!string.IsNullOrEmpty(BloodSplatterDecal))
                {
                    Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.1f, Vector3.up, null);
                    Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.4f, Vector3.up, null);
                    Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.7f, Vector3.up, null);
                    Effect.server.Run(BloodSplatterDecal, entity.transform.position + Vector3.up * 0.9f, Vector3.up, null);
                }
                
                // Keep wolf aggressive toward nearest player - search entire map
                var wolf = entity as BaseNpc;
                if (wolf != null)
                {
                    // NEVER let wolf run away or lose aggression
                    wolf.SetFact(BaseNpc.Facts.IsAggro, 1);
                    wolf.SetFact(BaseNpc.Facts.HasEnemy, 1);
                    wolf.SetFact(BaseNpc.Facts.IsAfraid, 0);
                    
                    // Keep maximum aggression stats
                    wolf.Stats.VisionRange = 200f;
                    wolf.Stats.AggressionRange = 200f;
                    wolf.Stats.DeaggroRange = 500f;
                    
                    // Find ANY player on the map
                    var nearestPlayer = FindNearestPlayer(wolf.transform.position, 500f);
                    if (nearestPlayer != null)
                    {
                        wolf.AttackTarget = nearestPlayer;
                        wolf.SetFact(BaseNpc.Facts.HasEnemy, 1);
                        
                        // Force wolf to chase player by setting destination
                        if (wolf.NavAgent != null)
                        {
                            if (wolf.NavAgent.isOnNavMesh)
                            {
                                wolf.NavAgent.SetDestination(nearestPlayer.transform.position);
                            }
                            else
                            {
                                // Try to warp to navmesh if not on it (custom maps issue)
                                UnityEngine.AI.NavMeshHit hit;
                                if (UnityEngine.AI.NavMesh.SamplePosition(wolf.transform.position, out hit, 10f, -1))
                                {
                                    wolf.NavAgent.Warp(hit.position);
                                    // Only set destination if warp succeeded
                                    if (wolf.NavAgent.isOnNavMesh)
                                    {
                                        wolf.NavAgent.SetDestination(nearestPlayer.transform.position);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            foreach (var entity in toRemove)
            {
                _hellhoundsOnFire.Remove(entity);
            }
        }
        
        /// <summary>
        /// Keep ALL zombies (scarecrows) focused on nearest player - never lose target, never freeze
        /// </summary>
        private void ZombieTargetTick()
        {
            if (_activeZombies.Count == 0)
                return;
            
            var toRemove = new List<BaseEntity>();
            
            foreach (var entity in _activeZombies)
            {
                if (entity == null || entity.IsDestroyed)
                {
                    toRemove.Add(entity);
                    continue;
                }
                
                // Skip hellhounds - they have their own tick
                if (_hellhoundsOnFire.Contains(entity))
                    continue;
                
                // Find nearest player on the map - search 500m (whole map)
                var nearestPlayer = FindNearestPlayer(entity.transform.position, 500f);
                if (nearestPlayer == null)
                    continue;
                
                // Handle as NPCPlayer (scarecrow zombies)
                var npc = entity as NPCPlayer;
                if (npc != null)
                {
                    // Set last attacker - this tells the AI who damaged us and triggers aggression
                    npc.lastAttacker = nearestPlayer;
                    npc.lastDealtDamageTime = Time.time;
                    
                    // Keep NavAgent moving toward player - NEVER stop
                    if (npc.NavAgent != null)
                    {
                        if (npc.NavAgent.isOnNavMesh)
                        {
                            npc.NavAgent.SetDestination(nearestPlayer.transform.position);
                            npc.NavAgent.isStopped = false;
                            npc.NavAgent.speed = 6.5f;  // Ensure speed is set
                        }
                        else
                        {
                            // Try to warp to navmesh if not on it
                            UnityEngine.AI.NavMeshHit hit;
                            if (UnityEngine.AI.NavMesh.SamplePosition(npc.transform.position, out hit, 10f, -1))
                            {
                                npc.NavAgent.Warp(hit.position);
                                // Only set destination if warp succeeded
                                if (npc.NavAgent.isOnNavMesh)
                                {
                                    npc.NavAgent.SetDestination(nearestPlayer.transform.position);
                                    npc.NavAgent.isStopped = false;
                                }
                            }
                        }
                    }
                    
                    continue;
                }
                
                // Handle as BaseNpc (wolves or other animals)
                var baseNpc = entity as BaseNpc;
                if (baseNpc != null)
                {
                    // Force aggression facts
                    baseNpc.SetFact(BaseNpc.Facts.IsAggro, 1);
                    baseNpc.SetFact(BaseNpc.Facts.HasEnemy, 1);
                    baseNpc.SetFact(BaseNpc.Facts.IsAfraid, 0);
                    
                    baseNpc.AttackTarget = nearestPlayer;
                    
                    if (baseNpc.NavAgent != null && baseNpc.NavAgent.isOnNavMesh)
                    {
                        baseNpc.NavAgent.SetDestination(nearestPlayer.transform.position);
                    }
                }
            }
            
            foreach (var entity in toRemove)
            {
                _activeZombies.Remove(entity);
            }
        }
        
        // Check brutes for proximity to players - explode if too close
        private void BruteTick()
        {
            if (_explosiveBrutes.Count == 0)
                return;
            
            var toRemove = new List<BaseEntity>();
            var toExplode = new List<BaseEntity>();
            
            foreach (var entity in _explosiveBrutes)
            {
                if (entity == null || entity.IsDestroyed)
                {
                    toRemove.Add(entity);
                    continue;
                }
                
                Vector3 brutePos = entity.transform.position;
                
                // Check if any player is within explosion proximity
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null || player.IsDead() || player.IsSleeping())
                        continue;
                    
                    float dist = Vector3.Distance(player.transform.position, brutePos);
                    if (dist <= 2.5f)  // Explosion proximity
                    {
                        toExplode.Add(entity);
                        break;
                    }
                }
            }
            
            // Remove dead/destroyed brutes
            foreach (var entity in toRemove)
            {
                _explosiveBrutes.Remove(entity);
            }
            
            // Explode brutes that got close to players
            foreach (var entity in toExplode)
            {
                if (entity == null || entity.IsDestroyed)
                    continue;
                    
                Vector3 explosionPos = entity.transform.position + Vector3.up * 1.5f;  // Head height
                
                // Remove from tracking first to prevent duplicate explosions
                _explosiveBrutes.Remove(entity);
                
                // BOOM! Spawn beancan grenade at brute's head position
                BaseEntity grenade = GameManager.server.CreateEntity(BeancanPrefab, explosionPos, Quaternion.identity, true);
                if (grenade != null)
                {
                    grenade.Spawn();
                    
                    // Make it explode immediately
                    var timedExplosive = grenade as TimedExplosive;
                    if (timedExplosive != null)
                    {
                        timedExplosive.SetFuse(0.05f);  // Explode even faster
                    }
                    else
                    {
                        // Fallback: manually trigger explosion effect
                        Effect.server.Run(ExplosionEffect, explosionPos);
                        
                        // Damage nearby players directly
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            if (player == null || player.IsDead())
                                continue;
                            float dist = Vector3.Distance(player.transform.position, explosionPos);
                            if (dist <= 5f)
                            {
                                float damage = (5f - dist) * 30f;  // Closer = more damage
                                player.Hurt(damage, Rust.DamageType.Explosion, entity);
                            }
                        }
                    }
                }
                
                // Kill the brute
                entity.Kill();
            }
        }

        #endregion

        #region Wave System + Hellhounds + CUI

        private bool IsHellhoundWave(int waveNumber)
        {
            // Every 3rd wave is a hellhound wave (mixed wolves + zombies)
            return waveNumber > 0 && (waveNumber % 3 == 0);
        }

        private bool StartWaveModeInternal(Vector3 center, string profileName, string spawnSetName)
        {
            if (_waveModeActive)
            {
                PrintWarning("[NecroZombies] Wave mode already active.");
                return false;
            }

            _waveModeActive = true;
            _currentWave = 0;
            _fallbackCenter = center;
            _waveProfileName = profileName;
            _waveSpawnSetName = string.IsNullOrEmpty(spawnSetName) ? null : spawnSetName.ToLower();

            Puts($"[NecroZombies] Starting wave mode - profile: '{profileName}', spawnSet: '{spawnSetName ?? "null"}' -> '{_waveSpawnSetName ?? "null"}'");
            
            // Debug: list all available spawn sets
            if (_config.SpawnSets != null && _config.SpawnSets.Count > 0)
            {
                Puts($"[NecroZombies] Available spawn sets: {string.Join(", ", _config.SpawnSets.Keys)}");
                
                // Zombies ONLY spawn at /zspawnadd points - find a valid set
                bool foundValidSet = false;
                
                // First try the specified spawn set
                if (_waveSpawnSetName != null && _config.SpawnSets.ContainsKey(_waveSpawnSetName) && 
                    _config.SpawnSets[_waveSpawnSetName].Count > 0)
                {
                    foundValidSet = true;
                    Puts($"[NecroZombies] Using specified spawn set '{_waveSpawnSetName}' with {_config.SpawnSets[_waveSpawnSetName].Count} point(s).");
                    foreach (var p in _config.SpawnSets[_waveSpawnSetName])
                    {
                        Puts($"[NecroZombies]   Point: ({p.x:F1}, {p.y:F1}, {p.z:F1})");
                    }
                }
                else
                {
                    // Try to find any valid spawn set
                    foreach (var kvp in _config.SpawnSets)
                    {
                        if (kvp.Value != null && kvp.Value.Count > 0)
                        {
                            _waveSpawnSetName = kvp.Key;
                            foundValidSet = true;
                            Puts($"[NecroZombies] Using first available spawn set '{_waveSpawnSetName}' with {kvp.Value.Count} point(s).");
                            foreach (var p in kvp.Value)
                            {
                                Puts($"[NecroZombies]   Point: ({p.x:F1}, {p.y:F1}, {p.z:F1})");
                            }
                            break;
                        }
                    }
                }
                
                if (!foundValidSet)
                {
                    PrintWarning("[NecroZombies] No valid spawn sets found! Use /zspawnadd <setname> to add spawn points. Zombies will not spawn.");
                }
            }
            else
            {
                PrintWarning("[NecroZombies] No spawn sets configured! Use /zspawnadd <setname> to add spawn points. Zombies will not spawn.");
            }

            _currentWaveZombies.Clear();
            _currentWaveSpawned = 0;
            _currentWaveTotalToSpawn = 0;

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = null;
            _waveCheckTimer?.Destroy();
            _waveCheckTimer = null;
            
            // Start persistent wave HUD updates
            _waveHudTimer?.Destroy();
            _waveHudTimer = timer.Every(1f, UpdateWaveHud);

            StartNextWave();
            return true;
        }

        private void StopWaveModeInternal()
        {
            _waveModeActive = false;

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = null;

            _waveCheckTimer?.Destroy();
            _waveCheckTimer = null;
            
            _waveHudTimer?.Destroy();
            _waveHudTimer = null;

            _currentWaveZombies.Clear();
            _currentWaveSpawned = 0;
            _currentWaveTotalToSpawn = 0;

            DestroyWaveBannerForAll();
            DestroyWaveHudForAll();
        }

        private void StartNextWave()
        {
            if (!_waveModeActive)
                return;

            var waves = _config.Waves;

            _currentWave++;

            if (waves.MaxWaves > 0 && _currentWave > waves.MaxWaves)
            {
                Puts("[NecroZombies] Reached max waves, stopping wave mode.");
                StopWaveModeInternal();
                return;
            }

            _currentWaveZombies.Clear();
            _currentWaveSpawned = 0;

            int baseCount = waves.BaseCount;
            int countIncrease = waves.CountPerWaveIncrease;
            _currentWaveTotalToSpawn = Mathf.Max(1, baseCount + (_currentWave - 1) * countIncrease);
            _currentWaveInitialCount = _currentWaveTotalToSpawn;

            // Get dynamic spawn interval based on wave
            var (_, dynamicInterval) = GetWaveSpawnSettings();
            _currentWaveSpawnInterval = dynamicInterval;

            _currentWaveStartTime = Time.realtimeSinceStartup;

            bool hellWave = IsHellhoundWave(_currentWave);
            if (hellWave)
                ShowStageBanner("HELLHOUNDS", "Stay sharp", waves.BannerDuration);
            else
                ShowWaveBanner(_currentWave, _waveProfileName);

            Puts($"[NecroZombies] Wave {_currentWave} starting. Hellhounds: {hellWave}. Will spawn ~{_currentWaveTotalToSpawn} total. Interval: {_currentWaveSpawnInterval}s");

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = timer.Every(_currentWaveSpawnInterval, DripSpawnWave);

            _waveCheckTimer?.Destroy();
            _waveCheckTimer = timer.Every(1f, CheckWaveProgress);
        }

        // Track which spawn point to use next (round-robin)
        private int _spawnPointIndex = 0;
        
        /// <summary>
        /// Returns a spawn position ONLY from configured spawn sets.
        /// Returns Vector3.zero if no valid spawn set is configured.
        /// Zombies ONLY spawn at /zspawnadd points, never near players or fallback positions.
        /// </summary>
        private Vector3 GetRandomSpawnPosition()
        {
            // Find ANY valid spawn set with points
            List<Vector3> spawnPoints = null;
            
            // First try the specified spawn set
            if (_waveSpawnSetName != null &&
                _config.SpawnSets != null &&
                _config.SpawnSets.TryGetValue(_waveSpawnSetName, out var specificList) &&
                specificList != null &&
                specificList.Count > 0)
            {
                spawnPoints = specificList;
            }
            // Otherwise try to find ANY spawn set with points
            else if (_config.SpawnSets != null && _config.SpawnSets.Count > 0)
            {
                foreach (var kvp in _config.SpawnSets)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        spawnPoints = kvp.Value;
                        _waveSpawnSetName = kvp.Key;  // Use this set
                        Puts($"[NecroZombies] Using first available spawn set '{kvp.Key}' with {kvp.Value.Count} point(s)");
                        break;
                    }
                }
            }
            
            // If we found valid spawn points, use them
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                // Round-robin through spawn points instead of random
                // This ensures all spawn points are used evenly
                _spawnPointIndex = (_spawnPointIndex + 1) % spawnPoints.Count;
                var point = spawnPoints[_spawnPointIndex];
                
                // Small random offset around the spawn point (1-2m spread)
                Vector2 circle = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(0.5f, 2f);
                return point + new Vector3(circle.x, 0f, circle.y);
            }
            
            // NO fallback - zombies ONLY spawn at configured spawn points
            // Return Vector3.zero to signal no valid spawn position
            return Vector3.zero;
        }
        
        /// <summary>
        /// Calculate dynamic spawn settings based on current wave
        /// Early waves: spawn 1-2 at a time, slow interval
        /// Later waves: spawn 2-4 at a time, faster interval
        /// </summary>
        private (int groupSize, float interval) GetWaveSpawnSettings()
        {
            int wave = _currentWave;
            
            // Group size: waves 1-3 = 1-2, waves 4-6 = 2, waves 7+ = 2-4
            int groupSize;
            if (wave <= 3)
                groupSize = UnityEngine.Random.Range(1, 3); // 1-2
            else if (wave <= 6)
                groupSize = 2;
            else if (wave <= 10)
                groupSize = UnityEngine.Random.Range(2, 4); // 2-3
            else
                groupSize = UnityEngine.Random.Range(2, 5); // 2-4
            
            // Spawn interval: starts at 5s, decreases each wave, minimum 1.5s
            float baseInterval = 5.0f;
            float intervalReduction = 0.3f * (wave - 1);
            float interval = Mathf.Max(1.5f, baseInterval - intervalReduction);
            
            return (groupSize, interval);
        }
        
        /// <summary>
        /// Get zombie variant for current wave (brutes only after wave 4)
        /// </summary>
        private string GetZombieVariantForWave()
        {
            float roll = UnityEngine.Random.Range(0f, 1f);
            
            if (_currentWave < 4)
            {
                // Waves 1-3: Only default (70%) and runner (30%), NO brutes
                if (roll < 0.70f)
                    return "default";
                else
                    return "runner";
            }
            else
            {
                // Wave 4+: default (60%), runner (25%), brute (15%)
                if (roll < 0.60f)
                    return "default";
                else if (roll < 0.85f)
                    return "runner";
                else
                    return "brute";
            }
        }

        private void DripSpawnWave()
        {
            if (!_waveModeActive)
            {
                _waveSpawnTimer?.Destroy();
                _waveSpawnTimer = null;
                return;
            }

            var waves = _config.Waves;

            if (_currentWaveTotalToSpawn <= 0)
            {
                _waveSpawnTimer?.Destroy();
                _waveSpawnTimer = null;
                return;
            }

            if (_activeZombies.Count >= waves.MaxActiveZombies)
                return;

            bool hellWave = IsHellhoundWave(_currentWave);
            
            // Get dynamic spawn settings based on current wave
            var (groupSize, spawnInterval) = GetWaveSpawnSettings();
            int spawnedThisTick = 0;

            for (int i = 0; i < groupSize; i++)
            {
                if (_currentWaveTotalToSpawn <= 0)
                    break;

                if (_activeZombies.Count >= waves.MaxActiveZombies)
                    break;

                Vector3 spawnPos = GetRandomSpawnPosition();
                
                // Check if we got a valid spawn position from /zspawnadd points
                if (spawnPos == Vector3.zero)
                {
                    PrintWarning("[NecroZombies] No valid spawn points configured! Use /zspawnadd <setname> to add spawn points. Stopping wave.");
                    _waveSpawnTimer?.Destroy();
                    _waveSpawnTimer = null;
                    return;
                }

                bool spawnedOk;

                if (hellWave)
                {
                    // Mixed: 50% chance wolves, 50% scarecrows
                    bool spawnWolf = UnityEngine.Random.Range(0f, 1f) < 0.5f;

                    if (spawnWolf)
                    {
                        var baseHell = GetProfile("hellhound");
                        var hellProfile = new ZombieProfile
                        {
                            ProfileName = baseHell.ProfileName,
                            MeleeShortname = baseHell.MeleeShortname,
                            MeleeSkinId = baseHell.MeleeSkinId,
                            ClothingShortname = baseHell.ClothingShortname,
                            ClothingSkinId = baseHell.ClothingSkinId,
                            DisplayName = baseHell.DisplayName,
                            Health = baseHell.Health * (1f + _config.Waves.HealthMultiplierPerWave * (_currentWave - 1)),
                            Speed = baseHell.Speed * (1f + _config.Waves.SpeedMultiplierPerWave * (_currentWave - 1)),
                            AlwaysOnFire = baseHell.AlwaysOnFire
                        };

                        spawnedOk = SpawnHellhound(spawnPos, hellProfile, trackForWave: true);
                    }
                    else
                    {
                        // Use wave-appropriate variant (no brutes before wave 4)
                        string variantName = GetZombieVariantForWave();
                        
                        var baseProfile = GetProfile(variantName);
                        float healthMultiplier = 1f + _config.Waves.HealthMultiplierPerWave * (_currentWave - 1);
                        
                        var waveProfile = new ZombieProfile
                        {
                            ProfileName = baseProfile.ProfileName,
                            PrefabType = baseProfile.PrefabType,
                            MeleeShortname = baseProfile.MeleeShortname,
                            MeleeSkinId = baseProfile.MeleeSkinId,
                            ClothingShortname = baseProfile.ClothingShortname,
                            ClothingSkinId = baseProfile.ClothingSkinId,
                            HeadwearShortname = baseProfile.HeadwearShortname,
                            HeadwearSkinId = baseProfile.HeadwearSkinId,
                            ShirtShortname = baseProfile.ShirtShortname,
                            ShirtSkinId = baseProfile.ShirtSkinId,
                            PantsShortname = baseProfile.PantsShortname,
                            PantsSkinId = baseProfile.PantsSkinId,
                            DisplayName = baseProfile.DisplayName,
                            Health = baseProfile.Health * healthMultiplier,
                            Speed = baseProfile.Speed * (1f + _config.Waves.SpeedMultiplierPerWave * (_currentWave - 1)),
                            AlwaysOnFire = baseProfile.AlwaysOnFire,
                            ExplodeOnProximity = baseProfile.ExplodeOnProximity,
                            ExplosionProximity = baseProfile.ExplosionProximity
                        };

                        spawnedOk = SpawnNecroZombie(spawnPos, waveProfile, trackForWave: true);
                    }
                }
                else
                {
                    // Use wave-appropriate variant (no brutes before wave 4)
                    string variantName = GetZombieVariantForWave();
                    
                    var baseProfile = GetProfile(variantName);
                    float healthMultiplier = 1f + _config.Waves.HealthMultiplierPerWave * (_currentWave - 1);
                    
                    var waveProfile = new ZombieProfile
                    {
                        ProfileName = baseProfile.ProfileName,
                        PrefabType = baseProfile.PrefabType,
                        MeleeShortname = baseProfile.MeleeShortname,
                        MeleeSkinId = baseProfile.MeleeSkinId,
                        ClothingShortname = baseProfile.ClothingShortname,
                        ClothingSkinId = baseProfile.ClothingSkinId,
                        HeadwearShortname = baseProfile.HeadwearShortname,
                        HeadwearSkinId = baseProfile.HeadwearSkinId,
                        ShirtShortname = baseProfile.ShirtShortname,
                        ShirtSkinId = baseProfile.ShirtSkinId,
                        PantsShortname = baseProfile.PantsShortname,
                        PantsSkinId = baseProfile.PantsSkinId,
                        DisplayName = baseProfile.DisplayName,
                        Health = baseProfile.Health * healthMultiplier,
                        Speed = baseProfile.Speed * (1f + _config.Waves.SpeedMultiplierPerWave * (_currentWave - 1)),
                        AlwaysOnFire = baseProfile.AlwaysOnFire,
                        ExplodeOnProximity = baseProfile.ExplodeOnProximity,
                        ExplosionProximity = baseProfile.ExplosionProximity
                    };

                    spawnedOk = SpawnNecroZombie(spawnPos, waveProfile, trackForWave: true);
                }

                if (spawnedOk)
                {
                    _currentWaveTotalToSpawn--;
                    _currentWaveSpawned++;
                    spawnedThisTick++;
                }
            }

            if (spawnedThisTick > 0)
            {
                Puts($"[NecroZombies] Wave {_currentWave}: drip-spawned {spawnedThisTick}, " +
                     $"{_currentWaveSpawned}/{_currentWaveInitialCount} spawned total, " +
                     $"{_currentWaveTotalToSpawn} remaining.");
            }

            if (_currentWaveTotalToSpawn <= 0)
            {
                _waveSpawnTimer?.Destroy();
                _waveSpawnTimer = null;
            }
        }

        private void CheckWaveProgress()
        {
            if (!_waveModeActive)
            {
                _waveCheckTimer?.Destroy();
                _waveCheckTimer = null;
                return;
            }

            var waves = _config.Waves;

            // Don't check progress until all zombies for this wave have spawned
            if (_currentWaveTotalToSpawn > 0)
                return;
            
            // Use the actual spawned count, not the initial target
            if (_currentWaveSpawned <= 0)
                return;

            int aliveInWave = 0;
            foreach (var be in _currentWaveZombies)
            {
                if (be != null && !be.IsDestroyed)
                    aliveInWave++;
            }

            // Black Ops style: ALL zombies must be dead to advance
            // When RequiredKillRatioToAdvance is 1.0, we check if aliveInWave == 0
            bool waveCleared = false;
            if (waves.RequiredKillRatioToAdvance >= 1.0f)
            {
                // Must kill every single zombie
                waveCleared = (aliveInWave == 0);
            }
            else
            {
                // Legacy ratio-based check
                int deadInWave = _currentWaveSpawned - aliveInWave;
                float killRatio = (float)deadInWave / _currentWaveSpawned;
                waveCleared = (killRatio >= waves.RequiredKillRatioToAdvance);
            }

            if (waveCleared)
            {
                Puts($"[NecroZombies] Wave {_currentWave} cleared! All zombies eliminated. Next wave in {waves.WaveStartDelay:F1}s.");
                _waveCheckTimer?.Destroy();
                _waveCheckTimer = null;

                // Notify other plugins that wave is complete (for respawning spectators)
                Interface.Oxide.CallHook("OnNecroZombiesWaveComplete", _currentWave);

                // Intermission banner between waves
                ShowStageBanner("WAVE COMPLETE", $"Prepare for Wave {_currentWave + 1}", waves.WaveStartDelay);
                timer.Once(waves.WaveStartDelay, StartNextWave);
                return;
            }

            // Only apply timeout if configured (0 = no timeout, Black Ops style)
            if (waves.WaveTimeoutSeconds > 0f)
            {
                float elapsed = Time.realtimeSinceStartup - _currentWaveStartTime;
                if (elapsed >= waves.WaveTimeoutSeconds)
                {
                    Puts($"[NecroZombies] Wave {_currentWave} timed out after {elapsed:F1}s. Forcing next wave in {waves.WaveStartDelay:F1}s.");
                    _waveCheckTimer?.Destroy();
                    _waveCheckTimer = null;

                    ShowStageBanner("INTERMISSION", "Time's up", waves.WaveStartDelay);
                    timer.Once(waves.WaveStartDelay, StartNextWave);
                }
            }
        }

        #endregion

        #region CUI Wave / Stage Banner

        private void ShowWaveBanner(int waveNumber, string profileName)
        {
            var waves = _config.Waves;
            if (!waves.EnableWaveBanner)
                return;

            string titleText = $"WAVE {waveNumber}";
            string subText = string.IsNullOrEmpty(profileName)
                ? ""
                : profileName.ToUpperInvariant();

            string json = BuildWaveBannerJson(titleText, subText);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                DestroyWaveBanner(player);
                CommunityEntity.ServerInstance.ClientRPCEx(
                    new Network.SendInfo { connection = player.net.connection },
                    null,
                    "AddUI",
                    json
                );
            }

            timer.Once(waves.BannerDuration, DestroyWaveBannerForAll);
        }

        private void ShowStageBanner(string title, string subtitle, float durationSeconds)
        {
            var waves = _config.Waves;
            if (!waves.EnableWaveBanner)
                return;

            string json = BuildWaveBannerJson(title, subtitle);

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                DestroyWaveBanner(player);
                CommunityEntity.ServerInstance.ClientRPCEx(
                    new Network.SendInfo { connection = player.net.connection },
                    null,
                    "AddUI",
                    json
                );
            }

            timer.Once(durationSeconds, DestroyWaveBannerForAll);
        }

        private void DestroyWaveBanner(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            CommunityEntity.ServerInstance.ClientRPCEx(
                new Network.SendInfo { connection = player.net.connection },
                null,
                "DestroyUI",
                WaveBannerPanel
            );
        }

        private void DestroyWaveBannerForAll()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyWaveBanner(player);
            }
        }
        
        // Persistent Wave HUD showing current wave and zombies remaining
        private void UpdateWaveHud()
        {
            if (!_waveModeActive)
            {
                DestroyWaveHudForAll();
                return;
            }
            
            int aliveZombies = 0;
            foreach (var be in _currentWaveZombies)
            {
                if (be != null && !be.IsDestroyed)
                    aliveZombies++;
            }
            
            bool isHellhoundWave = IsHellhoundWave(_currentWave);
            string waveType = isHellhoundWave ? "HELLHOUND WAVE" : "WAVE";
            
            // Black Ops style HUD - show zombies remaining
            string statusText;
            if (_currentWaveTotalToSpawn > 0)
            {
                // Still spawning zombies
                statusText = $"Incoming: {_currentWaveTotalToSpawn}";
            }
            else
            {
                // All spawned, show remaining
                statusText = $"Remaining: {aliveZombies}";
            }
            
            string hudText = $"<color=#ff4444>{waveType} {_currentWave}</color>\\n<color=#ffffff>{statusText}</color>";
            
            string json = BuildWaveHudJson(hudText);
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;
                
                DestroyWaveHud(player);
                CommunityEntity.ServerInstance.ClientRPCEx(
                    new Network.SendInfo { connection = player.net.connection },
                    null,
                    "AddUI",
                    json
                );
            }
        }
        
        private void DestroyWaveHud(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;
            
            CommunityEntity.ServerInstance.ClientRPCEx(
                new Network.SendInfo { connection = player.net.connection },
                null,
                "DestroyUI",
                WaveHudPanel
            );
        }
        
        private void DestroyWaveHudForAll()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyWaveHud(player);
            }
        }
        
        private string BuildWaveHudJson(string text)
        {
            // Raw JSON format for Oxide CUI - more reliable than serializing objects
            return $@"[
                {{
                    ""name"": ""{WaveHudPanel}"",
                    ""parent"": ""Overlay"",
                    ""components"": [
                        {{
                            ""type"": ""UnityEngine.UI.Image"",
                            ""color"": ""0 0 0 0.8""
                        }},
                        {{
                            ""type"": ""RectTransform"",
                            ""anchormin"": ""0.85 0.92"",
                            ""anchormax"": ""0.99 0.99""
                        }}
                    ]
                }},
                {{
                    ""name"": ""{WaveHudText}"",
                    ""parent"": ""{WaveHudPanel}"",
                    ""components"": [
                        {{
                            ""type"": ""UnityEngine.UI.Text"",
                            ""text"": ""{text.Replace("\"", "\\\"")}"",
                            ""fontSize"": 14,
                            ""align"": ""MiddleCenter"",
                            ""color"": ""1 1 1 1""
                        }},
                        {{
                            ""type"": ""RectTransform"",
                            ""anchormin"": ""0.05 0.05"",
                            ""anchormax"": ""0.95 0.95""
                        }}
                    ]
                }}
            ]";
        }

        private string BuildWaveBannerJson(string title, string subtitle)
        {
            var waves = _config.Waves;
            
            string subtitleJson = "";
            if (!string.IsNullOrEmpty(subtitle))
            {
                subtitleJson = $@",
                {{
                    ""name"": ""{WaveBannerSubtitle}"",
                    ""parent"": ""{WaveBannerPanel}"",
                    ""components"": [
                        {{
                            ""type"": ""UnityEngine.UI.Text"",
                            ""text"": ""{subtitle.Replace("\"", "\\\"")}"",
                            ""fontSize"": {waves.BannerSubSize},
                            ""align"": ""MiddleCenter"",
                            ""color"": ""{waves.BannerSubColor}""
                        }},
                        {{
                            ""type"": ""RectTransform"",
                            ""anchormin"": ""0 0"",
                            ""anchormax"": ""1 0.5""
                        }}
                    ]
                }}";
            }
            
            // Raw JSON format for Oxide CUI
            return $@"[
                {{
                    ""name"": ""{WaveBannerPanel}"",
                    ""parent"": ""Overlay"",
                    ""components"": [
                        {{
                            ""type"": ""UnityEngine.UI.Image"",
                            ""color"": ""0 0 0 0.7""
                        }},
                        {{
                            ""type"": ""RectTransform"",
                            ""anchormin"": ""0.25 0.85"",
                            ""anchormax"": ""0.75 0.95""
                        }}
                    ]
                }},
                {{
                    ""name"": ""{WaveBannerTitle}"",
                    ""parent"": ""{WaveBannerPanel}"",
                    ""components"": [
                        {{
                            ""type"": ""UnityEngine.UI.Text"",
                            ""text"": ""{title.Replace("\"", "\\\"")}"",
                            ""fontSize"": {waves.BannerTitleSize},
                            ""align"": ""MiddleCenter"",
                            ""color"": ""{waves.BannerTitleColor}""
                        }},
                        {{
                            ""type"": ""RectTransform"",
                            ""anchormin"": ""0 0.4"",
                            ""anchormax"": ""1 1""
                        }}
                    ]
                }}{subtitleJson}
            ]";
        }

        #endregion

        #region Public API

        object NecroZombies_SpawnHordeAt(Vector3 position, int amount, string profileName = "default")
        {
            int spawned = SpawnHordeInternal(position, amount, profileName, trackForWave: false);
            return spawned;
        }

        object NecroZombies_KillAll()
        {
            int killed = KillAllZombiesInternal();
            return killed;
        }

        object NecroZombies_GetActiveCount()
        {
            return _activeZombies.Count;
        }

        object NecroZombies_StartWaveMode(Vector3 center, string profileName = "default", string spawnSetName = null)
        {
            bool ok = StartWaveModeInternal(center, profileName, spawnSetName);
            return ok;
        }

        object NecroZombies_StopWaveMode()
        {
            StopWaveModeInternal();
            return true;
        }

        object NecroZombies_IsWaveModeActive()
        {
            return _waveModeActive;
        }

        // For external plugins: show BO-style game over banner
        object NecroZombies_ShowGameOver(string reason = "")
        {
            ShowStageBanner("GAME OVER", reason, _config.Waves.BannerDuration);
            return true;
        }

        #endregion
    }

    internal static class NecroExtensions
    {
        public static bool IsValid(this BasePlayer player)
        {
            return player != null && !player.IsDestroyed;
        }
    }
}