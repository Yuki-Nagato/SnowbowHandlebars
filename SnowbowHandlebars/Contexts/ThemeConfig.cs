using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars.Contexts {
	public record ThemeConfig(
		string CommonTitle,
		string CommonSubtitle,
		string Author,
		string Email,
		string SchemeAndHost,
		string BasePath,
		List<string> Languages,
		Dictionary<string, Dictionary<string, string>> Translation
	);
}
