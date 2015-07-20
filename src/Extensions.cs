using ICSharpCode.NRefactory.TypeSystem;
using System.Text;

namespace MiniSharp
{
	public enum QuoteStyle
	{
		Double,
		Single,
		SingleOrDouble,
	}

	public static class Globals
	{
		public static string Quote(this string text, QuoteStyle style)
		{
			// Use whichever quote character is less frequent
			if (style == QuoteStyle.SingleOrDouble) {
				var singleQuotes = 0;
				var doubleQuotes = 0;
				foreach (var c in text) {
					if (c == '"') doubleQuotes++;
					else if (c == '\'') singleQuotes++;
				}
				style = singleQuotes <= doubleQuotes ? QuoteStyle.Single : QuoteStyle.Double;
			}

			// Generate the string using substrings of unquoted stuff for speed
			var quote = style == QuoteStyle.Single ? "'" : "\"";
			var builder = new StringBuilder();
			var start = 0;
			builder.Append(quote);
			for (var i = 0; i < text.Length; i++) {
				var c = text[i];
				string escape;
				if (c == quote[0]) escape = "\\" + quote;
				else if (c == '\\') escape = "\\\\";
				else if (c == '\t') escape = "\\t";
				else if (c == '\n') escape = "\\n";
				else continue;
				builder.Append(text.Substring(start, i - start));
				builder.Append(escape);
				start = i + 1;
			}
			builder.Append(text.Substring(start));
			builder.Append(quote);
			return builder.ToString();
		}

		public static IType BaseClass(this IType type)
		{
			foreach (var baseType in type.DirectBaseTypes) {
				if (baseType.Kind == TypeKind.Class) {
					return baseType;
				}
			}
			return null;
		}
	}
}
