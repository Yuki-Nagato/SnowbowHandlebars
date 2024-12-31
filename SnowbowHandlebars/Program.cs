using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using HandlebarsDotNet.PathStructure;
using log4net;
using log4net.DateFormatter;
using SnowbowHandlebars.Contexts;
using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.ServiceModel.Syndication;
using static SnowbowHandlebars.MarkdownParser;
using System.Xml;
using Vlingo.Xoom.UUID;
using System.Collections.Concurrent;
[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]

namespace SnowbowHandlebars {
	internal class Program {
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType ?? throw new VitalObjectNullException("DeclaringType"));

		static async Task Main(string[] args) {
			log.Debug("Snowbow started.");
			Argument.MakeEffect(args);
			if (Argument.Verb == "build") {
				await BuildAsync();
			}
			else if (Argument.Verb == "server") {
				await ServerAsync();
			}
		}

		static HandlebarsTemplate<object, object> MakeHandlebars() {
			var handlebars = Handlebars.Create(new HandlebarsConfiguration() { ThrowOnUnresolvedBindingExpression = true, TextEncoder = new UnicodeFreeTextEncoder() });
			DirectoryInfo themeDir = Argument.Directory.GetDir($"themes/{Argument.Theme}");
			foreach (FileInfo fileInfo in themeDir.GetFiles("*.hbs", SearchOption.AllDirectories)) {
				string key = fileInfo.RelativeTo(themeDir);
				log.DebugFormat("Register template {0}", key);
				handlebars.RegisterTemplate(key, fileInfo.ReadAllText());
			}

			HandlebarsHelpers.Register(handlebars);
			handlebars.RegisterHelper("T", (in HelperOptions options, in Context context, in Arguments arguments) => ((ContextAggregation)options.Data["Root"]).Translate((string)arguments.First()));
			handlebars.RegisterHelper("AP", (in HelperOptions options, in Context context, in Arguments arguments) => ((ContextAggregation)options.Data["Root"]).AbsolutePath((string)arguments.First()));
			handlebars.RegisterHelper("ALP", (in HelperOptions options, in Context context, in Arguments arguments) => ((ContextAggregation)options.Data["Root"]).AbsoluteLanguagedPath((string)arguments.First()));
			handlebars.RegisterHelper("TP", (in HelperOptions options, in Context context, in Arguments arguments) => ((ContextAggregation)options.Data["Root"]).TranslatePath((string)arguments.First()));
			handlebars.RegisterHelper("FrontMatterOrDefault", (in HelperOptions options, in Context context, in Arguments arguments) => {
				var argArr = arguments.ToArray();
				if (context.Value is ContextAggregation aggCtx) {
					return aggCtx.Page.FrontMatterOrDefault((string)argArr[0], argArr[1]);
				}
				else if (context.Value is PageContext pageCtx) {
					return pageCtx.FrontMatterOrDefault((string)argArr[0], argArr[1]);
				}
				else {
					return ((ContextAggregation)options.Data["Root"]).Page.FrontMatterOrDefault((string)argArr[0], argArr[1]);
				}
			});
			handlebars.RegisterHelper("ToString", (context, arguments) => {
				StringBuilder sb = new();
				foreach (var arg in arguments) {
					sb.Append(arg);
				}
				return sb.ToString();
			});
			handlebars.RegisterHelper("Len", (context, arguments) => {
				if (arguments[0] is string str) {
					return str.Length;
				}
				else if (arguments[0] is ICollection collection) {
					return collection.Count;
				}
				else {
					int cnt = 0;
					var enumerator = ((IEnumerable)arguments[0]).GetEnumerator();
					while (enumerator.MoveNext()) {
						cnt++;
					}
					return cnt;
				}
			});
			handlebars.RegisterHelper("Ternary", (context, arguments) => {
				return (bool)arguments[0] ? arguments[1] : arguments[2];
			});

			var template = handlebars.Compile(Argument.Directory.GetFile($"themes/{Argument.Theme}/index.hbs").ReadAllText());
			return template;
		}

