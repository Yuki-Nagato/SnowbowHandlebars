using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars.Contexts {
	public record ArticleContext(Dictionary<string, List<PageContext>> LanguageArticlesDictionary, Dictionary<string, SortedDictionary<string, List<PageContext>>> LanguageTagArticlesDictionary, Dictionary<string, SortedDictionary<string, List<PageContext>>> LanguageCategoryArticlesDictionary);
}
