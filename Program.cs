using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NotEnoughChats
{
    // ═══════════════════════════════════════════════════════════════════
    //  PROGRAM — entry point, auth screen, command loop
    // ═══════════════════════════════════════════════════════════════════

    internal static class Program
    {
        private const string ApiKey = "AIzaSyBu91iHvbnj1xMjRdk9WnCFqMA2ZMv58g8";
        private const string DatabaseUrl = "https://pychat-7a057-default-rtdb.firebaseio.com";

        private static ChatClient client;
        internal static Account CurrentAccount;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "NotEnoughChats";
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible = true;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                CurrentAccount = AuthScreen();
                if (CurrentAccount == null) return;

                Console.WriteLine();
                UI.Status("Connecting to server...", UI.Warning);

                FirebaseClient firebase = new FirebaseClient(ApiKey, DatabaseUrl);
                firebase.SignInAnonymously();

                UI.Status("Connected to server", UI.Success);
                firebase.RegisterUser(CurrentAccount.Username, CurrentAccount.DisplayName);
                UI.Status("Signed in as " + CurrentAccount.Username, UI.Success);

                client = new ChatClient(CurrentAccount, firebase);
                client.Start();
                CommandLoop(client);
                client.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                UI.Status("Fatal error", UI.Danger);
                Console.WriteLine("  " + ex.Message);
                Console.WriteLine();
                Console.WriteLine("  Press Enter to exit...");
                Console.ReadLine();
            }
        }

        // ─── AUTH ──────────────────────────────────────────────────────

        private static Account AuthScreen()
        {
            while (true)
            {
                WelcomeScreen();

                Console.WriteLine("  [1]  Sign in");
                Console.WriteLine("  [2]  Create account");
                Console.WriteLine("  [3]  Continue as guest");
                Console.WriteLine("  [0]  Exit");
                Console.WriteLine();

                UI.Prompt("  > ");
                string choice = (Console.ReadLine() ?? "").Trim();

                if (choice == "0") return null;
                if (choice == "1") { var a = TryLogin(); if (a != null) return a; }
                else if (choice == "2") { var a = TryRegister(); if (a != null) return a; }
                else if (choice == "3")
                {
                    string guest = PromptGuestUsername();
                    if (guest != null)
                        return new Account { Username = guest, DisplayName = guest, IsGuest = true };
                }
            }
        }

        private static Account TryLogin()
        {
            Console.WriteLine();
            UI.Header("SIGN IN", UI.Blurple);
            Console.WriteLine();

            UI.Write("  Username: ", UI.Muted);
            string username = ValidateUsername(Console.ReadLine());
            if (username == null) { UI.Status("Invalid username format.", UI.Danger); return null; }

            UI.Write("  Password: ", UI.Muted);
            string password = ReadMaskedPassword();
            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                UI.Status("Password must be at least 4 characters.", UI.Danger);
                return null;
            }

            Account stored = AccountStore.Find(username);
            if (stored == null) { UI.Status("Account not found. Create one first.", UI.Danger); return null; }

            string hash = HashPassword(password, stored.Salt);
            if (hash != stored.PasswordHash) { UI.Status("Incorrect password.", UI.Danger); return null; }

            UI.Status("Welcome back, " + stored.DisplayName, UI.Success);
            return stored;
        }

        private static Account TryRegister()
        {
            Console.WriteLine();
            UI.Header("CREATE ACCOUNT", UI.Blurple);
            Console.WriteLine();

            UI.Write("  Username: ", UI.Muted);
            string username = ValidateUsername(Console.ReadLine());
            if (username == null)
            {
                UI.Status("Invalid username. Use 1-24 chars: a-z, 0-9, _, -.", UI.Danger);
                return null;
            }
            if (AccountStore.Find(username) != null)
            {
                UI.Status("Username already registered on this device.", UI.Danger);
                return null;
            }

            UI.Write("  Display name (optional): ", UI.Muted);
            string display = Console.ReadLine();
            display = string.IsNullOrWhiteSpace(display) ? username : display.Trim();
            if (display.Length > 32) display = display.Substring(0, 32);

            UI.Write("  Password: ", UI.Muted);
            string password = ReadMaskedPassword();
            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                UI.Status("Password must be at least 4 characters.", UI.Danger);
                return null;
            }

            UI.Write("  Confirm password: ", UI.Muted);
            string confirm = ReadMaskedPassword();
            if (password != confirm) { UI.Status("Passwords do not match.", UI.Danger); return null; }

            string salt = GenerateSalt();
            string hash = HashPassword(password, salt);

            Account acc = new Account
            {
                Username = username,
                DisplayName = display,
                PasswordHash = hash,
                Salt = salt,
                IsGuest = false,
                CreatedAt = DateTime.UtcNow,
                Status = "online",
                NotificationsEnabled = true,
                BlockedUids = new List<string>()
            };

            AccountStore.Save(acc);
            UI.Status("Account created. Signing you in...", UI.Success);
            return acc;
        }

        private static string PromptGuestUsername()
        {
            Console.WriteLine();
            UI.Header("GUEST MODE", UI.Blurple);
            Console.WriteLine();

            UI.Write("  Choose a guest name: ", UI.Muted);
            string username = ValidateUsername(Console.ReadLine());
            if (username == null) { UI.Status("Invalid username.", UI.Danger); return null; }

            UI.Status("Continuing as guest: " + username, UI.Warning);
            return username;
        }

        private static string ReadMaskedPassword()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                    continue;
                }
                if (!char.IsControl(key.KeyChar)) { sb.Append(key.KeyChar); Console.Write("*"); }
            }
            return sb.ToString();
        }

        internal static string ValidateUsername(string username)
        {
            if (username == null) return null;
            username = username.Trim();
            if (username.Length < 1 || username.Length > 24) return null;

            const string allowed = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
            foreach (char c in username)
                if (allowed.IndexOf(c) < 0) return null;

            return username;
        }

        private static string GenerateSalt()
        {
            byte[] salt = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }

        internal static string HashPassword(string password, string salt)
        {
            using (var sha = SHA256.Create())
            {
                byte[] input = Encoding.UTF8.GetBytes(salt + ":" + password);
                byte[] hash = sha.ComputeHash(input);
                return Convert.ToBase64String(hash);
            }
        }

        private static void WelcomeScreen()
        {
            Console.Clear();
            Console.WriteLine();
            Console.WriteLine();

            UI.Centered("╭────────────────────────────────────────────────────╮", UI.Blurple);
            UI.Centered("│                                                    │", UI.Blurple);
            UI.Centered("│          N O T   E N O U G H   C H A T S           │", UI.Blurple);
            UI.Centered("│                                                    │", UI.Blurple);
            UI.Centered("│                T E R M I N A L                     │", UI.Muted);
            UI.Centered("│               M E S S E N G E R                    │", UI.Muted);
            UI.Centered("│                                                    │", UI.Blurple);
            UI.Centered("╰────────────────────────────────────────────────────╯", UI.Blurple);

            Console.WriteLine();
            Console.WriteLine();
        }

        // ─── COMMAND LOOP ─────────────────────────────────────────────

        private static void CommandLoop(ChatClient chat)
        {
            while (chat.Running)
            {
                try
                {
                    UI.Prompt("> ");
                    string input = Console.ReadLine();
                    if (input == null) break;

                    input = input.Trim();
                    if (input.Length == 0) continue;

                    if (input.StartsWith("/"))
                    {
                        HandleCommand(chat, input);
                        continue;
                    }

                    chat.SendText(input);
                }
                catch (Exception ex) { UI.Status(ex.Message, UI.Danger); }
            }
        }

        private static void HandleCommand(ChatClient chat, string command)
        {
            string lower = command.ToLowerInvariant();
            string[] parts = command.Split(new[] { ' ' }, 2);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "/h": case "/help": chat.Render(); PrintHelp(); return;
                case "/c": case "/clear": chat.Render(); return;
                case "/q":
                case "/quit":
                case "/exit":
                    chat.Stop();
                    Console.WriteLine();
                    UI.Status("Signed out", UI.Muted);
                    return;
                case "/id": chat.ShowOwnId(); return;
                case "/f": case "/friends": chat.ShowFriends(); return;
                case "/r": case "/requests": chat.ShowRequests(); return;
                case "/p": case "/public": chat.EnterPublic(); return;
                case "/whois": chat.ShowUserProfile(arg); return;
                case "/status": chat.SetStatus(arg); return;
                case "/nick": chat.SetNick(arg); return;
                case "/bio": chat.SetBio(arg); return;
                case "/notify": chat.ToggleNotifications(arg); return;
                case "/ping": chat.Ping(); return;
                case "/stats": chat.ShowStats(); return;
                case "/block": chat.BlockUser(arg); return;
                case "/unblock": chat.UnblockUser(arg); return;
                case "/blocked": chat.ShowBlocked(); return;

                case "/m":
                case "/msg":
                    if (string.IsNullOrWhiteSpace(arg)) { UI.Status("Usage: /m <username|#id>", UI.Danger); return; }
                    chat.EnterPrivate(arg);
                    return;

                case "/a":
                case "/add":
                    if (string.IsNullOrWhiteSpace(arg)) { UI.Status("Usage: /a <username|#id>", UI.Danger); return; }
                    chat.AddFriend(arg);
                    return;

                case "/accept":
                case "/acc":
                    if (string.IsNullOrWhiteSpace(arg)) { UI.Status("Usage: /accept <username|#id>", UI.Danger); return; }
                    chat.AcceptFriend(arg);
                    return;

                case "/up":
                case "/upload":
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        string selected = SelectImage();
                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            chat.UploadImage(selected);
                            chat.Refresh();
                            chat.Render();
                        }
                    }
                    else
                    {
                        chat.UploadImage(arg.Trim('"'));
                        chat.Refresh();
                        chat.Render();
                    }
                    return;

                case "/v":
                case "/view":
                    {
                        int n;
                        if (!int.TryParse(arg, out n)) { UI.Status("Usage: /v <id>", UI.Danger); return; }
                        chat.PreviewImage(n);
                        return;
                    }

                case "/d":
                case "/download":
                    {
                        int n;
                        if (!int.TryParse(arg, out n)) { UI.Status("Usage: /d <id>", UI.Danger); return; }
                        chat.DownloadImage(n);
                        return;
                    }

                default:
                    UI.Status("Unknown command. Type /h for help.", UI.Danger);
                    return;
            }
        }

        private static string SelectImage()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select image";
                dialog.Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*";
                dialog.Multiselect = false;
                if (dialog.ShowDialog() == DialogResult.OK) return dialog.FileName;
            }
            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine();
            UI.Header("COMMANDS", UI.Blurple);
            Console.WriteLine();

            var rows = new (string cmd, string desc)[]
            {
                ("/h  /help",              "Show this help"),
                ("/id",                    "Show your user ID"),
                ("/f  /friends",           "List friends"),
                ("/r  /requests",          "List friend requests"),
                ("/a  <user|#id>",         "Send friend request"),
                ("/accept <user|#id>",     "Accept friend request"),
                ("/m  <user|#id>",         "Open private chat"),
                ("/p  /public",            "Go back to public chat"),
                ("/whois <user|#id>",      "Show user profile"),
                ("/status <s>",            "Set status: online, idle, dnd, invisible"),
                ("/nick <name>",           "Change display name"),
                ("/bio <text>",            "Set short bio"),
                ("/notify on|off",         "Toggle notification sounds"),
                ("/ping",                  "Measure server latency"),
                ("/stats",                 "Show your stats"),
                ("/block <user|#id>",      "Block a user"),
                ("/unblock <user|#id>",    "Unblock a user"),
                ("/blocked",               "List blocked users"),
                ("/up [path]",             "Upload image (opens picker if no path)"),
                ("/v  <id>",               "Preview image"),
                ("/d  <id>",               "Download image"),
                ("/c  /clear",             "Redraw screen"),
                ("/q  /quit",              "Sign out and exit"),
            };

            foreach (var row in rows)
            {
                UI.Write("  " + row.cmd.PadRight(26), UI.White);
                Console.WriteLine(row.desc);
            }

            Console.WriteLine();
            UI.WriteLine("  Tip: use usernames or #IDs anywhere — both work.", UI.Muted);
            UI.WriteLine("  Every message shows its sender ID for easy copying.", UI.Muted);
            Console.WriteLine();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UI — terminal rendering helpers
    // ═══════════════════════════════════════════════════════════════════

    internal static class UI
    {
        // Palette
        public static ConsoleColor Blurple = ConsoleColor.Blue;
        public static ConsoleColor Success = ConsoleColor.Green;
        public static ConsoleColor Danger = ConsoleColor.Red;
        public static ConsoleColor Warning = ConsoleColor.Yellow;
        public static ConsoleColor Muted = ConsoleColor.DarkGray;
        public static ConsoleColor White = ConsoleColor.White;
        public static ConsoleColor Accent = ConsoleColor.Cyan;
        public static ConsoleColor Self = ConsoleColor.Blue;
        public static ConsoleColor Other = ConsoleColor.Green;
        public static ConsoleColor Ping = ConsoleColor.Magenta;

        public static void Write(string text, ConsoleColor color)
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = prev;
        }

        public static void WriteLine(string text, ConsoleColor color)
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }

        public static void Status(string text, ConsoleColor color)
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write("  ● ");
            Console.ForegroundColor = prev;
            Console.WriteLine(text);
        }

        public static void Header(string text, ConsoleColor color)
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine("  " + text);
            Console.ForegroundColor = prev;
        }

        public static void Prompt(string text)
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = Blurple;
            Console.Write(text);
            Console.ForegroundColor = prev;
        }

        public static void Centered(string text, ConsoleColor color)
        {
            int width = 80;
            try { width = Console.WindowWidth; } catch { }
            int pad = Math.Max(0, (width - text.Length) / 2);

            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(new string(' ', pad));
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }

        public static void Divider()
        {
            int width = 80;
            try { width = Math.Max(40, Console.WindowWidth - 2); } catch { }

            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = Muted;
            Console.WriteLine("  " + new string('─', Math.Min(width, 78)));
            Console.ForegroundColor = prev;
        }

        public static string ShortTime(long unix)
        {
            try
            {
                var utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix);
                var local = utc.ToLocalTime();
                var now = DateTime.Now;

                if (local.Date == now.Date) return local.ToString("HH:mm");
                if (local.Date == now.Date.AddDays(-1)) return "yesterday " + local.ToString("HH:mm");
                return local.ToString("MMM d HH:mm");
            }
            catch { return "--:--"; }
        }

        public static string ShortTimeAgo(long unix)
        {
            try
            {
                var utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix);
                var span = DateTime.UtcNow - utc;

                if (span.TotalSeconds < 60) return "just now";
                if (span.TotalMinutes < 60) return (int)span.TotalMinutes + "m ago";
                if (span.TotalHours < 24) return (int)span.TotalHours + "h ago";
                if (span.TotalDays < 7) return (int)span.TotalDays + "d ago";
                return utc.ToLocalTime().ToString("MMM d, yyyy");
            }
            catch { return "unknown"; }
        }

        public static string StatusDot(string status)
        {
            switch ((status ?? "online").ToLowerInvariant())
            {
                case "idle": return "◐";
                case "dnd": return "◉";
                case "invisible": return "○";
                default: return "●";
            }
        }

        public static ConsoleColor StatusColor(string status)
        {
            switch ((status ?? "online").ToLowerInvariant())
            {
                case "idle": return Warning;
                case "dnd": return Danger;
                case "invisible": return Muted;
                default: return Success;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ACCOUNT + LOCAL STORE
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class Account
    {
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string PasswordHash { get; set; }
        public string Salt { get; set; }
        public bool IsGuest { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "online";
        public string Bio { get; set; } = "";
        public bool NotificationsEnabled { get; set; } = true;
        public List<string> BlockedUids { get; set; } = new List<string>();
    }

    internal static class AccountStore
    {
        private static readonly string Path =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");

        private static List<Account> cache;

        private static List<Account> Load()
        {
            if (cache != null) return cache;
            try
            {
                if (File.Exists(Path))
                    cache = JsonConvert.DeserializeObject<List<Account>>(File.ReadAllText(Path))
                            ?? new List<Account>();
                else
                    cache = new List<Account>();
            }
            catch { cache = new List<Account>(); }
            return cache;
        }

        public static Account Find(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            return Load().FirstOrDefault(a =>
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        public static void Save(Account acc)
        {
            var list = Load();
            int idx = list.FindIndex(a =>
                string.Equals(a.Username, acc.Username, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = acc;
            else list.Add(acc);

            try { File.WriteAllText(Path, JsonConvert.SerializeObject(list, Formatting.Indented)); }
            catch (Exception ex) { Console.WriteLine("Warning: could not save account: " + ex.Message); }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FIREBASE CLIENT
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class FirebaseClient
    {
        private readonly string apiKey;
        private readonly string databaseUrl;

        private string idToken;
        private string localId;
        private string chatId;

        public string LocalId => localId;
        public string ChatId => chatId;

        public FirebaseClient(string apiKey, string databaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key is missing.");
            this.apiKey = apiKey;
            this.databaseUrl = databaseUrl.TrimEnd('/');
        }

        // ─── AUTH ─────────────────────────────────────────────────────

        public void SignInAnonymously()
        {
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key="
                         + Uri.EscapeDataString(apiKey);
            string body = "{\"returnSecureToken\":true}";
            string response = Send("POST", url, body);

            FirebaseAuthResponse auth;
            try { auth = JsonConvert.DeserializeObject<FirebaseAuthResponse>(response); }
            catch (Exception ex) { throw new Exception("Invalid auth response: " + ex.Message); }

            if (auth == null || string.IsNullOrWhiteSpace(auth.IdToken))
                throw new Exception("No auth token returned.");

            idToken = auth.IdToken;
            localId = auth.LocalId;
        }

        public void RegisterUser(string username, string displayName)
        {
            EnsureAuth();

            string userUrl = databaseUrl + "/users/" + Uri.EscapeDataString(localId)
                             + ".json?auth=" + Uri.EscapeDataString(idToken);
            string existing = Send("GET", userUrl);

            if (!string.IsNullOrWhiteSpace(existing) && existing.Trim() != "null")
            {
                var obj = JObject.Parse(existing);
                var existingId = obj["chatId"];
                if (existingId != null)
                {
                    chatId = existingId.ToString();
                    Send("PATCH", userUrl, JsonConvert.SerializeObject(new
                    {
                        username = username,
                        displayName = displayName
                    }));
                    return;
                }
            }

            chatId = GenerateChatId();

            var user = new FirebaseUser
            {
                Username = username,
                DisplayName = displayName,
                ChatId = chatId,
                Uid = localId,
                CreatedAt = UnixTime(),
                Status = "online"
            };

            Send("PUT", userUrl, JsonConvert.SerializeObject(user));
        }

        public void UpdateSelf(string status = null, string displayName = null, string bio = null)
        {
            EnsureAuth();
            string userUrl = databaseUrl + "/users/" + Uri.EscapeDataString(localId)
                             + ".json?auth=" + Uri.EscapeDataString(idToken);

            var patch = new Dictionary<string, object>();
            if (status != null) patch["status"] = status;
            if (displayName != null) patch["displayName"] = displayName;
            if (bio != null) patch["bio"] = bio;

            if (patch.Count == 0) return;
            Send("PATCH", userUrl, JsonConvert.SerializeObject(patch));
        }

        private string GenerateChatId()
        {
            string candidate = CalculateNumericId(localId);
            for (int i = 0; i < 100000; i++)
            {
                var existing = FindUserByChatId(candidate);
                if (existing == null || existing.Uid == localId) return candidate;

                int n;
                if (!int.TryParse(candidate, out n)) n = 1;
                n = (n + 1) % 100000;
                candidate = n.ToString("D5");
            }
            throw new Exception("Could not generate a unique ID.");
        }

        private static string CalculateNumericId(string uid)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(uid));
                int number = Math.Abs(BitConverter.ToInt32(hash, 0)) % 100000;
                return number.ToString("D5");
            }
        }

        // ─── USER LOOKUP ──────────────────────────────────────────────

        public FirebaseUser FindUserByChatId(string id)
        {
            EnsureAuth();
            id = NormalizeChatId(id);
            if (id == null) return null;

            return GetAllUsersRaw().FirstOrDefault(u => u.ChatId == id);
        }

        public FirebaseUser FindUserByUsername(string username)
        {
            EnsureAuth();
            if (string.IsNullOrWhiteSpace(username)) return null;
            username = username.Trim();

            return GetAllUsersRaw().FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        public FirebaseUser FindUserByDisplayName(string name)
        {
            EnsureAuth();
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            return GetAllUsersRaw().FirstOrDefault(u =>
                string.Equals(u.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
        }

        public FirebaseUser ResolveUser(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            token = token.Trim();

            string id = NormalizeChatId(token);
            if (id != null)
            {
                var byId = FindUserByChatId(id);
                if (byId != null) return byId;
            }
            return FindUserByDisplayName(token);
        }

        private List<FirebaseUser> GetAllUsersRaw()
        {
            string url = databaseUrl + "/users.json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);

            var users = new List<FirebaseUser>();
            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return users;

            JObject root;
            try { root = JObject.Parse(response); } catch { return users; }

            foreach (var prop in root.Properties())
            {
                try
                {
                    var user = prop.Value.ToObject<FirebaseUser>();
                    if (user != null)
                    {
                        if (string.IsNullOrWhiteSpace(user.Uid)) user.Uid = prop.Name;
                        users.Add(user);
                    }
                }
                catch { }
            }
            return users;
        }

        public List<FirebaseUser> GetAllUsers() => GetAllUsersRaw();

        public FirebaseUser GetUserByUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return null;
            string url = databaseUrl + "/users/" + Uri.EscapeDataString(uid)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);

            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return null;
            return JsonConvert.DeserializeObject<FirebaseUser>(response);
        }

        // ─── FRIENDS ──────────────────────────────────────────────────

        public List<string> GetFriendIds()
        {
            EnsureAuth();
            string url = databaseUrl + "/friends/" + Uri.EscapeDataString(localId)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);

            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return ids;

            var root = JObject.Parse(response);
            foreach (var prop in root.Properties()) ids.Add(prop.Name);
            return ids;
        }

        public List<FirebaseUser> GetFriends()
        {
            var ids = GetFriendIds();
            var result = new List<FirebaseUser>();
            foreach (var id in ids)
            {
                var user = GetUserByUid(id);
                if (user != null) result.Add(user);
            }
            return result.OrderBy(u => u.DisplayName ?? u.Username).ToList();
        }

        public void SendFriendRequest(FirebaseUser target, string username)
        {
            EnsureAuth();
            if (target == null) throw new Exception("User not found.");
            if (target.Uid == localId) throw new Exception("You cannot add yourself.");

            var friends = GetFriendIds();
            if (friends.Contains(target.Uid)) throw new Exception("Already friends.");

            var request = new FriendRequest
            {
                FromUid = localId,
                FromChatId = chatId,
                FromUsername = username,
                CreatedAt = UnixTime()
            };

            string url = databaseUrl + "/friendRequests/" + Uri.EscapeDataString(target.Uid)
                         + "/" + Uri.EscapeDataString(localId)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            Send("PUT", url, JsonConvert.SerializeObject(request));
        }

        public Dictionary<string, FriendRequest> GetFriendRequests()
        {
            EnsureAuth();
            string url = databaseUrl + "/friendRequests/" + Uri.EscapeDataString(localId)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);

            var result = new Dictionary<string, FriendRequest>();
            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return result;

            var root = JObject.Parse(response);
            foreach (var prop in root.Properties())
            {
                try
                {
                    var req = prop.Value.ToObject<FriendRequest>();
                    if (req != null) result[prop.Name] = req;
                }
                catch { }
            }
            return result;
        }

        public void AcceptFriend(FirebaseUser requester)
        {
            EnsureAuth();
            if (requester == null) throw new Exception("User not found.");

            string reqUrl = databaseUrl + "/friendRequests/" + Uri.EscapeDataString(localId)
                            + "/" + Uri.EscapeDataString(requester.Uid)
                            + ".json?auth=" + Uri.EscapeDataString(idToken);

            string reqResponse = Send("GET", reqUrl);
            if (string.IsNullOrWhiteSpace(reqResponse) || reqResponse.Trim() == "null")
                throw new Exception("No friend request from this user.");

            string myUrl = databaseUrl + "/friends/" + Uri.EscapeDataString(localId)
                           + "/" + Uri.EscapeDataString(requester.Uid)
                           + ".json?auth=" + Uri.EscapeDataString(idToken);
            string theirUrl = databaseUrl + "/friends/" + Uri.EscapeDataString(requester.Uid)
                              + "/" + Uri.EscapeDataString(localId)
                              + ".json?auth=" + Uri.EscapeDataString(idToken);

            var me = GetUserByUid(localId) ?? new FirebaseUser
            {
                Uid = localId,
                ChatId = chatId,
                Username = "Guest",
                DisplayName = "Guest"
            };

            var myFriend = new FirebaseFriend
            {
                Uid = requester.Uid,
                ChatId = requester.ChatId,
                Username = requester.Username,
                DisplayName = requester.DisplayName,
                AddedAt = UnixTime()
            };
            var theirFriend = new FirebaseFriend
            {
                Uid = localId,
                ChatId = chatId,
                Username = me.Username,
                DisplayName = me.DisplayName,
                AddedAt = UnixTime()
            };

            Send("PUT", myUrl, JsonConvert.SerializeObject(myFriend));
            Send("PUT", theirUrl, JsonConvert.SerializeObject(theirFriend));
            Send("DELETE", reqUrl);
        }

        // ─── MESSAGES ─────────────────────────────────────────────────

        public Dictionary<string, FirebaseMessage> GetPublicMessages()
        {
            EnsureAuth();
            string url = databaseUrl + "/messages.json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);
            return ParseMessages(response);
        }

        public Dictionary<string, FirebaseMessage> GetPrivateMessages(string otherUid)
        {
            EnsureAuth();
            string conversation = GetConversationKey(localId, otherUid);
            string url = databaseUrl + "/privateMessages/" + Uri.EscapeDataString(conversation)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);
            return ParseMessages(response);
        }

        public List<FirebaseMessage> GetPrivateMessagesRaw()
        {
            EnsureAuth();
            string url = databaseUrl + "/privateMessages.json?auth=" + Uri.EscapeDataString(idToken);
            string response = Send("GET", url);

            var all = new List<FirebaseMessage>();
            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return all;

            JObject root;
            try { root = JObject.Parse(response); } catch { return all; }

            foreach (var convProp in root.Properties())
            {
                if (!convProp.Name.Contains(localId)) continue;
                if (!(convProp.Value is JObject)) continue;

                foreach (var msgProp in ((JObject)convProp.Value).Properties())
                {
                    if (!(msgProp.Value is JObject)) continue;
                    try
                    {
                        var obj = (JObject)msgProp.Value;
                        var m = new FirebaseMessage
                        {
                            Id = msgProp.Name,
                            Username = GetStr(obj, "username"),
                            DisplayName = GetStr(obj, "displayName"),
                            ChatId = GetStr(obj, "chatId"),
                            Content = GetStr(obj, "content"),
                            Type = GetStr(obj, "type"),
                            Mime = GetStr(obj, "mime"),
                            Data = GetStr(obj, "data"),
                            Timestamp = GetLong(obj, "timestamp"),
                            SenderUid = GetStr(obj, "senderUid"),
                            RecipientUid = GetStr(obj, "recipientUid")
                        };
                        if (string.IsNullOrWhiteSpace(m.Type)) m.Type = "text";
                        all.Add(m);
                    }
                    catch { }
                }
            }
            return all;
        }

        private static Dictionary<string, FirebaseMessage> ParseMessages(string response)
        {
            var result = new Dictionary<string, FirebaseMessage>();
            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null") return result;

            JObject root;
            try { root = JObject.Parse(response); } catch { return result; }

            foreach (var prop in root.Properties())
            {
                if (!(prop.Value is JObject)) continue;
                try
                {
                    var obj = (JObject)prop.Value;
                    var m = new FirebaseMessage
                    {
                        Id = prop.Name,
                        Username = GetStr(obj, "username"),
                        DisplayName = GetStr(obj, "displayName"),
                        ChatId = GetStr(obj, "chatId"),
                        Content = GetStr(obj, "content"),
                        Type = GetStr(obj, "type"),
                        Mime = GetStr(obj, "mime"),
                        Data = GetStr(obj, "data"),
                        Timestamp = GetLong(obj, "timestamp"),
                        SenderUid = GetStr(obj, "senderUid"),
                        RecipientUid = GetStr(obj, "recipientUid")
                    };
                    if (string.IsNullOrWhiteSpace(m.Type)) m.Type = "text";
                    result[prop.Name] = m;
                }
                catch { }
            }
            return result;
        }

        private static string GetStr(JObject obj, string name)
        {
            JToken token;
            if (!obj.TryGetValue(name, out token)) return null;
            if (token == null || token.Type == JTokenType.Null) return null;
            return token.ToString();
        }

        private static long GetLong(JObject obj, string name)
        {
            JToken token;
            if (!obj.TryGetValue(name, out token)) return 0;
            if (token == null || token.Type == JTokenType.Null) return 0;
            long v;
            return long.TryParse(token.ToString(), out v) ? v : 0;
        }

        public void PushPublicMessage(FirebaseMessage message)
        {
            EnsureAuth();
            string url = databaseUrl + "/messages.json?auth=" + Uri.EscapeDataString(idToken);
            Send("POST", url, JsonConvert.SerializeObject(message));
        }

        public void PushPrivateMessage(string otherUid, FirebaseMessage message)
        {
            EnsureAuth();
            string conversation = GetConversationKey(localId, otherUid);
            string url = databaseUrl + "/privateMessages/" + Uri.EscapeDataString(conversation)
                         + ".json?auth=" + Uri.EscapeDataString(idToken);
            Send("POST", url, JsonConvert.SerializeObject(message));
        }

        // ─── UTILITIES ────────────────────────────────────────────────

        public static string GetConversationKey(string uid1, string uid2)
        {
            if (string.Compare(uid1, uid2, StringComparison.Ordinal) < 0) return uid1 + "_" + uid2;
            return uid2 + "_" + uid1;
        }

        public static string NormalizeChatId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (value.StartsWith("#")) value = value.Substring(1);

            int number;
            if (!int.TryParse(value, out number)) return null;
            if (number < 0 || number > 99999) return null;
            return number.ToString("D5");
        }

        public long Ping()
        {
            EnsureAuth();
            var sw = Stopwatch.StartNew();
            Send("GET", databaseUrl + "/.json?shallow=true&auth=" + Uri.EscapeDataString(idToken));
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private void EnsureAuth()
        {
            if (string.IsNullOrWhiteSpace(idToken))
                throw new InvalidOperationException("Not authenticated.");
        }

        private static long UnixTime()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / TimeSpan.TicksPerSecond;
        }

        private static string Send(string method, string url, string body = null)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.UserAgent = "NotEnoughChats/4.0";
            request.Accept = "application/json";

            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                string error = ex.Message;
                if (ex.Response != null)
                {
                    using (var stream = ex.Response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string response = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            try
                            {
                                var json = JObject.Parse(response);
                                var message = json.SelectToken("error.message");
                                error = message != null ? message.ToString() : response;
                            }
                            catch { error = response; }
                        }
                    }
                }
                throw new Exception("Server request failed: " + error);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CHAT CLIENT
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class ChatClient
    {
        private readonly Account account;
        private readonly FirebaseClient firebase;

        private readonly object messageLock = new object();
        private readonly object renderLock = new object();

        private List<FirebaseMessage> messages = new List<FirebaseMessage>();

        private Thread listenerThread;
        private volatile bool running;

        private string lastSignature = "";

        private bool privateMode;
        private FirebaseUser privateUser;

        // Notifications
        private readonly HashSet<string> seenFriendRequests = new HashSet<string>();
        private readonly HashSet<string> seenPrivateMessageIds = new HashSet<string>();
        private readonly HashSet<string> seenFriendUids = new HashSet<string>();
        private readonly Dictionary<string, long> lastDmByUser = new Dictionary<string, long>();
        private bool initialLoadDone = false;

        public bool Running => running;

        public ChatClient(Account account, FirebaseClient firebase)
        {
            this.account = account;
            this.firebase = firebase;
        }

        public void Start()
        {
            running = true;

            try
            {
                foreach (var r in firebase.GetFriendRequests()) seenFriendRequests.Add(r.Key);
                foreach (var f in firebase.GetFriends()) seenFriendUids.Add(f.Uid);
                foreach (var m in firebase.GetPrivateMessagesRaw())
                {
                    seenPrivateMessageIds.Add(m.Id);
                    if (!string.IsNullOrWhiteSpace(m.SenderUid))
                    {
                        if (!lastDmByUser.ContainsKey(m.SenderUid) || lastDmByUser[m.SenderUid] < m.Timestamp)
                            lastDmByUser[m.SenderUid] = m.Timestamp;
                    }
                }
                initialLoadDone = true;
            }
            catch { }

            Refresh();
            Render();

            listenerThread = new Thread(ListenLoop) { IsBackground = true };
            listenerThread.Start();
        }

        public void Stop() { running = false; }

        // ─── CHANNEL SWITCHING ────────────────────────────────────────

        public void EnterPublic()
        {
            privateMode = false;
            privateUser = null;
            lastSignature = "";
            Refresh();
            Render();
            UI.Status("Switched to #public", UI.Accent);
        }

        public void EnterPrivate(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) { UI.Status("Usage: /m <username|#id>", UI.Danger); return; }

            FirebaseUser user;
            try { user = firebase.ResolveUser(token); }
            catch (Exception ex) { UI.Status(ex.Message, UI.Danger); return; }

            if (user == null) { UI.Status("User '" + token + "' was not found.", UI.Danger); return; }
            if (user.Uid == firebase.LocalId) { UI.Status("You cannot DM yourself.", UI.Danger); return; }

            var friends = firebase.GetFriends();
            bool isFriend = friends.Any(x => x.Uid == user.Uid);
            if (!isFriend)
            {
                UI.Status("You are not friends with " + (user.DisplayName ?? user.Username)
                          + " (#" + user.ChatId + "). Use /a first.", UI.Danger);
                return;
            }

            if (account.BlockedUids.Contains(user.Uid))
            {
                UI.Status("You blocked this user. Use /unblock to unblock.", UI.Danger);
                return;
            }

            privateMode = true;
            privateUser = user;
            lastSignature = "";
            Refresh();
            Render();
            UI.Status("Opened DM with " + (user.DisplayName ?? user.Username) + " (#" + user.ChatId + ")", UI.Accent);
        }

        // ─── INFO COMMANDS ────────────────────────────────────────────

        public void ShowOwnId()
        {
            Console.WriteLine();
            UI.Write("  Your ID: ", UI.Muted);
            UI.WriteLine("#" + firebase.ChatId, UI.Blurple);
            Console.WriteLine();
        }

        public void ShowFriends()
        {
            var friends = firebase.GetFriends();
            var dms = firebase.GetPrivateMessagesRaw();

            Console.WriteLine();
            UI.Header("FRIENDS (" + friends.Count + ")", UI.Blurple);
            Console.WriteLine();

            if (friends.Count == 0)
            {
                UI.WriteLine("  No friends yet. Use /a <username> to add someone.", UI.Muted);
                Console.WriteLine();
                return;
            }

            foreach (var f in friends)
            {
                bool unread = dms.Any(m =>
                    m.SenderUid == f.Uid &&
                    (!lastDmByUser.ContainsKey(f.Uid) || m.Timestamp > lastDmByUser[f.Uid]));

                UI.Write("  ", UI.Muted);
                UI.Write(UI.StatusDot(f.Status), UI.StatusColor(f.Status));

                string name = f.DisplayName ?? f.Username;
                UI.Write(name, UI.White);
                UI.Write("  #" + f.ChatId, UI.Accent);

                if (unread) UI.Write("  ● new", UI.Ping);

                UI.Write("  →  /m " + (f.DisplayName ?? f.Username), UI.Muted);
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        public void ShowRequests()
        {
            var requests = firebase.GetFriendRequests();
            Console.WriteLine();
            UI.Header("FRIEND REQUESTS (" + requests.Count + ")", UI.Blurple);
            Console.WriteLine();

            if (requests.Count == 0)
            {
                UI.WriteLine("  No pending requests.", UI.Muted);
                Console.WriteLine();
                return;
            }

            foreach (var r in requests.Values)
            {
                UI.Write("  ● ", UI.Warning);
                UI.Write(r.FromUsername, UI.White);
                UI.Write("  #" + r.FromChatId, UI.Accent);
                UI.Write("  →  /accept " + r.FromUsername, UI.Muted);
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        public void ShowUserProfile(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) { UI.Status("Usage: /whois <username|#id>", UI.Danger); return; }

            var user = firebase.ResolveUser(token);
            if (user == null) { UI.Status("User not found.", UI.Danger); return; }

            Console.WriteLine();
            UI.Header("PROFILE", UI.Blurple);
            Console.WriteLine();

            UI.Write("  Display name:  ", UI.Muted);
            UI.WriteLine(user.DisplayName ?? user.Username, UI.White);

            UI.Write("  Username:      ", UI.Muted);
            UI.WriteLine(user.Username, UI.White);

            UI.Write("  ID:            ", UI.Muted);
            UI.WriteLine("#" + user.ChatId, UI.Accent);

            UI.Write("  Status:        ", UI.Muted);
            UI.Write(UI.StatusDot(user.Status) + " ", UI.StatusColor(user.Status));
            UI.WriteLine(user.Status ?? "online", UI.White);

            if (!string.IsNullOrWhiteSpace(user.Bio))
            {
                UI.Write("  Bio:           ", UI.Muted);
                UI.WriteLine(user.Bio, UI.White);
            }

            UI.Write("  Joined:        ", UI.Muted);
            UI.WriteLine(UI.ShortTimeAgo(user.CreatedAt), UI.White);

            var friends = firebase.GetFriends();
            if (friends.Any(f => f.Uid == user.Uid))
            {
                UI.Write("  Relationship:  ", UI.Muted);
                UI.WriteLine("friend", UI.Success);
            }
            else
            {
                UI.Write("  Relationship:  ", UI.Muted);
                UI.WriteLine("not friends  →  /a " + user.Username, UI.Muted);
            }

            Console.WriteLine();
        }

        public void ShowStats()
        {
            Console.WriteLine();
            UI.Header("YOUR STATS", UI.Blurple);
            Console.WriteLine();

            try
            {
                var publicMsgs = firebase.GetPublicMessages();
                var myPublicCount = publicMsgs.Values.Count(m => m.SenderUid == firebase.LocalId);

                var friends = firebase.GetFriends();
                var requests = firebase.GetFriendRequests();
                var dms = firebase.GetPrivateMessagesRaw();
                var myDmCount = dms.Count(m => m.SenderUid == firebase.LocalId);

                UI.Write("  Public messages sent:  ", UI.Muted);
                UI.WriteLine(myPublicCount.ToString(), UI.White);

                UI.Write("  Private messages sent: ", UI.Muted);
                UI.WriteLine(myDmCount.ToString(), UI.White);

                UI.Write("  Friends:               ", UI.Muted);
                UI.WriteLine(friends.Count.ToString(), UI.White);

                UI.Write("  Pending requests:      ", UI.Muted);
                UI.WriteLine(requests.Count.ToString(), UI.White);
            }
            catch (Exception ex) { UI.Status("Could not fetch stats: " + ex.Message, UI.Danger); }

            Console.WriteLine();
        }

        public void Ping()
        {
            try
            {
                long ms = firebase.Ping();
                UI.Status("Pong — " + ms + " ms", UI.Success);
            }
            catch (Exception ex) { UI.Status("Ping failed: " + ex.Message, UI.Danger); }
        }

        // ─── PROFILE EDITS ────────────────────────────────────────────

        public void SetStatus(string arg)
        {
            string s = (arg ?? "").Trim().ToLowerInvariant();
            if (s != "online" && s != "idle" && s != "dnd" && s != "invisible")
            {
                UI.Status("Usage: /status online|idle|dnd|invisible", UI.Danger);
                return;
            }

            account.Status = s;
            AccountStore.Save(account);

            try { firebase.UpdateSelf(status: s); }
            catch (Exception ex) { UI.Status("Update failed: " + ex.Message, UI.Danger); return; }

            UI.Status("Status set to " + s, UI.Success);
        }

        public void SetNick(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) { UI.Status("Usage: /nick <name>", UI.Danger); return; }
            arg = arg.Trim();
            if (arg.Length > 32) { UI.Status("Max 32 characters.", UI.Danger); return; }

            account.DisplayName = arg;
            AccountStore.Save(account);

            try { firebase.UpdateSelf(displayName: arg); }
            catch (Exception ex) { UI.Status("Update failed: " + ex.Message, UI.Danger); return; }

            UI.Status("Display name set to " + arg, UI.Success);
            Render();
        }

        public void SetBio(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) { UI.Status("Usage: /bio <text>", UI.Danger); return; }
            arg = arg.Trim();
            if (arg.Length > 200) arg = arg.Substring(0, 200);

            account.Bio = arg;
            AccountStore.Save(account);

            try { firebase.UpdateSelf(bio: arg); }
            catch (Exception ex) { UI.Status("Update failed: " + ex.Message, UI.Danger); return; }

            UI.Status("Bio updated", UI.Success);
        }

        public void ToggleNotifications(string arg)
        {
            string s = (arg ?? "").Trim().ToLowerInvariant();
            if (s == "on") account.NotificationsEnabled = true;
            else if (s == "off") account.NotificationsEnabled = false;
            else { UI.Status("Usage: /notify on|off", UI.Danger); return; }

            AccountStore.Save(account);
            UI.Status("Notifications " + (account.NotificationsEnabled ? "enabled" : "disabled"), UI.Success);
        }

        // ─── BLOCKING ─────────────────────────────────────────────────

        public void BlockUser(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) { UI.Status("Usage: /block <username|#id>", UI.Danger); return; }
            var user = firebase.ResolveUser(token);
            if (user == null) { UI.Status("User not found.", UI.Danger); return; }
            if (user.Uid == firebase.LocalId) { UI.Status("You cannot block yourself.", UI.Danger); return; }

            if (!account.BlockedUids.Contains(user.Uid)) account.BlockedUids.Add(user.Uid);
            AccountStore.Save(account);
            UI.Status("Blocked " + (user.DisplayName ?? user.Username), UI.Success);
        }

        public void UnblockUser(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) { UI.Status("Usage: /unblock <username|#id>", UI.Danger); return; }
            var user = firebase.ResolveUser(token);
            if (user == null) { UI.Status("User not found.", UI.Danger); return; }

            account.BlockedUids.Remove(user.Uid);
            AccountStore.Save(account);
            UI.Status("Unblocked " + (user.DisplayName ?? user.Username), UI.Success);
        }

        public void ShowBlocked()
        {
            Console.WriteLine();
            UI.Header("BLOCKED USERS", UI.Blurple);
            Console.WriteLine();

            if (account.BlockedUids.Count == 0)
            {
                UI.WriteLine("  No blocked users.", UI.Muted);
                Console.WriteLine();
                return;
            }

            var allUsers = firebase.GetAllUsers();
            foreach (var uid in account.BlockedUids)
            {
                var u = allUsers.FirstOrDefault(x => x.Uid == uid);
                UI.Write("  ● ", UI.Danger);
                UI.Write(u != null ? (u.DisplayName ?? u.Username) : uid, UI.White);
                if (u != null) UI.Write("  #" + u.ChatId, UI.Accent);
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        // ─── FRIEND ACTIONS ───────────────────────────────────────────

        public void AddFriend(string token)
        {
            try
            {
                var user = firebase.ResolveUser(token);
                if (user == null) { UI.Status("User '" + token + "' was not found.", UI.Danger); return; }

                firebase.SendFriendRequest(user, account.Username);
                UI.Status("Friend request sent to " + (user.DisplayName ?? user.Username)
                          + " (#" + user.ChatId + ")", UI.Success);
            }
            catch (Exception ex) { UI.Status(ex.Message, UI.Danger); }
        }

        public void AcceptFriend(string token)
        {
            try
            {
                var user = firebase.ResolveUser(token);
                if (user == null) { UI.Status("User '" + token + "' was not found.", UI.Danger); return; }

                firebase.AcceptFriend(user);
                seenFriendUids.Add(user.Uid);
                UI.Status("You are now friends with " + (user.DisplayName ?? user.Username), UI.Success);
            }
            catch (Exception ex) { UI.Status(ex.Message, UI.Danger); }
        }

        // ─── MESSAGE REFRESH ──────────────────────────────────────────

        public void Refresh()
        {
            try
            {
                Dictionary<string, FirebaseMessage> data = privateMode && privateUser != null
                    ? firebase.GetPrivateMessages(privateUser.Uid)
                    : firebase.GetPublicMessages();

                SetMessages(data.Values
                    .Where(x => x != null)
                    .Where(x => !account.BlockedUids.Contains(x.SenderUid))
                    .OrderBy(x => x.Timestamp)
                    .TakeLastCompatible(Constants.MaxMessages)
                    .ToList());

                // Mark DMs from this user as read
                if (privateMode && privateUser != null)
                {
                    long max = data.Values.Select(x => x.Timestamp).DefaultIfEmpty(0).Max();
                    if (max > 0) lastDmByUser[privateUser.Uid] = max;
                }
            }
            catch (Exception ex) { UI.Status("Load failed: " + ex.Message, UI.Danger); }
        }

        private bool RefreshIfChanged()
        {
            try
            {
                Dictionary<string, FirebaseMessage> data = privateMode && privateUser != null
                    ? firebase.GetPrivateMessages(privateUser.Uid)
                    : firebase.GetPublicMessages();

                var loaded = data.Values
                    .Where(x => x != null)
                    .Where(x => !account.BlockedUids.Contains(x.SenderUid))
                    .OrderBy(x => x.Timestamp)
                    .TakeLastCompatible(Constants.MaxMessages)
                    .ToList();

                string sig = JsonConvert.SerializeObject(loaded);
                bool changed = !string.Equals(sig, lastSignature, StringComparison.Ordinal);

                lock (messageLock) { messages = loaded; }
                lastSignature = sig;

                if (privateMode && privateUser != null)
                {
                    long max = loaded.Select(x => x.Timestamp).DefaultIfEmpty(0).Max();
                    if (max > 0) lastDmByUser[privateUser.Uid] = max;
                }

                return changed;
            }
            catch { return false; }
        }

        private void SetMessages(List<FirebaseMessage> loaded)
        {
            string sig = JsonConvert.SerializeObject(loaded);
            lock (messageLock) { messages = loaded; }
            lastSignature = sig;
        }

        // ─── LISTENER LOOP ────────────────────────────────────────────

        private void ListenLoop()
        {
            while (running)
            {
                try { CheckNotifications(); } catch { }
                if (RefreshIfChanged()) Render();
                Thread.Sleep(Constants.PollInterval);
            }
        }

        // ─── NOTIFICATIONS ────────────────────────────────────────────

        private void CheckNotifications()
        {
            if (!initialLoadDone) return;

            // Friend requests
            try
            {
                var requests = firebase.GetFriendRequests();
                foreach (var kv in requests)
                {
                    if (seenFriendRequests.Contains(kv.Key)) continue;
                    seenFriendRequests.Add(kv.Key);
                    Notify(
                        "👋  " + kv.Value.FromUsername + " sent you a friend request",
                        "Accept with: /accept " + kv.Value.FromUsername,
                        UI.Ping);
                }
            }
            catch { }

            // New friends
            try
            {
                var friends = firebase.GetFriends();
                foreach (var f in friends)
                {
                    if (seenFriendUids.Contains(f.Uid)) continue;
                    seenFriendUids.Add(f.Uid);
                    Notify(
                        "🤝  " + (f.DisplayName ?? f.Username) + " added you as a friend",
                        "Say hi with: /m " + (f.DisplayName ?? f.Username),
                        UI.Success);
                }
            }
            catch { }

            // New DMs
            try
            {
                var dms = firebase.GetPrivateMessagesRaw();
                foreach (var m in dms)
                {
                    if (string.IsNullOrWhiteSpace(m.Id)) continue;
                    if (seenPrivateMessageIds.Contains(m.Id)) continue;
                    seenPrivateMessageIds.Add(m.Id);
                    if (m.SenderUid == firebase.LocalId) continue;
                    if (account.BlockedUids.Contains(m.SenderUid)) continue;
                    if (privateMode && privateUser != null && m.SenderUid == privateUser.Uid) continue;

                    FirebaseUser sender = null;
                    try { sender = firebase.GetUserByUid(m.SenderUid); } catch { }

                    string fromName = sender != null
                        ? (sender.DisplayName ?? sender.Username)
                        : (m.DisplayName ?? m.Username ?? "someone");
                    string fromId = sender != null ? "#" + sender.ChatId : "";

                    Notify(
                        "💬  New message from " + fromName + " " + fromId,
                        "Open with: /m " + fromName,
                        UI.Ping);
                }
            }
            catch { }
        }

        private void Notify(string title, string hint, ConsoleColor color)
        {
            if (account.NotificationsEnabled)
            {
                try { Console.Beep(880, 120); } catch { }
            }

            Console.WriteLine();
            ConsoleColor prev = Console.ForegroundColor;

            Console.ForegroundColor = color;
            Console.Write("  ┌─ ");
            Console.ForegroundColor = UI.White;
            Console.Write(title);
            Console.ForegroundColor = color;
            Console.WriteLine(" ─┐");

            Console.ForegroundColor = UI.Muted;
            Console.Write("  └─ ");
            Console.Write(hint);
            Console.ForegroundColor = color;
            Console.WriteLine(" ─┘");

            Console.ForegroundColor = prev;
            Console.WriteLine();
        }

        // ─── SENDING ──────────────────────────────────────────────────

        public void SendText(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            content = content.Trim();

            if (content.Length > Constants.MaxMessageLength)
            {
                UI.Status("Message too long. Max: " + Constants.MaxMessageLength, UI.Danger);
                return;
            }

            var msg = new FirebaseMessage
            {
                Username = account.Username,
                DisplayName = account.DisplayName ?? account.Username,
                ChatId = firebase.ChatId,
                Content = content,
                Type = "text",
                Timestamp = UnixTime(),
                SenderUid = firebase.LocalId
            };

            try
            {
                if (privateMode && privateUser != null)
                {
                    msg.RecipientUid = privateUser.Uid;
                    firebase.PushPrivateMessage(privateUser.Uid, msg);
                }
                else
                {
                    firebase.PushPublicMessage(msg);
                }

                Refresh();
                Render();
            }
            catch (Exception ex) { UI.Status("Send failed: " + ex.Message, UI.Danger); }
        }

        public void UploadImage(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            filePath = filePath.Trim().Trim('"');

            if (!File.Exists(filePath)) { UI.Status("File not found.", UI.Danger); return; }

            var info = new FileInfo(filePath);
            if (info.Length > Constants.MaxImageSize)
            {
                UI.Status("Image too large (max 1.5 MB).", UI.Danger);
                return;
            }

            string mime = GetImageMime(filePath);
            if (mime == null) { UI.Status("Unsupported format.", UI.Danger); return; }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(filePath); }
            catch (Exception ex) { UI.Status("Read failed: " + ex.Message, UI.Danger); return; }

            if (!IsValidImage(bytes)) { UI.Status("Invalid image file.", UI.Danger); return; }

            try
            {
                var msg = new FirebaseMessage
                {
                    Username = account.Username,
                    DisplayName = account.DisplayName ?? account.Username,
                    ChatId = firebase.ChatId,
                    Content = Path.GetFileName(filePath),
                    Type = "image",
                    Mime = mime,
                    Data = Convert.ToBase64String(bytes),
                    Timestamp = UnixTime(),
                    SenderUid = firebase.LocalId
                };

                if (privateMode && privateUser != null)
                {
                    msg.RecipientUid = privateUser.Uid;
                    firebase.PushPrivateMessage(privateUser.Uid, msg);
                }
                else
                {
                    firebase.PushPublicMessage(msg);
                }

                UI.Status("Uploaded " + Path.GetFileName(filePath), UI.Success);
                Refresh();
                Render();
            }
            catch (Exception ex) { UI.Status("Upload failed: " + ex.Message, UI.Danger); }
        }

        // ─── IMAGE PREVIEW / DOWNLOAD ─────────────────────────────────

        public FirebaseMessage GetMessage(int number)
        {
            List<FirebaseMessage> current;
            lock (messageLock) { current = new List<FirebaseMessage>(messages); }

            if (current.Count == 0) { UI.Status("No messages.", UI.Danger); return null; }
            if (number < 1 || number > current.Count)
            {
                UI.Status("Invalid ID. Use 1-" + current.Count + ".", UI.Danger);
                return null;
            }
            return current[number - 1];
        }

        public void PreviewImage(int number)
        {
            var msg = GetMessage(number);
            if (msg == null) return;
            if (!string.Equals(msg.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                UI.Status("That message is not an image.", UI.Danger);
                return;
            }
            using (var form = new ImagePreviewForm(msg, this)) form.ShowDialog();
        }

        public void DownloadImage(int number)
        {
            var msg = GetMessage(number);
            if (msg == null) return;
            SaveImage(msg, false);
        }

        public string SaveImage(FirebaseMessage msg, bool silent)
        {
            if (msg == null) return null;
            if (!string.Equals(msg.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                if (!silent) UI.Status("Not an image.", UI.Danger);
                return null;
            }
            if (string.IsNullOrWhiteSpace(msg.Data))
            {
                if (!silent) UI.Status("No data.", UI.Danger);
                return null;
            }

            byte[] decoded;
            try { decoded = Convert.FromBase64String(msg.Data); }
            catch { if (!silent) UI.Status("Invalid data.", UI.Danger); return null; }

            string filename = Path.GetFileName(
                string.IsNullOrWhiteSpace(msg.Content) ? "image.png" : msg.Content);

            string downloads = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads");
            Directory.CreateDirectory(downloads);

            string output = Path.Combine(downloads, filename);
            string stem = Path.GetFileNameWithoutExtension(filename);
            string ext = Path.GetExtension(filename);

            int counter = 1;
            while (File.Exists(output))
            {
                output = Path.Combine(downloads, stem + "_" + counter + ext);
                counter++;
            }

            File.WriteAllBytes(output, decoded);
            string full = Path.GetFullPath(output);

            if (!silent) UI.Status("Saved to " + full, UI.Success);
            return full;
        }

        // ─── RENDER ───────────────────────────────────────────────────

        public void Render()
        {
            lock (renderLock)
            {
                List<FirebaseMessage> current;
                lock (messageLock) { current = new List<FirebaseMessage>(messages); }

                Console.Clear();

                RenderHeader();
                RenderMessages(current);
                RenderFooter();
            }
        }

        private void RenderHeader()
        {
            Console.WriteLine();

            UI.Write("  ╭─ ", UI.Blurple);
            UI.Write("NotEnoughChats", UI.White);
            UI.Write("  ·  ", UI.Muted);
            UI.Write("Connected", UI.Success);
            UI.WriteLine("  ─────────────────────────────────────────╮", UI.Blurple);

            UI.Write("  │  ", UI.Blurple);
            UI.Write(UI.StatusDot(account.Status) + " ", UI.StatusColor(account.Status));
            UI.Write(account.DisplayName ?? account.Username, UI.White);
            UI.Write("   ", UI.Muted);
            UI.Write("ID: ", UI.Muted);
            UI.Write("#" + firebase.ChatId, UI.Accent);
            Console.WriteLine();

            UI.Write("  │  ", UI.Blurple);
            if (privateMode && privateUser != null)
            {
                UI.Write("Channel: ", UI.Muted);
                UI.Write("@ " + (privateUser.DisplayName ?? privateUser.Username), UI.Warning);
                UI.Write("  (#" + privateUser.ChatId + ")", UI.Muted);
            }
            else
            {
                UI.Write("Channel: ", UI.Muted);
                UI.Write("# public", UI.Accent);
            }
            Console.WriteLine();

            UI.WriteLine("  ╰──────────────────────────────────────────────────────────╯", UI.Blurple);
            Console.WriteLine();
        }

        private void RenderMessages(List<FirebaseMessage> current)
        {
            UI.Divider();
            Console.WriteLine();

            if (current.Count == 0)
            {
                UI.WriteLine("  No messages yet.", UI.Muted);
            }
            else
            {
                foreach (var pair in current.Select((m, i) => new { m, i }))
                {
                    var m = pair.m;
                    int idx = pair.i + 1;

                    string sender = m.DisplayName ?? m.Username ?? "unknown";
                    bool isSelf = m.SenderUid == firebase.LocalId;

                    UI.Write("  [" + idx.ToString("D2") + "] ", UI.Muted);
                    UI.Write(UI.ShortTime(m.Timestamp) + "  ", UI.Muted);
                    UI.Write(sender, isSelf ? UI.Self : UI.Other);
                    if (!string.IsNullOrWhiteSpace(m.ChatId))
                    {
                        UI.Write(" #" + m.ChatId, UI.Accent);
                    }
                    UI.WriteLine(":", UI.Muted);

                    if (string.Equals(m.Type, "image", StringComparison.OrdinalIgnoreCase))
                    {
                        UI.Write("        🖼  ", UI.Warning);
                        UI.Write("[IMAGE #" + idx + "]", UI.Blurple);
                        UI.WriteLine("  " + (string.IsNullOrWhiteSpace(m.Content) ? "image" : m.Content), UI.Muted);
                    }
                    else
                    {
                        UI.WriteLine("        " + (m.Content ?? ""), UI.White);
                    }

                    Console.WriteLine();
                }
            }

            UI.Divider();
            Console.WriteLine();
        }

        private void RenderFooter()
        {
            UI.Write("  ", UI.Muted);

            string[] keys = privateMode
                ? new[] { "/p", "/v", "/d", "/up", "/c", "/q", "/h" }
                : new[] { "/h", "/m", "/f", "/a", "/up", "/v", "/d", "/q" };

            for (int i = 0; i < keys.Length; i++)
            {
                UI.Write(keys[i], i == 0 ? UI.Blurple : UI.Muted);
                if (i < keys.Length - 1) UI.Write("  ", UI.Muted);
            }

            Console.WriteLine();
            Console.WriteLine();
        }

        // ─── HELPERS ──────────────────────────────────────────────────

        private static bool IsValidImage(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms, true, true))
                    return img.Width > 0 && img.Height > 0;
            }
            catch { return false; }
        }

        private static string GetImageMime(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                default: return null;
            }
        }

        private static long UnixTime()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / TimeSpan.TicksPerSecond;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IMAGE PREVIEW FORM
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class ImagePreviewForm : Form
    {
        private readonly FirebaseMessage message;
        private readonly ChatClient client;

        private PictureBox pictureBox;
        private Button downloadButton;
        private Button closeButton;
        private Image image;

        public ImagePreviewForm(FirebaseMessage message, ChatClient client)
        {
            this.message = message;
            this.client = client;

            Text = "NotEnoughChats — " + (string.IsNullOrWhiteSpace(message.Content) ? "Image" : message.Content);
            Width = 900;
            Height = 700;
            MinimumSize = new Size(500, 400);
            BackColor = Color.FromArgb(30, 31, 34);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUI();
            LoadImage();
        }

        private void BuildUI()
        {
            var title = new Label
            {
                Text = string.IsNullOrWhiteSpace(message.Content) ? "Image" : message.Content,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(30, 31, 34),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 45,
                Padding = new Padding(15, 10, 15, 5)
            };

            pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 31, 34),
                SizeMode = PictureBoxSizeMode.Zoom,
                Padding = new Padding(15)
            };

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 65,
                BackColor = Color.FromArgb(30, 31, 34)
            };

            downloadButton = new Button
            {
                Text = "Download",
                Width = 120,
                Height = 35,
                Left = 15,
                Top = 15,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(88, 101, 242),
                ForeColor = Color.White
            };
            downloadButton.FlatAppearance.BorderSize = 0;
            downloadButton.Click += DownloadButton_Click;

            closeButton = new Button
            {
                Text = "Close",
                Width = 120,
                Height = 35,
                Top = 15,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(64, 68, 75),
                ForeColor = Color.White
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += delegate { Close(); };

            bottom.Resize += delegate
            {
                closeButton.Left = bottom.ClientSize.Width - closeButton.Width - 15;
            };

            bottom.Controls.Add(downloadButton);
            bottom.Controls.Add(closeButton);

            Controls.Add(pictureBox);
            Controls.Add(bottom);
            Controls.Add(title);
        }

        private void LoadImage()
        {
            try
            {
                byte[] data = Convert.FromBase64String(message.Data);
                using (var ms = new MemoryStream(data))
                using (var src = Image.FromStream(ms))
                    image = new Bitmap(src);
                pictureBox.Image = image;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load image:\r\n\r\n" + ex.Message,
                    "NotEnoughChats", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DownloadButton_Click(object sender, EventArgs e)
        {
            downloadButton.Enabled = false;
            downloadButton.Text = "Saving...";

            Task.Run(delegate
            {
                try
                {
                    string result = client.SaveImage(message, true);
                    BeginInvoke(new Action(delegate
                    {
                        downloadButton.Enabled = true;
                        downloadButton.Text = "Download";
                        if (result != null)
                            MessageBox.Show(this, "Saved to:\r\n\r\n" + result,
                                "NotEnoughChats", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        downloadButton.Enabled = true;
                        downloadButton.Text = "Download";
                        MessageBox.Show(this, ex.Message, "Save Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (image != null) { image.Dispose(); image = null; }
            base.OnFormClosed(e);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MODELS
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class FirebaseAuthResponse
    {
        [JsonProperty("idToken")] public string IdToken { get; set; }
        [JsonProperty("refreshToken")] public string RefreshToken { get; set; }
        [JsonProperty("expiresIn")] public string ExpiresIn { get; set; }
        [JsonProperty("localId")] public string LocalId { get; set; }
    }

    internal sealed class FirebaseUser
    {
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("chatId")] public string ChatId { get; set; }
        [JsonProperty("uid")] public string Uid { get; set; }
        [JsonProperty("createdAt")] public long CreatedAt { get; set; }
        [JsonProperty("status")] public string Status { get; set; } = "online";
        [JsonProperty("bio")] public string Bio { get; set; }
    }

    internal sealed class FirebaseFriend
    {
        [JsonProperty("uid")] public string Uid { get; set; }
        [JsonProperty("chatId")] public string ChatId { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("addedAt")] public long AddedAt { get; set; }
    }

    internal sealed class FriendRequest
    {
        [JsonProperty("fromUid")] public string FromUid { get; set; }
        [JsonProperty("fromChatId")] public string FromChatId { get; set; }
        [JsonProperty("fromUsername")] public string FromUsername { get; set; }
        [JsonProperty("createdAt")] public long CreatedAt { get; set; }
    }

    internal sealed class FirebaseMessage
    {
        [JsonIgnore] public string Id { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("chatId")] public string ChatId { get; set; }
        [JsonProperty("content")] public string Content { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("mime")] public string Mime { get; set; }
        [JsonProperty("data")] public string Data { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
        [JsonProperty("senderUid")] public string SenderUid { get; set; }
        [JsonProperty("recipientUid")] public string RecipientUid { get; set; }
    }

    internal static class Constants
    {
        public const int MaxMessages = 100;
        public const int MaxMessageLength = 2000;
        public const long MaxImageSize = 1572864L;
        public const int PollInterval = 1500;
    }

    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> TakeLastCompatible<T>(this IEnumerable<T> source, int count)
        {
            var list = source.ToList();
            if (count <= 0) return Enumerable.Empty<T>();
            if (list.Count <= count) return list;
            return list.Skip(list.Count - count);
        }
    }
}