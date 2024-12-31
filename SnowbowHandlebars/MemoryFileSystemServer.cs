using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars {
	public class MemoryFileSystemServer {
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType ?? throw new VitalObjectNullException("DeclaringType"));
		public MemoryFileSystem MemoryFileSystem { get; set; } = new MemoryFileSystem();
		private readonly HttpListener httpListener;

		public MemoryFileSystemServer(HttpListener httpListener) {
			this.httpListener = httpListener;
		}

		public void Start() {
			httpListener.Start();
			httpListener.BeginGetContext(Callback, null);
			foreach (string prefix in httpListener.Prefixes) {
				log.Info("Listening on " + prefix);
			}
		}

		private void Callback(IAsyncResult ar) {
			try {
				HttpListenerContext context = httpListener.EndGetContext(ar);
				httpListener.BeginGetContext(Callback, null);
				string reqPath = context.Request.Url?.LocalPath ?? throw new VitalObjectNullException(nameof(reqPath));
				if (reqPath.EndsWith('/')) {
					reqPath += "index.html";
				}
				if (!MemoryFileSystem.ContainsKey(reqPath)) {
					context.Response.StatusCode = (int)HttpStatusCode.NotFound;
					context.Response.ContentType = "text/plain; charset=utf-8";
					context.Response.Close("404 not found".ToUtf8Bytes(), false);
				}
				else {
					context.Response.ContentType = MimeMapping.MimeUtility.GetMimeMapping(reqPath);
					context.Response.Close(MemoryFileSystem[reqPath], false);
				}
			}
			catch (Exception) {
				log.Info("Server stopped.");
				return;
			}
		}

		public void Stop() {
			httpListener.Stop();
		}
	}
}
