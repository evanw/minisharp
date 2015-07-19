using System.Collections.Generic;
using System.Text;
using System;

namespace MiniSharp
{
	public struct SourceMapping
	{
		// These indices are all 0-based
		public readonly int inputIndex;
		public readonly int originalLine;
		public readonly int originalColumn;
		public readonly int generatedLine;
		public readonly int generatedColumn;

		public SourceMapping(int inputIndex, int originalLine, int originalColumn, int generatedLine, int generatedColumn)
		{
			this.inputIndex = inputIndex;
			this.originalLine = originalLine;
			this.originalColumn = originalColumn;
			this.generatedLine = generatedLine;
			this.generatedColumn = generatedColumn;
		}
	}

	// https://github.com/mozilla/source-map
	public class SourceMapGenerator
	{
		public void AddMapping(Input input, int originalLine, int originalColumn, int generatedLine, int generatedColumn)
		{
			var inputIndex = inputs.IndexOf(input);
			if (inputIndex == -1) {
				inputIndex = inputs.Count;
				inputs.Add(input);
			}
			mappings.Add(new SourceMapping(inputIndex, originalLine, originalColumn, generatedLine, generatedColumn));
		}

		public override string ToString()
		{
			var sourceNames = new List<string>();
			var sourceContents = new List<string>();

			foreach (var input in inputs) {
				sourceNames.Add(input.name.Quote(QuoteStyle.Double));
				sourceContents.Add(input.contents.Quote(QuoteStyle.Double));
			}

			var builder = new StringBuilder();
			builder.Append("{\"version\":3,\"sources\":[");
			builder.Append(string.Join(",", sourceNames));
			builder.Append("],\"sourcesContent\":[");
			builder.Append(string.Join(",", sourceContents));
			builder.Append("],\"names\":[],\"mappings\":\"");

			// Sort the mappings in increasing order by generated location
			mappings.Sort((a, b) => {
				var delta = a.generatedLine - b.generatedLine;
				return delta != 0 ? delta : a.generatedColumn - b.generatedColumn;
			});

			var previousGeneratedColumn = 0;
			var previousGeneratedLine = 0;
			var previousOriginalColumn = 0;
			var previousOriginalLine = 0;
			var previousSourceIndex = 0;

			// Generate the base64 VLQ encoded mappings
			foreach (var mapping in mappings) {
				var generatedLine = mapping.generatedLine;

				// Insert ',' for the same line and ';' for a line
				if (previousGeneratedLine == generatedLine) {
					if (previousGeneratedColumn == mapping.generatedColumn && (previousGeneratedLine != 0 || previousGeneratedColumn != 0)) {
						continue;
					}
					builder.Append(",");
				} else {
					previousGeneratedColumn = 0;
					while (previousGeneratedLine < generatedLine) {
						builder.Append(";");
						previousGeneratedLine++;
					}
				}

				// Record the generated column (the line is recorded using ';' above)
				builder.Append(EncodeVLQ(mapping.generatedColumn - previousGeneratedColumn));
				previousGeneratedColumn = mapping.generatedColumn;

				// Record the generated source
				builder.Append(EncodeVLQ(mapping.inputIndex - previousSourceIndex));
				previousSourceIndex = mapping.inputIndex;

				// Record the original line
				builder.Append(EncodeVLQ(mapping.originalLine - previousOriginalLine));
				previousOriginalLine = mapping.originalLine;

				// Record the original column
				builder.Append(EncodeVLQ(mapping.originalColumn - previousOriginalColumn));
				previousOriginalColumn = mapping.originalColumn;
			}

			builder.Append("\"}\n");
			return builder.ToString();
		}

		public string ToURL()
		{
			return "data:application/json;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToString()));
		}

		// A single base 64 digit can contain 6 bits of data. For the base 64 variable
		// length quantities we use in the source map spec, the first bit is the sign,
		// the next four bits are the actual value, and the 6th bit is the continuation
		// bit. The continuation bit tells us whether there are more digits in this
		// value following this digit.
		//
		//   Continuation
		//   |    Sign
		//   |    |
		//   V    V
		//   101011
		//
		private static string EncodeVLQ(int value)
		{
			var vlq = value < 0 ? -value << 1 | 1 : value << 1;
			var builder = new StringBuilder();

			while (true) {
				var digit = vlq & 31;
				vlq >>= 5;

				// If there are still more digits in this value, we must make sure the
				// continuation bit is marked
				if (vlq != 0) {
					digit |= 32;
				}

				builder.Append(BASE64[digit]);

				if (vlq == 0) {
					break;
				}
			}

			return builder.ToString();
		}

		private List<SourceMapping> mappings = new List<SourceMapping>();
		private List<Input> inputs = new List<Input>();

		private const string BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
	}
}
