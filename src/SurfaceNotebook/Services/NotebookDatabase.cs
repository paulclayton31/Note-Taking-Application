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
            if (version is < 0 or > 3) throw new InvalidDataException("This notebook library requires a newer app version.");
            // Take a consistent SQLite backup before the first v0.2 library migration.
            var backupPath = Path.Combine(directory, "library-v02-backup.db");
            if (version == 1 && !File.Exists(backupPath))
            {
                Backup(backupPath);
            }
            if (version == 2) Backup(Path.Combine(directory, "library-preview2-backup.db"));
            Execute("""
                CREATE TABLE IF NOT EXISTS notebooks(id TEXT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS pages(id TEXT PRIMARY KEY, notebook_id TEXT NOT NULL REFERENCES notebooks(id),
                    name TEXT NOT NULL, position INTEGER NOT NULL, view_json TEXT);
                CREATE TABLE IF NOT EXISTS strokes(page_id TEXT NOT NULL REFERENCES pages(id), id TEXT NOT NULL,
                    ordinal INTEGER NOT NULL, data_json TEXT NOT NULL, PRIMARY KEY(page_id,id));
                CREATE INDEX IF NOT EXISTS strokes_page_order ON strokes(page_id,ordinal);
                CREATE TABLE IF NOT EXISTS app_state(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                """);
            if (version < 2)
            {
                using var migration = connection.BeginTransaction();
                Execute("ALTER TABLE notebooks ADD COLUMN paper_json TEXT; ALTER TABLE pages ADD COLUMN paper_json TEXT; ALTER TABLE pages ADD COLUMN objects_json TEXT; PRAGMA user_version=2;", migration);
                migration.Commit();
            }
            if (version < 3)
            {
                using var migration = connection.BeginTransaction();
                Execute("""
                    CREATE TABLE folders(id TEXT PRIMARY KEY,name TEXT NOT NULL,parent_id TEXT REFERENCES folders(id));
                    ALTER TABLE notebooks ADD COLUMN folder_id TEXT REFERENCES folders(id);
                    ALTER TABLE notebooks ADD COLUMN favorite INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE notebooks ADD COLUMN last_opened INTEGER NOT NULL DEFAULT 0;
                    PRAGMA user_version=3;
                    """, migration);
                migration.Commit();
            }
            if (!string.Equals(Convert.ToString(Scalar("PRAGMA quick_check;")), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Notebook integrity check failed. Your library has been left in place.");
            var recovered = GetState("clean_shutdown") == "false";
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM notebooks;")) == 0)
                CreateNotebook(new NotebookSummary(Guid.NewGuid(), "My notebook"), Guid.NewGuid());
            var notebooks = new List<NotebookSummary>();
            using (var command = Command("SELECT id,name,paper_json,folder_id,favorite,last_opened FROM notebooks ORDER BY rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) notebooks.Add(new NotebookSummary(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                    reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<PaperSettings>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetInt32(4) != 0, reader.GetInt64(5)));
            var pages = new List<PageSummary>();
            using (var command = Command("SELECT id,notebook_id,name,position,view_json,paper_json FROM pages ORDER BY position,rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) pages.Add(new PageSummary(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<PageView>(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<PaperSettings>(reader.GetString(5))));
            var last = Guid.TryParse(GetState("last_page"), out var id) && pages.Any(p => p.Id == id) ? id : pages[0].Id;
            SetState("clean_shutdown", "false");
            var preferences = GetState("workspace");
            WorkspacePreferences? workspace = null;
            try { if (!string.IsNullOrWhiteSpace(preferences)) workspace = JsonSerializer.Deserialize<WorkspacePreferences>(preferences); }
            catch (JsonException) { /* A corrupt preference must not prevent opening saved notes. */ }
            var folders = new List<FolderSummary>();
            using (var command = Command("SELECT id,name,parent_id FROM folders ORDER BY name;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) folders.Add(new FolderSummary(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2))));
            Snapshot = new LibrarySnapshot(notebooks, pages, last, recovered, workspace, folders);
        }
        catch { connection.Dispose(); instanceLock.Dispose(); throw; }
    }

    public void CreateNotebook(NotebookSummary notebook, Guid firstPage)
    {
        using var transaction = connection.BeginTransaction();
        Execute("INSERT INTO notebooks(id,name,paper_json,folder_id,favorite,last_opened) VALUES($id,$name,$paper,$folder,$favorite,$opened);", transaction,
            ("$id", notebook.Id.ToString()), ("$name", notebook.Name), ("$paper", JsonSerializer.Serialize(notebook.Paper ?? PaperSettings.Default)),
            ("$folder", SqlId(notebook.FolderId)), ("$favorite", notebook.Favorite ? 1 : 0), ("$opened", notebook.LastOpened));
        Execute("INSERT INTO pages(id,notebook_id,name,position,paper_json) VALUES($id,$book,'Page 1',0,$paper);", transaction,
            ("$id", firstPage.ToString()), ("$book", notebook.Id.ToString()), ("$paper", JsonSerializer.Serialize(notebook.Paper ?? PaperSettings.Default)));
        transaction.Commit();
    }
    public void CreatePage(PageSummary page) => Execute(
        "INSERT INTO pages(id,notebook_id,name,position,paper_json) VALUES($id,$book,$name,$position,$paper);", null,
        ("$id", page.Id.ToString()), ("$book", page.NotebookId.ToString()), ("$name", page.Name), ("$position", page.Position), ("$paper", JsonSerializer.Serialize(page.Paper ?? PaperSettings.Default)));
    public void RenameNotebook(Guid id, string name) => Execute("UPDATE notebooks SET name=$name WHERE id=$id;", null, ("$id", id.ToString()), ("$name", name));
    public void RenamePage(Guid id, string name) => Execute("UPDATE pages SET name=$name WHERE id=$id;", null, ("$id", id.ToString()), ("$name", name));
    public void SaveView(Guid id, PageView view) => Execute("UPDATE pages SET view_json=$view WHERE id=$id;", null,
        ("$id", id.ToString()), ("$view", JsonSerializer.Serialize(view)));
    public void SelectPage(Guid id, long? opened = null)
    {
        using var transaction = connection.BeginTransaction();
        Execute("INSERT OR REPLACE INTO app_state(key,value) VALUES('last_page',$id); UPDATE notebooks SET last_opened=$opened WHERE id=(SELECT notebook_id FROM pages WHERE id=$id);",
            transaction, ("$id", id.ToString()), ("$opened", opened ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        transaction.Commit();
    }
    public void SavePreferences(WorkspacePreferences preferences) => SetState("workspace", JsonSerializer.Serialize(preferences));
    public void SavePaper(Guid id, PaperSettings paper)
    {
        paper.Validate();
        Execute("UPDATE pages SET paper_json=$paper WHERE id=$id;", null, ("$id", id.ToString()), ("$paper", JsonSerializer.Serialize(paper)));
    }
    public void SaveNotebookPaper(Guid id, PaperSettings paper)
    {
        paper.Validate();
        using var transaction = connection.BeginTransaction();
        Execute("UPDATE notebooks SET paper_json=$paper WHERE id=$id; UPDATE pages SET paper_json=$paper WHERE notebook_id=$id;", transaction,
            ("$id", id.ToString()), ("$paper", JsonSerializer.Serialize(paper)));
        transaction.Commit();
    }
    public void SaveTexts(Guid id, List<PageText> texts) => Execute("UPDATE pages SET objects_json=$texts WHERE id=$id;", null,
        ("$id", id.ToString()), ("$texts", JsonSerializer.Serialize(texts)));
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
        var paper = page.Paper ?? (page.Width == PageDocument.DefaultWidth && page.Height == PageDocument.DefaultHeight
            ? PaperSettings.Default : PaperSettings.Default with { Format = PaperFormat.Custom, CustomWidth = page.Width / 96, CustomHeight = page.Height / 96 });
        Execute("UPDATE pages SET paper_json=$paper,objects_json=$texts WHERE id=$page;", transaction,
            ("$page", id.ToString()), ("$paper", JsonSerializer.Serialize(paper)), ("$texts", JsonSerializer.Serialize(page.Texts ?? [])));
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
        using (var command = Command("SELECT data_json FROM strokes WHERE page_id=$page ORDER BY ordinal,rowid;", null, ("$page", page.ToString())))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) strokes.Add(JsonSerializer.Deserialize<InkStroke>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid saved stroke."));
        var paperJson = Convert.ToString(Scalar("SELECT paper_json FROM pages WHERE id=$page;", ("$page", page.ToString())));
        var objectsJson = Convert.ToString(Scalar("SELECT objects_json FROM pages WHERE id=$page;", ("$page", page.ToString())));
        var paper = string.IsNullOrEmpty(paperJson) ? PaperSettings.Default : JsonSerializer.Deserialize<PaperSettings>(paperJson) ?? PaperSettings.Default;
        var texts = string.IsNullOrEmpty(objectsJson) ? [] : JsonSerializer.Deserialize<List<PageText>>(objectsJson) ?? [];
        var document = new PageDocument(PageDocument.CurrentVersion, paper.Width, paper.Height, strokes, paper, texts);
        document.Validate(enforceSizeLimits: false);
        return document;
    }

    public void CreateFolder(FolderSummary folder) => Execute("INSERT INTO folders(id,name,parent_id) VALUES($id,$name,$parent);", null,
        ("$id", folder.Id.ToString()), ("$name", folder.Name), ("$parent", SqlId(folder.ParentId)));
    public void RenameFolder(Guid id, string name) => Execute("UPDATE folders SET name=$name WHERE id=$id;", null, ("$id", id.ToString()), ("$name", name));
    public void MoveNotebook(Guid id, Guid? folder) => Execute("UPDATE notebooks SET folder_id=$folder WHERE id=$id;", null,
        ("$id", id.ToString()), ("$folder", SqlId(folder)));
    public void FavoriteNotebook(Guid id, bool favorite) => Execute("UPDATE notebooks SET favorite=$favorite WHERE id=$id;", null,
        ("$id", id.ToString()), ("$favorite", favorite ? 1 : 0));
    public void MoveFolder(Guid id, Guid? parent)
    {
        if (parent is Guid target)
        {
            var cycle = Convert.ToInt64(Scalar("""
                WITH RECURSIVE descendants(id) AS (
                    SELECT id FROM folders WHERE id=$id UNION SELECT f.id FROM folders f JOIN descendants d ON f.parent_id=d.id
                ) SELECT COUNT(*) FROM descendants WHERE id=$parent;
                """, ("$id", id.ToString()), ("$parent", target.ToString())));
            if (cycle != 0) throw new InvalidOperationException("A folder cannot be moved into itself or one of its subfolders.");
        }
        Execute("UPDATE folders SET parent_id=$parent WHERE id=$id;", null, ("$id", id.ToString()), ("$parent", SqlId(parent)));
    }
    public DuplicatedPage DuplicatePage(Guid source, Guid destination, string name)
    {
        var original = LoadPage(source);
        Guid notebook; int position; PageView? view;
        using (var command = Command("SELECT notebook_id,position,view_json FROM pages WHERE id=$id;", null, ("$id", source.ToString())))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("The source page no longer exists.");
            notebook = Guid.Parse(reader.GetString(0)); position = reader.GetInt32(1) + 1;
            view = reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<PageView>(reader.GetString(2));
        }
        var document = original with
        {
            Strokes = original.Strokes.Select(s => s with { Id = Guid.NewGuid() }).ToList(),
            Texts = original.Texts?.Select(t => t with { Id = Guid.NewGuid() }).ToList()
        };
        var page = new PageSummary(destination, notebook, name, position, view, document.Paper);
        using var transaction = connection.BeginTransaction();
        Execute("UPDATE pages SET position=position+1 WHERE notebook_id=$book AND position >= $position;", transaction,
            ("$book", notebook.ToString()), ("$position", position));
        Execute("INSERT INTO pages(id,notebook_id,name,position,view_json,paper_json,objects_json) VALUES($id,$book,$name,$position,$view,$paper,$texts);", transaction,
            ("$id", destination.ToString()), ("$book", notebook.ToString()), ("$name", name), ("$position", position),
            ("$view", JsonSerializer.Serialize(view)), ("$paper", JsonSerializer.Serialize(document.Paper)),
            ("$texts", JsonSerializer.Serialize(document.Texts ?? [])));
        for (var i = 0; i < document.Strokes.Count; i++)
        {
            var stroke = document.Strokes[i];
            Execute("INSERT INTO strokes(page_id,id,ordinal,data_json) VALUES($page,$id,$ordinal,$data);", transaction,
                ("$page", destination.ToString()), ("$id", stroke.Id.ToString()), ("$ordinal", i), ("$data", JsonSerializer.Serialize(stroke)));
        }
        transaction.Commit();
        return new DuplicatedPage(page, document);
    }
    public void ReorderPages(Guid notebook, IReadOnlyList<Guid> order)
    {
        var current = new HashSet<Guid>();
        using (var command = Command("SELECT id FROM pages WHERE notebook_id=$book;", null, ("$book", notebook.ToString())))
        using (var reader = command.ExecuteReader()) while (reader.Read()) current.Add(Guid.Parse(reader.GetString(0)));
        if (order.Count != current.Count || order.Distinct().Count() != order.Count || !current.SetEquals(order))
            throw new InvalidOperationException("Page order must contain each page in the notebook exactly once.");
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < order.Count; i++) Execute("UPDATE pages SET position=$position WHERE id=$id AND notebook_id=$book;", transaction,
            ("$position", i), ("$id", order[i].ToString()), ("$book", notebook.ToString()));
        transaction.Commit();
    }
    private static object SqlId(Guid? id) => id is Guid value ? value.ToString() : DBNull.Value;
    private void Backup(string path)
    {
        if (File.Exists(path)) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            { backup.Open(); connection.BackupDatabase(backup); }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
