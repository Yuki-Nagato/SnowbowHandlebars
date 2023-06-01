using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars {
	public class MemoryFileSystem : Dictionary<string, byte[]> {
		private void CreateFile(string path, byte[] content) {
			if (!path.StartsWith('/')) {
				throw new ArgumentException("file path invalid", nameof(path));
			}
			if (path.EndsWith('/')) {
				path += "index.html";
			}
			foreach (string p in Keys) {
				if (p == path || p.StartsWith(path + "/") || path.StartsWith(p + "/")) {
					throw new ArgumentException("file already exists: " + p);
				}
			}
			base.Add(path, content);
		}

		public void Add(string path, string content) {
			CreateFile(path, content.ToUtf8Bytes());
		}

		public new void Add(string path, byte[] content) {
			CreateFile(path, content);
		}

		public void Merge(MemoryFileSystem other) {
			foreach(var (path, content) in other) {
				CreateFile(path, content);
			}
		}

		public string ReadFileAsString(string path) {
			return this[path].ToUtf8String();
		}

		public void WriteToDisk(DirectoryInfo root, int ignorePrefix) {
			string rootPath = root.FullName.Replace('\\', '/').TrimEnd('/');
			foreach (var kvp in this) {
				string path = "";
				var splittedPaths = kvp.Key.Split('/').Skip(ignorePrefix + 1);
				foreach (var splittedPath in splittedPaths) {
					path += "/" + splittedPath;
				}
				var toWrite = new FileInfo(rootPath + path);
				toWrite.Directory?.Create();
				toWrite.WriteAllBytes(kvp.Value);
			}
		}
	}
}
