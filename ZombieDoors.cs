using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("ZombieDoors", "KillaDome", "1.1.9")]
    [Description("Purchasable doors that stay open once bought. Supports Scrap & Economy.")]
    public class ZombieDoors : RustPlugin
    {
        // Dependencies for Economy
        [PluginReference] Plugin ServerRewards, Economics;

        // --- Data & Config ---
        private class DoorInfo
        {
            public int Cost;
            public bool IsOpen;
            public ulong DoorId;
            public string PrefabName;

            // Store raw coordinates
            public float x, y, z;
            public float rx, ry, rz, rw;

            [JsonIgnore]
            public Vector3 Position => new Vector3(x, y, z);

            [JsonIgnore]
            public Quaternion Rotation => new Quaternion(rx, ry, rz, rw);

            public void SetLocation(Vector3 pos, Quaternion rot)
            {
                x = pos.x; y = pos.y; z = pos.z;
                rx = rot.x; ry = rot.y; rz = rot.z; rw = rot.w;
            }
        }

        private List<DoorInfo> _doorDataList = new List<DoorInfo>();
        private Dictionary<ulong, DoorInfo> _doorLookup = new Dictionary<ulong, DoorInfo>();

        private const string PermAdmin = "zombiedoors.admin";
        private const string UIName = "ZombieDoorUI";
        private const int ScrapItemID = -932201673; // Item ID for Scrap
        
        private Timer _uiTimer;

        // --- Oxide Hooks ---

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
            BuildLookup();
        }

        private void OnServerInitialized()
        {
            _uiTimer = timer.Every(0.25f, CheckPlayerLook);
            
            // Sync visual state on restart
            foreach (var info in _doorDataList)
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(info.DoorId)) as Door;
                if (entity != null)
                {
                    if (info.IsOpen)
                    {
                        // Open: Unlock and Open
                        entity.SetFlag(BaseEntity.Flags.Locked, false);
                        entity.SetFlag(BaseEntity.Flags.Open, true);
                    }
                    else
                    {
                        // Closed: Must be Unlocked in engine so 'E' works, but Closed physically
                        entity.SetFlag(BaseEntity.Flags.Locked, false);
                        entity.SetFlag(BaseEntity.Flags.Open, false);
                    }
                    entity.SendNetworkUpdate();
                }
            }
        }

        private void Unload()
        {
            SaveData();
            if (_uiTimer != null) _uiTimer.Destroy();
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UIName);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is Door && _doorLookup.ContainsKey(entity.net.ID.Value))
            {
                _doorLookup.Remove(entity.net.ID.Value);
            }
        }

        // --- Core Logic: Handling Door Interaction ---

        // Hook 1: Proactive Prevention
        object CanUseDoor(BasePlayer player, Door door)
        {
            if (door == null) return null;

            if (_doorLookup.TryGetValue(door.net.ID.Value, out DoorInfo info))
            {
                // If bought, keep it open (deny closing)
                if (info.IsOpen) return false; 

                // If not bought, deny default opening and trigger buy attempt
                // Note: If this return false fails to stop the door (due to lag/plugins), OnDoorOpened catches it.
                AttemptBuyDoor(player, door, info);
                return false; 
            }
            return null;
        }

        // Hook 2: Reactive Failsafe (The "Snap Shut" Fix)
        void OnDoorOpened(Door door, BasePlayer player)
        {
            if (door == null || player == null) return;

            if (_doorLookup.TryGetValue(door.net.ID.Value, out DoorInfo info))
            {
                // If the door opened physically but hasn't been paid for yet
                if (!info.IsOpen)
                {
                    // 1. Slam it shut immediately
                    door.SetFlag(BaseEntity.Flags.Open, false);
                    door.SendNetworkUpdateImmediate();

                    // 2. Process the payment logic
                    AttemptBuyDoor(player, door, info);
                }
            }
        }

        // Hook 3: Knocking (Just in case)
        object OnDoorKnock(Door door, BasePlayer player)
        {
            if (door == null) return null;

            if (_doorLookup.TryGetValue(door.net.ID.Value, out DoorInfo info))
            {
                if (!info.IsOpen)
                {
                    AttemptBuyDoor(player, door, info);
                    return true; 
                }
            }
            return null;
        }

        private void AttemptBuyDoor(BasePlayer player, Door door, DoorInfo info)
        {
            // 1. Determine Payment Method (Economy Plugin or Scrap)
            bool useScrap = (ServerRewards == null && Economics == null);
            string currencyName = useScrap ? "Scrap" : "Points";

            // 2. Check Balance
            bool canAfford = false;
            if (useScrap)
            {
                canAfford = player.inventory.GetAmount(ScrapItemID) >= info.Cost;
            }
            else
            {
                canAfford = GetBalance(player.UserIDString) >= info.Cost;
            }

            // 3. Process Transaction
            if (canAfford)
            {
                bool transactionSuccess = false;
                if (useScrap)
                {
                    player.inventory.Take(null, ScrapItemID, info.Cost);
                    transactionSuccess = true;
                }
                else
                {
                    transactionSuccess = Withdraw(player.UserIDString, info.Cost);
                }

                if (transactionSuccess)
                {
                    info.IsOpen = true;
                    SaveData();

                    // Open the door permanently now
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                    door.SetFlag(BaseEntity.Flags.Open, true);
                    door.SendNetworkUpdateImmediate();

                    Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", door.transform.position);
                    SendReply(player, $"<color=#00ff00>Path Cleared!</color> -{info.Cost} {currencyName}");
                    CuiHelper.DestroyUi(player, UIName);
                }
                else
                {
                    SendReply(player, "<color=red>Transaction failed.</color>");
                }
            }
            else
            {
                // Ensure it stays closed visually if they can't afford it
                door.SetFlag(BaseEntity.Flags.Open, false);
                door.SendNetworkUpdateImmediate();

                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", door.transform.position);
                SendReply(player, $"<color=red>Not enough {currencyName}!</color> You need {info.Cost}.");
            }
        }

        // --- UI Logic ---

        private void CheckPlayerLook()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.IsSleeping() || player.IsDead()) continue;

                RaycastHit hit;
                if (Physics.Raycast(player.eyes.HeadRay(), out hit, 4.5f, Rust.Layers.Mask.Construction | Rust.Layers.Mask.Deployed)) 
                {
                    var entity = hit.GetEntity();
                    if (entity is Door && _doorLookup.TryGetValue(entity.net.ID.Value, out DoorInfo info))
                    {
                        if (!info.IsOpen)
                        {
                            DrawDoorUI(player, info.Cost);
                            continue; 
                        }
                    }
                }
                CuiHelper.DestroyUi(player, UIName);
            }
        }

        private void DrawDoorUI(BasePlayer player, int cost)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-100 -25", OffsetMax = "100 25" },
                Image = { Color = "0 0 0 0.8" }
            }, "Hud", UIName);

            string currency = (ServerRewards == null && Economics == null) ? "SCRAP" : "POINTS";

            container.Add(new CuiLabel
            {
                Text = { Text = $"UNLOCK PATH\n<color=#ffcc00>{cost} {currency}</color>", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UIName);

            CuiHelper.DestroyUi(player, UIName);
            CuiHelper.AddUi(player, container);
        }

        // --- Respawner & Reset Logic ---

        [ConsoleCommand("zdoor.reset")]
        private void ConsoleCmdReset(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), PermAdmin)) return;
            ResetAllDoors();
            SendReply(arg, "All Zombie Doors have been reset and respawned.");
        }

        [ChatCommand("zdoor")]
        private void ChatCmdZDoor(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin)) return;

            if (args.Length > 0)
            {
                // Added aliases for "spawn" and "respawn" to map to reset
                string sub = args[0].ToLower();
                if (sub == "reset" || sub == "spawn" || sub == "respawn")
                {
                    ResetAllDoors();
                    SendReply(player, "All Zombie Doors have been reset and respawned.");
                    return;
                }
            }
            
            HandleAddRemove(player, args);
        }

        private void ResetAllDoors()
        {
            _doorLookup.Clear();
            var validDoors = new List<DoorInfo>();
            
            Puts($"[ZombieDoors] Processing reset for {_doorDataList.Count} saved doors.");

            foreach (var info in _doorDataList)
            {
                var door = BaseNetworkable.serverEntities.Find(new NetworkableId(info.DoorId)) as Door;

                if (door != null && door.IsDestroyed) 
                {
                    door = null;
                }

                // 1. If door is missing (destroyed), Respawn it
                if (door == null)
                {
                    if (string.IsNullOrEmpty(info.PrefabName))
                    {
                        Puts($"[ZombieDoors] Error: Missing PrefabName for door at {info.Position}. Cannot respawn.");
                        continue;
                    }

                    door = GameManager.server.CreateEntity(info.PrefabName, info.Position, info.Rotation) as Door;
                    if (door != null)
                    {
                        door.Spawn();
                        info.DoorId = door.net.ID.Value;
                        Puts($"[ZombieDoors] Respawned door: {info.PrefabName}");
                    }
                    else
                    {
                        Puts($"[ZombieDoors] Failed to create entity: {info.PrefabName}");
                        continue;
                    }
                }

                // 2. Reset State
                if (door != null)
                {
                    door.health = door.MaxHealth();
                    
                    // Force Closed, but DO NOT set Locked flag so 'E' works
                    door.SetFlag(BaseEntity.Flags.Open, false);
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                    
                    info.IsOpen = false;
                    door.SendNetworkUpdateImmediate();
                    
                    _doorLookup[door.net.ID.Value] = info;
                    validDoors.Add(info);
                }
            }
            
            _doorDataList = validDoors;
            SaveData();
        }

        private void HandleAddRemove(BasePlayer player, string[] args)
        {
            if (args.Length == 0)
            {
                SendReply(player, "Usage: /zdoor add <cost> | /zdoor remove | /zdoor reset (or spawn)");
                return;
            }

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, Rust.Layers.Mask.Construction | Rust.Layers.Mask.Deployed))
            {
                SendReply(player, "Look at a door/gate first.");
                return;
            }

            var door = hit.GetEntity() as Door;
            if (door == null)
            {
                SendReply(player, "That is not a door or gate.");
                return;
            }

            if (args[0].ToLower() == "add" && args.Length >= 2)
            {
                if (int.TryParse(args[1], out int cost))
                {
                    var info = new DoorInfo
                    {
                        DoorId = door.net.ID.Value,
                        Cost = cost,
                        IsOpen = false,
                        PrefabName = door.PrefabName
                    };
                    info.SetLocation(door.transform.position, door.transform.rotation);
                    
                    Puts($"[ZombieDoors] Saving door: {door.PrefabName} at {door.transform.position}");

                    _doorDataList.RemoveAll(x => x.DoorId == door.net.ID.Value);
                    _doorDataList.Add(info);
                    _doorLookup[door.net.ID.Value] = info;

                    // Force Close but NOT Locked flag so interactions work
                    door.SetFlag(BaseEntity.Flags.Open, false);
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                    door.SendNetworkUpdateImmediate();

                    SaveData();
                    SendReply(player, $"Zombie Door set! Cost: {cost}. Locked (Virtual).");
                }
            }
            else if (args[0].ToLower() == "remove")
            {
                if (_doorLookup.Remove(door.net.ID.Value))
                {
                    _doorDataList.RemoveAll(x => x.DoorId == door.net.ID.Value);
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                    door.SendNetworkUpdateImmediate();
                    
                    SaveData();
                    SendReply(player, "Removed Zombie Door status.");
                }
                else
                {
                    SendReply(player, "This is not a Zombie Door.");
                }
            }
        }

        // --- API Methods (Called by CaptainPrice) ---

        private void ForceOpenDoor(ulong doorId)
        {
            if (_doorLookup.TryGetValue(doorId, out DoorInfo info))
            {
                var door = BaseNetworkable.serverEntities.Find(new NetworkableId(doorId)) as Door;
                if (door != null)
                {
                    info.IsOpen = true;
                    SaveData();

                    door.SetFlag(BaseEntity.Flags.Locked, false);
                    door.SetFlag(BaseEntity.Flags.Open, true);
                    door.SendNetworkUpdateImmediate();

                    Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", door.transform.position);
                    Puts($"[ZombieDoors] Door {doorId} force opened by CaptainPrice");
                }
            }
        }

        private bool IsDoorLocked(ulong doorId)
        {
            if (_doorLookup.TryGetValue(doorId, out DoorInfo info))
            {
                return !info.IsOpen;
            }
            return false;
        }

        // --- Helpers ---

        private void BuildLookup()
        {
            _doorLookup.Clear();
            foreach (var info in _doorDataList)
            {
                _doorLookup[info.DoorId] = info;
            }
        }

        private double GetBalance(string userId)
        {
            if (ServerRewards != null) return (int)ServerRewards.Call("CheckPoints", ulong.Parse(userId));
            if (Economics != null) return (double)Economics.Call("Balance", userId);
            return 0;
        }

        private bool Withdraw(string userId, int amount)
        {
            if (ServerRewards != null) return (bool)ServerRewards.Call("TakePoints", ulong.Parse(userId), amount);
            if (Economics != null) return (bool)Economics.Call("Withdraw", userId, (double)amount);
            return false;
        }

        private void LoadData()
        {
            _doorDataList = Interface.Oxide.DataFileSystem.ReadObject<List<DoorInfo>>("ZombieDoors");
            if (_doorDataList == null) _doorDataList = new List<DoorInfo>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("ZombieDoors", _doorDataList);
        }
    }
}