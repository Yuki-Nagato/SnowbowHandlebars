using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars {
	public static class Executor {
		public static async Task<int> ExecAsync(string fileName, string arguments, TextReader? stdin, TextWriter? stdout, TextWriter? stderr) {
			using var p = new Process();
			p.StartInfo.FileName = fileName;
			p.StartInfo.Arguments = arguments;

			p.StartInfo.UseShellExecute = false;
			p.StartInfo.CreateNoWindow = true;
			p.StartInfo.RedirectStandardInput = true;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardError = true;
			p.StartInfo.StandardInputEncoding = Helper.UTF8Enc;
			p.StartInfo.StandardOutputEncoding = Helper.UTF8Enc;
			p.StartInfo.StandardErrorEncoding = Helper.UTF8Enc;
			p.Start();

			var stdinTask = Task.Run(() => {
				if (stdin != null) {
					var buf = new char[1024];
					while (true) {
						var len = stdin.Read(buf, 0, buf.Length);
						if (len <= 0) {
							break;
						}
						p.StandardInput.Write(buf, 0, len);
						p.StandardInput.Flush();
					}
				}
				p.StandardInput.Close();
			});
			var stdoutTask = Task.Run(() => {
				var buf = new char[1024];
				while (true) {
					var len = p.StandardOutput.Read(buf, 0, buf.Length);
					if (len <= 0) {
						break;
					}
					stdout?.Write(buf, 0, len);
					stdout?.Flush();
				}
				stdout?.Close();
			});
			var stderrTask = Task.Run(() => {
				var buf = new char[1024];
				while (true) {
					var len = p.StandardError.Read(buf, 0, buf.Length);
					if (len <= 0) {
						break;
					}
					stderr?.Write(buf, 0, len);
					stderr?.Flush();
				}
				stderr?.Close();
			});
			await Task.WhenAll(stdinTask, stdoutTask, stderrTask);
			await p.WaitForExitAsync();
			return p.ExitCode;
		}
	}
}
