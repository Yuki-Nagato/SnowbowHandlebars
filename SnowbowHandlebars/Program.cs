using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using log4net;
using log4net.DateFormatter;
using SnowbowHandlebars.Contexts;
using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static SnowbowHandlebars.MarkdownParser;
[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]

namespace SnowbowHandlebars {
	internal class Program {
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().NN().DeclaringType);

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
			handlebars.RegisterHelper("T", (context, arguments) => ((ContextAggregation)context.Value).Translate((string)arguments.First()));
			handlebars.RegisterHelper("AP", (context, arguments) => ((ContextAggregation)context.Value).AbsolutePath((string)arguments.First()));
			handlebars.RegisterHelper("ALP", (Context context, Arguments arguments) => ((ContextAggregation)context.Value).AbsoluteLanguagedPath((string)arguments.First()));
			handlebars.RegisterHelper("TP", (context, arguments) => ((ContextAggregation)context.Value).TranslatePath((string)arguments.First()));
			handlebars.RegisterHelper("FrontMatterOrDefault", (context, arguments) => {
				var argArr = arguments.ToArray();
				if (argArr.Length != 2) {
					throw new ArgumentException("FrontMatterOrDefault should have 2 arguments.");
				}
				ContextAggregation ctx = (ContextAggregation)context.Value;
				return ctx.Page.FrontMatterOrDefault((string)argArr[0], argArr[1]);
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
			MemoryFileSystem mfs = new MemoryFileSystem();
			var template = MakeHandlebars();

			ArticleContext articleContext = await BuildArticleContextAsync(themeConfig);
			foreach (var (language, contexts) in articleContext.LanguageArticlesDictionary) {
				foreach (PageContext ctx in contexts) {
					string rendered = template(ctx);
					mfs.Add(ctx.Path, rendered);
				}
			}

			List<PageContext> pageContexts = await BuildPageContextAsync(articleContext, themeConfig);
			foreach (PageContext ctx in pageContexts) {
				string rendered = template(ctx);
				mfs.Add(ctx.Path, rendered);
			}

			foreach (string language in themeConfig.Languages) {
				PageContext ctx = new PageContext("index", "index", language, "", "", language, null, themeConfig);
				string rendered = template(ctx);
				mfs.Add(ctx.Path, rendered);
			}

			mfs.Merge(CopyAssets(themeConfig));
			log.Info("Generation finished.");
			return mfs;
		}

		static async Task<ArticleContext> BuildArticleContextAsync(ThemeConfig themeConfig) {
			ArticleContext result = new(new Dictionary<string, List<PageContext>>(), new Dictionary<string, SortedDictionary<string, List<PageContext>>>(), new Dictionary<string, SortedDictionary<string, List<PageContext>>>());

			List<Dictionary<string, PageContext>> languageArticleDictionaries = new();

			var articleDirs = Argument.Directory.GetDirectories("articles/????????-*");
			foreach (DirectoryInfo articleDir in articleDirs) {
				Match m = Regex.Match(articleDir.Name, @"^(\d{8})-(.+)$");
				if (!m.Success) {
					continue;
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
			}

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

		static async Task<List<PageContext>> BuildPageContextAsync(ArticleContext articleContext, ThemeConfig themeConfig) {
			List<PageContext> result = new();
			DirectoryInfo pagesDir = Argument.Directory.GetDir("pages");
			foreach (FileInfo fileInfo in pagesDir.GetFiles("*.md", SearchOption.AllDirectories)) {
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
			}
			return result;
		}

		static MemoryFileSystem CopyAssets(ThemeConfig themeConfig) {
			MemoryFileSystem mfs = new MemoryFileSystem();

			DirectoryInfo themeAssets = Argument.Directory.GetDir("themes").GetDir(Argument.Theme).GetDir("assets");
			foreach (FileInfo fileInfo in themeAssets.GetFiles("*", SearchOption.AllDirectories)) {
				mfs.Add("/" + fileInfo.RelativeTo(themeAssets), fileInfo.ReadAllBytes());
			}

			var articleDirs = Argument.Directory.GetDirectories("articles/????????-*");
			foreach (DirectoryInfo articleDir in articleDirs) {
				Match m = Regex.Match(articleDir.Name, @"^(\d{8})-(.+)$");
				if (!m.Success) continue;

				string commonName = m.Groups[2].Value;
				DirectoryInfo[] articleAssets = articleDir.GetDirectories("assets");
				if (articleAssets.Length == 1) {
					foreach (FileInfo fileInfo in articleAssets[0].GetFiles("*", SearchOption.AllDirectories)) {
						mfs.Add("/article-assets/" + commonName + "/" + fileInfo.RelativeTo(articleAssets[0]), fileInfo.ReadAllBytes());
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
			mfs.Add("/", meta);
			return mfs;
		}

		static async Task BuildAsync() {
			ThemeConfig themeConfig = GetThemeConfig();
			MemoryFileSystem mfs = await GenerateAllAsync(themeConfig);
			DirectoryInfo publishDir = new DirectoryInfo("public");
			if (publishDir.Exists) {
				foreach (FileInfo file in publishDir.EnumerateFiles()) {
					file.Delete();
				}
				foreach (DirectoryInfo dir in publishDir.EnumerateDirectories()) {
					dir.Delete(true);
				}
			}
			mfs.WriteToDisk(publishDir, 0);
		}

		static async Task ServerAsync() {
			MemoryFileSystem mfs = new MemoryFileSystem();
			HttpListener listener = new HttpListener();
			ThemeConfig themeConfig = GetThemeConfig();
			listener.Prefixes.Add("http://127.0.0.1:4000" + themeConfig.BasePath);
			listener.Start();
			log.InfoFormat("Listening on {0}", "http://127.0.0.1:4000" + themeConfig.BasePath);
			_ = Task.Run(() => {
				while (true) {
					var context = listener.GetContext();
					var req = context.Request;
					var resp = context.Response;
					var path = req.Url!.LocalPath;
					byte[] respContent;
					bool existed = mfs.TryGetValue(path.EndsWith('/') ? path + "index.html" : path, out respContent!);
					if (!existed) {
						resp.StatusCode = (int)HttpStatusCode.NotFound;
						resp.ContentType = "text/plain; charset=utf-8";
						resp.Close("404 not found".ToUtf8Bytes(), true);
						continue;
					}
					resp.ContentType = MimeMapping.MimeUtility.GetMimeMapping(path.EndsWith('/') ? path + "index.html" : path);
					resp.Close(respContent, false);
				}
			});
			mfs = await GenerateAllAsync(themeConfig);
			var watcher = new FileSystemWatcher(Environment.CurrentDirectory);
			watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
			watcher.IncludeSubdirectories = true;

			watcher.Changed += async (sender, args) => {
				try {
					watcher.EnableRaisingEvents = false;
					await Task.Delay(100);
					mfs = await GenerateAllAsync(themeConfig);
				}
				catch (Exception e) {
					log.Error(e.ToString());
				}
				finally {
					watcher.EnableRaisingEvents = true;
				}
			};
			watcher.EnableRaisingEvents = true;
			log.Info("Watching " + watcher.Path);
			Console.ReadLine();
		}
	}
}