using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FishingSim.Core;
using System.Text.Json.Nodes;
using Godot;
using FileAccess = Godot.FileAccess;

namespace FishingSim.Accounts;

/// <summary>
/// Local accounts. Passwords are never stored: only a random salt and an iterated SHA-256 hash
/// (compatible with the hashes written by earlier versions). There is no registration: the first and only
/// account is the default admin.
/// </summary>
public static class UserStore
{
    public const string DefaultUser = "admin";
    public const string DefaultPassword = "admin";
    public const int Iterations = 5000;

    /// <summary>Location of the accounts file; tests point it somewhere temporary.</summary>
    public static string Path { get; set; } = "user://users.json";

    private static string Hash(string salt, string password)
    {
        string h = salt + ":" + password;
        for (int i = 0; i < Iterations; i++)
            h = Sha256Hex(salt + h);
        return h;
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static Dictionary<string, (string Salt, string Hash)> Load()
    {
        var users = new Dictionary<string, (string, string)>();
        if (!FileAccess.FileExists(Path))
            return users;
        try
        {
            if (JsonNode.Parse(FileAccess.GetFileAsString(Path)) is JsonObject root)
            {
                foreach (var (name, node) in root)
                {
                    if (node is JsonObject rec && rec["salt"] is JsonNode salt && rec["hash"] is JsonNode hash)
                        users[name] = (salt.GetValue<string>(), hash.GetValue<string>());
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            GD.PushWarning($"Accounts file is unreadable: {e.Message}");
        }
        return users;
    }

    private static void Store(Dictionary<string, (string Salt, string Hash)> users)
    {
        var root = new JsonObject();
        foreach (var (name, (salt, hash)) in users)
            root[name] = new JsonObject { ["salt"] = salt, ["hash"] = hash };
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonText.Pretty(root));
    }

    private static (string Salt, string Hash) Make(string password)
    {
        string salt = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return (salt, Hash(salt, password));
    }

    /// <summary>Creates the default account when no accounts exist yet. Never overwrites an existing one.</summary>
    public static void EnsureDefault()
    {
        var users = Load();
        if (users.Count > 0)
            return;
        users[DefaultUser] = Make(DefaultPassword);
        Store(users);
    }

    public static int Count() => Load().Count;

    /// <summary>Checks a login (case-insensitive, trimmed) in constant time with respect to the hash.</summary>
    public static bool Verify(string user, string password)
    {
        if (!Load().TryGetValue(user.Trim().ToLowerInvariant(), out var rec))
            return false;
        byte[] expected = Encoding.ASCII.GetBytes(rec.Hash);
        byte[] actual = Encoding.ASCII.GetBytes(Hash(rec.Salt, password));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
