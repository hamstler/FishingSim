using System.Threading.Tasks;
using FishingSim.Accounts;
using Godot;
using FileAccess = Godot.FileAccess;

namespace FishingSim.Tests;

/// <summary>The account store: default admin, hashing, verification.</summary>
public sealed class AccountsSuite : ITestSuite
{
    public string Name => "accounts";

    public Task Run(TestContext t)
    {
        UserStore.Path = "/tmp/fishsim_cs_users_test.json";
        if (FileAccess.FileExists(UserStore.Path))
            DirAccess.RemoveAbsolute(UserStore.Path);

        t.Check(UserStore.Count() == 0, "starts empty");
        UserStore.EnsureDefault();
        t.Check(UserStore.Count() == 1, "default account created: exactly one user");
        t.Check(UserStore.Verify("admin", "admin"), "admin / admin logs in");
        t.Check(UserStore.Verify("  ADMIN ", "admin"), "login is case-insensitive and trimmed");
        t.Check(!UserStore.Verify("admin", "Admin"), "password is case-sensitive");
        t.Check(!UserStore.Verify("admin", ""), "empty password rejected");
        t.Check(!UserStore.Verify("ktos", "admin"), "unknown user rejected");

        string raw = FileAccess.GetFileAsString(UserStore.Path);
        t.Check(!raw.Contains("\"password\"") && raw.Contains("\"hash\"") && raw.Contains("\"salt\""), "file holds salt and hash, no password field");
        string firstHash = System.Text.Json.Nodes.JsonNode.Parse(raw)!["admin"]!["hash"]!.GetValue<string>();
        t.Check(firstHash.Length == 64 && firstHash != "admin", "hash is 64 hex chars, not the password");
        UserStore.EnsureDefault();
        string again = System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(UserStore.Path))!["admin"]!["hash"]!.GetValue<string>();
        t.Check(UserStore.Count() == 1 && again == firstHash, "EnsureDefault never overwrites an existing account");

        // Compatibility: a record written by the earlier GDScript version still verifies.
        string legacySalt = "00112233445566778899aabbccddeeff";
        string h = legacySalt + ":" + "admin";
        for (int i = 0; i < UserStore.Iterations; i++)
            h = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacySalt + h))).ToLowerInvariant();
        using (var f = FileAccess.Open(UserStore.Path, FileAccess.ModeFlags.Write))
            f.StoreString($"{{\"admin\": {{\"salt\": \"{legacySalt}\", \"hash\": \"{h}\"}}}}");
        t.Check(UserStore.Verify("admin", "admin"), "hashes in the original format still verify");
        return Task.CompletedTask;
    }
}
