using NUnit.Framework;
using System.Collections.Generic;

namespace Shade.Tests
{
	[TestFixture]
	public class EndToEnd
	{
		public static void Check(string source, string expected)
		{
			var input = new InputContext();
			if (input.Compile(new List<Input> { new Input("<test>", source) })) {
				var output = new OutputContext(input);
				Assert.AreEqual(expected, output.Code);
			} else {
				Assert.AreEqual(expected, input.GenerateLog());
			}
		}

		[Test]
		public void StaticClass()
		{
			Check(
@"public static class Test {
	public static int foo = 0;
	public static int bar = 1;
	public static void Foo() {
	}
	public static int Bar() {
		return foo + bar;
	}
}",
@"(function() {
  var Test = {};

  Test.Foo = function() {
  };

  Test.Bar = function() {
    return Test.foo + Test.bar;
  };

  Test.foo = 0;
  Test.bar = 1;
})();
");
		}
	}
}
