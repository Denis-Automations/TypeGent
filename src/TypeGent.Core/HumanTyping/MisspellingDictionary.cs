namespace TypeGent.Core.HumanTyping;

/// <summary>
/// A curated dictionary of common English cognitive misspellings (not mechanical
/// finger-slip typos). Each entry maps the <em>correct</em> spelling to the
/// <em>misspelled</em> variant that a real user might type from memory.
///
/// These are <strong>knowledge errors</strong>, not motor errors: the typist "knows"
/// the wrong spelling. Phase 7 (v2) of the build plan.
/// </summary>
public static class MisspellingDictionary
{
    /// <summary>
    /// Maps correct word (lowercase) → common misspelling. All comparisons are
    /// case-insensitive (the engine restores original case when it applies one).
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
            ["resistance"]   = "resistance",  // correct; include anyway for mix
            ["experience"]   = "experiance",
            ["occurrence"]   = "occurrance",
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

            // Phonetic confusions
            ["which"]        = "wich",
            ["would"]        = "woud",
            ["could"]        = "coud",
            ["should"]       = "shoud",
            ["because"]      = "becuase",
            ["together"]     = "togeather",
            ["whether"]      = "wether",
            ["weather"]      = "wether",
            ["quite"]        = "quite",     // often confused with "quiet" — same string; engine skips no-ops
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

            // Homophones & near-homophones
            ["accept"]       = "except",
            ["except"]       = "accept",
            ["affect"]       = "effect",
            ["effect"]       = "affect",
            ["principal"]    = "principle",
            ["stationary"]   = "stationery",
            ["compliment"]   = "complement",
            ["discrete"]     = "discreet",
            ["elicit"]       = "illicit",
            ["emigrate"]     = "immigrate",
            ["ensure"]       = "insure",
            ["foreword"]     = "forward",
            ["further"]      = "farther",
            ["ingenious"]    = "ingenuous",
            ["loath"]        = "loathe",
            ["moot"]         = "mute",
            ["passed"]       = "past",
            ["peak"]         = "peek",
            ["precede"]      = "proceed",
            ["stationary"]   = "stationery",
            ["strait"]       = "straight",
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

        // Skip no-op entries (e.g. "quite"→"quite")
        return !string.Equals(word, misspelling, StringComparison.OrdinalIgnoreCase);
    }
}
