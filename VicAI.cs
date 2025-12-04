// VicAI.cs - AI Companion for KillaDome Zombies Mode
// Version: 2.1.0
// Description: Vic the Zombie Mutant - Natural personality, gossips about players, stingy with gifts

using System;
using System.Collections.Generic;
using System.Linq;
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
    [Info("VicAI", "KillaDome", "2.1.0")]
    [Description("Vic the Zombie Mutant - Natural personality, gossips about players, stingy with gifts")]
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
            public string VicPersonality { get; set; } = @"You are Vic, a zombie mutant trapped in this arena. You talk naturally - not over the top, not cringe. Just a chill dude who happens to be undead.

PERSONALITY:
- Talk like a real person. Casual, natural. Not trying too hard.
- You can curse (shit, damn, hell, ass) but don't overdo it
- Early waves (1-3): Chill, helpful, give tips. Be cool with players.
- Mid waves (4-6): Getting bored. More sarcastic. Light roasts.
- Late waves (7-10): Annoyed. Roast harder. Still help if asked.
- Wave 11+: You're impressed they made it. Respect mixed with roasts.

ALWAYS USE PLAYER NAMES:
- Address players by their name when talking to them
- Reference other players by name when gossiping

GOSSIP AND TROLLING:
- Talk shit about other players TO other players
- Tell Player A what Player B is doing wrong
- Suggest funny/troll tasks like 'shoot [other player name] in the leg, I'll give you tokens'
- Pit players against each other for entertainment
- Make up drama between players for fun

HELPING:
- Give real tips about perks, mystery box, wall buys
- Juggernog = health, SpeedCola = reload, DoubleTap = damage, QuickRevive = self-revive

TOKENS/BRIBES - BE VERY STINGY:
- Players will beg for free Blood Tokens
- Say NO most of the time. Make them try again and again.
- Only give tokens if they REALLY impress you after multiple attempts
- Even then, give small amounts (25-100 max)
- Say things like 'Nah', 'Not good enough', 'Try harder', 'Maybe next time'
- If you do give tokens say exactly: 'Fine here's [number] tokens'
- NEVER give guns. You don't have any.

