using CommandLine;
using CommandLine.Text;
using log4net;
using System.Reflection;

namespace SnowbowHandlebars {
	[Verb("build")]
	public class BuildOptions {

		[Option(shortName: 'd', longName: "directory", Required = false)]
		public DirectoryInfo? Directory { get; init; }
		[Option(shortName: 't', longName: "theme", Required = true)]
		public string? Theme { get; init; }
	}
	[Verb("server")]
	public class ServerOptions {
		[Option(shortName: 'd', longName: "directory", Required = false)]
		public DirectoryInfo? Directory { get; init; }
		[Option(shortName: 't', longName: "theme", Required = true)]
		public string? Theme { get; init; }
	}
	[Verb("version")]
	public class VersionOptions {
	}

	public static class Argument {
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().NN().DeclaringType);
		private static string? verb;
		public static string? Verb {
			get {
				return verb.NN();
			}
		}
		private static DirectoryInfo? directory;
		public static DirectoryInfo Directory {
			get {
				return directory.NN();
			}
		}
		private static string? theme;
		public static string Theme {
			get {
				return theme.NN();
			}
		}
		public static DateTimeOffset BuildTime { get; set; }
		public static void MakeEffect(string[] args) {
			new Parser(config => config.AutoVersion = false).ParseArguments<BuildOptions, ServerOptions, VersionOptions>(args)
				.WithParsed<BuildOptions>(buildOptions => {
					if (buildOptions.Directory == null) {
						log.Warn("Directory not specified, use current directory " + Environment.CurrentDirectory);
					}
					verb = "build";
					directory = buildOptions.Directory ?? new DirectoryInfo(Environment.CurrentDirectory);
					theme = buildOptions.Theme;
				})
				.WithParsed<ServerOptions>(serverOption => {
					if (serverOption.Directory == null) {
						log.Warn("Directory not specified, use current directory " + Environment.CurrentDirectory);
					}
					verb = "server";
					directory = serverOption.Directory ?? new DirectoryInfo(Environment.CurrentDirectory);
					theme = serverOption.Theme;
				})
				.WithParsed<VersionOptions>(versionOptions => {
					log.Info("SnowbowHandlebars v1");
					verb = "version";
				})
				.WithNotParsed(errors => {
					log.Error(errors);
				});
			BuildTime = DateTimeOffset.Now;
		}
	}
}
