namespace SENGENSystem.Server.Features.Survey
{
    // The fixed ISO/IEC 25010 rating instrument, authored bilingually (English + Filipino) so every
    // respondent understands each item. The Likert answers are keyed by QuestionKey; SurveyContent is
    // the single source of truth the client renders and the results aggregate against. Question keys
    // are a stable contract — append, never renumber, so historical responses keep their meaning.
    public sealed record SurveyQuestion(string Key, string En, string Fil);

    public sealed record SurveyCharacteristic(string Code, string NameEn, string NameFil, string HintEn, string HintFil, IReadOnlyList<SurveyQuestion> Questions);

    public sealed record SurveyScalePoint(int Value, string En, string Fil);

    public static class SurveyContent
    {
        public const string Title = "SEN-GEN Software Quality Evaluation (ISO/IEC 25010)";
        public const string TitleFil = "Pagsusuri sa Kalidad ng SEN-GEN (ISO/IEC 25010)";

        public static readonly IReadOnlyList<SurveyScalePoint> Scale =
        [
            new(1, "Strongly Disagree", "Lubos na Hindi Sang-ayon"),
            new(2, "Disagree", "Hindi Sang-ayon"),
            new(3, "Neutral", "Katamtaman"),
            new(4, "Agree", "Sang-ayon"),
            new(5, "Strongly Agree", "Lubos na Sang-ayon")
        ];

        public static readonly IReadOnlyList<SurveyCharacteristic> Characteristics =
        [
            new("functional_suitability", "Functional Suitability", "Angkop na Punksyonalidad",
                "Whether the system does what you need, correctly.",
                "Kung ginagawa ng sistema ang kailangan mo, nang tama.",
            [
                new("fs1",
                    "The system provides all the functions I need for my enrollment or scheduling tasks.",
                    "Naibibigay ng sistema ang lahat ng tungkuling kailangan ko sa aking gawain sa enrollment o scheduling."),
                new("fs2",
                    "The functions produce correct and accurate results.",
                    "Ang mga tungkulin ay nagbibigay ng tama at wastong resulta."),
            ]),
            new("performance_efficiency", "Performance Efficiency", "Kahusayan sa Pagganap",
                "How fast and resource-efficient the system feels.",
                "Kung gaano kabilis at matipid sa rekurso ang sistema.",
            [
                new("pe1",
                    "The system responds quickly to my actions.",
                    "Mabilis tumugon ang sistema sa aking mga aksyon."),
                new("pe2",
                    "The system loads pages and completes tasks in a reasonable time.",
                    "Mabilis maglo-load ang mga pahina at natatapos ang mga gawain sa makatwirang oras."),
            ]),
            new("compatibility", "Compatibility", "Pagkakatugma",
                "How well it works with your devices, browsers, and other tools.",
                "Kung gaano kaayos itong gumagana sa iyong device, browser, at ibang tool.",
            [
                new("co1",
                    "The system works well on the devices and browsers I use.",
                    "Gumagana nang maayos ang sistema sa mga device at browser na ginagamit ko."),
            ]),
            new("usability", "Usability", "Kadalian ng Paggamit",
                "How easy the system is to learn and use.",
                "Kung gaano kadaling matutunan at gamitin ang sistema.",
            [
                new("us1",
                    "The system is easy to learn and navigate.",
                    "Madaling matutunan at libutin ang sistema."),
                new("us2",
                    "The interface is clear and the instructions are easy to understand.",
                    "Malinaw ang interface at madaling maunawaan ang mga tagubilin."),
            ]),
            new("reliability", "Reliability", "Pagiging Maaasahan",
                "Whether it works consistently without failing.",
                "Kung gumagana itong tuloy-tuloy nang hindi nagkakaproblema.",
            [
                new("re1",
                    "The system works consistently without crashing or unexpected errors.",
                    "Gumagana nang tuloy-tuloy ang sistema nang walang pag-crash o hindi inaasahang error."),
            ]),
            new("security", "Security", "Seguridad",
                "Whether your data and account are kept safe.",
                "Kung pinananatiling ligtas ang iyong datos at account.",
            [
                new("se1",
                    "I trust that my personal data and account are kept secure.",
                    "Nagtitiwala akong pinananatiling ligtas ang aking personal na datos at account."),
            ]),
            new("maintainability", "Maintainability", "Kadalian ng Pagpapanatili",
                "Whether updates and fixes happen smoothly.",
                "Kung maayos na naisasagawa ang mga update at pag-aayos.",
            [
                new("ma1",
                    "Updates and fixes are applied smoothly without disrupting my work.",
                    "Ang mga update at pag-aayos ay maayos na naisasagawa nang hindi nakakaabala sa aking gawain."),
            ]),
            new("portability", "Portability", "Pagiging Madaling Ilipat",
                "Whether you can use it across different devices or setups.",
                "Kung magagamit mo ito sa iba't ibang device o setup.",
            [
                new("po1",
                    "I can use the system on different devices or browsers without problems.",
                    "Magagamit ko ang sistema sa iba't ibang device o browser nang walang problema."),
            ]),
        ];

        public const string SuggestionsEn = "Suggestions — what would make SEN-GEN better for you?";
        public const string SuggestionsFil = "Mga Mungkahi — ano ang makapagpapabuti sa SEN-GEN para sa iyo?";
        public const string CommentsEn = "Further Comments";
        public const string CommentsFil = "Karagdagang Puna";

        /// <summary>Every question key in the instrument, for validating a submission is complete.</summary>
        public static IReadOnlyList<string> AllQuestionKeys() =>
            Characteristics.SelectMany(c => c.Questions.Select(q => q.Key)).ToList();

        /// <summary>Maps each question key to the characteristic code it scores, for aggregation.</summary>
        public static IReadOnlyDictionary<string, string> QuestionCharacteristic() =>
            Characteristics
                .SelectMany(c => c.Questions.Select(q => (q.Key, c.Code)))
                .ToDictionary(x => x.Key, x => x.Code);
    }
}