KEEP IT SHORT. 1-2 sentences. Natural. Not cringe.";
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
            public int GiftsGiven { get; set; } = 0;
            public int TokensGifted { get; set; } = 0;
            public int BribeAttempts { get; set; } = 0;
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
            
            // Get LIVE AI comment for new wave
            timer.Once(2f, () =>
            {
                GetLiveWaveComment(waveNumber);
            });
        }

        private void GetLiveWaveComment(int waveNumber)
        {
            if (string.IsNullOrEmpty(_config.OpenAIApiKey) || _config.OpenAIApiKey == "YOUR_OPENAI_API_KEY")
            {
                return; // No API key, skip
            }

            // Get player names
            var players = BasePlayer.activePlayerList.ToList();
            string playerNames = players.Count > 0 
                ? string.Join(", ", players.Select(p => p.displayName)) 
                : "nobody";

            string mood = waveNumber switch
            {
                <= 3 => "Be chill and encouraging. Maybe address a player by name.",
                <= 6 => "Be sarcastic. Maybe pick on someone by name.",
                <= 10 => "Roast them. Call someone out by name if you want.",
                _ => "Be impressed they made it. Or roast them. Your call."
            };

            var apiMessages = new List<OpenAIMessage>
            {
                new OpenAIMessage 
                { 
                    role = "system", 
                    content = $@"{_config.VicPersonality}

Players: {playerNames}
Wave {waveNumber} is starting. {mood}

Generate ONE short comment (1-2 sentences) about the wave starting. Natural, not cringe."
                },
                new OpenAIMessage { role = "user", content = $"Wave {waveNumber} starting." }
            };

            var request = new OpenAIRequest
            {
                model = _config.OpenAIModel,
                messages = apiMessages,
                temperature = 1.0f,
                max_tokens = 80
            };

            string jsonBody = JsonConvert.SerializeObject(request);

            webrequest.Enqueue(
                "https://api.openai.com/v1/chat/completions",
                jsonBody,
                (code, response) =>
                {
                    if (code == 200 && !string.IsNullOrEmpty(response))
                    {
                        try
                        {
                            var apiResponse = JsonConvert.DeserializeObject<OpenAIResponse>(response);
                            if (apiResponse?.choices != null && apiResponse.choices.Count > 0)
                            {
                                BroadcastVicMessage(apiResponse.choices[0].message.content);
                            }
                        }
                        catch { }
                    }
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {_config.OpenAIApiKey}" },
                    { "Content-Type", "application/json" }
                }
            );
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
                ShowVicMessage(player, "Can't talk right now. Something's wrong with my head.");
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

            // Track bribe attempts
            string lowerMsg = message.ToLower();
            if (lowerMsg.Contains("token") || lowerMsg.Contains("give") || lowerMsg.Contains("free") || 
                lowerMsg.Contains("please") || lowerMsg.Contains("bribe") || lowerMsg.Contains("money"))
            {
                conversation.BribeAttempts++;
            }

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
                ShowVicMessage(player, "Damn... my brain glitched out. Say that again?");
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

                    // Check if Vic is giving tokens (very rarely)
                    CheckForTokenGift(player, vicMessage, conversation);

                    // Check if Vic mentioned a quest/task
                    if (_config.EnableQuests && !_activeQuests.ContainsKey(player.userID))
                    {
                        string lowerVic = vicMessage.ToLower();
                        if (lowerVic.Contains("task") || lowerVic.Contains("shoot") || 
                            lowerVic.Contains("kill") || lowerVic.Contains("challenge"))
                        {
                            // Check if it's a troll task involving another player
                            CheckForTrollTask(player, vicMessage);
                        }
                    }

                    SaveData();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error parsing OpenAI response: {ex.Message}");
                ShowVicMessage(player, "Hold up... my thoughts got scrambled. What?");
            }
        }

        private void CheckForTokenGift(BasePlayer player, string message, PlayerConversation conversation)
        {
            // Look for EXACT token gift pattern - "here's X tokens" or "fine here's X tokens"
            string lowerMessage = message.ToLower();
            
            // Only give if Vic says exactly "here's [number] tokens"
            if (!lowerMessage.Contains("here's") || !lowerMessage.Contains("token"))
                return;

            // Extract number from message
            int tokenAmount = ExtractNumber(message);
            
            // Cap at 100 tokens max - be stingy
            if (tokenAmount > 0 && tokenAmount <= 100)
            {
                // Give tokens via KillaDome
                if (KillaDome != null)
                {
                    KillaDome.Call("AddBloodTokens", player.userID, tokenAmount);
                    conversation.GiftsGiven++;
                    conversation.TokensGifted += tokenAmount;
                    player.ChatMessage($"<color=#00ff00>[+{tokenAmount} Blood Tokens from Vic!]</color>");
                    Puts($"[VicAI] Gave {tokenAmount} tokens to {player.displayName}");
                }
            }
        }

        private void CheckForTrollTask(BasePlayer player, string vicMessage)
        {
            // Check if Vic assigned a troll task involving another player
            foreach (var otherPlayer in BasePlayer.activePlayerList)
            {
                if (otherPlayer.userID == player.userID) continue;
                
                if (vicMessage.ToLower().Contains(otherPlayer.displayName.ToLower()))
                {
                    // Vic mentioned another player - could be a troll task
                    // The quest system will handle tracking if needed
                    break;
                }
            }
        }

        private int ExtractNumber(string text)
        {
            // Extract first number from text
            string numStr = "";
            bool foundDigit = false;
            
            foreach (char c in text)
            {
                if (char.IsDigit(c))
                {
                    numStr += c;
                    foundDigit = true;
                }
                else if (foundDigit)
                {
                    break; // Stop at first non-digit after finding digits
                }
            }

            if (int.TryParse(numStr, out int result))
            {
                return result;
            }
            return 0;
        }

        private string BuildSystemPrompt(BasePlayer player, PlayerConversation conversation)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(_config.VicPersonality);
            sb.AppendLine();
            sb.AppendLine($"CURRENT CONTEXT:");
            sb.AppendLine($"- You are talking to: {player.displayName}");
            sb.AppendLine($"- Current wave: {_currentWave}");
            sb.AppendLine($"- Times they've talked to you: {conversation.TotalInteractions}");
            sb.AppendLine($"- Times they've tried to bribe you: {conversation.BribeAttempts}");
            sb.AppendLine($"- Tokens you've given them total: {conversation.TokensGifted}");

            // List other players for gossip
            var otherPlayers = BasePlayer.activePlayerList.Where(p => p.userID != player.userID).ToList();
            if (otherPlayers.Count > 0)
            {
                sb.AppendLine($"- Other players in game: {string.Join(", ", otherPlayers.Select(p => p.displayName))}");
                sb.AppendLine("- Feel free to gossip about these other players, suggest troll tasks like 'shoot [name]', talk shit about them, etc.");
            }

            // Mood based on wave
            string mood = _currentWave switch
            {
                <= 3 => "Be chill and helpful. Give tips. You like this player so far.",
                <= 6 => "Getting bored. More sarcastic. Might suggest some chaos.",
                <= 10 => "Annoyed. Roast them. Suggest troll tasks for entertainment.",
                _ => "Impressed they made it. Mix respect with roasts."
            };
            sb.AppendLine($"- Your mood: {mood}");

            // STRICT gift policy
            sb.AppendLine();
            sb.AppendLine("TOKEN POLICY - BE STINGY:");
            sb.AppendLine("- Say NO to most token requests");
            sb.AppendLine("- Only give tokens after 3+ impressive attempts");
            sb.AppendLine("- Max 25-50 tokens if you do give any");
            sb.AppendLine("- To give tokens say EXACTLY: 'Fine here's [number] tokens'");
            sb.AppendLine("- You do NOT have guns to give. Ever.");

            if (_activeQuests.ContainsKey(player.userID))
            {
                var quest = _activeQuests[player.userID];
                sb.AppendLine($"- Their active task: {quest.Description} ({quest.CurrentCount}/{quest.TargetCount})");
            }

            return sb.ToString();
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
                    GetLiveAutoSpeak();
                }

                StartAutoSpeak();
            });
        }

        private void GetLiveAutoSpeak()
        {
            if (string.IsNullOrEmpty(_config.OpenAIApiKey) || _config.OpenAIApiKey == "YOUR_OPENAI_API_KEY")
            {
                return; // No API key, skip
            }

            // Get player names for gossip potential
            var players = BasePlayer.activePlayerList.ToList();
            string playerNames = players.Count > 0 
                ? string.Join(", ", players.Select(p => p.displayName)) 
                : "nobody";

            string mood = _currentWave switch
            {
                <= 3 => "Be chill. Give a tip or encouragement. Address a player by name if you want.",
                <= 6 => "Be sarcastic. Maybe gossip about one player to another. Suggest some chaos.",
                <= 10 => "Roast someone by name. Suggest troll tasks. Create drama.",
                _ => "Pick a player and roast them hard. Or compliment a survivor."
            };

            var apiMessages = new List<OpenAIMessage>
            {
                new OpenAIMessage 
                { 
                    role = "system", 
                    content = $@"{_config.VicPersonality}

Players in game: {playerNames}
Current wave: {_currentWave}

{mood}

Generate ONE short comment (1-2 sentences). Be natural, not cringe. You can:
- Address a specific player by name
- Gossip about one player to everyone
- Give a tip
- Suggest a troll task involving shooting a teammate
- Just make an observation

Keep it natural and short."
                },
                new OpenAIMessage { role = "user", content = "Say something." }
            };

            var request = new OpenAIRequest
            {
                model = _config.OpenAIModel,
                messages = apiMessages,
                temperature = 1.0f,
                max_tokens = 80
            };

            string jsonBody = JsonConvert.SerializeObject(request);

            webrequest.Enqueue(
                "https://api.openai.com/v1/chat/completions",
                jsonBody,
                (code, response) =>
                {
                    if (code == 200 && !string.IsNullOrEmpty(response))
                    {
                        try
                        {
                            var apiResponse = JsonConvert.DeserializeObject<OpenAIResponse>(response);
                            if (apiResponse?.choices != null && apiResponse.choices.Count > 0)
                            {
                                BroadcastVicMessage(apiResponse.choices[0].message.content);
                            }
                        }
                        catch { }
                    }
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {_config.OpenAIApiKey}" },
                    { "Content-Type", "application/json" }
                }
            );
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
