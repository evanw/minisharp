using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using System.Collections.Generic;
using System.Diagnostics;
using System;

namespace Shade
{
	public class Input
	{
		public string name;
		public string contents;
		public SyntaxTree tree;
		public CSharpUnresolvedFile file;
		public CSharpAstResolver resolver;

		public Input(string name, string contents)
		{
			this.name = name;
			this.contents = contents;
		}
	}

	public class InputContext
	{
		public INamespace root;
		public List<Input> inputs;
		public List<Error> diagnostics = new List<Error>();
		public List<ITypeDefinition> types = new List<ITypeDefinition>();
		public Dictionary<string, long> timingInMilliseconds = new Dictionary<string, long>();
		public Dictionary<IMethod, MethodDeclaration> methods = new Dictionary<IMethod, MethodDeclaration>();
		public Dictionary<IField, VariableInitializer> fields = new Dictionary<IField, VariableInitializer>();
		public Dictionary<IField, EnumMemberDeclaration> enums = new Dictionary<IField, EnumMemberDeclaration>();
		public Dictionary<IMethod, OperatorDeclaration> operators = new Dictionary<IMethod, OperatorDeclaration>();
		public Dictionary<IProperty, IndexerDeclaration> indexers = new Dictionary<IProperty, IndexerDeclaration>();
		public Dictionary<IProperty, PropertyDeclaration> properties = new Dictionary<IProperty, PropertyDeclaration>();
		public Dictionary<IMethod, ConstructorDeclaration> constructors = new Dictionary<IMethod, ConstructorDeclaration>();

		public bool Compile(List<Input> inputs)
		{
			this.inputs = inputs;
			var parser = new CSharpParser();
			var project = (IProjectContent)new CSharpProjectContent();

			// Parse each input
			var parsing = Stopwatch.StartNew();
			foreach (var input in inputs) {
				input.tree = parser.Parse(input.contents, input.name);
				input.file = input.tree.ToTypeSystem();
				project = project.AddOrUpdateFiles(input.file);
			}
			timingInMilliseconds["Parsing"] = parsing.ElapsedMilliseconds;

			// Add errors and warnings
			foreach (var diagnostic in parser.ErrorsAndWarnings) {
				diagnostics.Add(diagnostic);
			}

			// Compilation fails for parse errors
			if (parser.HasErrors) {
				return false;
			}

			// Scan the type system
			var compiling = Stopwatch.StartNew();
			var compilation = project.CreateCompilation();
			root = compilation.RootNamespace;
			ScanTypes(root);
			timingInMilliseconds["Compiling"] = compiling.ElapsedMilliseconds;

			// Scan the syntax tree, linking it to the type system
			var visitor = new Visitor(this);
			foreach (var input in inputs) {
				visitor.resolver = input.resolver = new CSharpAstResolver(compilation, input.tree, input.file);
				input.tree.AcceptVisitor(visitor);
			}

			// Transform the syntax tree into valid JavaScript
			var lowering = Stopwatch.StartNew();
			var success = LoweringContext.Lower(this);
			timingInMilliseconds["Lowering"] = lowering.ElapsedMilliseconds;
			return success;
		}

		public void ReportWarning(DomRegion region, string message)
		{
			diagnostics.Add(new Error(ErrorType.Warning, message, region));
		}

		public void ReportError(DomRegion region, string message)
		{
			diagnostics.Add(new Error(ErrorType.Error, message, region));
		}

		public void WriteLogToConsole()
		{
			foreach (var diagnostic in diagnostics) {
				Console.WriteLine("{0}({1},{2}): {3}: {4}",
					diagnostic.Region.FileName,
					diagnostic.Region.BeginLine + 1,
					diagnostic.Region.BeginColumn + 1,
					diagnostic.ErrorType == ErrorType.Warning ? "warning" : "error",
					diagnostic.Message);
			}
		}

		private void ScanTypes(INamespace parent)
		{
			foreach (var child in parent.ChildNamespaces) {
				ScanTypes(child);
			}

			foreach (var child in parent.Types) {
				ScanTypes(child);
			}
		}

		private void ScanTypes(ITypeDefinition parent)
		{
			types.Add(parent);

			foreach (var child in parent.NestedTypes) {
				ScanTypes(child);
			}
		}

		private class Visitor : DepthFirstAstVisitor
		{
			public InputContext context;
			public CSharpAstResolver resolver;

			public Visitor(InputContext context)
			{
				this.context = context;
			}

			public override void VisitVariableInitializer(VariableInitializer node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.fields[(IField)resolved.Member] = node;
				}
			}

			public override void VisitMethodDeclaration(MethodDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.methods[(IMethod)resolved.Member] = node;
				}
			}

			public override void VisitIndexerDeclaration(IndexerDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.indexers[(IProperty)resolved.Member] = node;
				}
			}

			public override void VisitOperatorDeclaration(OperatorDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.operators[(IMethod)resolved.Member] = node;
				}
			}

			public override void VisitPropertyDeclaration(PropertyDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.properties[(IProperty)resolved.Member] = node;
				}
			}

			public override void VisitConstructorDeclaration(ConstructorDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.constructors[(IMethod)resolved.Member] = node;
				}
			}

			public override void VisitEnumMemberDeclaration(EnumMemberDeclaration node)
			{
				var resolved = resolver.Resolve(node) as MemberResolveResult;
				if (resolved != null) {
					context.enums[(IField)resolved.Member] = node;
				}
			}
		}
	}
}
