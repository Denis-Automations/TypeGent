namespace TypeGent.Core.HumanTyping;

/// <summary>
/// A curated dictionary of common English cognitive misspellings (not mechanical
/// finger-slip typos). Each entry maps the <em>correct</em> spelling to a
/// <em>misspelled</em> non-word variant that a real user might type from memory.
///
/// These are <strong>knowledge errors</strong>, not motor errors: the typist "knows"
/// the wrong spelling. Phase 7 (v2) of the build plan.
/// <para>
/// Scope (Phase A5): strictly orthographic misspellings — the misspelled form is not a
/// correctly-spelled English word. Wrong-word homophone substitutions (e.g.
/// <c>accept</c>→<c>except</c>, <c>affect</c>→<c>effect</c>, <c>weather</c>→<c>wether</c>)
/// are intentionally excluded; they produce semantically odd "type the wrong word, then
/// fix it" sequences and belong to a different error class.
/// </para>
/// </summary>
public static class MisspellingDictionary
{
    /// <summary>
    /// Maps correct word (lowercase) → common misspelling. All comparisons are
    /// case-insensitive (the engine restores original case when it applies one).
    /// Every value differs from its key — there are no no-op (self-mapping) entries.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Entries =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Classic ie/ei confusions
            ["receive"]      = "recieve",
            ["believe"]      = "beleive",
            ["achieve"]      = "acheive",
            ["friend"]       = "freind",
            ["piece"]        = "peice",
            ["field"]        = "feild",
            ["yield"]        = "yeild",
            ["siege"]        = "seige",
            ["weird"]        = "wierd",
            ["their"]        = "thier",

            // Double-letter confusion (too few or too many)
            ["occurrence"]   = "occurence",
            ["necessary"]    = "neccessary",
            ["recommend"]    = "reccommend",
            ["accommodate"]  = "acommodate",
            ["committee"]    = "commitee",
            ["tomorrow"]     = "tommorrow",
            ["disappear"]    = "dissapear",
            ["possession"]   = "posession",
            ["address"]      = "adress",
            ["professor"]    = "proffessor",
            ["occasion"]     = "occassion",
            ["embarrass"]    = "embarass",
            ["aggression"]   = "agression",
            ["millennium"]   = "millenium",
            ["parallel"]     = "paralell",

            // Silent-letter / unstressed-vowel confusions
            ["separate"]     = "seperate",
            ["definitely"]   = "definately",
            ["conscience"]   = "consience",
            ["conscientious"]= "consciencious",
            ["environment"]  = "enviroment",
            ["government"]   = "goverment",
            ["maintenance"]  = "maintainance",
            ["relevant"]     = "relevent",
            ["prevalent"]    = "prevelant",
            ["independent"]  = "independant",
            ["dependent"]    = "dependant",
            ["existence"]    = "existance",
            ["experience"]   = "experiance",
            ["hierarchy"]    = "heirarchy",
            ["category"]     = "catagory",
            ["February"]     = "Febuary",
            ["Wednesday"]    = "Wendsday",
            ["library"]      = "libary",
            ["surprise"]     = "suprise",
            ["guarantee"]    = "guarentee",
            ["privilege"]    = "priviledge",
            ["license"]      = "lisence",
            ["rhythm"]       = "rythm",
            ["parliament"]   = "parliment",
            ["pronunciation"] = "pronounciation",
            ["harassment"]   = "harrassment",
            ["questionnaire"] = "questionaire",

            // Dropped-letter & doubling misspellings
            ["which"]        = "wich",
            ["would"]        = "woud",
            ["could"]        = "coud",
            ["should"]       = "shoud",
            ["because"]      = "becuase",
            ["together"]     = "togeather",
            ["losing"]       = "loosing",
            ["choosing"]     = "chosing",
            ["writing"]      = "writting",
            ["coming"]       = "comming",
            ["running"]      = "runing",
            ["beginning"]    = "begining",
            ["occurring"]    = "occuring",
            ["referring"]    = "refering",
            ["transferred"]  = "transfered",
            ["preferred"]    = "prefered",
        };

    /// <summary>
    /// Try to get the misspelled form for <paramref name="word"/>.
    /// Returns <see langword="false"/> when the word is not in the dictionary or
    /// the misspelling would be identical (a no-op entry).
    /// </summary>
    public static bool TryGet(string word, out string misspelling)
    {
        if (!Entries.TryGetValue(word, out misspelling!))
            return false;

        // Skip no-op entries (e.g. a word mapped to itself)
        return !string.Equals(word, misspelling, StringComparison.OrdinalIgnoreCase);
    }
}
