using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DriveBySedanGangs", "belisario-afk", "1.0.0")]
    [Description("AI-driven sedan gangs with scientists that chase and attack players")]
    public class DriveBySedanGangs : RustPlugin
    {
        #region Fields

        private Timer _scientistHopTimer;
        private readonly Dictionary<ScientistNPC, Vector3> _lastScientistPositions = 
            new Dictionary<ScientistNPC, Vector3>();
        private readonly Dictionary<ScientistNPC, float> _scientistStuckTime = 
            new Dictionary<ScientistNPC, float>();

        private readonly Dictionary<BaseEntity, List<ScientistNPC>> _sedanScientists = 
            new Dictionary<BaseEntity, List<ScientistNPC>>();
        private readonly HashSet<BaseEntity> _deployedSedans = new HashSet<BaseEntity>();
        private readonly Dictionary<BaseEntity, DriveByState> _driveByStates = 
            new Dictionary<BaseEntity, DriveByState>();
        private readonly Dictionary<ScientistNPC, int> _scientistSeats = 
            new Dictionary<ScientistNPC, int>();
        private readonly HashSet<BaseEntity> _retiringSedans = new HashSet<BaseEntity>();

        private ConfigData _config;

        private const float FollowUpdateInterval = 0.5f;
        private const float TerritoryCheckInterval = 5f;
        private static readonly int GroundLayerMask = LayerMask.GetMask("Terrain", "World", "Construction", "Default");

        #endregion

        #region Configuration

        private class ConfigData
        {
            public int MaxGangs = 5;
            public float ScientistHealth = 150f;
            public float ChaseRange = 100f;
            public float DeployRange = 30f;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>();
                if (_config == null)
                    LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            LoadConfig();
            timer.Every(FollowUpdateInterval, UpdateAllSedans);
            timer.Every(TerritoryCheckInterval, CheckPlayerTerritories);
            
            // Add scientist hop timer (check every 1 second)
            _scientistHopTimer = timer.Every(1f, ScientistHopTick);
        }

        private void Unload()
        {
            // Clean up all sedans and scientists
            foreach (var kvp in _sedanScientists.ToArray())
            {
                var car = kvp.Key;
                if (car != null && !car.IsDestroyed)
                {
                    RetireSedan(car);
                }
            }

            _scientistHopTimer?.Destroy();
            _lastScientistPositions.Clear();
            _scientistStuckTime.Clear();
            
            _sedanScientists.Clear();
            _deployedSedans.Clear();
            _driveByStates.Clear();
            _scientistSeats.Clear();
            _retiringSedans.Clear();
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is ScientistNPC npc)
            {
                HandleScientistDeath(npc);
            }
        }

        #endregion

        #region Core Methods

        private void UpdateAllSedans()
        {
            // Update sedan AI logic (driving, chasing, etc.)
            foreach (var car in _deployedSedans.ToArray())
            {
                if (car == null || car.IsDestroyed)
                {
                    _deployedSedans.Remove(car);
                    continue;
                }
                
                // Update driving logic here
            }
        }

        private void CheckPlayerTerritories()
        {
            // Check if players have entered gang territories
            // Spawn/deploy sedans as needed
        }

        #endregion

        #region Scientist Management

        private void HandleScientistDeath(ScientistNPC npc)
        {
            if (npc == null) return;

            _scientistSeats.Remove(npc);
            _lastScientistPositions.Remove(npc);
            _scientistStuckTime.Remove(npc);
            
            // Remove from gang tracking
            foreach (var kvp in _sedanScientists.ToArray())
            {
                var sciList = kvp.Value;
                if (sciList != null && sciList.Contains(npc))
                {
                    sciList.Remove(npc);
                    break;
                }
            }
        }

        private void RetireSedan(BaseEntity car)
        {
            if (car == null) return;
            if (_retiringSedans.Contains(car)) return;
            _retiringSedans.Add(car);

            NextFrame(() =>
            {
                if (car == null || car.IsDestroyed) return;

                if (_sedanScientists.TryGetValue(car, out var sciList) && sciList != null)
                {
                    foreach (var npc in sciList.ToArray())
                    {
                        if (npc == null) continue;

                        // Clean up tracking
                        _lastScientistPositions.Remove(npc);
                        _scientistStuckTime.Remove(npc);
                        
                        // Dismount and kill scientist
                        if (npc.isMounted)
                        {
                            var mounted = npc.GetMounted();
                            if (mounted != null)
                            {
                                npc.DismountObject();
                            }
                        }
                        
                        if (!npc.IsDestroyed)
                        {
                            npc.Kill();
                        }
                    }
                    
                    sciList.Clear();
                }

                _sedanScientists.Remove(car);
                _deployedSedans.Remove(car);
                _driveByStates.Remove(car);
                _retiringSedans.Remove(car);

                if (!car.IsDestroyed)
                {
                    car.Kill();
                }
            });
        }

        #endregion

        #region Anti-Stuck Hop System

        private void ScientistHopTick()
        {
            const float stuckThreshold = 0.5f; // Movement less than 0.5m = stuck
            const float stuckTimeLimit = 3f;   // Stuck for 3 seconds = hop
            const float hopDistance = 5f;      // Hop 5 units toward player
            const float hopHeight = 0.4f;      // Small hop for scientists
            
            float now = Time.realtimeSinceStartup;
            
            // Check all deployed scientists (only check on-foot, not mounted)
            foreach (var kvp in _sedanScientists.ToArray())
            {
                var car = kvp.Key;
                var sciList = kvp.Value;
                
                if (car == null || sciList == null) continue;
                if (!_deployedSedans.Contains(car)) continue; // Only check deployed scientists
                
                // Get target player for this gang
                if (!_driveByStates.TryGetValue(car, out var state)) continue;
                var target = BasePlayer.FindByID(state.TargetID);
                if (target == null || target.IsDead()) continue;
                
                foreach (var sci in sciList.ToArray())
                {
                    if (sci == null || sci.IsDestroyed || sci.isMounted) continue;
                    
                    Vector3 currentPos = sci.transform.position;
                    
                    // Check if scientist has moved since last tick
                    if (_lastScientistPositions.TryGetValue(sci, out var lastPos))
                    {
                        float distMoved = Vector3.Distance(currentPos, lastPos);
                        
                        if (distMoved < stuckThreshold)
                        {
                            // Scientist hasn't moved much - increment stuck time
                            if (!_scientistStuckTime.ContainsKey(sci))
                                _scientistStuckTime[sci] = now;
                            
                            float stuckDuration = now - _scientistStuckTime[sci];
                            
                            if (stuckDuration >= stuckTimeLimit)
                            {
                                // STUCK! Perform hop toward player
                                Vector3 toPlayer = target.transform.position - currentPos;
                                toPlayer.y = 0f;
                                
                                if (toPlayer.magnitude > 1f)
                                {
                                    toPlayer.Normalize();
                                    Vector3 hopTarget = currentPos + toPlayer * hopDistance 
                                        + Vector3.up * hopHeight;
                                    
                                    // Raycast to find ground - only hop if valid position found
                                    if (FindGroundPosition(hopTarget, out var groundPos))
                                    {
                                        // Teleport scientist to new position
                                        sci.MovePosition(groundPos);
                                        sci.TransformChanged();
                                        
                                        // Update NavMesh destination after hop
                                        if (sci.Brain?.Navigator != null)
                                        {
                                            sci.Brain.Navigator.SetDestination(target.transform.position, 
                                                BaseNavigator.NavigationSpeed.Normal);
                                        }
                                        
                                        Puts($"[DriveBySedanGangs] Scientist hopped {hopDistance}m " +
                                             $"after being stuck for {stuckDuration:F1}s");
                                    }
                                    else
                                    {
                                        // No valid ground found, skip this hop attempt
                                        Puts($"[DriveBySedanGangs] Scientist hop failed - no valid ground found");
                                    }
                                }
                                
                                // Reset stuck timer after hop attempt
                                _scientistStuckTime.Remove(sci);
                            }
                        }
                        else
                        {
                            // Scientist is moving normally - reset stuck tracking
                            _scientistStuckTime.Remove(sci);
                        }
                    }
                    
                    // Update last known position
                    _lastScientistPositions[sci] = currentPos;
                }
            }
        }

        #endregion

        #region Helper Methods

        private bool FindGroundPosition(Vector3 position, out Vector3 groundPos)
        {
            // Raycast down to find ground with comprehensive layer mask
            RaycastHit hit;
            
            if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out hit, 100f, GroundLayerMask))
            {
                // Additional validation: check if position is not inside a collider
                Vector3 testPos = hit.point + Vector3.up * 0.5f;
                if (!Physics.CheckSphere(testPos, 0.4f, GroundLayerMask))
                {
                    groundPos = hit.point + Vector3.up * 0.1f; // Slight offset above ground
                    return true;
                }
            }
            
            // No valid ground found - return failure
            groundPos = Vector3.zero;
            return false;
        }

        #endregion

        #region Data Classes

        private class DriveByState
        {
            public ulong TargetID;
            public Vector3 LastKnownPosition;
            public float LastSeenTime;
        }

        #endregion

        #region Commands

        [ChatCommand("stalksedan")]
        private void CmdStalkSedan(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("You need admin privileges to use this command.");
                return;
            }

            // Spawn a sedan gang to stalk the player
            player.ChatMessage("Spawning drive-by sedan gang...");
            
            // Implementation would create sedan with scientists here
        }

        #endregion
    }
}
