using log4net.Config;
using log4net;
using log4net.Core;

namespace SnowbowHandlebars {
	public static class Logger {
		public static readonly ILog Log = LogManager.GetLogger(typeof(Logger));
		static Logger() {
			XmlConfigurator.Configure();
		}
	}
}
