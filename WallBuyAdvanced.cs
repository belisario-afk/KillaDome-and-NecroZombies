// File: WallBuyAdvanced.cs
// Advanced WallBuy plugin for uMod/Oxide (FULL FIXED)
// - Stores Euler rotations (Vector3) to avoid Quaternion JSON self-reference
// - Live editor with keybind nudges, save/cancel, spawn/remove, preview
// - Default prefab: assets/prefabs/weapons/ak47u/ak47u.entity.prefab
//
// Author: Vic (fixed version)

using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WallBuyAdvanced", "Vic", "1.1.1")]
    [Description("Wall-Buy system with live editor (D-pad / keys) for Rust - Fixed serialization")]
    public class WallBuyAdvanced : RustPlugin
    {
        #region Data structures

        private class WallPoint
        {
            public Vector3 Position;
            public Vector3 RotationEuler; // store euler angles (x,y,z) instead of Quaternion
            public string Shortname;
            public int Amount;
            public int Cost;
            public string Prefab;
        }

        private class WallRuntime
        {
            public BaseEntity VisualEntity;
        }

        private class EditorState
        {
            public int Index = -1;
            public WallPoint Backup = null;
            public BaseEntity PreviewEntity = null;
            public bool Active = false;
        }

        private List<WallPoint> points = new List<WallPoint>();
        private List<WallRuntime> runtimes = new List<WallRuntime>();
        private Dictionary<ulong, EditorState> editors = new Dictionary<ulong, EditorState>();

        // default prefab (change if you want a different default)
        private const string defaultPrefab = "assets/prefabs/weapons/ak47u/ak47u.entity.prefab";

        #endregion

        #region Save/Load

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, points);
        private void LoadData() => points = Interface.Oxide.DataFileSystem.ReadObject<List<WallPoint>>(Name) ?? new List<WallPoint>();

        #endregion

        #region Server init / spawn visuals

        private void OnServerInitialized()
        {
            LoadData();
            runtimes = new List<WallRuntime>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var vr = new WallRuntime { VisualEntity = SpawnVisualForPoint(p) };
                runtimes.Add(vr);
            }
            Puts($"WallBuyAdvanced: Loaded {points.Count} points.");
        }

        // Convert stored Euler to Quaternion and spawn
        private BaseEntity SpawnVisualForPoint(WallPoint p)
        {
            if (string.IsNullOrEmpty(p.Prefab)) p.Prefab = defaultPrefab;
            Quaternion rot = Quaternion.Euler(p.RotationEuler);
            var ent = GameManager.server.CreateEntity(p.Prefab, p.Position, rot);
            if (ent == null)
            {
                Puts($"WallBuyAdvanced: Failed to spawn visual prefab '{p.Prefab}' at {p.Position}. Trying default prefab.");
                ent = GameManager.server.CreateEntity(defaultPrefab, p.Position, rot);
                if (ent == null)
                {
                    Puts("WallBuyAdvanced: Default prefab spawn ALSO failed! Aborting visual spawn.");
                    return null;
                }
            }

            ent.Spawn();
            // Try disabling physics so it behaves like a mounted prop
            try
            {
                var rb = ent.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
            }
            catch { }

            return ent;
        }

        #endregion

        #region Admin commands (add/list/remove)

        [ChatCommand("wallbuy_add")]
        private void CmdAddWallBuy(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /wallbuy_add <item_shortname> <cost> [<prefab>]");
                return;
            }

            var def = ItemManager.FindItemDefinition(args[0]);
            if (def == null)
            {
                player.ChatMessage($"Invalid item shortname: {args[0]}");
                return;
            }

            if (!int.TryParse(args[1], out int cost))
            {
                player.ChatMessage("Cost must be a number.");
                return;
            }

            string prefab = args.Length >= 3 ? args[2] : defaultPrefab;

            // Raycast to surface
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 10f))
            {
                player.ChatMessage("Aim at a surface within 10m.");
                return;
            }

            // small offset to avoid clipping
            Vector3 pos = hit.point + (hit.normal * 0.01f);

            // compute a flat & sideways rotation: look into wall (inverse normal) then rotate to be sideways/flat
            Quaternion rot = Quaternion.LookRotation(hit.normal * -1f) * Quaternion.Euler(0f, 90f, 90f);

            var wp = new WallPoint
            {
                Position = pos,
                RotationEuler = rot.eulerAngles,
                Shortname = def.shortname,
                Amount = 1,
                Cost = cost,
                Prefab = prefab
            };

            points.Add(wp);
            SaveData();

            // Spawn visual and add runtime
            var visual = SpawnVisualForPoint(wp);
            runtimes.Add(new WallRuntime { VisualEntity = visual });

            player.ChatMessage($"✔ WallBuy added: {def.shortname} | Cost: {cost} | Prefab: {prefab}");
        }

        [ChatCommand("wallbuy_list")]
        private void CmdList(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            if (points.Count == 0) { player.ChatMessage("No wallbuys configured."); return; }

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                player.ChatMessage($"{i}: {p.Shortname} | Cost: {p.Cost} | Prefab: {p.Prefab} | Pos: {p.Position} | RotEuler: {p.RotationEuler}");
            }
        }

        [ChatCommand("wallbuy_remove")]
        private void CmdRemove(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length != 1) { player.ChatMessage("Usage: /wallbuy_remove <id>"); return; }
            if (!int.TryParse(args[0], out int id) || id < 0 || id >= points.Count) { player.ChatMessage("Invalid ID."); return; }

            // cleanup runtime visual
            if (id < runtimes.Count && runtimes[id] != null && runtimes[id].VisualEntity != null && !runtimes[id].VisualEntity.IsDestroyed)
            {
                runtimes[id].VisualEntity.Kill();
            }

            points.RemoveAt(id);
            if (id < runtimes.Count) runtimes.RemoveAt(id);

            SaveData();
            player.ChatMessage($"✔ Removed wallbuy id {id} and cleaned up visuals.");
        }

        #endregion

        #region Editor Mode (edit/save/cancel)

        [ChatCommand("wallbuy_edit")]
        private void CmdEdit(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length != 1) { player.ChatMessage("Usage: /wallbuy_edit <id>"); return; }
            if (!int.TryParse(args[0], out int id) || id < 0 || id >= points.Count) { player.ChatMessage("Invalid ID."); return; }

            // cancel existing edit for this admin if active
            if (editors.ContainsKey(player.userID) && editors[player.userID].Active)
            {
                CancelEdit(player, silent: true);
            }

            var state = new EditorState
            {
                Index = id,
                Backup = new WallPoint
                {
                    Position = points[id].Position,
                    RotationEuler = points[id].RotationEuler,
                    Shortname = points[id].Shortname,
                    Amount = points[id].Amount,
                    Cost = points[id].Cost,
                    Prefab = points[id].Prefab
                },
                Active = true
            };

            // remove existing saved visual so preview is the only visible model
            if (id < runtimes.Count && runtimes[id] != null && runtimes[id].VisualEntity != null && !runtimes[id].VisualEntity.IsDestroyed)
            {
                runtimes[id].VisualEntity.Kill();
            }

            // spawn preview using the stored point
            state.PreviewEntity = SpawnVisualForPoint(points[id]);
            if (state.PreviewEntity == null)
            {
                player.ChatMessage("Failed to spawn preview entity. Editing cancelled.");
                return;
            }

            editors[player.userID] = state;

            player.ChatMessage("=== WallBuy EDIT MODE ===");
            player.ChatMessage("W/S/A/D = move | R (reload) = yaw +15 | F (duck) = yaw -15");
            player.ChatMessage("SPACE = scale +0.05 | SHIFT = scale -0.05");
            player.ChatMessage("/wallbuy_save = save & exit | /wallbuy_cancel = cancel & revert");
        }

        [ChatCommand("wallbuy_save")]
        private void CmdSave(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            if (!editors.ContainsKey(player.userID) || !editors[player.userID].Active) { player.ChatMessage("You are not editing any wallbuy."); return; }

            var st = editors[player.userID];
            if (st.PreviewEntity == null || st.PreviewEntity.IsDestroyed)
            {
                player.ChatMessage("Preview entity missing; cannot save.");
                editors.Remove(player.userID);
                return;
            }

            int index = st.Index;
            points[index].Position = st.PreviewEntity.transform.position;
            points[index].RotationEuler = st.PreviewEntity.transform.rotation.eulerAngles;

            // cleanup preview
            st.PreviewEntity.Kill();

            // respawn the proper visual to the runtime slot
            var newVisual = SpawnVisualForPoint(points[index]);
            if (index < runtimes.Count) runtimes[index].VisualEntity = newVisual;
            else runtimes.Add(new WallRuntime { VisualEntity = newVisual });

            SaveData();
            editors.Remove(player.userID);
            player.ChatMessage($"✔ Saved wallbuy {index}.");
        }

        [ChatCommand("wallbuy_cancel")]
        private void CmdCancel(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            CancelEdit(player, silent: false);
        }

        private void CancelEdit(BasePlayer player, bool silent)
        {
            if (!editors.ContainsKey(player.userID)) { if (!silent) player.ChatMessage("You are not editing any wallbuy."); return; }
            var st = editors[player.userID];
            if (st == null || !st.Active) { if (!silent) player.ChatMessage("No active editor."); editors.Remove(player.userID); return; }

            if (st.PreviewEntity != null && !st.PreviewEntity.IsDestroyed) st.PreviewEntity.Kill();

            // respawn original visual using backup data
            if (st.Index >= 0 && st.Index < points.Count)
            {
                points[st.Index].Position = st.Backup.Position;
                points[st.Index].RotationEuler = st.Backup.RotationEuler;
                var visual = SpawnVisualForPoint(points[st.Index]);
                if (st.Index < runtimes.Count) runtimes[st.Index].VisualEntity = visual;
                else runtimes.Add(new WallRuntime { VisualEntity = visual });
            }

            editors.Remove(player.userID);
            if (!silent) player.ChatMessage("Edit cancelled and changes reverted.");
        }

        #endregion

        #region Player Input (editor controls + buying)

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            // Editor mode handling has precedence
            if (editors.ContainsKey(player.userID) && editors[player.userID].Active)
            {
                var st = editors[player.userID];
                if (st.PreviewEntity == null || st.PreviewEntity.IsDestroyed)
                {
                    player.ChatMessage("Preview entity lost. Use /wallbuy_cancel.");
                    return;
                }

                float moveStep = 0.05f;
                float rotStep = 15f;
                float scaleStep = 0.05f;
                bool changed = false;

                // Move forward/back relative to player's look but projected onto preview's up (wall plane)
                if (input.WasJustPressed(BUTTON.FORWARD))
                {
                    Vector3 forward = Vector3.ProjectOnPlane(player.eyes.HeadForward(), st.PreviewEntity.transform.up).normalized;
                    st.PreviewEntity.transform.position += forward * moveStep;
                    changed = true;
                }
                if (input.WasJustPressed(BUTTON.BACKWARD))
                {
                    Vector3 back = Vector3.ProjectOnPlane(player.eyes.HeadForward(), st.PreviewEntity.transform.up).normalized;
                    st.PreviewEntity.transform.position -= back * moveStep;
                    changed = true;
                }
                if (input.WasJustPressed(BUTTON.LEFT))
                {
                    Vector3 left = -player.eyes.HeadRight();
                    left = Vector3.ProjectOnPlane(left, st.PreviewEntity.transform.up).normalized;
                    st.PreviewEntity.transform.position += left * moveStep;
                    changed = true;
                }
                if (input.WasJustPressed(BUTTON.RIGHT))
                {
                    Vector3 right = player.eyes.HeadRight();
                    right = Vector3.ProjectOnPlane(right, st.PreviewEntity.transform.up).normalized;
                    st.PreviewEntity.transform.position += right * moveStep;
                    changed = true;
                }

                if (input.WasJustPressed(BUTTON.RELOAD)) // R
                {
                    st.PreviewEntity.transform.rotation *= Quaternion.Euler(0f, rotStep, 0f);
                    changed = true;
                }
                if (input.WasJustPressed(BUTTON.DUCK)) // F
                {
                    st.PreviewEntity.transform.rotation *= Quaternion.Euler(0f, -rotStep, 0f);
                    changed = true;
                }

                if (input.WasJustPressed(BUTTON.JUMP)) // SPACE
                {
                    var s = st.PreviewEntity.transform.localScale;
                    s += new Vector3(scaleStep, scaleStep, scaleStep);
                    st.PreviewEntity.transform.localScale = s;
                    changed = true;
                }
                if (input.WasJustPressed(BUTTON.SPRINT)) // SHIFT
                {
                    var s = st.PreviewEntity.transform.localScale;
                    s -= new Vector3(scaleStep, scaleStep, scaleStep);
                    if (s.x < 0.01f) s = Vector3.one * 0.01f;
                    st.PreviewEntity.transform.localScale = s;
                    changed = true;
                }

                if (changed)
                {
                    st.PreviewEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }

                // While editing, block normal buy checks for this player
                return;
            }

            // Normal player buy interaction
            if (input.WasJustPressed(BUTTON.USE))
            {
                RaycastHit hit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 3f)) return;

                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    if (Vector3.Distance(hit.point, p.Position) <= 1.2f)
                    {
                        TryPurchase(player, p);
                        return;
                    }
                }
            }
        }

        #endregion

        #region Purchase logic

        private void TryPurchase(BasePlayer player, WallPoint p)
        {
            var def = ItemManager.FindItemDefinition(p.Shortname);
            if (def == null)
            {
                player.ChatMessage($"Error: item '{p.Shortname}' no longer exists.");
                Puts($"WallBuyAdvanced: Missing item definition '{p.Shortname}' for a wallbuy at {p.Position}");
                return;
            }

            var scrap = ItemManager.FindItemDefinition("scrap");
            if (scrap == null)
            {
                player.ChatMessage("Server does not have scrap defined!");
                return;
            }

            int scrapId = scrap.itemid;
            int bal = player.inventory.GetAmount(scrapId);
            if (bal < p.Cost)
            {
                player.ChatMessage($"✖ Not enough scrap. Need {p.Cost}.");
                return;
            }

            player.inventory.Take(null, scrapId, p.Cost);

            var item = ItemManager.Create(def, p.Amount);
            if (item == null)
            {
                player.ChatMessage("Failed to create item; refunding scrap.");
                player.inventory.GiveItem(ItemManager.CreateByName("scrap", p.Cost));
                return;
            }

            if (!player.inventory.GiveItem(item))
            {
                item.Drop(player.transform.position + Vector3.up, Vector3.zero);
                player.ChatMessage("Inventory full — item dropped at your feet.");
            }
            else
            {
                player.ChatMessage($"✔ Purchased {p.Shortname} for {p.Cost} scrap!");
            }
        }

        #endregion

        #region Cleanup / Unload

        private void Unload()
        {
            foreach (var r in runtimes)
            {
                if (r != null && r.VisualEntity != null && !r.VisualEntity.IsDestroyed) r.VisualEntity.Kill();
            }
            runtimes.Clear();

            foreach (var kv in editors)
            {
                var st = kv.Value;
                if (st != null)
                {
                    if (st.PreviewEntity != null && !st.PreviewEntity.IsDestroyed) st.PreviewEntity.Kill();
                }
            }
            editors.Clear();
        }

        #endregion
    }
}
