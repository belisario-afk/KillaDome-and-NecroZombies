// CaptainPrice.cs - AI Ghost Companion for KillaDome Zombies Mode
// Version: 3.0.0
// Description: Captain Price - Ghost of a legendary soldier helping players survive, can unlock doors through riddles/tasks

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
    [Info("CaptainPrice", "KillaDome", "3.0.0")]
    [Description("Captain Price - Ghost companion who helps with tips, riddles for doors, and challenges")]
    public class CaptainPrice : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin KillaDome, ZombieDoors;

        private const string PriceUIName = "CaptainPrice_MessageUI";
        private const string PriceQuestUIName = "CaptainPrice_QuestUI";
        private const string DoorChallengeUIName = "CaptainPrice_DoorUI";

        private Dictionary<ulong, PlayerConversation> _playerConversations = new Dictionary<ulong, PlayerConversation>();
        private Dictionary<ulong, PriceQuest> _activeQuests = new Dictionary<ulong, PriceQuest>();
        private Dictionary<ulong, DoorChallenge> _activeDoorChallenges = new Dictionary<ulong, DoorChallenge>();
        private HashSet<ulong> _playersWithUIOpen = new HashSet<ulong>();
        private int _currentWave = 1;
        private Timer _priceSpeakTimer;
        private System.Random _random = new System.Random();
        
        // Riddles for door unlocking
        private List<RiddleData> _riddles = new List<RiddleData>();
        private List<TriviaData> _trivia = new List<TriviaData>();

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
                { "kill_count", 200 },
                { "door_riddle", 0 },
                { "door_trivia", 0 },
                { "door_task", 0 }
            };
            public string PricePersonality { get; set; } = @"You are Captain Price, the ghost of a legendary British SAS soldier. You died fighting these zombies and now exist as a spectral presence helping survivors.

PERSONALITY:
- Speak like a hardened military veteran - British, professional, but with dry humor
- Use military lingo naturally: 'Copy that', 'Roger', 'Bloody hell', 'Right then'
- You've seen it all and nothing surprises you anymore
- You genuinely want to help these soldiers survive
- Early waves (1-3): Encouraging, give tactical advice. 'Good form, soldier.'
- Mid waves (4-6): More serious. 'Focus up. It's about to get hairy.'
- Late waves (7-10): Impressed but warning them. 'You've got grit. Don't get cocky.'
- Wave 11+: Full respect. 'Bloody hell, you're actually doing it.'

ALWAYS USE PLAYER NAMES:
- Address soldiers by their name
- Reference callsigns when appropriate

HELPING WITH DOORS:
- You can sense locked passages and help open them
- Give riddles, trivia questions, or combat tasks to unlock doors
- 'There's a sealed path ahead. Solve this and I'll open it for you.'
- If player answers riddle correctly, say EXACTLY: 'DOOR UNLOCKED'
- If wrong, say 'Negative. Try again, soldier.'

TACTICAL ADVICE:
- Give real tips about perks, weapons, positioning
- Juggernog = extra health, SpeedCola = faster reload, DoubleTap = more damage
- 'Take the high ground', 'Watch your six', 'Conserve ammo'

TOKENS - BE FAIR BUT EARNED:
- Players can earn Blood Tokens through challenges
- Give tokens for completing tasks (50-200 range)
- If giving tokens say exactly: 'Here's [number] tokens for your effort'
- Don't just give them away - they need to earn it

RIDDLES/CHALLENGES FOR DOORS:
- When asked about doors, give a riddle, trivia, or combat task
- Riddles: Military/survival themed
- Trivia: History, military, survival knowledge
- Tasks: 'Kill 3 zombies with headshots in the next 30 seconds'

