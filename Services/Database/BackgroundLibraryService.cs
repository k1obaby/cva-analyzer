using CvaAnalyzer.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.IO;

namespace CvaAnalyzer.Services.Database;

public class BackgroundLibraryService
{
    private static string GetDbPath()
    {
        var exeDir = AppContext.BaseDirectory;
        return Path.Combine(exeDir, "backgrounds.db");
    }

    private string DbPath => GetDbPath();

    public BackgroundLibraryService()
    {
        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Backgrounds (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SampleName TEXT NOT NULL,
                ScanRate REAL NOT NULL,
                Electrolyte TEXT,
                WorkingElectrode TEXT,
                ReferenceElectrode TEXT,
                Atmosphere TEXT,
                CellType TEXT,
                DepositionMethod TEXT,
                Illumination TEXT,
                DataJson TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }

    public void SaveBackground(BackgroundData background)
    {
        var json = JsonSerializer.Serialize(background.Points);

        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Backgrounds (
                SampleName, ScanRate, Electrolyte, WorkingElectrode,
                ReferenceElectrode, Atmosphere, CellType, DepositionMethod, Illumination, DataJson
            ) VALUES (@sn, @sr, @el, @we, @re, @at, @ct, @dm, @il, @dj)";

        command.Parameters.AddWithValue("@sn", background.SampleName);
        command.Parameters.AddWithValue("@sr", background.Metadata.ScanRate);
        command.Parameters.AddWithValue("@el", background.Metadata.Electrolyte);
        command.Parameters.AddWithValue("@we", background.Metadata.WorkingElectrode);
        command.Parameters.AddWithValue("@re", background.Metadata.ReferenceElectrode);
        command.Parameters.AddWithValue("@at", background.Metadata.Atmosphere);
        command.Parameters.AddWithValue("@ct", background.Metadata.CellType);
        command.Parameters.AddWithValue("@dm", background.Metadata.DepositionMethod);
        command.Parameters.AddWithValue("@il", background.Metadata.Illumination);
        command.Parameters.AddWithValue("@dj", json);

        command.ExecuteNonQuery();
    }

    public void UpdateBackground(BackgroundData background)
    {
        if (background?.Metadata == null || background.Metadata.Id <= 0)
            return;

        var json = JsonSerializer.Serialize(background.Points);

        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Backgrounds SET
                SampleName = @sn, ScanRate = @sr, Electrolyte = @el, WorkingElectrode = @we,
                ReferenceElectrode = @re, Atmosphere = @at, CellType = @ct, DepositionMethod = @dm, Illumination = @il, DataJson = @dj
            WHERE Id = @id";

        command.Parameters.AddWithValue("@id", background.Metadata.Id);
        command.Parameters.AddWithValue("@sn", background.SampleName);
        command.Parameters.AddWithValue("@sr", background.Metadata.ScanRate);
        command.Parameters.AddWithValue("@el", background.Metadata.Electrolyte);
        command.Parameters.AddWithValue("@we", background.Metadata.WorkingElectrode);
        command.Parameters.AddWithValue("@re", background.Metadata.ReferenceElectrode);
        command.Parameters.AddWithValue("@at", background.Metadata.Atmosphere);
        command.Parameters.AddWithValue("@ct", background.Metadata.CellType);
        command.Parameters.AddWithValue("@dm", background.Metadata.DepositionMethod);
        command.Parameters.AddWithValue("@il", background.Metadata.Illumination);
        command.Parameters.AddWithValue("@dj", json);

        command.ExecuteNonQuery();
    }

    public void DeleteBackground(int id)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Backgrounds WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public List<BackgroundData> LoadAllBackgrounds()
    {
        var backgrounds = new List<BackgroundData>();
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Backgrounds";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var metadata = new BackgroundMetadata
            {
                Id = reader.GetInt32(0),
                ScanRate = reader.GetDouble(2),
                Electrolyte = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                WorkingElectrode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                ReferenceElectrode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Atmosphere = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                CellType = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                DepositionMethod = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                Illumination = reader.IsDBNull(9) ? string.Empty : reader.GetString(9)
            };

            var points = JsonSerializer.Deserialize<List<VoltammetryPoint>>(reader.GetString(10)) ?? new List<VoltammetryPoint>();

            backgrounds.Add(new BackgroundData
            {
                SampleName = reader.GetString(1),
                Points = { },
                Metadata = metadata
            });

            backgrounds[^1].Points.Clear();
            foreach (var p in points)
                backgrounds[^1].Points.Add(p);
        }

        return backgrounds;
    }
}