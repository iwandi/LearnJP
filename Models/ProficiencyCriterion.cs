namespace LearnJP.Models;

public enum ProficiencyCriterion
{
    JapaneseToEnglish,
    EnglishToJapanese,
    SimilarSoundDifferentiation,
    SimilarMeaningDifferentiation
}

public static class ProficiencyCriterionExtensions
{
    public static string Display(this ProficiencyCriterion c) => c switch
    {
        ProficiencyCriterion.JapaneseToEnglish => "JP → EN",
        ProficiencyCriterion.EnglishToJapanese => "EN → JP",
        ProficiencyCriterion.SimilarSoundDifferentiation => "Similar sound",
        ProficiencyCriterion.SimilarMeaningDifferentiation => "Similar meaning",
        _ => c.ToString()
    };

    public static IReadOnlyList<ProficiencyCriterion> All { get; } = new[]
    {
        ProficiencyCriterion.JapaneseToEnglish,
        ProficiencyCriterion.EnglishToJapanese,
        ProficiencyCriterion.SimilarSoundDifferentiation,
        ProficiencyCriterion.SimilarMeaningDifferentiation
    };
}
