using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using Microsoft.CodeAnalysis.Formatting;

namespace UsefulRoslynCodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToStringCodeFixProvider)), Shared]
    public class ToStringCodeFixProvider : CodeFixProvider
    {
        private const string title = "Make uppercase";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(UsefulRoslynCodeFixesAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();

            var classDeclaration = root.DescendantNodes().FirstOrDefault(node => node is ClassDeclarationSyntax) as ClassDeclarationSyntax;
            if (classDeclaration == null) return;

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedSolution: c => MakeUppercaseAsync(context.Document, classDeclaration, c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Solution> MakeUppercaseAsync(Document document, ClassDeclarationSyntax classDecl, CancellationToken cancellationToken)
        {
            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            var props = root.DescendantNodes().Where(x => x is PropertyDeclarationSyntax);

            StringBuilder sb = new StringBuilder(@"
                StringBuilder resultSb = new StringBuilder("""");
            ");

            foreach (SyntaxNode currentProp in props)
            {
                var currentSyntax = currentProp as PropertyDeclarationSyntax;

                sb.Append("resultSb.AppendFormat(\"{0}: {1}, \", nameof(" + currentSyntax.Identifier.Value + "), " + currentSyntax.Identifier.Value + ");");
                sb.Append(Environment.NewLine);
            }

            // The new ToString method to add
            var methodToInsert = GetMethodDeclarationSyntax(returnTypeName: "string ",
                      methodName: "ToString",
                      body: sb.ToString());
            var newClassDecl = classDecl.AddMembers(methodToInsert);

            // Replace to node and done!
            return document.WithSyntaxRoot(
                    root.ReplaceNode(classDecl, newClassDecl)
                ).Project.Solution;
        }

        private MethodDeclarationSyntax GetMethodDeclarationSyntax(string returnTypeName, string methodName, string body)
        {
            var syntax = SyntaxFactory.ParseStatement(@"" + body + "return resultSb.ToString();");
            var parameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(GetParametersList(new string[0], new string[0])));
            var modifiers = new SyntaxToken[] { SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.OverrideKeyword) };

            return SyntaxFactory.MethodDeclaration(attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                          modifiers: SyntaxFactory.TokenList(modifiers),
                          returnType: SyntaxFactory.ParseTypeName(returnTypeName),
                          explicitInterfaceSpecifier: null,
                          identifier: SyntaxFactory.Identifier(methodName),
                          typeParameterList: null,
                          parameterList: parameterList,
                          constraintClauses: SyntaxFactory.List<TypeParameterConstraintClauseSyntax>(),
                          body: SyntaxFactory.Block(syntax),
                          semicolonToken: SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                  // Annotate that this node should be formatted
                  .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private IEnumerable<ParameterSyntax> GetParametersList(string[] parameterTypes, string[] paramterNames)
        {
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                yield return SyntaxFactory.Parameter(attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                                                         modifiers: SyntaxFactory.TokenList(),
                                                         type: SyntaxFactory.ParseTypeName(parameterTypes[i]),
                                                         identifier: SyntaxFactory.Identifier(paramterNames[i]),
                                                         @default: null);
            }
        }
    }
}