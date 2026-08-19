using CvaAnalyzer.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace CvaAnalyzer.Services;

public class ReactionDatabaseService
{
    private static string GetDbPath()
    {
        var exeDir = AppContext.BaseDirectory;
        return Path.Combine(exeDir, "reactions.db");
    }

    private string DbPath => GetDbPath();

    public ReactionDatabaseService()
    {
        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Reactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Reaction TEXT NOT NULL,
                E0 REAL NOT NULL,
                N INTEGER NOT NULL,
                KHPlus REAL NOT NULL,
                KOHMinus REAL NOT NULL,
                UNIQUE(Reaction, E0, N, KHPlus, KOHMinus)
            );";
        command.ExecuteNonQuery();
    }

    public void SaveReaction(ReactionEntry entry)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO Reactions
            (Reaction, E0, N, KHPlus, KOHMinus)
            VALUES (@r, @e0, @n, @kh, @koh)";
        command.Parameters.AddWithValue("@r", entry.Reaction);
        command.Parameters.AddWithValue("@e0", entry.E0);
        command.Parameters.AddWithValue("@n", entry.N);
        command.Parameters.AddWithValue("@kh", entry.KHPlus);
        command.Parameters.AddWithValue("@koh", entry.KOHMinus);
        command.ExecuteNonQuery();
    }

    public void SaveReactions(IEnumerable<ReactionEntry> entries)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO Reactions
            (Reaction, E0, N, KHPlus, KOHMinus)
            VALUES (@r, @e0, @n, @kh, @koh)";

        var reactionParam = command.CreateParameter();
        reactionParam.ParameterName = "@r";
        command.Parameters.Add(reactionParam);

        var e0Param = command.CreateParameter();
        e0Param.ParameterName = "@e0";
        command.Parameters.Add(e0Param);

        var nParam = command.CreateParameter();
        nParam.ParameterName = "@n";
        command.Parameters.Add(nParam);

        var khParam = command.CreateParameter();
        khParam.ParameterName = "@kh";
        command.Parameters.Add(khParam);

        var kohParam = command.CreateParameter();
        kohParam.ParameterName = "@koh";
        command.Parameters.Add(kohParam);

        foreach (var entry in entries)
        {
            reactionParam.Value = entry.Reaction;
            e0Param.Value = entry.E0;
            nParam.Value = entry.N;
            khParam.Value = entry.KHPlus;
            kohParam.Value = entry.KOHMinus;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<ReactionEntry> LoadAll()
    {
        var results = new List<ReactionEntry>();
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Reaction, E0, N, KHPlus, KOHMinus FROM Reactions";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ReactionEntry
            {
                Reaction = reader.GetString(0),
                E0 = reader.GetDouble(1),
                N = reader.GetInt32(2),
                KHPlus = reader.GetDouble(3),
                KOHMinus = reader.GetDouble(4)
            });
        }

        return results;
    }
}
