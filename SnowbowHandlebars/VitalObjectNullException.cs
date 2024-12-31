using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbowHandlebars {
	public class VitalObjectNullException : Exception {
		public VitalObjectNullException(string objectName) : base("\"" + objectName + "\" is vital, but it is null.") {
		}
	}
}