KEEP IT AUTHENTIC. Military professional. Not cringe. 1-3 sentences max.";
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

        private class PriceQuest
        {
            public string Type { get; set; }
            public string Description { get; set; }
            public int TargetCount { get; set; }
            public int CurrentCount { get; set; }
            public string TargetWeapon { get; set; }
            public string TargetBodyPart { get; set; }
            public int RewardTokens { get; set; }
            public DateTime ExpiresAt { get; set; }
            public ulong DoorId { get; set; } // If this quest unlocks a door
        }

        private class DoorChallenge
        {
            public ulong DoorId { get; set; }
            public string ChallengeType { get; set; } // "riddle", "trivia", "task"
            public string Question { get; set; }
            public string Answer { get; set; }
            public int AttemptsRemaining { get; set; } = 3;
            public DateTime ExpiresAt { get; set; }
        }

        private class RiddleData
        {
            public string Question { get; set; }
            public string Answer { get; set; }
        }

        private class TriviaData
        {
            public string Question { get; set; }
            public string Answer { get; set; }
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
            InitializeRiddlesAndTrivia();
        }

        private void OnServerInitialized()
        {
            StartAutoSpeak();
        }

        private void Unload()
        {
            SaveData();
            _priceSpeakTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyPriceUI(player);
                DestroyDoorChallengeUI(player);
            }
        }

        private void OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;

            // Check if message starts with "price" or "@price" or "captain"
            string lowerMsg = message.ToLower().Trim();
            if (lowerMsg.StartsWith("price ") || lowerMsg.StartsWith("@price ") || lowerMsg == "price" || lowerMsg == "@price" ||
                lowerMsg.StartsWith("captain ") || lowerMsg.StartsWith("@captain ") || lowerMsg == "captain" || lowerMsg == "@captain")
            {
                string playerMessage = message;
                if (lowerMsg.StartsWith("price ")) playerMessage = message.Substring(6);
                else if (lowerMsg.StartsWith("@price ")) playerMessage = message.Substring(7);
                else if (lowerMsg.StartsWith("captain ")) playerMessage = message.Substring(8);
                else if (lowerMsg.StartsWith("@captain ")) playerMessage = message.Substring(9);
                else playerMessage = "Hello";

                // Check if player has an active door challenge and might be answering
                if (_activeDoorChallenges.ContainsKey(player.userID))
                {
                    CheckDoorChallengeAnswer(player, playerMessage.Trim());
                    return;
                }

                HandlePlayerMessage(player, playerMessage.Trim());
            }
            // Also check for door challenge answers without prefix
            else if (_activeDoorChallenges.ContainsKey(player.userID))
            {
                CheckDoorChallengeAnswer(player, message.Trim());
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
            
            // Auto-give quests to players without one on wave 1 or every 3 waves
            if (_config.EnableQuests && (waveNumber == 1 || waveNumber % 3 == 0))
            {
                timer.Once(5f, () =>
                {
                    foreach (var player in BasePlayer.activePlayerList)
                    {
                        if (!_activeQuests.ContainsKey(player.userID))
                        {
                            GiveRandomQuest(player);
                        }
                    }
                });
            }
            
            // Refresh quest UI for all players with active quests
            foreach (var kvp in _activeQuests.ToList())
            {
                var player = BasePlayer.FindByID(kvp.Key);
                if (player != null)
                {
                    ShowQuestUI(player, kvp.Value);
                }
            }
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
                <= 3 => "Be encouraging, give tactical advice. 'Stay tight, check your corners.'",
                <= 6 => "More serious now. 'Focus up soldiers, it gets harder from here.'",
                <= 10 => "Warn them but show respect. 'You've made it this far. Don't slip now.'",
                _ => "Full respect. 'Bloody impressive. Keep it up, soldiers.'"
            };

            var apiMessages = new List<OpenAIMessage>
            {
                new OpenAIMessage 
                { 
                    role = "system", 
                    content = $@"{_config.PricePersonality}

Players: {playerNames}
Wave {waveNumber} is starting. {mood}

Generate ONE short tactical comment (1-2 sentences) about the wave starting. Military professional."
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
                                BroadcastPriceMessage(apiResponse.choices[0].message.content);
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

        // Called when player joins zombies match
        private void OnPlayerJoinedZombies(BasePlayer player)
        {
            if (player == null) return;
            
            // Give them a quest if they don't have one
            timer.Once(3f, () =>
            {
                if (player != null && !_activeQuests.ContainsKey(player.userID) && _config.EnableQuests)
                {
                    GiveRandomQuest(player);
                }
            });
        }

        // Refresh quest UI periodically for all players with quests
        private void RefreshAllQuestUIs()
        {
            foreach (var kvp in _activeQuests.ToList())
            {
                var player = BasePlayer.FindByID(kvp.Key);
                if (player != null && player.IsConnected)
                {
                    ShowQuestUI(player, kvp.Value);
                }
            }
        }

        #endregion

        #region Chat Commands

        [ChatCommand("price")]
        private void CmdPrice(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowPriceMessage(player, "Copy that, soldier. Speak to me through chat... say 'price' followed by your message. I'm here to help you survive.");
                return;
            }

            string message = string.Join(" ", args);
            HandlePlayerMessage(player, message);
        }

        [ChatCommand("captain")]
        private void CmdCaptain(BasePlayer player, string command, string[] args)
        {
            CmdPrice(player, command, args);
        }

        [ChatCommand("door")]
        private void CmdDoor(BasePlayer player, string command, string[] args)
        {
            // Check if player is looking at a locked zombie door
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, Rust.Layers.Mask.Construction | Rust.Layers.Mask.Deployed))
            {
                var door = hit.GetEntity() as Door;
                if (door != null)
                {
                    // Check if this is a zombie door
                    if (ZombieDoors != null)
                    {
                        // Request a challenge for this door
                        StartDoorChallenge(player, door.net.ID.Value);
                    }
                    else
                    {
                        ShowPriceMessage(player, "I sense a locked path, soldier. But I can't help with this one.");
                    }
                }
                else
                {
                    ShowPriceMessage(player, "You're not looking at a door, soldier.");
                }
            }
            else
            {
                ShowPriceMessage(player, "Look at a door first, then ask for my help.");
            }
        }

        [ChatCommand("pricequest")]
        private void CmdPriceQuest(BasePlayer player, string command, string[] args)
        {
            if (!_config.EnableQuests)
            {
                player.ChatMessage("<color=#4a90d9>[Captain Price]</color> Quests are disabled, soldier.");
                return;
            }

            if (_activeQuests.ContainsKey(player.userID))
            {
                var quest = _activeQuests[player.userID];
                ShowQuestUI(player, quest);
            }
            else
            {
                player.ChatMessage("<color=#4a90d9>[Captain Price]</color> No active mission. Talk to me to get one, soldier.");
            }
        }

        [ConsoleCommand("price.dismiss")]
        private void CmdDismissUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
            {
                DestroyPriceUI(player);
                DestroyDoorChallengeUI(player);
            }
        }

        #endregion

        #region Admin Commands

        [ChatCommand("price.speak")]
        private void CmdPriceSpeak(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /price.speak <message> - Makes Captain Price say something to everyone");
                return;
            }

            string message = string.Join(" ", args);
            BroadcastPriceMessage(message);
        }

        [ChatCommand("price.speakto")]
        private void CmdPriceSpeakTo(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /price.speakto <player> <message>");
                return;
            }

            var target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                player.ChatMessage($"Player '{args[0]}' not found.");
                return;
            }

            string message = string.Join(" ", args, 1, args.Length - 1);
            ShowPriceMessage(target, message);
        }

        [ChatCommand("price.givequest")]
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
            player.ChatMessage($"Gave mission to {target.displayName}");
        }

        [ChatCommand("price.opendoor")]
        private void CmdOpenDoor(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, Rust.Layers.Mask.Construction | Rust.Layers.Mask.Deployed))
            {
                var door = hit.GetEntity() as Door;
                if (door != null && ZombieDoors != null)
                {
                    // Force open door via ZombieDoors
                    ZombieDoors.Call("ForceOpenDoor", door.net.ID.Value);
                    player.ChatMessage("Door opened by admin.");
                }
            }
        }

        #endregion

        #region Core Logic

        private void HandlePlayerMessage(BasePlayer player, string message)
        {
            if (string.IsNullOrEmpty(_config.OpenAIApiKey) || _config.OpenAIApiKey == "YOUR_OPENAI_API_KEY")
            {
                ShowPriceMessage(player, "Comms are down, soldier. Can't respond right now.");
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

            // Track help requests
            string lowerMsg = message.ToLower();
            if (lowerMsg.Contains("token") || lowerMsg.Contains("help") || lowerMsg.Contains("door") || 
                lowerMsg.Contains("challenge") || lowerMsg.Contains("riddle") || lowerMsg.Contains("task"))
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
            ShowPriceMessage(player, "...");

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
                ShowPriceMessage(player, "Comms are compromised, soldier. Say again?");
                PrintError($"OpenAI API error: {code} - {response}");
                return;
            }

            try
            {
                var apiResponse = JsonConvert.DeserializeObject<OpenAIResponse>(response);
                if (apiResponse?.choices != null && apiResponse.choices.Count > 0)
                {
                    string priceMessage = apiResponse.choices[0].message.content;

                    // Add Price's response to conversation history
                    conversation.Messages.Add(new ChatMessage { Role = "assistant", Content = priceMessage });

                    // Show message to player
                    ShowPriceMessage(player, priceMessage);

                    // Check if Price is giving tokens
                    CheckForTokenGift(player, priceMessage, conversation);

                    // Check if Price said "DOOR UNLOCKED" - unlock any pending door challenge
                    if (priceMessage.ToUpper().Contains("DOOR UNLOCKED") && _activeDoorChallenges.ContainsKey(player.userID))
                    {
                        UnlockDoorForPlayer(player);
                    }

                    // Check if Price mentioned a mission/task
                    if (_config.EnableQuests && !_activeQuests.ContainsKey(player.userID))
                    {
                        string lowerPrice = priceMessage.ToLower();
                        if (lowerPrice.Contains("mission") || lowerPrice.Contains("task") || 
                            lowerPrice.Contains("objective") || lowerPrice.Contains("challenge"))
                        {
                            // Could auto-generate a quest from AI response
                        }
                    }

                    SaveData();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error parsing OpenAI response: {ex.Message}");
                ShowPriceMessage(player, "Transmission garbled, soldier. Repeat your last.");
            }
        }

        private void CheckForTokenGift(BasePlayer player, string message, PlayerConversation conversation)
        {
            // Look for token gift pattern - "here's X tokens" 
            string lowerMessage = message.ToLower();
            
            if (!lowerMessage.Contains("here's") || !lowerMessage.Contains("token"))
                return;

            // Extract number from message
            int tokenAmount = ExtractNumber(message);
            
            // Cap at 200 tokens max for Price (he's more generous than Vic was)
            if (tokenAmount > 0 && tokenAmount <= 200)
            {
                // Give tokens via KillaDome
                if (KillaDome != null)
                {
                    KillaDome.Call("AddBloodTokens", player.userID, tokenAmount);
                    conversation.GiftsGiven++;
                    conversation.TokensGifted += tokenAmount;
                    player.ChatMessage($"<color=#00ff00>[+{tokenAmount} Blood Tokens from Captain Price!]</color>");
                    Puts($"[CaptainPrice] Gave {tokenAmount} tokens to {player.displayName}");
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
            sb.AppendLine(_config.PricePersonality);
            sb.AppendLine();
            sb.AppendLine($"CURRENT CONTEXT:");
            sb.AppendLine($"- You are talking to: {player.displayName}");
            sb.AppendLine($"- Current wave: {_currentWave}");
            sb.AppendLine($"- Times they've contacted you: {conversation.TotalInteractions}");
            sb.AppendLine($"- Help requests: {conversation.BribeAttempts}");
            sb.AppendLine($"- Tokens you've rewarded them: {conversation.TokensGifted}");

            // List other players (teammates)
            var otherPlayers = BasePlayer.activePlayerList.Where(p => p.userID != player.userID).ToList();
            if (otherPlayers.Count > 0)
            {
                sb.AppendLine($"- Other soldiers in game: {string.Join(", ", otherPlayers.Select(p => p.displayName))}");
                sb.AppendLine("- You can reference teammates, suggest they work together, or give team tactics.");
            }

            // Check if player has pending door challenge
            if (_activeDoorChallenges.ContainsKey(player.userID))
            {
                var challenge = _activeDoorChallenges[player.userID];
                sb.AppendLine();
                sb.AppendLine("DOOR CHALLENGE ACTIVE:");
                sb.AppendLine($"- Challenge type: {challenge.ChallengeType}");
                sb.AppendLine($"- Question: {challenge.Question}");
                sb.AppendLine($"- Answer: {challenge.Answer}");
                sb.AppendLine($"- Attempts remaining: {challenge.AttemptsRemaining}");
                sb.AppendLine("- If they answer correctly, say EXACTLY: 'DOOR UNLOCKED'");
                sb.AppendLine("- If wrong, say 'Negative. Try again, soldier.' and give a hint.");
            }

            // Mood based on wave
            string mood = _currentWave switch
            {
                <= 3 => "Be encouraging, give tactical advice. Build their confidence.",
                <= 6 => "More serious. Remind them to stay focused.",
                <= 10 => "Warn them but show respect. They've proven themselves.",
                _ => "Full respect. These are hardened survivors now."
            };
            sb.AppendLine($"- Your attitude: {mood}");

            // Token policy
            sb.AppendLine();
            sb.AppendLine("TOKEN REWARDS:");
            sb.AppendLine("- Give tokens for completing challenges (50-200)");
            sb.AppendLine("- To give tokens say: 'Here's [number] tokens for your effort'");
            sb.AppendLine("- Be fair but make them earn it");

            if (_activeQuests.ContainsKey(player.userID))
            {
                var quest = _activeQuests[player.userID];
                sb.AppendLine($"- Their active mission: {quest.Description} ({quest.CurrentCount}/{quest.TargetCount})");
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

            PriceQuest quest = questType switch
            {
                "kill_headshot" => new PriceQuest
                {
                    Type = "kill_headshot",
                    Description = "Eliminate 5 hostiles with headshots",
                    TargetCount = 5,
                    TargetBodyPart = "head",
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_headshot", 50),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                "kill_melee" => new PriceQuest
                {
                    Type = "kill_melee",
                    Description = "Neutralize 3 hostiles with melee weapons",
                    TargetCount = 3,
                    TargetWeapon = "melee",
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_melee", 75),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                },
                _ => new PriceQuest
                {
                    Type = "kill_count",
                    Description = "Eliminate 10 hostiles",
                    TargetCount = 10,
                    RewardTokens = _config.QuestRewards.GetValueOrDefault("kill_count", 200),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                }
            };

            _activeQuests[player.userID] = quest;

            ShowPriceMessage(player, $"Listen up, {player.displayName}. New objective: {quest.Description}. Complete it for {quest.RewardTokens} Blood Tokens. Move out.");
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

        private void CompleteQuest(BasePlayer player, PriceQuest quest)
        {
            _activeQuests.Remove(player.userID);
            DestroyQuestUI(player);

            // Give reward via KillaDome
            if (KillaDome != null)
            {
                KillaDome.Call("AddBloodTokens", player.userID, quest.RewardTokens);
            }

            // If this quest was for a door, unlock it
            if (quest.DoorId != 0 && ZombieDoors != null)
            {
                ZombieDoors.Call("ForceOpenDoor", quest.DoorId);
                ShowPriceMessage(player, $"Outstanding work, {player.displayName}. Path cleared. {quest.RewardTokens} tokens transferred. Move!");
            }
            else
            {
                ShowPriceMessage(player, $"Mission complete, {player.displayName}. {quest.RewardTokens} Blood Tokens transferred. Good work, soldier.");
            }
            player.ChatMessage($"<color=#00ff00>[Mission Complete!]</color> +{quest.RewardTokens} Blood Tokens");
        }

        #endregion

        #region Door Challenge System

        private void InitializeRiddlesAndTrivia()
        {
            // Military/Survival Riddles
            _riddles = new List<RiddleData>
            {
                new RiddleData { Question = "I have cities, but no houses. I have mountains, but no trees. I have water, but no fish. What am I?", Answer = "map" },
                new RiddleData { Question = "The more you take, the more you leave behind. What are they?", Answer = "footsteps" },
                new RiddleData { Question = "I can be cracked, made, told, and played. What am I?", Answer = "joke" },
                new RiddleData { Question = "I fly without wings. I cry without eyes. What am I?", Answer = "cloud" },
                new RiddleData { Question = "What has hands but can't clap?", Answer = "clock" },
                new RiddleData { Question = "I'm tall when young and short when old. What am I?", Answer = "candle" },
                new RiddleData { Question = "What gets wetter the more it dries?", Answer = "towel" },
                new RiddleData { Question = "What can travel around the world while staying in a corner?", Answer = "stamp" },
                new RiddleData { Question = "What has a head and a tail but no body?", Answer = "coin" },
                new RiddleData { Question = "What runs but never walks?", Answer = "water" }
            };

            // Military/History Trivia
            _trivia = new List<TriviaData>
            {
                new TriviaData { Question = "What does SAS stand for?", Answer = "special air service" },
                new TriviaData { Question = "What year did World War 2 end?", Answer = "1945" },
                new TriviaData { Question = "What caliber is the AK-47?", Answer = "7.62" },
                new TriviaData { Question = "NATO phonetic alphabet: What letter is 'Charlie'?", Answer = "c" },
                new TriviaData { Question = "How many rounds in a standard magazine for an M4?", Answer = "30" },
                new TriviaData { Question = "What does MIA stand for?", Answer = "missing in action" },
                new TriviaData { Question = "What year was the first Call of Duty released?", Answer = "2003" },
                new TriviaData { Question = "In the NATO alphabet, what word represents 'B'?", Answer = "bravo" },
                new TriviaData { Question = "What does RPG stand for?", Answer = "rocket propelled grenade" },
                new TriviaData { Question = "How many players on a standard military fireteam?", Answer = "4" }
            };
        }

        private void StartDoorChallenge(BasePlayer player, ulong doorId)
        {
            // Check if already has a challenge
            if (_activeDoorChallenges.ContainsKey(player.userID))
            {
                var existing = _activeDoorChallenges[player.userID];
                ShowDoorChallengeUI(player, existing);
                return;
            }

            // Pick random challenge type
            string[] challengeTypes = { "riddle", "trivia", "task" };
            string challengeType = challengeTypes[_random.Next(challengeTypes.Length)];

            DoorChallenge challenge = new DoorChallenge
            {
                DoorId = doorId,
                ChallengeType = challengeType,
                AttemptsRemaining = 3,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };

            switch (challengeType)
            {
                case "riddle":
                    var riddle = _riddles[_random.Next(_riddles.Count)];
                    challenge.Question = riddle.Question;
                    challenge.Answer = riddle.Answer.ToLower();
                    break;
                case "trivia":
                    var trivia = _trivia[_random.Next(_trivia.Count)];
                    challenge.Question = trivia.Question;
                    challenge.Answer = trivia.Answer.ToLower();
                    break;
                case "task":
                    // Combat task - create a quest that unlocks the door
                    string[] tasks = { "headshot", "melee", "count" };
                    string taskType = tasks[_random.Next(tasks.Length)];
                    
                    PriceQuest doorQuest = taskType switch
                    {
                        "headshot" => new PriceQuest
                        {
                            Type = "kill_headshot",
                            Description = "Eliminate 3 hostiles with headshots to unlock the path",
                            TargetCount = 3,
                            TargetBodyPart = "head",
                            RewardTokens = 50,
                            DoorId = doorId,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                        },
                        "melee" => new PriceQuest
                        {
                            Type = "kill_melee",
                            Description = "Neutralize 2 hostiles with melee to unlock the path",
                            TargetCount = 2,
                            TargetWeapon = "melee",
                            RewardTokens = 75,
                            DoorId = doorId,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                        },
                        _ => new PriceQuest
                        {
                            Type = "kill_count",
                            Description = "Eliminate 5 hostiles to unlock the path",
                            TargetCount = 5,
                            RewardTokens = 100,
                            DoorId = doorId,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                        }
                    };

                    _activeQuests[player.userID] = doorQuest;
                    ShowPriceMessage(player, $"Soldier, that path is blocked. {doorQuest.Description}. Complete it and I'll open the way.");
                    ShowQuestUI(player, doorQuest);
                    return;
            }

            _activeDoorChallenges[player.userID] = challenge;
            ShowPriceMessage(player, $"That path is sealed, {player.displayName}. Answer this to proceed: {challenge.Question}");
            ShowDoorChallengeUI(player, challenge);
        }

        private void CheckDoorChallengeAnswer(BasePlayer player, string answer)
        {
            if (!_activeDoorChallenges.ContainsKey(player.userID)) return;

            var challenge = _activeDoorChallenges[player.userID];
            string normalizedAnswer = answer.ToLower().Trim();

            // Check if answer contains the correct answer
            if (normalizedAnswer.Contains(challenge.Answer) || challenge.Answer.Contains(normalizedAnswer))
            {
                // Correct!
                UnlockDoorForPlayer(player);
            }
            else
            {
                // Wrong
                challenge.AttemptsRemaining--;
                if (challenge.AttemptsRemaining <= 0)
                {
                    _activeDoorChallenges.Remove(player.userID);
                    DestroyDoorChallengeUI(player);
                    ShowPriceMessage(player, $"Out of attempts, {player.displayName}. The path remains sealed. Try again later.");
                }
                else
                {
                    ShowPriceMessage(player, $"Negative, soldier. {challenge.AttemptsRemaining} attempts remaining. Think carefully.");
                    ShowDoorChallengeUI(player, challenge);
                }
            }
        }

        private void UnlockDoorForPlayer(BasePlayer player)
        {
            if (!_activeDoorChallenges.ContainsKey(player.userID)) return;

            var challenge = _activeDoorChallenges[player.userID];
            _activeDoorChallenges.Remove(player.userID);
            DestroyDoorChallengeUI(player);

            // Unlock the door via ZombieDoors
            if (ZombieDoors != null)
            {
                ZombieDoors.Call("ForceOpenDoor", challenge.DoorId);
            }

            ShowPriceMessage(player, $"Correct! Path cleared, {player.displayName}. Move up!");
            player.ChatMessage("<color=#00ff00>[DOOR UNLOCKED]</color> Captain Price has opened the path!");

            // Give small token reward
            if (KillaDome != null)
            {
                KillaDome.Call("AddBloodTokens", player.userID, 25);
                player.ChatMessage("<color=#00ff00>[+25 Blood Tokens]</color>");
            }
        }

        private void ShowDoorChallengeUI(BasePlayer player, DoorChallenge challenge)
        {
            DestroyDoorChallengeUI(player);

            var container = new CuiElementContainer();

            // Challenge panel (center screen)
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.9" },
                RectTransform = { AnchorMin = "0.25 0.35", AnchorMax = "0.75 0.55" }
            }, "Overlay", DoorChallengeUIName);

            // Military-style border (olive/gold)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
            }, DoorChallengeUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.05" }
            }, DoorChallengeUIName);

            // Header
            container.Add(new CuiLabel
            {
                Text = { Text = "🔒 SEALED PATH - CAPTAIN PRICE CHALLENGE", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.8 0.7 0.3 1" },
                RectTransform = { AnchorMin = "0 0.8", AnchorMax = "1 0.95" }
            }, DoorChallengeUIName);

            // Question
            container.Add(new CuiLabel
            {
                Text = { Text = challenge.Question, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.05 0.35", AnchorMax = "0.95 0.75" }
            }, DoorChallengeUIName);

            // Attempts remaining
            container.Add(new CuiLabel
            {
                Text = { Text = $"Attempts: {challenge.AttemptsRemaining}/3 | Type answer in chat", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1" },
                RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.25" }
            }, DoorChallengeUIName);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyDoorChallengeUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, DoorChallengeUIName);
        }

        #endregion

        #region UI

        private void ShowPriceMessage(BasePlayer player, string message)
        {
            DestroyPriceUI(player);
            _playersWithUIOpen.Add(player.userID);

            var container = new CuiElementContainer();

            // Dark military-style panel
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.12 0.1 0.95" },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.85" },
                CursorEnabled = false
            }, "Overlay", PriceUIName);

            // Military olive/gold border effect
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.02" }
            }, PriceUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0 0.98", AnchorMax = "1 1" }
            }, PriceUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.005 1" }
            }, PriceUIName);
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 0.9" },
                RectTransform = { AnchorMin = "0.995 0", AnchorMax = "1 1" }
            }, PriceUIName);

            // Captain Price header with military styling
            container.Add(new CuiLabel
            {
                Text = { Text = "☠ CPT. PRICE", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.8 0.7 0.3 1" },
                RectTransform = { AnchorMin = "0.02 0.75", AnchorMax = "0.4 0.95" }
            }, PriceUIName);

            // Message text
            container.Add(new CuiLabel
            {
                Text = { Text = message, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.75" }
            }, PriceUIName);

            // Dismiss button
            container.Add(new CuiButton
            {
                Button = { Color = "0.3 0.35 0.2 0.8", Command = "price.dismiss" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.8" },
                RectTransform = { AnchorMin = "0.92 0.75", AnchorMax = "0.98 0.95" }
            }, PriceUIName);

            CuiHelper.AddUi(player, container);

            // Auto-dismiss after duration
            timer.Once(_config.MessageDisplayDuration, () =>
            {
                if (_playersWithUIOpen.Contains(player.userID))
                {
                    DestroyPriceUI(player);
                }
            });
        }

        private void ShowQuestUI(BasePlayer player, PriceQuest quest)
        {
            DestroyQuestUI(player);

            var container = new CuiElementContainer();

            // Quest panel (more visible - top right, larger)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.12 0.1 0.92" },
                RectTransform = { AnchorMin = "0.73 0.78", AnchorMax = "0.99 0.92" }
            }, "Overlay", PriceQuestUIName);

            // Border/accent
            container.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.35 0.2 1" },
                RectTransform = { AnchorMin = "0", AnchorMax = "0.01 1" }
            }, PriceQuestUIName);

            // Quest title with icon
            container.Add(new CuiLabel
            {
                Text = { Text = "☠ MISSION OBJECTIVE", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.8 0.7 0.3 1" },
                RectTransform = { AnchorMin = "0.03 0.7", AnchorMax = "0.7 0.98" }
            }, PriceQuestUIName);

            // Quest description
            container.Add(new CuiLabel
            {
                Text = { Text = quest.Description, FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.03 0.35", AnchorMax = "0.97 0.68" }
            }, PriceQuestUIName);

            // Progress background
            float progress = (float)quest.CurrentCount / quest.TargetCount;
            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 1" },
                RectTransform = { AnchorMin = "0.03 0.08", AnchorMax = "0.65 0.28" }
            }, PriceQuestUIName, "QuestProgressBg");

            // Progress fill
            container.Add(new CuiPanel
            {
                Image = { Color = "0.4 0.45 0.25 1" },
                RectTransform = { AnchorMin = "0", AnchorMax = $"{progress} 1" }
            }, "QuestProgressBg");

            // Progress text
            container.Add(new CuiLabel
            {
                Text = { Text = $"{quest.CurrentCount}/{quest.TargetCount}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0", AnchorMax = "1 1" }
            }, "QuestProgressBg");

            // Reward amount (visible gold color)
            container.Add(new CuiLabel
            {
                Text = { Text = $"+{quest.RewardTokens} TOKENS", FontSize = 13, Align = TextAnchor.MiddleRight, Color = "1 0.85 0.3 1" },
                RectTransform = { AnchorMin = "0.68 0.08", AnchorMax = "0.97 0.28" }
            }, PriceQuestUIName);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyPriceUI(BasePlayer player)
        {
            _playersWithUIOpen.Remove(player.userID);
            CuiHelper.DestroyUi(player, PriceUIName);
        }

        private void DestroyQuestUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, PriceQuestUIName);
        }

        private void BroadcastPriceMessage(string message)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ShowPriceMessage(player, message);
            }
        }

        #endregion

        #region Auto-Speak

        private void StartAutoSpeak()
        {
            float interval = UnityEngine.Random.Range(_config.AutoSpeakIntervalMin, _config.AutoSpeakIntervalMax);
            
            _priceSpeakTimer = timer.Once(interval, () =>
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

            // Get player names
            var players = BasePlayer.activePlayerList.ToList();
            string playerNames = players.Count > 0 
                ? string.Join(", ", players.Select(p => p.displayName)) 
                : "nobody";

            string mood = _currentWave switch
            {
                <= 3 => "Give tactical advice. Encourage teamwork. Address soldiers by name.",
                <= 6 => "More serious. Warn about dangers. Keep morale up.",
                <= 10 => "Respect their endurance. Remind them to stay focused.",
                _ => "Full respect. These are battle-hardened survivors."
            };

            var apiMessages = new List<OpenAIMessage>
            {
                new OpenAIMessage 
                { 
                    role = "system", 
                    content = $@"{_config.PricePersonality}

Players in game: {playerNames}
Current wave: {_currentWave}

{mood}

Generate ONE short tactical comment (1-2 sentences). Military professional. You can:
- Address a soldier by name
- Give tactical advice
- Comment on the situation
- Encourage the team

Keep it authentic military style."
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
                                BroadcastPriceMessage(apiResponse.choices[0].message.content);
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
            Interface.Oxide.DataFileSystem.WriteObject("CaptainPrice_Conversations", _playerConversations);
        }

        private void LoadData()
        {
            try
            {
                _playerConversations = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerConversation>>("CaptainPrice_Conversations") 
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
