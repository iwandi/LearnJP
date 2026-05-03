namespace LearnJP.Models;

public enum ProficiencyCriterion
{
    TargetToBase,
    BaseToTarget,
    SimilarSoundDifferentiation,
    SimilarMeaningDifferentiation
}

public static class ProficiencyCriterionExtensions
{
    public static string Display(this ProficiencyCriterion c) => c switch
    {
        ProficiencyCriterion.TargetToBase => "Target → Base",
        ProficiencyCriterion.BaseToTarget => "Base → Target",
        ProficiencyCriterion.SimilarSoundDifferentiation => "Similar sound",
        ProficiencyCriterion.SimilarMeaningDifferentiation => "Similar meaning",
        _ => c.ToString()
    };

    public static IReadOnlyList<ProficiencyCriterion> All { get; } = new[]
    {
        ProficiencyCriterion.TargetToBase,
        ProficiencyCriterion.BaseToTarget,
        ProficiencyCriterion.SimilarSoundDifferentiation,
        ProficiencyCriterion.SimilarMeaningDifferentiation
    };
}
