using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SnowbowHandlebars.MarkdownParser;

namespace SnowbowHandlebars.Contexts {
	public record PageContext {
		private readonly ThemeConfig themeConfig;
		public PageContext(string defaultLayout, string commonName, string? language, string relativePath, string content, string? contentLanguage, Dictionary<string, object?>? frontMatter, ThemeConfig themeConfig) {
			this.defaultLayout = defaultLayout;
			CommonName = commonName;
			Language = language;
			RelativePath = relativePath;
			Content = content;
			ContentLanguage = contentLanguage;
			FrontMatter = frontMatter;
			this.themeConfig = themeConfig;
		}
		private string defaultLayout;
		public string Layout => FrontMatterOrDefault("layout", defaultLayout).NN();
		public string CommonName { get; init; }
		public string? Language { get; init; }
		public string RelativePath { get; init; }
		public string Content { get; init; }
		public string? ContentLanguage { get; init; }
		public string PartialContent {
			get {
				HtmlDocument contentHtmlDocument = new();
				contentHtmlDocument.LoadHtml(Content);
				HtmlNode endNode = contentHtmlDocument.DocumentNode.SelectSingleNode("//comment()[contains(., 'more')]");
				if (endNode == null)
					return Content;
				int endPos = endNode.StreamPosition;
				string result = Content[..endPos];
				return result;
			}
		}
		public Dictionary<string, object?>? FrontMatter { get; init; }
		public DateTimeOffset? Time {
			get {
				return (DateTimeOffset?)FrontMatter?.GetValueOrDefault("time");
			}
		}
		public string Title => FrontMatterOrDefault("title", CommonName).NN();
		public bool Toc => FrontMatterOrDefault("toc", false);
		public bool AutoNumber => FrontMatterOrDefault("autoNumber", false);
		public List<string> Tags => FrontMatterOrDefault<List<string>>("tags", null) ?? new List<string>();
		public List<string> Categories => FrontMatterOrDefault<List<string>>("categories", null) ?? new List<string>();
		public int? IndexOfArticleInLanguage { get; set; }
		public string Path {
			get {
				if (Language != null) {
					return themeConfig.BasePath + Language + "/" + RelativePath;
				}
				else {
					return themeConfig.BasePath + RelativePath;
				}
			}
		}
		public string Permalink {
			get {
				return themeConfig.SchemeAndHost + Path;
			}
		}
		private static void BuildToc(StringBuilder sb, int level, ref int pos, TableOfContentsItem[] tocs) {
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
				sb.Append('\n');
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
		public T? FrontMatterOrDefault<T>(string key, T? @default) {
			if (FrontMatter == null) {
				return @default;
			}
			if (!FrontMatter.ContainsKey(key)) {
				return @default;
			}
			return (T?)FrontMatter[key];
		}
	}
}
