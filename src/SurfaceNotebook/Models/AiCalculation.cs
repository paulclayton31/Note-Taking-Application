using System.IO;

namespace SurfaceNotebook.Models;

public sealed record AiCalculation(string Model, string Problem, string Answer, string Explanation,
    string Instruction, byte[]? ImagePng = null)
{
    public string DisplayText => $"Gemini · {Model}\n\nWhat I read\n{Problem}\n\nAnswer\n{Answer}\n\nSolution\n{Explanation}";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || string.IsNullOrWhiteSpace(Problem) || Problem.Length > 12000 ||
            string.IsNullOrWhiteSpace(Answer) || Answer.Length > 12000 || Explanation is null || Explanation.Length > 24000 ||
            Instruction is null || Instruction.Length > 12000) throw new InvalidDataException("Invalid AI calculation.");
        if (ImagePng is { Length: > 8_000_000 }) throw new InvalidDataException("AI source image is too large.");
        if (ImagePng is not null) new PageImage(Guid.NewGuid(), 0, 0, 1, 1, ImagePng).Validate();
    }
}
