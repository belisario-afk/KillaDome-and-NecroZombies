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
    [Info("NecroZombies", "belisario-afk", "2.3.0")]
    [Description("Spawns fast, aggressive scarecrow-based zombies and hellhounds with spawn sets, drip waves, CUI, and horror VFX")]
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

            public string ClothingShortname = "halloween.mummysuit";
            public ulong ClothingSkinId = 0;

            public string DisplayName = "Necro Zombie";

            public bool AlwaysOnFire = true;
        }

        private class WaveSettings
        {
            // Total zombies per wave
            public int BaseCount = 12;
            public int CountPerWaveIncrease = 4;

            // Stat scaling per wave
            public float HealthMultiplierPerWave = 0.1f;
            public float SpeedMultiplierPerWave = 0.05f;

            public int MaxWaves = 0;              // 0 = endless

            public int MaxActiveZombies = 40;     // global hard cap

            // Wave progression logic
            public float RequiredKillRatioToAdvance = 0.9f;
            public float WaveStartDelay = 5f;
            public float WaveTimeoutSeconds = 120f;

            // Drip spawning inside a wave
            public int GroupSize = 3;
            public float SpawnIntervalSeconds = 2.5f;
            public float SpawnIntervalMinSeconds = 1.0f;
            public float SpawnIntervalPerWaveMultiplier = 0.9f;

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
                AlwaysOnFire = true
            };

            _config.Profiles["runner"] = new ZombieProfile
            {
                ProfileName = "runner",
                Health = 75f,
                Speed = 10f,
                MeleeShortname = "knife.bone",
                MeleeSkinId = _config.MeleeSkinId,
                ClothingShortname = "halloween.mummysuit",
                ClothingSkinId = 0,
                DisplayName = "Necro Runner",
                AlwaysOnFire = true
            };

            _config.Profiles["brute"] = new ZombieProfile
            {
                ProfileName = "brute",
                Health = 250f,
                Speed = 5f,
                MeleeShortname = "mace.base",
                MeleeSkinId = 0,
                ClothingShortname = "halloween.mummysuit",
                ClothingSkinId = 0,
                DisplayName = "Necro Brute",
                AlwaysOnFire = true
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
                AlwaysOnFire = true
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
                AlwaysOnFire = true
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

        private const string ZombiePrefab = "assets/prefabs/npc/scarecrow/scarecrow_dungeonnoroam.prefab";
        private const string WolfPrefab = "assets/rust.ai/agents/wolf/wolf.prefab";

        private const float RaycastMaxDistance = 200f;

        // Fire & gore VFX
        private const string BurnEffectPrefab = "assets/bundled/prefabs/fx/fire/fire_v3.prefab";
        private const string BloodSlashEffect = "assets/bundled/prefabs/fx/impacts/slash/blood14slash.prefab";
        private const string FliesMediumEffect = "assets/bundled/prefabs/fx/animals/flies/flies_medium.prefab";
        private const string FliesLoopEffect = "assets/bundled/prefabs/fx/animals/flies/flies_looping.prefab";
        private const string EatCeleryEffect = "assets/bundled/prefabs/fx/gestures/eat_celery.prefab";
        private const string DrinkVomitEffect = "assets/bundled/prefabs/fx/gestures/drink_vomit.prefab";

        // CUI IDs
        private const string WaveBannerPanel = "NecroWaveBanner.Panel";
        private const string WaveBannerTitle = "NecroWaveBanner.Title";
        private const string WaveBannerSubtitle = "NecroWaveBanner.Subtitle";

        private readonly HashSet<BaseEntity> _activeZombies = new HashSet<BaseEntity>();

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

        private bool _loggedTypeOnce;

        #endregion

        #region Hooks

        private void Init()
        {
            Puts($"[NecroZombies] Using zombie prefab: {ZombiePrefab}");
            _hopTimer = timer.Every(1f, HopTick);
        }

        private void Unload()
        {
            _hopTimer?.Destroy();
            _hopTimer = null;

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = null;

            _waveCheckTimer?.Destroy();
            _waveCheckTimer = null;

            DestroyWaveBannerForAll();
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
                SendReply(player, "<color=#ffcc55>No spawn sets configured.</color>");
                return;
            }

            Puts("[NecroZombies] Spawn sets:");
            foreach (var kvp in _config.SpawnSets)
            {
                Puts($"  Set '{kvp.Key}': {kvp.Value.Count} point(s)");
                int idx = 0;
                foreach (var p in kvp.Value)
                {
                    Puts($"    [{idx++}] {p}");
                }
            }

            SendReply(player, "<color=#55ff55>Spawn set info printed to server console.</color>");
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

        private bool SpawnNecroZombie(Vector3 position, ZombieProfile profile, bool trackForWave)
        {
            var waves = _config.Waves;

            if (_activeZombies.Count >= waves.MaxActiveZombies)
                return false;

            BaseEntity entity = GameManager.server.CreateEntity(ZombiePrefab, position, Quaternion.identity, true);
            if (entity == null)
            {
                PrintWarning($"[NecroZombies] Failed to create entity. Prefab path: {ZombiePrefab}");
                return false;
            }

            entity.Spawn();
            _activeZombies.Add(entity);

            if (trackForWave)
                _currentWaveZombies.Add(entity);

            if (!_loggedTypeOnce)
            {
                _loggedTypeOnce = true;
                Puts($"[NecroZombies] Spawned entity type: {entity.GetType().FullName}");
            }

            if (profile.AlwaysOnFire)
            {
                entity.SetFlag(BaseEntity.Flags.OnFire, true);
                entity.SendNetworkUpdate();
            }

            if (!string.IsNullOrEmpty(FliesMediumEffect))
            {
                Effect.server.Run(FliesMediumEffect, entity.transform.position + Vector3.up * 1.2f, Vector3.up, null);
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

            if (profile.AlwaysOnFire)
            {
                entity.SetFlag(BaseEntity.Flags.OnFire, true);
                entity.SendNetworkUpdate();
            }

            if (!string.IsNullOrEmpty(BurnEffectPrefab))
            {
                Effect.server.Run(BurnEffectPrefab, entity.transform.position + Vector3.up * 0.1f, Vector3.up, null);
            }

            // Optional: small flies at body height
            if (!string.IsNullOrEmpty(FliesMediumEffect))
            {
                Effect.server.Run(FliesMediumEffect, entity.transform.position + Vector3.up * 0.5f, Vector3.up, null);
            }

            // Most wolf prefabs are BaseNpc; if you want to directly tweak HP/speed we can experiment further.
            // For now we rely on base stats + AlwaysOnFire VFX.

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

                if (!string.IsNullOrEmpty(BurnEffectPrefab))
                {
                    Effect.server.Run(BurnEffectPrefab, npcPos + Vector3.up * 0.1f, Vector3.up, null);
                }

                if (!string.IsNullOrEmpty(FliesLoopEffect))
                {
                    Effect.server.Run(FliesLoopEffect, npcPos + Vector3.up * 1.2f, Vector3.up, null);
                }

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

            if (_waveSpawnSetName != null)
            {
                if (_config.SpawnSets == null || !_config.SpawnSets.ContainsKey(_waveSpawnSetName) ||
                    _config.SpawnSets[_waveSpawnSetName].Count == 0)
                {
                    PrintWarning($"[NecroZombies] Spawn set '{_waveSpawnSetName}' not found or empty, falling back to single center.");
                    _waveSpawnSetName = null;
                }
                else
                {
                    Puts($"[NecroZombies] Using spawn set '{_waveSpawnSetName}' with {_config.SpawnSets[_waveSpawnSetName].Count} point(s).");
                }
            }

            _currentWaveZombies.Clear();
            _currentWaveSpawned = 0;
            _currentWaveTotalToSpawn = 0;

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = null;
            _waveCheckTimer?.Destroy();
            _waveCheckTimer = null;

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

            _currentWaveZombies.Clear();
            _currentWaveSpawned = 0;
            _currentWaveTotalToSpawn = 0;

            DestroyWaveBannerForAll();
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

            float interval = waves.SpawnIntervalSeconds * Mathf.Pow(waves.SpawnIntervalPerWaveMultiplier, _currentWave - 1);
            _currentWaveSpawnInterval = Mathf.Max(waves.SpawnIntervalMinSeconds, interval);

            _currentWaveStartTime = Time.realtimeSinceStartup;

            bool hellWave = IsHellhoundWave(_currentWave);
            if (hellWave)
                ShowStageBanner("HELLHOUNDS", "Stay sharp", waves.BannerDuration);
            else
                ShowWaveBanner(_currentWave, _waveProfileName);

            Puts($"[NecroZombies] Wave {_currentWave} starting. Hellhounds: {hellWave}. Will spawn ~{_currentWaveTotalToSpawn} total.");

            _waveSpawnTimer?.Destroy();
            _waveSpawnTimer = timer.Every(_currentWaveSpawnInterval, DripSpawnWave);

            _waveCheckTimer?.Destroy();
            _waveCheckTimer = timer.Every(1f, CheckWaveProgress);
        }

        private Vector3 GetRandomSpawnPosition()
        {
            if (_waveSpawnSetName != null &&
                _config.SpawnSets != null &&
                _config.SpawnSets.TryGetValue(_waveSpawnSetName, out var list) &&
                list != null &&
                list.Count > 0)
            {
                var point = list[UnityEngine.Random.Range(0, list.Count)];
                Vector2 circle = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(0.5f, 2f);
                return point + new Vector3(circle.x, 0f, circle.y);
            }

            Vector2 fallbackCircle = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(3f, 6f);
            return _fallbackCenter + new Vector3(fallbackCircle.x, 0f, fallbackCircle.y);
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

            int groupSize = waves.GroupSize;
            int spawnedThisTick = 0;

            for (int i = 0; i < groupSize; i++)
            {
                if (_currentWaveTotalToSpawn <= 0)
                    break;

                if (_activeZombies.Count >= waves.MaxActiveZombies)
                    break;

                Vector3 spawnPos = GetRandomSpawnPosition();

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
                        // normal zombies as mix
                        var baseProfile = GetProfile(_waveProfileName);
                        var waveProfile = new ZombieProfile
                        {
                            ProfileName = baseProfile.ProfileName,
                            MeleeShortname = baseProfile.MeleeShortname,
                            MeleeSkinId = baseProfile.MeleeSkinId,
                            ClothingShortname = baseProfile.ClothingShortname,
                            ClothingSkinId = baseProfile.ClothingSkinId,
                            DisplayName = $"{baseProfile.DisplayName} [Wave {_currentWave}]",
                            Health = baseProfile.Health * (1f + _config.Waves.HealthMultiplierPerWave * (_currentWave - 1)),
                            Speed = baseProfile.Speed * (1f + _config.Waves.SpeedMultiplierPerWave * (_currentWave - 1)),
                            AlwaysOnFire = baseProfile.AlwaysOnFire
                        };

                        spawnedOk = SpawnNecroZombie(spawnPos, waveProfile, trackForWave: true);
                    }
                }
                else
                {
                    var baseProfile = GetProfile(_waveProfileName);
                    var waveProfile = new ZombieProfile
                    {
                        ProfileName = baseProfile.ProfileName,
                        MeleeShortname = baseProfile.MeleeShortname,
                        MeleeSkinId = baseProfile.MeleeSkinId,
                        ClothingShortname = baseProfile.ClothingShortname,
                        ClothingSkinId = baseProfile.ClothingSkinId,
                        DisplayName = $"{baseProfile.DisplayName} [Wave {_currentWave}]",
                        Health = baseProfile.Health * (1f + _config.Waves.HealthMultiplierPerWave * (_currentWave - 1)),
                        Speed = baseProfile.Speed * (1f + _config.Waves.SpeedMultiplierPerWave * (_currentWave - 1)),
                        AlwaysOnFire = baseProfile.AlwaysOnFire
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

            if (_currentWaveInitialCount <= 0)
                return;

            int aliveInWave = 0;
            foreach (var be in _currentWaveZombies)
            {
                if (be != null && !be.IsDestroyed)
                    aliveInWave++;
            }

            int deadInWave = _currentWaveInitialCount - aliveInWave;
            float killRatio = (float)deadInWave / _currentWaveInitialCount;

            if (killRatio >= waves.RequiredKillRatioToAdvance)
            {
                Puts($"[NecroZombies] Wave {_currentWave} kill ratio reached ({killRatio:P0}). Next wave in {waves.WaveStartDelay:F1}s.");
                _waveCheckTimer?.Destroy();
                _waveCheckTimer = null;

                // Intermission banner between waves
                ShowStageBanner("INTERMISSION", $"Wave {_currentWave} cleared", waves.WaveStartDelay);
                timer.Once(waves.WaveStartDelay, StartNextWave);
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _currentWaveStartTime;
            if (elapsed >= waves.WaveTimeoutSeconds && waves.WaveTimeoutSeconds > 0f)
            {
                Puts($"[NecroZombies] Wave {_currentWave} timed out after {elapsed:F1}s. Forcing next wave in {waves.WaveStartDelay:F1}s.");
                _waveCheckTimer?.Destroy();
                _waveCheckTimer = null;

                ShowStageBanner("INTERMISSION", "Time's up", waves.WaveStartDelay);
                timer.Once(waves.WaveStartDelay, StartNextWave);
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

        private string BuildWaveBannerJson(string title, string subtitle)
        {
            var waves = _config.Waves;

            var container = new CuiElementContainer();

            // Root panel
            var panel = new CuiElement
            {
                Name = WaveBannerPanel,
                Parent = "Hud",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0.55"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.2 0.9",
                        AnchorMax = "0.8 0.98"
                    }
                }
            };
            container.elements.Add(panel);

            // Title
            var titleElement = new CuiElement
            {
                Name = WaveBannerTitle,
                Parent = WaveBannerPanel,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = title,
                        FontSize = waves.BannerTitleSize,
                        Align = (int)TextAnchor.MiddleCenter,
                        Color = waves.BannerTitleColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.3",
                        AnchorMax = "1 1"
                    }
                }
            };
            container.elements.Add(titleElement);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subElement = new CuiElement
                {
                    Name = WaveBannerSubtitle,
                    Parent = WaveBannerPanel,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = subtitle,
                            FontSize = waves.BannerSubSize,
                            Align = (int)TextAnchor.MiddleCenter,
                            Color = waves.BannerSubColor
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 0.5"
                        }
                    }
                };
                container.elements.Add(subElement);
            }

            return JsonConvert.SerializeObject(container);
        }

        // Minimal CUI support types
        private class CuiElementContainer
        {
            [JsonProperty("elements")]
            public List<CuiElement> elements = new List<CuiElement>();
        }

        private class CuiElement
        {
            [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
            public string Name;

            [JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
            public string Parent;

            [JsonProperty("components")]
            public List<object> Components = new List<object>();
        }

        private class CuiRectTransformComponent
        {
            [JsonProperty("type")]
            public string Type = "RectTransform";

            [JsonProperty("anchormin")]
            public string AnchorMin;

            [JsonProperty("anchormax")]
            public string AnchorMax;
        }

        private class CuiImageComponent
        {
            [JsonProperty("type")]
            public string Type = "UnityEngine.UI.Image";

            [JsonProperty("color")]
            public string Color = "1 1 1 1";
        }

        private class CuiTextComponent
        {
            [JsonProperty("type")]
            public string Type = "UnityEngine.UI.Text";

            [JsonProperty("text")]
            public string Text;

            [JsonProperty("fontSize")]
            public int FontSize;

            [JsonProperty("align")]
            public int Align;

            [JsonProperty("color")]
            public string Color = "1 1 1 1";
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