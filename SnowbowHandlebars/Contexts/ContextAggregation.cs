using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars.Contexts {
	public record ContextAggregation(ThemeConfig Theme, ArticleContext Article, PageContext Page) {
		public string Translate(string text) {
			return Theme.Translation[Page.Language ?? Theme.Languages[0]][text];
		}
		public string AbsoluteLanguagedPath(string path) {
			return Theme.BasePath + (Page.Language ?? Theme.Languages[0]) + path;
		}

		public string AbsolutePath(string path) {
			return Theme.BasePath + path.TrimStart('/');
		}

		public string TranslatePath(string language) {
			return Theme.BasePath + language + "/" + Page.RelativePath;
		}

	}
}