		static ThemeConfig GetThemeConfig() {
			ThemeConfig themeConfig = Argument.Directory.GetFile($"themes/{Argument.Theme}/theme-config.json").ReadAllText().DeserializeJson<ThemeConfig>();
			log.DebugFormat("themeConfig: {0}", themeConfig);
			return themeConfig;
		}


		static async Task<MemoryFileSystem> GenerateAllAsync(ThemeConfig themeConfig) {
			log.Info("Generating all...");
			Argument.BuildTime = DateTimeOffset.Now;
			MemoryFileSystem mfs = new();
			var template = MakeHandlebars();

			ArticleContext articleContext = await BuildArticleContextAsync(themeConfig);
			foreach (var (language, contexts) in articleContext.LanguageArticlesDictionary) {
				foreach (PageContext ctx in contexts) {
					string rendered = template(new ContextAggregation(themeConfig, articleContext, ctx));
					mfs.Add(ctx.Path, rendered);
				}
			}

			IEnumerable<PageContext> pageContexts = await BuildPageContextAsync(articleContext, themeConfig);
			foreach (PageContext ctx in pageContexts) {
				string rendered = template(new ContextAggregation(themeConfig, articleContext, ctx));
				mfs.Add(ctx.Path, rendered);
			}

			foreach (string language in themeConfig.Languages) {
				PageContext ctx = new("index", "index", language, "", "", language, null, themeConfig);
				string rendered = template(new ContextAggregation(themeConfig, articleContext, ctx));
				mfs.Add(ctx.Path, rendered);
			}

			mfs.Merge(CopyAssets(themeConfig));
			mfs.Merge(BuildAtomSyndication(themeConfig, articleContext));
			log.Info("Generation finished.");
			return mfs;
		}

		static async Task<ArticleContext> BuildArticleContextAsync(ThemeConfig themeConfig) {
			ArticleContext result = new(new Dictionary<string, List<PageContext>>(), new Dictionary<string, SortedDictionary<string, List<PageContext>>>(), new Dictionary<string, SortedDictionary<string, List<PageContext>>>());

			ConcurrentBag<Dictionary<string, PageContext>> languageArticleDictionaries = new();

			var articleDirs = Argument.Directory.GetDirectories("articles/????????-*");
			await Parallel.ForEachAsync(articleDirs, async (articleDir, cancellationToken) => {
				Match m = Regex.Match(articleDir.Name, @"^(\d{8})-(.+)$");
				if (!m.Success) {
					return;
				}
				string commonName = m.Groups[2].Value;
				Dictionary<string, PageContext> languageArticleDictionary = new();
				FileInfo[] articleMarkdownFiles = articleDir.GetFiles("*.*.md");
				foreach (FileInfo articleMarkdownFile in articleMarkdownFiles) {
					string language = articleMarkdownFile.Name.Split('.')[^2];
					if (!themeConfig.Languages.Contains(language)) {
						continue;
					}
					var (frontMatter, content) = await ParseMarkdownAsync(articleMarkdownFile.ReadAllText());
					content = RedirectArticleAssetPath(themeConfig, commonName, content);
					PageContext ctx = new("article", commonName, language, "articles/" + commonName + "/", content, language, frontMatter, themeConfig);
					languageArticleDictionary.Add(language, ctx);
				}
				if (languageArticleDictionary.Any()) {
					languageArticleDictionaries.Add(languageArticleDictionary);
				}
			});

			// Fill ArticleContexts without i18n source.
			foreach (var languageArticleDictionary in languageArticleDictionaries) {
				foreach (string lang in themeConfig.Languages) {
					if (!languageArticleDictionary.ContainsKey(lang)) {
						foreach (string l in themeConfig.Languages) {
							if (languageArticleDictionary.ContainsKey(l)) {
								PageContext newCtx = languageArticleDictionary[l] with { Language = lang };
								languageArticleDictionary.Add(lang, newCtx);
								break;
							}
						}
					}
				}
			}

			foreach (string language in themeConfig.Languages) {
				List<PageContext> articles = new();
				result.LanguageArticlesDictionary.Add(language, articles);
				foreach (var languageArticleDictionary in languageArticleDictionaries) {
					articles.Add(languageArticleDictionary[language]);
				}
				articles.Sort((a, b) => {
					if (a.Time.HasValue && !b.Time.HasValue) {
						return -1;
					}
					if (!a.Time.HasValue && b.Time.HasValue) {
						return 1;
					}
					if (a.Time.HasValue && b.Time.HasValue && a.Time != b.Time) {
						return b.Time.Value.CompareTo(a.Time.Value);
					}
					return a.CommonName.CompareTo(b.CommonName);
				});
				for (int i = 0; i < articles.Count; i++) {
					articles[i].IndexOfArticleInLanguage = i;
				}
			}

			foreach (var (language, articles) in result.LanguageArticlesDictionary) {
				result.LanguageTagArticlesDictionary.Add(language, new());
				result.LanguageCategoryArticlesDictionary.Add(language, new());
				foreach (PageContext article in articles) {
					foreach (string tag in article.Tags) {
						if (!result.LanguageTagArticlesDictionary[language].ContainsKey(tag)) {
							result.LanguageTagArticlesDictionary[language].Add(tag, new());
						}
						result.LanguageTagArticlesDictionary[language][tag].Add(article);
					}
					foreach (string category in article.Categories) {
						if (!result.LanguageCategoryArticlesDictionary[language].ContainsKey(category)) {
							result.LanguageCategoryArticlesDictionary[language].Add(category, new());
						}
						result.LanguageCategoryArticlesDictionary[language][category].Add(article);
					}
				}
			}

			return result;
		}

