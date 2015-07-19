using NUnit.Framework;
using System.Collections.Generic;

namespace MiniSharp.Tests
{
	[TestFixture]
	public class MangleEndToEnd
	{
		public static void Check(string source, string expected)
		{
			var input = new InputContext();
			if (input.Compile(new List<Input> { new Input("<test>", source) })) {
				var output = new OutputContext(input);
				output.ShouldMangle = true;
				Assert.AreEqual(expected, output.Code);
			} else {
				Assert.AreEqual(expected, input.GenerateLog());
			}
		}

		[Test]
		public void IfElse()
		{
			Check(
@"public static class Test {
	public static void Fun() {
		if (x) {
			if (y) { a(); }
		}

		if (x) {
			if (y) { a(); }
			else { b(); }
		}

		// Be careful about the dangling else problem here
		if (x) {
			if (y) { a(); }
		}
		else { c(); }

		if (x) {
			if (y) { a(); }
			else { b(); }
		}
		else { c(); }
	}
}",
@"(function() {
	var Test = {};

	Test.Fun = function() {
		if (x) if (y) a();

		if (x) if (y) a();
		else b();

		// Be careful about the dangling else problem here
		if (x) {
			if (y) a();
		}
		else c();

		if (x) if (y) a();
		else b();
		else c();
	};
})();
");
		}
	}
}
