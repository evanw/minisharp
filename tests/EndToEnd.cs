using NUnit.Framework;
using System.Collections.Generic;

namespace MiniSharp.Tests
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
		public void NormalClass()
		{
			Check(
@"public class Test {
	public int ivarNoInit;
	public int ivarInit = 1;
	public static int varNoInit;
	public static int varInit = 1;
	public int IFun() {
		return ivarNoInit + ivarInit;
	}
	public static int Fun() {
		return varNoInit + varInit;
	}
}",
@"(function() {
	function Test() {
		this.ivarNoInit = 0;
		this.ivarInit = 1;
	}

	Test.prototype.IFun = function() {
		return this.ivarNoInit + this.ivarInit | 0;
	};

	Test.Fun = function() {
		return Test.varNoInit + Test.varInit | 0;
	};

	Test.varNoInit = 0;
	Test.varInit = 1;
})();
");
		}

		[Test]
		public void NormalClassInsideNamespace()
		{
			Check(
@"namespace NS {
	public class Test {
		public int ivarNoInit;
		public int ivarInit = 1;
		public static int varNoInit;
		public static int varInit = 1;
		public int IFun() {
			return ivarNoInit + ivarInit;
		}
		public static int Fun() {
			return varNoInit + varInit;
		}
	}
}",
@"(function() {
	var NS = {};

	NS.Test = function() {
		this.ivarNoInit = 0;
		this.ivarInit = 1;
	};

	NS.Test.prototype.IFun = function() {
		return this.ivarNoInit + this.ivarInit | 0;
	};

	NS.Test.Fun = function() {
		return NS.Test.varNoInit + NS.Test.varInit | 0;
	};

	NS.Test.varNoInit = 0;
	NS.Test.varInit = 1;
})();
");
		}

		[Test]
		public void StaticClass()
		{
			Check(
@"public static class Test {
	public static int varNoInit;
	public static int varInit = 1;
	public static int Fun() {
		return varNoInit + varInit;
	}
}",
@"(function() {
	var Test = {};

	Test.Fun = function() {
		return Test.varNoInit + Test.varInit | 0;
	};

	Test.varNoInit = 0;
	Test.varInit = 1;
})();
");
		}

		[Test]
		public void StaticClassInsideNamespace()
		{
			Check(
@"namespace NS {
	public static class Test {
		public static int varNoInit;
		public static int varInit = 1;
		public static int Fun() {
			return varNoInit + varInit;
		}
	}
}",
@"(function() {
	var NS = {};
	NS.Test = {};

	NS.Test.Fun = function() {
		return NS.Test.varNoInit + NS.Test.varInit | 0;
	};

	NS.Test.varNoInit = 0;
	NS.Test.varInit = 1;
})();
");
		}

		[Test]
		public void ExtensionMethods()
		{
			Check(
@"namespace NS {
	public static class Test {
		public static void Fun(this int x, int y) {
		}
	}
}

namespace NS2 {
	using NS;

	public static class Test {
		public static void Fun() {
			0.Fun(1);
		}
	}
}",
@"(function() {
	var NS = {};
	var NS2 = {};
	NS.Test = {};
	NS2.Test = {};

	NS.Test.Fun = function(x, y) {
	};

	NS2.Test.Fun = function() {
		NS.Test.Fun(0, 1);
	};
})();
");
		}

		[Test]
		public void Casting()
		{
			Check(
@"static class Test {
	static void Fun(double d, int i, Foo f) {
		var i2d = (double)i;
		var d2i = (int)d;
		var i2f = (Foo)i;
		var f2i = (int)f;
		var d2f = (Foo)d;
		var f2d = (double)f;
	}
}

enum Foo {}
", @"(function() {
	var Test = {};
	var Foo = {};

	Test.Fun = function(d, i, f) {
		var i2d = i;
		var d2i = d | 0;
		var i2f = i;
		var f2i = f;
		var d2f = d | 0;
		var f2d = f;
	};
})();
");
		}

		[Test]
		public void DynamicCasting()
		{
			Check(
@"static class Test {
	static void Fun(dynamic x) {
		var d = (double)x;
		var i = (int)x;
		var f = (Foo)x;
		var b = (bool)x;
	}
}

enum Foo {}
", @"(function() {
	var Test = {};
	var Foo = {};

	Test.Fun = function(x) {
		var d = +x;
		var i = x | 0;
		var f = x | 0;
		var b = !!x;
	};
})();
");
		}

		[Test]
		public void DerivedClassAfter()
		{
			Check(
@"class Base {}
class Derived : Base {}
", @"(function() {
	function Base() {
	}

	function Derived() {
		Base.call(this);
	}

	Derived.prototype = Object.create(Base.prototype);
})();
");
		}

		[Test]
		public void DerivedClassBefore()
		{
			Check(
@"class Derived : Base {}
class Base {}
", @"(function() {
	function Base() {
	}

	function Derived() {
		Base.call(this);
	}

	Derived.prototype = Object.create(Base.prototype);
})();
");
		}

		[Test]
		public void DerivedClassAfterGeneric()
		{
			Check(
@"class Base<T> {}
class Derived : Base<int> {}
", @"(function() {
	function Base() {
	}

	function Derived() {
		Base.call(this);
	}

	Derived.prototype = Object.create(Base.prototype);
})();
");
		}

		[Test]
		public void DerivedClassBeforeGeneric()
		{
			Check(
@"class Derived : Base<int> {}
class Base<T> {}
", @"(function() {
	function Base() {
	}

	function Derived() {
		Base.call(this);
	}

	Derived.prototype = Object.create(Base.prototype);
})();
");
		}
	}
}
