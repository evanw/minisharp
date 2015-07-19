using System.Collections.Generic;
using System.IO;
using System;

namespace Shade
{
	public class Compiler
	{
		public static int Main(string[] args)
		{
			var boolFlags = new Dictionary<string, bool> {
				{ "--minify", false },
				{ "--mangle", false },
				{ "--timing", false },
			};
			string outputPath = null;

			// Parse command-line arguments
			var inputs = new List<Input>();
			for (var i = 0; i < args.Length; i++) {
				var arg = args[i];

				// Help
				if (arg == "-h" || arg == "-help" || arg == "--help" || arg == "-?" || arg == "/?") {
					WriteUsage();
					return 0;
				}

				// Boolean flags
				if (boolFlags.ContainsKey(arg)) {
					if (boolFlags[arg]) {
						Console.WriteLine("Duplicate flag \"" + arg + "\"");
						return 1;
					}
					boolFlags[arg] = true;
					continue;
				}

				// Output file
				if (arg == "--output" || arg == "-o") {
					if (outputPath != null) {
						Console.WriteLine("Duplicate flag \"" + arg + "\"");
						return 1;
					}
					if (i + 1 == args.Length) {
						Console.WriteLine("Missing path for flag \"" + arg + "\"");
						return 1;
					}
					outputPath = args[++i];
					continue;
				}

				// Invalid flags
				if (arg.StartsWith("-")) {
					Console.WriteLine("Invalid flag \"" + arg + "\"");
					return 1;
				}

				// Input files
				inputs.Add(new Input(arg, File.ReadAllText(arg)));
			}

			// Show usage if there are no inputs
			if (inputs.Count == 0) {
				WriteUsage();
				return 1;
			}

			// Parse inputs
			var context = new InputContext();
			if (!context.Compile(inputs)) {
				context.WriteLogToConsole();
				return 1;
			}

			// Generate output
			var output = new OutputContext(context);
			output.ShouldMinify = boolFlags["--minify"];
			output.ShouldMangle = boolFlags["--mangle"];
			if (outputPath != null) {
				File.WriteAllText(outputPath, output.Code);
			} else {
				Console.Write(output.Code);
			}

			// Write out timing info for debugging
			if (boolFlags["--timing"]) {
				foreach (var pair in context.timingInMilliseconds) {
					Console.Error.WriteLine(pair.Key + ": " + pair.Value + "ms");
				}
			}

			return 0;
		}

		public static void WriteUsage()
		{
			Console.WriteLine(@"
usage: shade.exe [options] [inputs] [-o output]

    --minify        Remove unnecessary whitespace to shrink output.
    --mangle        Apply syntax transformations to shrink output.
    -o, --output    Specify the output path (defaults to stdout).
    --timing        Report timing information for debugging.
");
		}
	}
}
