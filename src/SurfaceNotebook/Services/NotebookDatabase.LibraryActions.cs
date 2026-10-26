using System.Text.Json;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public sealed partial class NotebookDatabase
{
    public NotebookCopy PasteNotebook(NotebookCopy source, string name, Guid? folder)
    {
        var notebook = source.Notebook with { Id = Guid.NewGuid(), Name = name, FolderId = folder, Favorite = false, LastOpened = 0 };
        var copies = source.Pages.Select((item, index) =>
        {
            var document = item.Document with
            {
                Strokes = item.Document.Strokes.Select(s => s with { Id = Guid.NewGuid() }).ToList(),
                Texts = item.Document.Texts?.Select(t => t with { Id = Guid.NewGuid() }).ToList()
            };
            return new DuplicatedPage(item.Page with { Id = Guid.NewGuid(), NotebookId = notebook.Id, Position = index }, document);
        }).ToList();
        using var transaction = connection.BeginTransaction();
        Execute("INSERT INTO notebooks(id,name,paper_json,folder_id) VALUES($id,$name,$paper,$folder);", transaction,
            ("$id", notebook.Id.ToString()), ("$name", name), ("$paper", JsonSerializer.Serialize(notebook.Paper)), ("$folder", SqlId(folder)));
        foreach (var copy in copies)
        {
            Execute("INSERT INTO pages(id,notebook_id,name,position,view_json,paper_json,objects_json,background_json) VALUES($id,$book,$name,$position,$view,$paper,$texts,$background);", transaction,
                ("$id", copy.Page.Id.ToString()), ("$book", notebook.Id.ToString()), ("$name", copy.Page.Name), ("$position", copy.Page.Position),
                ("$view", JsonSerializer.Serialize(copy.Page.View)), ("$paper", JsonSerializer.Serialize(copy.Document.Paper)), ("$texts", JsonSerializer.Serialize(copy.Document.Texts ?? [])),
                ("$background", copy.Document.Background is null ? DBNull.Value : JsonSerializer.Serialize(copy.Document.Background)));
            for (var i = 0; i < copy.Document.Strokes.Count; i++)
            {
                var stroke = copy.Document.Strokes[i];
                Execute("INSERT INTO strokes(page_id,id,ordinal,data_json) VALUES($page,$id,$ordinal,$data);", transaction,
                    ("$page", copy.Page.Id.ToString()), ("$id", stroke.Id.ToString()), ("$ordinal", i), ("$data", JsonSerializer.Serialize(stroke)));
            }
        }
        transaction.Commit();
        return new NotebookCopy(notebook, copies);
    }

    public void DeletePage(Guid id)
    {
        string notebook; int position;
        using (var command = Command("SELECT notebook_id,position FROM pages WHERE id=$id;", null, ("$id", id.ToString())))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("The page no longer exists.");
            notebook = reader.GetString(0); position = reader.GetInt32(1);
        }
        using var transaction = connection.BeginTransaction();
        Execute("DELETE FROM strokes WHERE page_id=$id; DELETE FROM pages WHERE id=$id;", transaction, ("$id", id.ToString()));
        Execute("UPDATE pages SET position=position-1 WHERE notebook_id=$book AND position>$position;", transaction,
            ("$book", notebook), ("$position", position));
        transaction.Commit();
    }

    public void DeleteNotebook(Guid id)
    {
        using var transaction = connection.BeginTransaction();
        Execute("DELETE FROM strokes WHERE page_id IN (SELECT id FROM pages WHERE notebook_id=$id); DELETE FROM pages WHERE notebook_id=$id; DELETE FROM notebooks WHERE id=$id;", transaction,
            ("$id", id.ToString()));
        transaction.Commit();
    }
}
