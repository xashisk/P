using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.Types;
using System.Collections.Generic;

namespace Plang.Compiler.Backend.Rust
{
    internal class CompilationContext : CompilationContextBase
    {
        public CompilationContext(ICompilationJob job)
            : base(job)
        {
            Names = new RustNameManager("PGEN_");

            FileName = $"{ProjectName}.rs";
            ProjectDependencies = job.ProjectDependencies.Count == 0 ? new List<string>() { ProjectName } : job.ProjectDependencies;
            GlobalFunctionClassName = "GlobalFunctions";
        }

        public RustNameManager Names { get; }

        public IEnumerable<PLanguageType> UsedTypes => Names.UsedTypes;

        public string GlobalFunctionClassName { get; }

        public string FileName { get; }

        public IReadOnlyList<string> ProjectDependencies { get; }

        public string GetStaticMethodQualifiedName(Function function)
        {
            return $"{GlobalFunctionClassName}.{Names.GetNameForDecl(function)}";
        }
    }
}