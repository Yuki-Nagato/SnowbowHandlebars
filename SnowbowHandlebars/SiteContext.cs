using Newtonsoft.Json.Linq;
using System.Text;
using static SnowbowHandlebars.MarkdownParser;

namespace SnowbowHandlebars {
	public record ArticleAttribute {
		public string Language { get; init; }
		public List<string> Tags { get; init; }
		public List<string> Categories { get; init; }
		public int Index { get; set; }

		public ArticleAttribute(string language, Dictionary<string, object?> FrontMatter) {
			Language = language;
			Tags = (List<string>?)FrontMatter.GetValueOrDefault("tags") ?? new List<string>();
			Categories = (List<string>?)FrontMatter.GetValueOrDefault("categories") ?? new List<string>();
		}
	}
	public record SiteContext {
		public string Layout { get; init; }
		public string CommonName { get; init; }
		public string Title { get; init; }
		public string? Language { get; init; }
		public string Content { get; init; }
		public string PartialContent { get; init; }
		public DateTimeOffset Time { get; init; }
		public bool Toc { get; init; }
		public bool AutoNumber { get; init; }
		public string RelativePath { get; init; }
		public Dictionary<string, object?>? FrontMatter { get; init; }
		public Dictionary<string, List<SiteContext>> LanguageToArticles { get; init; }
		public Dictionary<string, SortedDictionary<string, List<SiteContext>>> LanguageTagToArticles { get; init; }
		public Dictionary<string, SortedDictionary<string, List<SiteContext>>> LanguageCategoryToArticles { get; init; }
		public ArticleAttribute? Article { get; init; }
		public ThemeConfig ThemeConfig { get; init; }

		public SiteContext(string layoutByDefault, string commonName, string? language, string content, string relativePath, Dictionary<string, object?>? frontMatter, Dictionary<string, List<SiteContext>> languageToArticles, Dictionary<string, SortedDictionary<string, List<SiteContext>>> languageTagToArticles, Dictionary<string, SortedDictionary<string, List<SiteContext>>> languageCategoryToArticles, ArticleAttribute? article, ThemeConfig themeConfig) {
			CommonName = commonName;
			Language = language;
			Content = content;
			RelativePath = relativePath;
			FrontMatter = frontMatter;
			LanguageToArticles = languageToArticles;
			LanguageTagToArticles = languageTagToArticles;
			LanguageCategoryToArticles = languageCategoryToArticles;
			Article = article;
			ThemeConfig = themeConfig;
			PartialContent = ExtractPartialHtml(content);

			Time = (DateTimeOffset?)frontMatter?.GetValueOrDefault("time") ?? Argument.BuildTime;
			Title = (string?)frontMatter?.GetValueOrDefault("title") ?? commonName;
			Layout = (string?)frontMatter?.GetValueOrDefault("layout") ?? layoutByDefault;
			Toc = (bool?)frontMatter?.GetValueOrDefault("toc") ?? false;
			AutoNumber = (bool?)frontMatter?.GetValueOrDefault("autoNumber") ?? false;
		}

		public string Path {
			get {
				if (Language != null) {
					return ThemeConfig.BasePath + Language + "/" + RelativePath;
				}
				else {
					return ThemeConfig.BasePath + RelativePath;
				}
			}
		}
		public string Permalink {
			get {
				return ThemeConfig.SchemeAndHost + Path;
			}
		}

		public string T(string text) {
			return ThemeConfig.Translation[Language ?? ThemeConfig.Languages[0]][text];
		}
		public string ALP(string path) {
			return ThemeConfig.BasePath + (Language ?? ThemeConfig.Languages[0]) + path;
		}

		public string AP(string path) {
			return ThemeConfig.BasePath + path.TrimStart('/');
		}

		public string TP(string language) {
			return ThemeConfig.BasePath + language + "/" + RelativePath;
		}
		public T? FrontMatterOrDefault<T>(string key, T? @default) {
			if (FrontMatter == null) {
				return @default;
			}
			if (!FrontMatter.ContainsKey(key)) {
				return @default;
			}
			return (T?)FrontMatter[key];
		}

		private void BuildToc(StringBuilder sb, int level, ref int pos, TableOfContentsItem[] tocs) {
			if (pos >= tocs.Length || tocs[pos].Level < level) {
				return;
			}
			sb.Append("<ol>\n");
			while (pos < tocs.Length && tocs[pos].Level >= level) {
				sb.Append("<li>");
				if (tocs[pos].Level == level) {
					sb.Append("<a href=\"#").Append(tocs[pos].Id).Append("\">").Append(tocs[pos].Text).Append("</a>");
					pos++;
				}
				sb.Append("\n");
				BuildToc(sb, level + 1, ref pos, tocs);
				sb.Append("</li>\n");
			}
			sb.Append("</ol>\n");
		}

		public string TocOl {
			get {
				StringBuilder sb = new();
				TableOfContentsItem[] tocs = GenerateTableOfContents(Content);
				int pos = 0;
				BuildToc(sb, 2, ref pos, tocs);
				return sb.ToString();
			}
		}
	}
	public record ThemeConfig(
		string CommonTitle,
		string CommonSubtitle,
		string Author,
		string Email,
		string SchemeAndHost,
		string BasePath,
		List<string> Languages,
		Dictionary<string, Dictionary<string, string>> Translation
	);
}
