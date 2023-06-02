using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using Newtonsoft.Json;
using System.Collections;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text.Unicode;
using System.Threading;
using YamlDotNet.Serialization.NodeTypeResolvers;
using static SnowbowHandlebars.MarkdownParser;
using System.Runtime.Intrinsics.Arm;

namespace SnowbowHandlebars {
	internal class Program {

		static async Task Main(string[] args) {
			Logger.Log.Debug("Snowbow started.");
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
			DirectoryInfo themeDir = Argument.Directory.NN().GetDir($"themes/{Argument.Theme}");
			foreach (FileInfo fileInfo in themeDir.GetFiles("*.hbs", SearchOption.AllDirectories)) {
				string key = fileInfo.RelativeTo(themeDir);
				Logger.Log.DebugFormat("Register template {0}", key);
				handlebars.RegisterTemplate(key, fileInfo.ReadAllText());
			}
			handlebars.RegisterHelper("T", (context, arguments) => {
				var argArr = arguments.ToArray();
				SiteContext siteContext;
				if (argArr.Length == 1) {
					siteContext = (SiteContext)context.Value;
				}
				else if (argArr.Length == 2) {
					siteContext = (SiteContext)argArr[1];
				}
				else {
					throw new ArgumentException("T should have 1 or 2 arguments.");
				}
				return siteContext.T((string)arguments[0]);
			});
			handlebars.RegisterHelper("AP", (context, arguments) => {
				var argArr = arguments.ToArray();
				SiteContext siteContext;
				if (argArr.Length == 1) {
					siteContext = (SiteContext)context.Value;
				}
				else if (argArr.Length == 2) {
					siteContext = (SiteContext)argArr[1];
				}
				else {
					throw new ArgumentException("AP should have 1 or 2 arguments.");
				}
				return siteContext.AP((string)arguments[0]);
			});
			handlebars.RegisterHelper("ALP", (in HelperOptions options, in Context context, in Arguments arguments) => {
				var argArr = arguments.ToArray();
				SiteContext siteContext;
				if (argArr.Length == 1) {
					siteContext = (SiteContext)context.Value;
				}
				else if (argArr.Length == 2) {
					siteContext = (SiteContext)argArr[1];
				}
				else {
					throw new ArgumentException("ALP should have 1 or 2 arguments.");
				}
				return siteContext.ALP((string)arguments[0]);
			});
			handlebars.RegisterHelper("TP", (context, arguments) => {
				var argArr = arguments.ToArray();
				SiteContext siteContext;
				if (argArr.Length == 1) {
					siteContext = (SiteContext)context.Value;
				}
				else if (argArr.Length == 2) {
					siteContext = (SiteContext)argArr[1];
				}
				else {
					throw new ArgumentException("TP should have 1 or 2 arguments.");
				}
				return siteContext.TP((string)arguments[0]);
			});
			handlebars.RegisterHelper("FrontMatterOrDefault", (context, arguments) => {
				return ((SiteContext)context.Value).FrontMatterOrDefault((string)arguments[0], arguments[1]);
			});
			handlebars.RegisterHelper("ToString", (context, arguments) => {
				StringBuilder sb = new StringBuilder();
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
			//HandlebarsHelpers.Register(handlebars, options => { options.UseCategoryPrefix = false; });
			HandlebarsHelpers.Register(handlebars);
			var template = handlebars.Compile(Argument.Directory.NN().GetFile($"themes/{Argument.Theme}/index.hbs").ReadAllText());
			return template;
		}

		static ThemeConfig GetThemeConfig() {
			ThemeConfig themeConfig = Argument.Directory.NN().GetFile($"themes/{Argument.Theme}/theme-config.json").ReadAllText().DeserializeJson<ThemeConfig>();
			Logger.Log.DebugFormat("themeConfig: {0}", themeConfig);
			return themeConfig;
		}


		static async Task<MemoryFileSystem> GenerateAllAsync(ThemeConfig themeConfig) {
			Logger.Log.Info("Generating all...");
			Argument.BuildTime = DateTimeOffset.Now;
			MemoryFileSystem mfs = new MemoryFileSystem();
			var template = MakeHandlebars();

			Dictionary<string, List<SiteContext>> articleContext = await BuildArticleContextAsync(themeConfig);
			foreach (var (language, contexts) in articleContext) {
				foreach (SiteContext ctx in contexts) {
					string rendered = template(ctx);
					mfs.Add(ctx.Path, rendered);
				}
			}

			List<SiteContext> pageContexts = await BuildPageContextAsync(articleContext, themeConfig);
			foreach (SiteContext ctx in pageContexts) {
				string rendered = template(ctx);
				mfs.Add(ctx.Path, rendered);
			}

			foreach (string language in themeConfig.Languages) {
				SiteContext ctx = new SiteContext("index", "index", language, "", "", null, articleContext, articleContext.First().Value.First().LanguageTagToArticles, articleContext.First().Value.First().LanguageCategoryToArticles, null, themeConfig);
				string rendered = template(ctx);
				mfs.Add(ctx.Path, rendered);
			}

			mfs.Merge(CopyAssets(themeConfig));
			Logger.Log.Info("Generation finished.");
			return mfs;
		}

		static async Task<Dictionary<string, List<SiteContext>>> BuildArticleContextAsync(ThemeConfig themeConfig) {
			// Shared Article Lists
			Dictionary<string, List<SiteContext>> languageToArticles = new();
			Dictionary<string, SortedDictionary<string, List<SiteContext>>> languageTagToArticles = new();
			Dictionary<string, SortedDictionary<string, List<SiteContext>>> languageCategoryToArticles = new();

			List<Dictionary<string, SiteContext>> dp = new();

			var articleDirs = Argument.Directory.NN().GetDirectories("articles/????????-*");
			foreach (DirectoryInfo articleDir in articleDirs) {
				Match m = Regex.Match(articleDir.Name, @"^(\d{8})-(.+)$");
				if (!m.Success) continue;

				string commonName = m.Groups[2].Value;
				dp.Add(new Dictionary<string, SiteContext>());

				FileInfo[] articleMarkdownFiles = articleDir.GetFiles("*.*.md");
				foreach (FileInfo articleMarkdownFile in articleMarkdownFiles) {
					string[] temp = articleMarkdownFile.Name.Split('.');
					string language = temp[^2];
					var (frontMatter, content) = await ParseMarkdownAsync(articleMarkdownFile.ReadAllText());
					content = RedirectArticleAssetPath(themeConfig, commonName, content);
					SiteContext ctx = new SiteContext("article", commonName, language, content, "articles/" + commonName + "/", frontMatter, languageToArticles, languageTagToArticles, languageCategoryToArticles, new ArticleAttribute(language, frontMatter.NN()), themeConfig);
					dp[^1].Add(language, ctx);
				}
			}

			foreach (var lap in dp) {
				foreach (string lang in themeConfig.Languages) {
					if (!lap.ContainsKey(lang)) {
						foreach (string l in themeConfig.Languages) {
							if (lap.ContainsKey(l)) {
								SiteContext newCtx = new SiteContext(lap[l].Layout, lap[l].CommonName, lang, lap[l].Content, lap[l].RelativePath, lap[l].FrontMatter, languageToArticles, languageTagToArticles, languageCategoryToArticles, lap[l].Article, themeConfig);
								lap.Add(lang, newCtx);
								break;
							}
						}
					}
				}
			}

			foreach (string language in themeConfig.Languages) {
				languageToArticles.Add(language, new());
				foreach (var lap in dp) {
					languageToArticles[language].Add(lap[language]);
				}
				languageToArticles[language].Sort((a, b) => {
					if (a.Time != b.Time) {
						return b.Time.CompareTo(a.Time);
					}
					return b.CommonName.CompareTo(a.CommonName);
				});
				for (int i = 0; i < languageToArticles[language].Count; i++) {
					languageToArticles[language][i].Article.NN().Index = i;
				}
			}

			foreach (var (language, contexts) in languageToArticles) {
				languageTagToArticles.Add(language, new());
				languageCategoryToArticles.Add(language, new());
				foreach (SiteContext ctx in contexts) {
					foreach (string tag in ctx.Article.NN().Tags) {
						if (!languageTagToArticles[language].ContainsKey(tag)) {
							languageTagToArticles[language].Add(tag, new());
						}
						languageTagToArticles[language][tag].Add(ctx);
					}
					foreach (string category in ctx.Article.NN().Categories) {
						if (!languageCategoryToArticles[language].ContainsKey(category)) {
							languageCategoryToArticles[language].Add(category, new());
						}
						languageCategoryToArticles[language][category].Add(ctx);
					}
				}
			}

			return languageToArticles;
		}

		static async Task<List<SiteContext>> BuildPageContextAsync(Dictionary<string, List<SiteContext>> languageToArticles, ThemeConfig themeConfig) {
			List<SiteContext> pageContexts = new List<SiteContext>();
			DirectoryInfo pagesDir = Argument.Directory.NN().GetDir("pages");
			foreach (FileInfo fileInfo in pagesDir.GetFiles("*.md", SearchOption.AllDirectories)) {
				var (frontMatter, content) = await ParseMarkdownAsync(fileInfo.ReadAllText());
				string path = fileInfo.RelativeTo(pagesDir);
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
				SiteContext ctx = new SiteContext("page", string.Join('_', path.Split(Path.GetInvalidFileNameChars())), language, content, path, frontMatter, languageToArticles, languageToArticles.First().Value.First().LanguageTagToArticles, languageToArticles.First().Value.First().LanguageCategoryToArticles, null, themeConfig);
				pageContexts.Add(ctx);
			}
			return pageContexts;
		}

		static MemoryFileSystem CopyAssets(ThemeConfig themeConfig) {
			MemoryFileSystem mfs = new MemoryFileSystem();

			DirectoryInfo themeAssets = Argument.Directory.NN().GetDir("themes").GetDir(Argument.Theme.NN()).GetDir("assets");
			foreach (FileInfo fileInfo in themeAssets.GetFiles("*", SearchOption.AllDirectories)) {
				mfs.Add("/" + fileInfo.RelativeTo(themeAssets), fileInfo.ReadAllBytes());
			}

			var articleDirs = Argument.Directory.NN().GetDirectories("articles/????????-*");
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
			Logger.Log.InfoFormat("Listening on {0}", "http://127.0.0.1:4000" + themeConfig.BasePath);
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
					Logger.Log.Error(e.ToString());
				}
				finally {
					watcher.EnableRaisingEvents = true;
				}
			};
			watcher.EnableRaisingEvents = true;
			Logger.Log.Info("Watching " + watcher.Path);
			Console.ReadLine();
		}
	}
}