		static async Task<IEnumerable<PageContext>> BuildPageContextAsync(ArticleContext articleContext, ThemeConfig themeConfig) {
			ConcurrentBag<PageContext> result = new();
			DirectoryInfo pagesDir = Argument.Directory.GetDir("pages");
			await Parallel.ForEachAsync(pagesDir.GetFiles("*.md", SearchOption.AllDirectories), async (fileInfo, cancellationToken) => {
				var (frontMatter, content) = await ParseMarkdownAsync(fileInfo.ReadAllText());
				string path = fileInfo.RelativeTo(pagesDir);
				string commonName = Path.GetFileNameWithoutExtension(fileInfo.FullName);
				string? language = null;
				foreach (string lang in themeConfig.Languages) {
					if (path.StartsWith(lang + "/")) {
						language = lang;
						path = path.RemoveStart(lang + "/");
						break;
					}
				}
				if (fileInfo.Name == "index.md") {
					path = path.RemoveEnd("index.md");
				}
				else {
					path = path.RemoveEnd(".md") + ".html";
				}
				PageContext ctx = new("page", commonName, language, path, content, language, frontMatter, themeConfig);
				result.Add(ctx);
			});
			return result;
		}

		static MemoryFileSystem CopyAssets(ThemeConfig themeConfig) {
			MemoryFileSystem mfs = new();

			DirectoryInfo themeAssets = Argument.Directory.GetDir("themes").GetDir(Argument.Theme).GetDir("assets");
			foreach (FileInfo fileInfo in themeAssets.GetFiles("*", SearchOption.AllDirectories)) {
				mfs.Add(themeConfig.BasePath + fileInfo.RelativeTo(themeAssets), fileInfo.ReadAllBytes());
			}

			var articleDirs = Argument.Directory.GetDirectories("articles/????????-*");
			foreach (DirectoryInfo articleDir in articleDirs) {
				Match m = Regex.Match(articleDir.Name, @"^(\d{8})-(.+)$");
				if (!m.Success) continue;

				string commonName = m.Groups[2].Value;
				DirectoryInfo[] articleAssets = articleDir.GetDirectories("assets");
				if (articleAssets.Length == 1) {
					foreach (FileInfo fileInfo in articleAssets[0].GetFiles("*", SearchOption.AllDirectories)) {
						mfs.Add(themeConfig.BasePath + "article-assets/" + commonName + "/" + fileInfo.RelativeTo(articleAssets[0]), fileInfo.ReadAllBytes());
					}
				}
			}

			string meta = @$"<!DOCTYPE html>
<html>
	<head>
		<meta charset=""utf-8"" />
		<meta http-equiv=""refresh"" content=""0; url={themeConfig.BasePath}{themeConfig.Languages[0]}/"" />
	</head>
	<body>
	</body>
</html>";
			mfs.Add(themeConfig.BasePath, meta);
			return mfs;
		}

