using System.IO;

namespace SurfaceNotebook.Models;

public sealed record AiCalculation(string Model, string Problem, string Answer, string Explanation,
    string Instruction, byte[]? ImagePng = null, SolutionStep[]? Steps = null)
{
    public string DisplayText => $"Answer\n{Answer}\n\nWhat I read\n{Problem}\n\nSolution\n{SolutionText}";
    public string SolutionText => Steps is { Length: > 0 } ? string.Join("\n\n", Steps.Select((step, i) => $"{i + 1}. {step.Title}\n{step.Detail}" + (step.Equations.Length == 0 ? "" : "\n\n" + string.Join("\n", step.Equations)))) : Explanation;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || string.IsNullOrWhiteSpace(Problem) || Problem.Length > 12000 ||
            string.IsNullOrWhiteSpace(Answer) || Answer.Length > 12000 || Explanation is null || Explanation.Length > 24000 ||
            Instruction is null || Instruction.Length > 12000) throw new InvalidDataException("Invalid AI calculation.");
        ValidateSteps(Steps);
        if (ImagePng is { Length: > 8_000_000 }) throw new InvalidDataException("AI source image is too large.");
        if (ImagePng is not null) new PageImage(Guid.NewGuid(), 0, 0, 1, 1, ImagePng).Validate();
    }
    public static void ValidateSteps(SolutionStep[]? steps)
    {
        if (steps is null) return; // Older saved results remain readable.
        if (steps.Length > 20 || steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 160 ||
            s.Detail is null || s.Detail.Length > 2000 || s.Equations is null || s.Equations.Length > 8 ||
            s.Equations.Any(e => string.IsNullOrWhiteSpace(e) || e.Length > 1000)) ||
            steps.Sum(s => s.Title.Length + s.Detail.Length + s.Equations.Sum(e => e.Length)) > 24000)
            throw new InvalidDataException("The solution steps are invalid or too long.");
    }
}

public sealed record SolutionStep(string Title, string Detail, string[] Equations)
{
    public string EquationText => string.Join("\n", Equations);
}
