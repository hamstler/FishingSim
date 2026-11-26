using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace FishingSim.Game
{
    /// <summary>
    /// Local player accounts (Godot: Accounts/UserStore, now with registration): each account has its own saved profile
    /// (accounts/&lt;name&gt;/profile.json under the game's data folder). Passwords are never stored, only a random salt and
    /// an iterated SHA-256 of salt and password; a login is checked in constant time against that hash.
    /// </summary>
    public static class Accounts
    {
        public const int Iterations = 5000;

        private sealed class Record
        {
            public string Salt = "", Hash = "", Created = "";
        }

        private sealed class Store
        {
            public Dictionary<string, Record> Users = new();
            public string Last = "";
        }

        public static string Root => Path.Combine(Application.persistentDataPath, "accounts");
        private static string FilePath => Path.Combine(Root, "users.json");

        /// <summary>The account last logged in (to fill the name on the login screen).</summary>
        public static string LastUser => Load().Last;

        public static int Count => Load().Users.Count;

        /// <summary>Where the account's progress is saved.</summary>
        public static string ProfilePath(string user) => Path.Combine(Root, Key(user), "profile.json");

        private static string Key(string user) => user.Trim().ToLowerInvariant();

        private static Store Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath)) ?? new Store();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Accounts file unreadable: {e.Message}");
            }
            return new Store();
        }

        private static void Save(Store s)
        {
            Directory.CreateDirectory(Root);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(s, Formatting.Indented));
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b)
                sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static string Hash(string salt, string password)
        {
            using var sha = SHA256.Create();
            string h = salt + ":" + password;
            for (int i = 0; i < Iterations; i++)
                h = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(salt + h)));
            return h;
        }

        private static bool SameText(string a, string b)
        {
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Why a name cannot be an account's (null = it can).</summary>
        public static string? NameProblem(string name)
        {
            name = name.Trim();
            if (name.Length < 3 || name.Length > 20)
                return "Nazwa konta: od 3 do 20 znaków.";
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
                    return "Nazwa konta: litery, cyfry, kropka, myślnik i podkreślenie.";
            return null;
        }

        /// <summary>Creates an account; null on success, else what is wrong.</summary>
        public static string? Register(string name, string password, string repeat)
        {
            if (NameProblem(name) is { } bad)
                return bad;
            if (password.Length < 4)
                return "Hasło: co najmniej 4 znaki.";
            if (password != repeat)
                return "Hasła się różnią.";
            var s = Load();
            if (s.Users.ContainsKey(Key(name)))
                return "Takie konto już istnieje.";
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            string saltHex = Hex(salt);
            s.Users[Key(name)] = new Record { Salt = saltHex, Hash = Hash(saltHex, password), Created = DateTime.UtcNow.ToString("o") };
            s.Last = name.Trim();
            Save(s);
            return null;
        }

        /// <summary>Checks a login; null on success, else what is wrong.</summary>
        public static string? Login(string name, string password)
        {
            var s = Load();
            if (!s.Users.TryGetValue(Key(name), out var rec))
                return "Nie ma takiego konta.";
            if (!SameText(rec.Hash, Hash(rec.Salt, password)))
                return "Złe hasło.";
            s.Last = name.Trim();
            Save(s);
            return null;
        }
    }
}