		public static MemoryFileSystem BuildAtomSyndication(ThemeConfig themeConfig, ArticleContext articleContext) {
			MemoryFileSystem result = new();
			using NameBasedGenerator uuidGenerator = new(HashType.Sha1);
			foreach (var (language, articles) in articleContext.LanguageArticlesDictionary) {
				SyndicationFeed feed = new(
					themeConfig.Translation[language].GetValueOrDefault(themeConfig.CommonTitle, themeConfig.CommonTitle),
					themeConfig.Translation[language].GetValueOrDefault(themeConfig.CommonSubtitle, themeConfig.CommonSubtitle),
					new Uri(themeConfig.SchemeAndHost + themeConfig.BasePath + language + "/"),
					uuidGenerator.GenerateGuid(UUIDNameSpace.Url, themeConfig.SchemeAndHost + themeConfig.BasePath + language + "/").ToString(),
					DateTimeOffset.Now
				);
				feed.Authors.Add(new SyndicationPerson() {
					Name = themeConfig.Author,
					Email = themeConfig.Email
				});
				List<SyndicationItem> items = new();
				foreach (PageContext article in articles) {
					SyndicationItem entry = new(
						article.Title,
						SyndicationContent.CreateHtmlContent(article.Content),
						new Uri(article.Permalink),
						uuidGenerator.GenerateGuid(UUIDNameSpace.Url, article.Permalink).ToString(), article.Time.GetValueOrDefault()
					);
					items.Add(entry);
				}
				feed.Items = items;
				StringWriter sw = new();
				XmlTextWriter xmlTextWriter = new(sw);
				feed.SaveAsAtom10(xmlTextWriter);
				xmlTextWriter.Close();
				sw.Close();
				result.Add(themeConfig.BasePath + language + "/atom.xml", sw.ToString());
				if (language == themeConfig.Languages[0]) {
					result.Add(themeConfig.BasePath + "atom.xml", sw.ToString());
				}
			}
			return result;
		}

		static async Task BuildAsync() {
			ThemeConfig themeConfig = GetThemeConfig();
			MemoryFileSystem mfs = await GenerateAllAsync(themeConfig);
			DirectoryInfo publishDir = new(Path.Combine(Argument.Directory.ToString(), "public"));
			if (publishDir.Exists) {
				foreach (FileInfo file in publishDir.EnumerateFiles()) {
					file.Delete();
				}
				foreach (DirectoryInfo dir in publishDir.EnumerateDirectories()) {
					dir.Delete(true);
				}
			}
			mfs.WriteToDisk(publishDir, themeConfig.BasePath.Count(ch => ch == '/') - 1);
		}

		static async Task ServerAsync() {
			HttpListener listener = new();
			ThemeConfig themeConfig = GetThemeConfig();
			listener.Prefixes.Add("http://127.0.0.1:4000" + themeConfig.BasePath);
			MemoryFileSystemServer server = new(listener);
			server.Start();
			server.MemoryFileSystem = await GenerateAllAsync(themeConfig);

			FileSystemWatcher watcher = new(Argument.Directory.FullName) {
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
				IncludeSubdirectories = true
			};

			CancellationTokenSource? cancellationTokenSource = null;
			watcher.Changed += async (sender, args) => {
				try {
					cancellationTokenSource?.Cancel();
					cancellationTokenSource?.Dispose();
					cancellationTokenSource = new CancellationTokenSource();
					await Task.Delay(500, cancellationTokenSource.Token);
					server.MemoryFileSystem = await GenerateAllAsync(themeConfig);
				}
				catch (TaskCanceledException) {

				}
				catch (Exception e) {
					log.Error(e.ToString());
				}
			};
			watcher.EnableRaisingEvents = true;
			log.Info("Watching " + watcher.Path);
			Console.ReadLine();
			cancellationTokenSource?.Dispose();
			server.Stop();
		}
	}
}