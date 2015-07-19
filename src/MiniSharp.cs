using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System;

namespace MiniSharp
{
	public class Compiler
	{
		public static int Main(string[] args)
		{
			var boolFlags = new Dictionary<string, bool> {
				{ "--minify", false },
				{ "--mangle", false },
				{ "--timing", false },
				{ "--server", false },
				{ "--source-map", false },
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

			// The server option ignores all other options
			if (boolFlags["--server"]) {
				return RunLocalServer() ? 0 : 1;
			}

			// Show usage if there are no inputs
			if (inputs.Count == 0) {
				WriteUsage();
				return 1;
			}

			// Parse inputs
			var context = new InputContext();
			if (!context.Compile(inputs)) {
				Console.Write(context.GenerateLog());
				return 1;
			}

			// Generate output
			var output = new OutputContext(context);
			output.ShouldMinify = boolFlags["--minify"];
			output.ShouldMangle = boolFlags["--mangle"];
			output.SourceMap = boolFlags["--source-map"] ? outputPath != null ? SourceMap.External : SourceMap.Inline : SourceMap.None;
			if (outputPath != null) {
				File.WriteAllText(outputPath, output.Code);
				if (output.SourceMap == SourceMap.External) {
					File.WriteAllText(outputPath + ".map", output.SourceMapCode);
				}
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

		public static bool RunLocalServer()
		{
			if (!HttpListener.IsSupported) {
				return false;
			}

			var listener = new HttpListener();
			var address = "http://localhost:8008/";
			listener.Prefixes.Add(address);
			listener.Start();
			Console.WriteLine("Serving on " + address);

			while (true) {
				var context = listener.GetContext();
				var request = context.Request;
				var response = context.Response;

				var content = new StreamReader(request.InputStream, request.ContentEncoding).ReadToEnd();
				var input = new InputContext();
				var responseText = "";

				try {
					if (!input.Compile(new List<Input> { new Input("<input>", content) })) {
						responseText = input.GenerateLog();
					} else {
						var output = new OutputContext(input);
						output.ShouldMinify = request.QueryString["minify"] == "true";
						output.ShouldMangle = request.QueryString["mangle"] == "true";
						output.SourceMap = request.QueryString["sourceMap"] == "true" ? SourceMap.Inline : SourceMap.None;
						responseText = output.Code;
					}
				} catch (Exception error) {
					responseText = error.Message + "\n" + error.StackTrace;
				}

				var buffer = Encoding.UTF8.GetBytes(responseText);
				response.Headers["Access-Control-Allow-Origin"] = "*";
				response.ContentLength64 = buffer.Length;
				response.OutputStream.Write(buffer, 0, buffer.Length);
				response.OutputStream.Close();
			}
		}

		public static void WriteUsage()
		{
			Console.WriteLine(@"
usage: shade.exe [options] [inputs] [-o output]

    --minify        Remove unnecessary whitespace to shrink output.
    --mangle        Apply syntax transformations to shrink output.
    -o, --output    Specify the output path (defaults to stdout).
    --timing        Report timing information for debugging.
    --source-map    Generate a source map along side the output.
    --server        Run a local HTTP service over port 8008.
");
		}
	}
}
