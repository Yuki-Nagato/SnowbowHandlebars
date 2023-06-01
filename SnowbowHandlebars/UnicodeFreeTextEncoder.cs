using HandlebarsDotNet;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars {
	public class UnicodeFreeTextEncoder : ITextEncoder {
		public void Encode(StringBuilder text, TextWriter target) {
			Encode(text.ToString(), target);
		}

		public void Encode(string text, TextWriter target) {
			Encode(text.GetEnumerator(), target);
		}

		public void Encode<T>(T text, TextWriter target) where T : IEnumerator<char> {
			while (text.MoveNext()) {
				switch (text.Current) {
					case '<':
						target.Write("&lt;");
						break;
					case '>':
						target.Write("&gt;");
						break;
					case '&':
						target.Write("&amp;");
						break;
					case '"':
						target.Write("&quot;");
						break;
					case '\'':
						target.Write("&apos;");
						break;
					default:
						target.Write(text.Current);
						break;
				}
			}
		}
	}
}
