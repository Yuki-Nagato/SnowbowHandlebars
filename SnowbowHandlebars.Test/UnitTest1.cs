using HandlebarsDotNet;

namespace SnowbowHandlebars.Test {
	[TestClass]
	public class UnitTest1 {
		[TestMethod]
		public void TestMethod1() {
			string source =
@"<div class=""entry"">
  <h1>{{title}}</h1>
  <div class=""body"">
    {{hello}}
  </div>
</div>";
			var handlebars = Handlebars.Create(new HandlebarsConfiguration() { ThrowOnUnresolvedBindingExpression = true });
			var template = handlebars.Compile(source);

			var context = new {
				title = "My new post",
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