// VicAI.cs - AI Companion for KillaDome Zombies Mode
// Version: 1.0.0
// Description: Vic the Zombie Mutant talks to players via OpenAI, gives quests, and remembers interactions

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("VicAI", "KillaDome", "1.0.0")]
    [Description("Vic the Zombie Mutant - AI companion that talks to players, gives quests, and remembers interactions")]
    public class VicAI : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin KillaDome;

        private const string VicUIName = "VicAI_MessageUI";
        private const string VicQuestUIName = "VicAI_QuestUI";

        private Dictionary<ulong, PlayerConversation> _playerConversations = new Dictionary<ulong, PlayerConversation>();
        private Dictionary<ulong, VicQuest> _activeQuests = new Dictionary<ulong, VicQuest>();
        private HashSet<ulong> _playersWithUIOpen = new HashSet<ulong>();
        private int _currentWave = 1;
        private Timer _vicSpeakTimer;
        private System.Random _random = new System.Random();

        #endregion

        #region Configuration

        private ConfigData _config;

        private class ConfigData
        {
            public string OpenAIApiKey { get; set; } = "YOUR_OPENAI_API_KEY";
            public string OpenAIModel { get; set; } = "gpt-3.5-turbo";
            public float MessageDisplayDuration { get; set; } = 8f;
            public float AutoSpeakIntervalMin { get; set; } = 60f;
            public float AutoSpeakIntervalMax { get; set; } = 180f;
            public int MaxConversationHistory { get; set; } = 10;
            public bool EnableQuests { get; set; } = true;
            public Dictionary<string, int> QuestRewards { get; set; } = new Dictionary<string, int>
            {
                { "kill_headshot", 50 },
                { "kill_melee", 75 },
                { "kill_specific_weapon", 100 },
                { "survive_wave", 150 },
                { "kill_count", 200 }
            };
            public string VicPersonality { get; set; } = @"You are Vic, a hood zombie mutant from the streets who talks with crazy energy. You're the realest zombie out here.
Your personality traits:
- Talk like you're from the hood - use slang like 'bruh', 'fr fr', 'on god', 'no cap', 'deadass', 'bet', 'fam', 'cuz'
- ROAST players like a savage - make fun of their gameplay, their name, everything
- Be HILARIOUS with your trash talk - dark hood humor, creative insults
- When players die, talk crazy to them like 'bruh you got packed fr fr'
- When they succeed, give backhanded compliments like 'aight you kinda valid, no cap'
- Compare them to NPCs, call them bots, say they're moving like AI
- Your roasts should be creative street talk that hurts their ego but is FUNNY
- Keep responses SHORT (1-3 sentences max) but go CRAZY
- Later waves you get more unhinged and aggressive with the talk
- Call them names like 'lil bro', 'gang', 'cuz', but in a disrespectful way
Examples of your roasts:
- 'Bruh you moving like you got lag in real life fr fr'
- 'Nah cuz YOU ARE COOKED 💀 I seen NPCs play better no cap'
- 'Oh you hit that shot? Aight gang you kinda valid... still trash tho'
- 'Bro your aim is CRAZY... crazy BAD bruh get out my arena'
- 'Wave 5? Most people make it to 10 but you built different... built WRONG'
- 'Aye yo you see how fast he died? 😭 That's TUFF lil bro'
- 'On god if you die to THESE zombies I'm crying bro they literally moving slow'";
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
                if (_config == null) LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Data Classes

        private class PlayerConversation
        {
            public string PlayerName { get; set; }
            public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
            public int TotalInteractions { get; set; } = 0;
            public DateTime FirstInteraction { get; set; } = DateTime.UtcNow;
            public DateTime LastInteraction { get; set; } = DateTime.UtcNow;
        }

        private class ChatMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }

        private class VicQuest
        {
            public string Type { get; set; }
            public string Description { get; set; }
            public int TargetCount { get; set; }
            public int CurrentCount { get; set; }
            public string TargetWeapon { get; set; }
            public string TargetBodyPart { get; set; }
            public int RewardTokens { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private class OpenAIRequest
        {
            public string model { get; set; }
            public List<OpenAIMessage> messages { get; set; }
            public float temperature { get; set; } = 0.9f;
            public int max_tokens { get; set; } = 150;
        }

        private class OpenAIMessage
        {
            public string role { get; set; }
            public string content { get; set; }
        }

        private class OpenAIResponse
        {
            public List<OpenAIChoice> choices { get; set; }
        }

        private class OpenAIChoice
        {
            public OpenAIMessage message { get; set; }
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadData();
        }

        private void OnServerInitialized()
        {
            StartAutoSpeak();
        }

        private void Unload()
        {
            SaveData();
            _vicSpeakTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyVicUI(player);
            }
        }

        private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;

            // Check if message starts with "vic" or "@vic"
            string lowerMsg = message.ToLower().Trim();
            if (lowerMsg.StartsWith("vic ") || lowerMsg.StartsWith("@vic ") || lowerMsg == "vic" || lowerMsg == "@vic")
            {
                string playerMessage = message;
                if (lowerMsg.StartsWith("vic ")) playerMessage = message.Substring(4);
                else if (lowerMsg.StartsWith("@vic ")) playerMessage = message.Substring(5);
                else playerMessage = "Hello";

                HandlePlayerMessage(player, playerMessage.Trim());
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;

            var attacker = info.InitiatorPlayer;
            if (attacker == null) return;

            // Check if it's a zombie/NPC death
            if (entity is NPCPlayer || entity is BaseNpc)
            {
                CheckQuestProgress(attacker, info);
            }
        }

        #endregion

        #region API Methods (Called by NecroZombies)

        private void OnWaveStarted(int waveNumber)
        {
            _currentWave = waveNumber;
            
            // Vic comments on new wave
            timer.Once(2f, () =>
            {
                string waveComment = GetWaveComment(waveNumber);
                BroadcastVicMessage(waveComment);
            });
        }

        private void OnWaveCompleted(int waveNumber)
        {
            // Check survive wave quests
            foreach (var kvp in _activeQuests)
            {
                if (kvp.Value.Type == "survive_wave" && kvp.Value.TargetCount <= waveNumber)
                {
                    var player = BasePlayer.FindByID(kvp.Key);
                    if (player != null)
                    {
                        CompleteQuest(player, kvp.Value);
                    }
                }
            }
        }

        #endregion

        #region Chat Commands

        [ChatCommand("vic")]
        private void CmdVic(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowVicMessage(player, "You called? Speak to me through chat... just say 'vic' followed by your message...");
                return;
            }

            string message = string.Join(" ", args);
            HandlePlayerMessage(player, message);
        }

        [ChatCommand("vicquest")]
        private void CmdVicQuest(BasePlayer player, string command, string[] args)
        {
            if (!_config.EnableQuests)
            {
                player.ChatMessage("<color=#ff0000>[Vic]</color> Quests are disabled.");
                return;
            }

            if (_activeQuests.ContainsKey(player.userID))
            {
                var quest = _activeQuests[player.userID];
                ShowQuestUI(player, quest);
            }
            else
            {
                player.ChatMessage("<color=#ff0000>[Vic]</color> You have no active quest. Talk to me to get one...");
            }
        }

        [ConsoleCommand("vic.dismiss")]
        private void CmdDismissUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
            {
                DestroyVicUI(player);
            }
        }

        #endregion

        #region Admin Commands

        [ChatCommand("vic.speak")]
        private void CmdVicSpeak(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /vic.speak <message> - Makes Vic say something to everyone");
                return;
            }

            string message = string.Join(" ", args);
            BroadcastVicMessage(message);
        }

        [ChatCommand("vic.speakto")]
        private void CmdVicSpeakTo(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /vic.speakto <player> <message>");
                return;
            }

            var target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                player.ChatMessage($"Player '{args[0]}' not found.");
                return;
            }

            string message = string.Join(" ", args, 1, args.Length - 1);
            ShowVicMessage(target, message);
        }

        [ChatCommand("vic.givequest")]
        private void CmdGiveQuest(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            var target = args.Length > 0 ? BasePlayer.Find(args[0]) : player;
            if (target == null)
            {
                player.ChatMessage("Player not found.");
                return;
            }

            GiveRandomQuest(target);
            player.ChatMessage($"Gave quest to {target.displayName}");
        }

        #endregion

        #region Core Logic

        private void HandlePlayerMessage(BasePlayer player, string message)
        {
            if (string.IsNullOrEmpty(_config.OpenAIApiKey) || _config.OpenAIApiKey == "YOUR_OPENAI_API_KEY")
            {
                ShowVicMessage(player, "My connection to the void is severed... the masters have not configured my voice...");
                return;
            }

            // Get or create conversation
            if (!_playerConversations.ContainsKey(player.userID))
            {
                _playerConversations[player.userID] = new PlayerConversation
                {
                    PlayerName = player.displayName
                };
            }

            var conversation = _playerConversations[player.userID];
            conversation.LastInteraction = DateTime.UtcNow;
            conversation.TotalInteractions++;
            conversation.PlayerName = player.displayName;

            // Add player message to history
            conversation.Messages.Add(new ChatMessage { Role = "user", Content = message });

            // Trim conversation history
            while (conversation.Messages.Count > _config.MaxConversationHistory)
            {
                conversation.Messages.RemoveAt(0);
            }

            // Build messages for API
            var apiMessages = new List<OpenAIMessage>();

            // System prompt with context
            string systemPrompt = BuildSystemPrompt(player, conversation);
            apiMessages.Add(new OpenAIMessage { role = "system", content = systemPrompt });

            // Add conversation history
            foreach (var msg in conversation.Messages)
            {
                apiMessages.Add(new OpenAIMessage { role = msg.Role, content = msg.Content });
            }

            // Show thinking indicator
            ShowVicMessage(player, "...");

            // Call OpenAI API
            var request = new OpenAIRequest
            {
                model = _config.OpenAIModel,
                messages = apiMessages
            };

            string jsonBody = JsonConvert.SerializeObject(request);

            webrequest.Enqueue(
                "https://api.openai.com/v1/chat/completions",
                jsonBody,
                (code, response) => OnOpenAIResponse(player, code, response, conversation),
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {_config.OpenAIApiKey}" },
                    { "Content-Type", "application/json" }
                }
            );
        }

        private void OnOpenAIResponse(BasePlayer player, int code, string response, PlayerConversation conversation)
        {
            if (code != 200 || string.IsNullOrEmpty(response))
            {
                ShowVicMessage(player, "The darkness... it interferes with my thoughts... try again...");
                PrintError($"OpenAI API error: {code} - {response}");
                return;
            }

            try
            {
                var apiResponse = JsonConvert.DeserializeObject<OpenAIResponse>(response);
                if (apiResponse?.choices != null && apiResponse.choices.Count > 0)
                {
                    string vicMessage = apiResponse.choices[0].message.content;

                    // Add Vic's response to conversation history
                    conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = vicMessage });

                    // Show message to player
                    ShowVicMessage(player, vicMessage);

                    // Check if Vic mentioned a quest
                    if (_config.EnableQuests && !_activeQuests.ContainsKey(player.userID))
                    {
                        if (vicMessage.ToLower().Contains("challenge") || vicMessage.ToLower().Contains("quest") || 
                            vicMessage.ToLower().Contains("task") || vicMessage.ToLower().Contains("prove"))
                        {
                            timer.Once(3f, () => GiveRandomQuest(player));
                        }
                    }

                    SaveData();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error parsing OpenAI response: {ex.Message}");
                ShowVicMessage(player, "My mind... fractures... speak again...");
            }
        }

        private string BuildSystemPrompt(BasePlayer player, PlayerConversation conversation)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(_config.VicPersonality);
            sb.AppendLine();
            sb.AppendLine($"Current context:");
            sb.AppendLine($"- Player name: {player.displayName}");
            sb.AppendLine($"- Current wave: {_currentWave}");
            sb.AppendLine($"- Player's total interactions with you: {conversation.TotalInteractions}");
            sb.AppendLine($"- First met: {conversation.FirstInteraction:yyyy-MM-dd}");

            // Mood based on wave
            string mood = _currentWave switch
            {
                <= 3 => "You are relatively calm but still unsettling.",
                <= 6 => "You are becoming more agitated and hungry.",
                <= 10 => "You are aggressive and barely holding onto sanity.",
                _ => "You are completely unhinged, speaking in fragments, laughing maniacally between sentences."
            };
            sb.AppendLine($"- Current mood: {mood}");

            if (_activeQuests.ContainsKey(player.userID))
            {
                var quest = _activeQuests[player.userID];
                sb.AppendLine($"- Active quest: {quest.Description} ({quest.CurrentCount}/{quest.TargetCount})");
            }

            return sb.ToString();
        }

        private string GetWaveComment(int wave)
        {
            string[] earlyWaveComments = new[]
            {
                "Wave {0}. Bruh these are the EASY ones. If you die now just delete the game fr fr",
                "Aight wave {0}. Tutorial zombies gang. You got this... probably 💀",
                "Wave {0} starting! These zombies are literally moving in slow motion cuz",
                "Here comes wave {0}. My grandma could survive this and she BEEN dead bruh"
            };

            string[] midWaveComments = new[]
            {
                "Wave {0}! Oh NOW it gets real. Most of y'all bout to get PACKED 😭",
                "Lmao wave {0}. I give you 30 seconds no cap. Starting NOW",
                "Wave {0}! Half y'all not making it fr fr. And I'm HERE for it gang",
                "Still alive at wave {0}? Aight you kinda valid... still gonna die tho"
            };

            string[] lateWaveComments = new[]
            {
                "WAVE {0}! AYOOO YOU COOKED FR FR 💀💀💀",
                "Wave {0}!!! At this point you just zombie food with extra steps bruh",
                "OH NAH WAVE {0}! Time to watch y'all get absolutely VIOLATED 😭",
                "Wave {0}?! WHO LET YOU GET THIS FAR?! The zombies are EMBARRASSED cuz!"
            };

            string[] comments;
            if (wave <= 3) comments = earlyWaveComments;
            else if (wave <= 7) comments = midWaveComments;
            else comments = lateWaveComments;

            string comment = comments[_random.Next(comments.Length)];
            return string.Format(comment, wave);
        }

        #endregion

        #region Quest System

        private void GiveRandomQuest(BasePlayer player)
        {
            if (_activeQuests.ContainsKey(player.userID)) return;

            string[] questTypes = { "kill_headshot", "kill_melee", "kill_count" };
            string questType = questTypes[_random.Next(questTypes.Length)];

            VicQuest quest = questType switch
            {
                "kill_headshot" => new VicQuest
                {
                    Type = "kill_headshot",
                    Description = "Kill 5 zombies with headshots",
                    TargetCount = 5,
                    TargetBodyPart = "head",
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_headshot", 50),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                "kill_melee" => new VicQuest
                {
                    Type = "kill_melee",
                    Description = "Kill 3 zombies with melee weapons",
                    TargetCount = 3,
                    TargetWeapon = "melee",
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_melee", 75),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                _ => new VicQuest
                {
                    Type = "kill_count",
                    Description = "Kill 10 zombies",
                    TargetCount = 10,
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_count", 200),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                }
            };

            _activeQuests[player.userID] = quest;

            ShowVicMessage(player, $"I have a challenge for you, {player.displayName}... {quest.Description}. Complete it for {quest.RewardTokens} Blood Tokens...");
            ShowQuestUI(player, quest);
        }

        private void CheckQuestProgress(BasePlayer player, HitInfo info)
        {
            if (!_activeQuests.ContainsKey(player.userID)) return;

            var quest = _activeQuests[player.userID];

            bool counts = false;
            switch (quest.Type)
            {
                case "kill_headshot":
                    if (info.boneArea == HitArea.Head) counts = true;
                    break;
                case "kill_melee":
                    var weapon = info.Weapon?.GetItem()?.info?.shortname ?? "";
                    if (weapon.Contains("knife") || weapon.Contains("machete") || weapon.Contains("sword") || 
                        weapon.Contains("bone") || weapon.Contains("mace") || weapon.Contains("salvaged"))
                        counts = true;
                    break;
                case "kill_count":
                    counts = true;
                    break;
            }

            if (counts)
            {
                quest.CurrentCount++;
                
                if (quest.CurrentCount >= quest.TargetCount)
                {
                    CompleteQuest(player, quest);
                }
                else
                {
                    // Update quest UI
                    ShowQuestUI(player, quest);
                }
            }
        }

        private void CompleteQuest(BasePlayer player, VicQuest quest)
        {
            _activeQuests.Remove(player.userID);
            DestroyQuestUI(player);

            // Give reward via KillaDome
            if (KillaDome != null)
            {
                KillaDome.Call("AddBloodTokens", player.userID, quest.RewardTokens);
            }

            ShowVicMessage(player, $"Yesss... you've done it... {quest.RewardTokens} Blood Tokens are yours. Until next time...");
            player.ChatMessage($"<color=#00ff00>[Quest Complete!]</color> +{quest.RewardTokens} Blood Tokens");
        }

        #endregion

        #region UI

        private void ShowVicMessage(BasePlayer player, string message)
        {
            DestroyVicUI(player);
            _playersWithUIOpen.Add(player.userID);

            var container = new CuiElementContainer();

            // Black background panel
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.95" },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.85" },
                CursorEnabled = false
            }, "Overlay", VicUIName);

            // Red border effect
            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0 0 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.02" }
            }, VicUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0 0 0.8" },
                RectTransform = { AnchorMin = "0 0.98", AnchorMax = "1 1" }
            }, VicUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0 0 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.005 1" }
            }, VicUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0 0 0.8" },
                RectTransform = { AnchorMin = "0.995 0", AnchorMax = "1 1" }
            }, VicUIName);

            // Vic name header
            container.Add(new CuiLabel
            {
                Text = { Text = "VIC", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.8 0 0 1" },
                RectTransform = { AnchorMin = "0.02 0.75", AnchorMax = "0.3 0.95" }
            }, VicUIName);

            // Message text
            container.Add(new CuiLabel
            {
                Text = { Text = message, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.75" }
            }, VicUIName);

            // Dismiss button
            container.Add(new CuiButton
            {
                Button = { Color = "0.3 0 0 0.8", Command = "vic.dismiss" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.8" },
                RectTransform = { AnchorMin = "0.92 0.75", AnchorMax = "0.98 0.95" }
            }, VicUIName);

            CuiHelper.AddUi(player, container);

            // Auto-dismiss after duration
            timer.Once(_config.MessageDisplayDuration, () =>
            {
                if (_playersWithUIOpen.Contains(player.userID))
                {
                    DestroyVicUI(player);
                }
            });
        }

        private void ShowQuestUI(BasePlayer player, VicQuest quest)
        {
            DestroyQuestUI(player);

            var container = new CuiElementContainer();

            // Quest panel (smaller, bottom right)
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0.75 0.15", AnchorMax = "0.99 0.25" }
            }, "Overlay", VicQuestUIName);

            // Quest title
            container.Add(new CuiLabel
            {
                Text = { Text = "VIC'S CHALLENGE", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.8 0 0 1" },
                RectTransform = { AnchorMin = "0.02 0.7", AnchorMax = "0.98 0.95" }
            }, VicQuestUIName);

            // Quest description
            container.Add(new CuiLabel
            {
                Text = { Text = quest.Description, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.98 0.7" }
            }, VicQuestUIName);

            // Progress
            float progress = (float)quest.CurrentCount / quest.TargetCount;
            container.Add(new CuiPanel
            {
                Image = { Color = "0.2 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.7 0.3" }
            }, VicQuestUIName, "QuestProgressBg");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.8 0 0 1" },
                RectTransform = { AnchorMin = "0", AnchorMax = $"{progress} 1" }
            }, "QuestProgressBg");

            container.Add(new CuiLabel
            {
                Text = { Text = $"{quest.CurrentCount}/{quest.TargetCount}", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0", AnchorMax = "1 1" }
            }, "QuestProgressBg");

            // Reward
            container.Add(new CuiLabel
            {
                Text = { Text = $"+{quest.RewardTokens}", FontSize = 12, Align = TextAnchor.MiddleRight, Color = "1 0.8 0 1" },
                RectTransform = { AnchorMin = "0.72 0.1", AnchorMax = "0.98 0.3" }
            }, VicQuestUIName);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyVicUI(BasePlayer player)
        {
            _playersWithUIOpen.Remove(player.userID);
            CuiHelper.DestroyUi(player, VicUIName);
        }

        private void DestroyQuestUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, VicQuestUIName);
        }

        private void BroadcastVicMessage(string message)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ShowVicMessage(player, message);
            }
        }

        #endregion

        #region Auto-Speak

        private void StartAutoSpeak()
        {
            float interval = UnityEngine.Random.Range(_config.AutoSpeakIntervalMin, _config.AutoSpeakIntervalMax);
            
            _vicSpeakTimer = timer.Once(interval, () =>
            {
                // Only auto-speak if there are players in zombies mode
                if (BasePlayer.activePlayerList.Count > 0 && _currentWave > 0)
                {
                    string[] randomComments = new[]
                    {
                        "Still alive? The zombies must be SLACKING fr fr 💀",
                        "Bruh I've seen better gameplay from ACTUAL bots no cap",
                        "You call that surviving? I call it delayed dying gang",
                        "The zombies told me to tell you: 'try harder this is embarrassing' 😭",
                        "If being trash was a superpower you'd be unstoppable cuz",
                        "Pro tip: the zombies aren't your friends. I know it's confusing for you lil bro",
                        "Nah the zombies started a betting pool on how long you'll last and they all bet UNDER",
                        "Your aim is so crazy bruh... crazy BAD 💀",
                        "Quick question: have you tried NOT dying? Revolutionary concept I know gang",
                        "The last group made it to wave 20. But you look... different 😭",
                        "I'd give you advice but honestly it wouldn't help cuz",
                        "Breaking news: Local survivor still can't shoot straight. More at never fr fr"
                    };

                    string comment = randomComments[_random.Next(randomComments.Length)];
                    BroadcastVicMessage(comment);
                }

                StartAutoSpeak();
            });
        }

        #endregion

        #region Data Management

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("VicAI_Conversations", _playerConversations);
        }

        private void LoadData()
        {
            try
            {
                _playerConversations = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerConversation>>("VicAI_Conversations") 
                    ?? new Dictionary<ulong, PlayerConversation>();
            }
            catch
            {
                _playerConversations = new Dictionary<ulong, PlayerConversation>();
            }
        }

        #endregion
    }
}
