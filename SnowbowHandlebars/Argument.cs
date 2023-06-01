using CommandLine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

	public static class Argument {
		public static string? Verb { get; private set; }
		public static DirectoryInfo? Directory { get; private set; }
		public static string? Theme { get; private set; }
		public static DateTimeOffset BuildTime { get; set; }
		public static void MakeEffect(string[] args) {
			_ = Parser.Default.ParseArguments<BuildOptions, ServerOptions>(args)
				.WithParsed<BuildOptions>(buildOptions => {
					if (buildOptions.Directory != null) {
						Environment.CurrentDirectory = buildOptions.Directory.FullName;
					}
					else {
						Logger.Log.Warn("Directory not specified, use current directory " + Environment.CurrentDirectory);
					}
					Verb = "build";
					Directory = new DirectoryInfo(Environment.CurrentDirectory);
					Theme = buildOptions.Theme;
				})
				.WithParsed<ServerOptions>(serverOption => {
					if (serverOption.Directory != null) {
						Environment.CurrentDirectory = serverOption.Directory.FullName;
					}
					else {
						Logger.Log.Warn("Directory not specified, use current directory " + Environment.CurrentDirectory);
					}
					Verb = "server";
					Directory = new DirectoryInfo(Environment.CurrentDirectory);
					Theme = serverOption.Theme;
				})
				.WithNotParsed(errors => {
					Logger.Log.Error(errors);
				});
			BuildTime = DateTimeOffset.Now;
		}
	}
}
