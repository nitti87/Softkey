using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

static class WordListSearch
{
    public static IEnumerable<string> Find(IEnumerable<string> words, string text)
    {
        string query = text.Trim().Normalize(NormalizationForm.FormC);
        return words.Where(word => query.Length == 0 || word.Normalize(NormalizationForm.FormC)
                .StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(word => word, StringComparer.CurrentCultureIgnoreCase);
    }
}
