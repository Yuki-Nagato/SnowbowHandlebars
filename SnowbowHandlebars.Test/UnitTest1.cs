using HandlebarsDotNet;

namespace SnowbowHandlebars.Test {
	internal record Hello(string test);
	[TestClass]
	public class UnitTest1 {
		[TestMethod]
		public void TestMethod1() {
			string source =
@"<div class=""entry"">
  <h1>{{temp.title}}</h1>
  <div class=""body"">
    {{T @hello}}
  </div>
</div>";
			var handlebars = Handlebars.Create(new HandlebarsConfiguration() { ThrowOnUnresolvedBindingExpression = true });
			handlebars.RegisterHelper("T", (in HelperOptions options, in Context context, in Arguments arguments) => {
				Console.WriteLine(options);
				return "TEST";
			});
			var template = handlebars.Compile(source);

			var context = new {
				temp = new {
					title = "My new post"
				},
				body = "This is my first post!"
			};
			var data = new {
				hello = "world"
			};

			var result = template(context, data);
			Console.WriteLine(result);
		}
	}
}