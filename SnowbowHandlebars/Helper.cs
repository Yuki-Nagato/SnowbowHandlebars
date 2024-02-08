using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Text;

namespace SnowbowHandlebars {
	public static class Helper {
		public static readonly UTF8Encoding UTF8Enc = new(false, true);

		public static string ReadAllText(this FileInfo file) {
			return File.ReadAllText(file.ToString(), UTF8Enc);
		}

		public static byte[] ReadAllBytes(this FileInfo file) {
			return File.ReadAllBytes(file.ToString());
		}

		public static void WriteAllBytes(this FileInfo file, byte[] bytes) {
			File.WriteAllBytes(file.ToString(), bytes);
		}

		public static FileInfo GetFile(this DirectoryInfo directory, string filename) {
			var result = new FileInfo(Path.Combine(directory.ToString(), filename));
			if (!result.Exists) {
				throw new FileNotFoundException(result.ToString());
			}
			return result;
		}

		public static DirectoryInfo GetDir(this DirectoryInfo directory, string dirname) {
			var result = new DirectoryInfo(Path.Combine(directory.ToString(), dirname));
			if (!result.Exists) {
				throw new DirectoryNotFoundException(result.ToString());
			}
			return result;
		}

		public static string RelativeTo(this FileInfo fileInfo, DirectoryInfo? directoryInfo = null) {
			return Path.GetRelativePath((directoryInfo ?? Argument.Directory).FullName, fileInfo.FullName).Replace('\\', '/');
		}

		public static T DeserializeJson<T>(this string json) {
			return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Error, CheckAdditionalContent = true }) ?? throw new Exception("Failed to deserialize JSON.");
		}

		public static byte[] ToUtf8Bytes(this string str) {
			return UTF8Enc.GetBytes(str);
		}

		public static string ToUtf8String(this byte[] bytes) {
			return UTF8Enc.GetString(bytes);
		}
		public static IEnumerable<T> OrEmptyIfNull<T>(this IEnumerable<T> source) {
			return source ?? Enumerable.Empty<T>();
		}

		public static T NN<T>(this T? obj, [CallerArgumentExpression("obj")] string? paramName = null) {
			if (obj == null) {
				throw new ArgumentNullException(paramName);
			}
			return obj;
		}

		public static string RemoveStart(this string str, string start) {
			if (!str.StartsWith(start)) {
				throw new Exception($"RemoveStart failed. \"{str}\" does not start with \"{start}\".");
			}
			return str[start.Length..];
		}
		public static string RemoveEnd(this string str, string end) {
			if (!str.EndsWith(end)) {
				throw new Exception($"RemoveEnd failed. \"{str}\" does not end with \"{end}\".");
			}
			return str[..^end.Length];
		}
	}
}
