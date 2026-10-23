using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

// Owned by AutosaveWriter's single background worker. Never used on the UI thread.
public sealed class NotebookDatabase : IDisposable
{
    private readonly FileStream instanceLock;
    private readonly SqliteConnection connection;
    private bool markedClean;
    public LibrarySnapshot Snapshot { get; }

    public NotebookDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        instanceLock = new FileStream(Path.Combine(directory, "library.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "library.db"), Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
            if (version is not 0 and not 1) throw new InvalidDataException("This notebook library requires a newer app version.");
            Execute("""
                CREATE TABLE IF NOT EXISTS notebooks(id TEXT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS pages(id TEXT PRIMARY KEY, notebook_id TEXT NOT NULL REFERENCES notebooks(id),
                    name TEXT NOT NULL, position INTEGER NOT NULL, view_json TEXT);
                CREATE TABLE IF NOT EXISTS strokes(page_id TEXT NOT NULL REFERENCES pages(id), id TEXT NOT NULL,
                    ordinal INTEGER NOT NULL, data_json TEXT NOT NULL, PRIMARY KEY(page_id,id));
                CREATE INDEX IF NOT EXISTS strokes_page_order ON strokes(page_id,ordinal);
                CREATE TABLE IF NOT EXISTS app_state(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                PRAGMA user_version=1;
                """);
            if (!string.Equals(Convert.ToString(Scalar("PRAGMA quick_check;")), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Notebook integrity check failed. Your library has been left in place.");
            var recovered = GetState("clean_shutdown") == "false";
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM notebooks;")) == 0)
                CreateNotebook(new NotebookSummary(Guid.NewGuid(), "My notebook"), Guid.NewGuid());
            var notebooks = new List<NotebookSummary>();
            using (var command = Command("SELECT id,name FROM notebooks ORDER BY rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) notebooks.Add(new NotebookSummary(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
            var pages = new List<PageSummary>();
            using (var command = Command("SELECT id,notebook_id,name,position,view_json FROM pages ORDER BY position,rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) pages.Add(new PageSummary(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<PageView>(reader.GetString(4))));
            var last = Guid.TryParse(GetState("last_page"), out var id) && pages.Any(p => p.Id == id) ? id : pages[0].Id;
            SetState("clean_shutdown", "false");
            Snapshot = new LibrarySnapshot(notebooks, pages, last, recovered);
        }
        catch { connection.Dispose(); instanceLock.Dispose(); throw; }
    }

    public void CreateNotebook(NotebookSummary notebook, Guid firstPage)
    {
        using var transaction = connection.BeginTransaction();
        Execute("INSERT INTO notebooks(id,name) VALUES($id,$name);", transaction, ("$id", notebook.Id.ToString()), ("$name", notebook.Name));
        Execute("INSERT INTO pages(id,notebook_id,name,position) VALUES($id,$book,'Page 1',0);", transaction,
            ("$id", firstPage.ToString()), ("$book", notebook.Id.ToString()));
        transaction.Commit();
    }
    public void CreatePage(PageSummary page) => Execute(
        "INSERT INTO pages(id,notebook_id,name,position) VALUES($id,$book,$name,$position);", null,
        ("$id", page.Id.ToString()), ("$book", page.NotebookId.ToString()), ("$name", page.Name), ("$position", page.Position));
    public void RenameNotebook(Guid id, string name) => Execute("UPDATE notebooks SET name=$name WHERE id=$id;", null, ("$id", id.ToString()), ("$name", name));
    public void RenamePage(Guid id, string name) => Execute("UPDATE pages SET name=$name WHERE id=$id;", null, ("$id", id.ToString()), ("$name", name));
    public void SaveView(Guid id, PageView view) => Execute("UPDATE pages SET view_json=$view WHERE id=$id;", null,
        ("$id", id.ToString()), ("$view", JsonSerializer.Serialize(view)));
    public void SelectPage(Guid id) => SetState("last_page", id.ToString());
    public void MarkClean() { SetState("clean_shutdown", "true"); markedClean = true; }
    public void MarkActive()
    {
        if (!markedClean) return;
        SetState("clean_shutdown", "false"); markedClean = false;
    }

    public void ApplyDelta(Guid page, IReadOnlyList<StoredStroke> added, IReadOnlyList<Guid> removed)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var id in removed)
            Execute("DELETE FROM strokes WHERE page_id=$page AND id=$id;", transaction, ("$page", page.ToString()), ("$id", id.ToString()));
        foreach (var item in added)
            Execute("INSERT OR REPLACE INTO strokes(page_id,id,ordinal,data_json) VALUES($page,$id,(SELECT COALESCE(MAX(ordinal),-1)+1 FROM strokes WHERE page_id=$page),$data);", transaction,
                ("$page", page.ToString()), ("$id", item.Stroke.Id.ToString()), ("$data", JsonSerializer.Serialize(item.Stroke)));
        transaction.Commit();
    }

    public void ReplacePage(Guid id, PageDocument page)
    {
        page.Validate(enforceSizeLimits: false);
        using var transaction = connection.BeginTransaction();
        Execute("DELETE FROM strokes WHERE page_id=$page;", transaction, ("$page", id.ToString()));
        for (var index = 0; index < page.Strokes.Count; index++)
        {
            var stroke = page.Strokes[index];
            Execute("INSERT INTO strokes(page_id,id,ordinal,data_json) VALUES($page,$id,$ordinal,$data);", transaction,
                ("$page", id.ToString()), ("$id", stroke.Id.ToString()), ("$ordinal", index), ("$data", JsonSerializer.Serialize(stroke)));
        }
        transaction.Commit();
    }

    public PageDocument LoadPage(Guid page)
    {
        var strokes = new List<InkStroke>();
        using var command = Command("SELECT data_json FROM strokes WHERE page_id=$page ORDER BY ordinal,rowid;", null, ("$page", page.ToString()));
        using var reader = command.ExecuteReader();
        while (reader.Read()) strokes.Add(JsonSerializer.Deserialize<InkStroke>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid saved stroke."));
        var document = new PageDocument(PageDocument.CurrentVersion, PageDocument.DefaultWidth, PageDocument.DefaultHeight, strokes);
        document.Validate(enforceSizeLimits: false);
        return document;
    }

    private string? GetState(string key) => Convert.ToString(Scalar("SELECT value FROM app_state WHERE key=$key;", ("$key", key)));
    private void SetState(string key, string value) => Execute("INSERT OR REPLACE INTO app_state(key,value) VALUES($key,$value);", null, ("$key", key), ("$value", value));
    private object? Scalar(string sql, params (string Key, object Value)[] values)
    {
        using var command = Command(sql, null, values);
        return command.ExecuteScalar();
    }
    private void Execute(string sql, SqliteTransaction? transaction = null, params (string Key, object Value)[] values)
    {
        using var command = Command(sql, transaction, values);
        command.ExecuteNonQuery();
    }
    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null, params (string Key, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value);
        return command;
    }
    public void Dispose() { connection.Dispose(); instanceLock.Dispose(); }
}
