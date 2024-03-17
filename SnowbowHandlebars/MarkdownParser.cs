using HtmlAgilityPack;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SnowbowHandlebars.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace SnowbowHandlebars {
	public static class MarkdownParser {
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().NN().DeclaringType);
		public static async Task<(Dictionary<string, object?>?, string)> ParseMarkdownAsync(string markdown) {
			string[] parts = markdown.Split("---", 3);
			string content;

			if (parts.Length == 1) {
				content = await PandocRenderAsync(markdown);
				return (null, content);
			}

			var deserializer = new DeserializerBuilder().Build();
			var yamlObject = deserializer.Deserialize(new StringReader(parts[1].Trim()));
			var serializer = new SerializerBuilder().JsonCompatible().Build();
			string json = serializer.Serialize(yamlObject);

			Dictionary<string, object?> frontMatter = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json, new JsonSerializerSettings() { DateParseHandling = DateParseHandling.DateTimeOffset, MissingMemberHandling = MissingMemberHandling.Error, Converters = { new GenericJsonConverter() } }).NN();

			content = await PandocRenderAsync(parts[2].Trim());
			return (frontMatter, content);
		}

		static async Task<string> PandocRenderAsync(string markdown) {
			var stdout = new StringWriter();
			var stderr = new StringWriter();
			var code = await Executor.ExecAsync("pandoc", "--from=markdown-smart --to=html5 --no-highlight --mathml --eol=lf --wrap=none", new StringReader(markdown), stdout, stderr);
			if (!string.IsNullOrEmpty(stderr.ToString())) {
				log.ErrorFormat("Pandoc error, {0}", stderr.ToString());
			}
			return stdout.ToString();
		}

		public static string RedirectArticleAssetPath(ThemeConfig themeConfig, string articleCommonName, string html) {
			var document = new HtmlDocument();
			document.LoadHtml(html);
			var nodes = document.DocumentNode.SelectNodes("//*[starts-with(@src, 'assets/')]");
			if (nodes != null) {
				foreach (var node in nodes) {
					var src = node.Attributes["src"].Value;
					node.Attributes["src"].Value = themeConfig.BasePath + "article-assets/" + articleCommonName + src[6..];
				}
			}

			nodes = document.DocumentNode.SelectNodes("//*[starts-with(@href, 'assets/')]");
			if (nodes != null) {
				foreach (var node in nodes) {
					var href = node.Attributes["href"].Value;
					node.Attributes["href"].Value = themeConfig.BasePath + "article-assets/" + articleCommonName + href[6..];
				}
			}

			return document.DocumentNode.OuterHtml;
		}

		public record TableOfContentsItem(string Tag, string Text, string Id) {
			public int Level {
				get {
					return Tag[1] - '0';
				}
			}
		}

		public static TableOfContentsItem[] GenerateTableOfContents(string contentHtml) {
			var ps = new SortedDictionary<int, TableOfContentsItem>();
			var document = new HtmlDocument();
			document.LoadHtml(contentHtml);

			foreach (var node in document.DocumentNode.SelectNodes("/h1").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			foreach (var node in document.DocumentNode.SelectNodes("/h2").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			foreach (var node in document.DocumentNode.SelectNodes("/h3").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			foreach (var node in document.DocumentNode.SelectNodes("/h4").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			foreach (var node in document.DocumentNode.SelectNodes("/h5").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			foreach (var node in document.DocumentNode.SelectNodes("/h6").OrEmptyIfNull()) {
				ps.Add(node.StreamPosition, new TableOfContentsItem(node.Name, node.InnerHtml, node.Attributes["id"].Value));
			}
			return ps.Values.ToArray();
		}
	}

	public class GenericJsonConverter : JsonConverter {
		public override bool CanConvert(Type objectType) {
			return objectType == typeof(Dictionary<string, object?>) || objectType == typeof(List<object?>);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
			if (objectType == typeof(Dictionary<string, object?>)) {
				JObject jsonObject = JObject.Load(reader);
				var dictionary = new Dictionary<string, object?>();

				foreach (var property in jsonObject.Properties()) {
					object? value = ConvertValue(property.Value, serializer);
					dictionary[property.Name] = value;
				}

				return dictionary;
			}
			else if (objectType == typeof(List<object?>)) {
				JArray jsonArray = JArray.Load(reader);
				var list = new List<object?>();

				foreach (var item in jsonArray) {
					object? value = ConvertValue(item, serializer);
					list.Add(value);
				}

				return list;
			}

			throw new InvalidOperationException($"Cannot convert {objectType.Name} using GenericJsonConverter.");
		}

		public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
			throw new NotImplementedException();
		}

		private static object? ConvertValue(JToken token, JsonSerializer serializer) {
			switch (token.Type) {
				case JTokenType.Object:
					return token.ToObject<Dictionary<string, object?>>(serializer);

				case JTokenType.Array:
					var array = (JArray)token;

					if (array.All(item => item.Type == JTokenType.String)) {
						return array.ToObject<List<string>>(serializer);
					}
					else {
						return array.ToObject<List<object?>>(serializer);
					}

				default:
					return token.ToObject<object?>(serializer);
			}
		}
	}
}
