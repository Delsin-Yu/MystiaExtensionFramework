using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mystia.Analyzers;

/// <summary>
/// Rejects code that a mod of the Mystia Extension Framework must never contain.
/// <para>
/// The framework is the only injection pipeline. A mod is a class library that declares types
/// implementing the <c>[AutoWire]</c> SDK interfaces; the host starts Il2CppInterop, injects the
/// components, installs the detours and routes the game's callbacks. A mod that patches the game
/// itself, starts a second interop runtime, or reaches into the generated interop layer competes
/// with the host for the same hooks, breaks on the next game build, and cannot be diagnosed from a
/// player's log. Every rule below is therefore an <see cref="DiagnosticSeverity.Error"/>: the code
/// must fail to compile instead of failing inside the game process.
/// </para>
/// <para>
/// The analyzer only inspects the source being compiled (never metadata), only reports once Roslyn
/// resolved the referenced symbol, and never reports in generated code, so a diagnostic always
/// points at the offending line of the mod.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BannedModCodeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>MYSTIA1001: Harmony, 0Harmony, BepInEx or MonoMod is referenced.</summary>
    public const string BannedDependencyDiagnosticId = "MYSTIA1001";

    /// <summary>MYSTIA1002: Il2CppInterop injection, startup or the raw <c>IL2CPP</c> helper is used.</summary>
    public const string Il2CppInjectionDiagnosticId = "MYSTIA1002";

    /// <summary>MYSTIA1003: System.Reflection, System.Reflection.Emit or Marshal is used.</summary>
    public const string ReflectionEscapeDiagnosticId = "MYSTIA1003";

    /// <summary>MYSTIA1004: a UnityEngine type is used.</summary>
    public const string UnityTypeDiagnosticId = "MYSTIA1004";

    /// <summary>MYSTIA1005: an identifier carries a compiler generated member name.</summary>
    public const string CompilerGeneratedDiagnosticId = "MYSTIA1005";

    /// <summary>MYSTIA1006: <c>[ModuleInitializer]</c> runs code when the mod assembly loads.</summary>
    public const string ModuleInitializerDiagnosticId = "MYSTIA1006";

    private const string Category = "Mystia.Ban";

    /// <summary>
    /// Harmony and its ecosystem patch the game at runtime. The host installs every hook through the
    /// bridge in a fixed order, so a mod that brings its own patcher either patches twice, misses
    /// the host's ordering, or keeps a hook alive after the mod is unloaded. AccessTools and every
    /// other HarmonyLib type are the same dependency under a different name.
    /// </summary>
    public static readonly DiagnosticDescriptor BannedDependency = new(
        BannedDependencyDiagnosticId,
        "Banned modding dependency 禁止的模组依赖",
        "禁止引用 '{0}'. Banned dependency '{0}': a mod must not patch or inject the game, the Mystia host owns the only injection pipeline.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Harmony, BepInEx and MonoMod patch the running game themselves. The framework "
            + "owns injection and hook ordering, so these dependencies double-patch the game and "
            + "break when the host or the game changes. 这些库会自行修改运行中的游戏，与框架的注入管线冲突.");

    /// <summary>
    /// The host starts Il2CppInterop exactly once, with the configuration (Unity version, detour
    /// provider, assembly directory) that matches the supported game build. Starting a second
    /// runtime, or injecting a component outside the host's component seam, corrupts the runtime
    /// state; the raw <c>IL2CPP</c> helper dereferences native pointers a mod cannot safely own.
    /// </summary>
    public static readonly DiagnosticDescriptor Il2CppInjection = new(
        Il2CppInjectionDiagnosticId,
        "Banned Il2CppInterop injection API 禁止的 Il2CppInterop 注入接口",
        "禁止使用 '{0}'. Banned Il2CppInterop injection API '{0}': the Mystia host starts the interop runtime and injects the components.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A second Il2CppInterop runtime or an out-of-band injected component fights the "
            + "host's runtime and its detours, and the raw IL2CPP helper hands out native pointers. "
            + "重复启动互操作运行时或绕过宿主注入组件会破坏运行时状态.");

    /// <summary>
    /// Reflection and <c>Marshal</c> defeat the boundary the SDK draws around the interop layer:
    /// they bind to private members of one exact game build (so the mod breaks silently after an
    /// update), they run native code the host never agreed to, and they cannot be audited from a
    /// player log. The SDK exposes the supported surface instead.
    /// </summary>
    public static readonly DiagnosticDescriptor ReflectionEscape = new(
        ReflectionEscapeDiagnosticId,
        "Banned reflection or interop escape 禁止的反射或互操作逃逸",
        "禁止使用 '{0}'. Banned reflection or interop escape '{0}': reflection, Reflection.Emit and Marshal bind private game members and bypass the framework boundary.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reflection and Marshal were used in the past to reach game members the SDK does "
            + "not expose. Those members are private to one game build, so such a mod breaks on the "
            + "next update and cannot be supported. 反射与 Marshal 绑定到某个游戏版本的私有成员，更新后即失效.");

    /// <summary>
    /// A mod never talks to Unity. Unity objects belong to the game's main thread and to the
    /// injected component the host creates; touching them from mod code crosses the interop
    /// boundary without the host's lifetime and thread management. Everything a mod needs
    /// (sprites, UI callbacks, scene services) arrives through the SDK interfaces.
    /// </summary>
    public static readonly DiagnosticDescriptor UnityType = new(
        UnityTypeDiagnosticId,
        "Banned UnityEngine type 禁止的 UnityEngine 类型",
        "禁止使用 UnityEngine 类型 '{0}'. Banned UnityEngine type '{0}': a mod talks to the framework, never to Unity directly.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "UnityEngine types carry Unity's thread affinity and native lifetime. The host "
            + "creates and owns them, then hands the mod the SDK abstractions instead. "
            + "UnityEngine 类型带有 Unity 的线程与原生生命周期约束，应由宿主持有.");

    /// <summary>
    /// Compiler generated members (<c>__c__DisplayClass*</c>, <c>ObjectCompilerGenerated*</c>,
    /// <c>Method_Internal_*</c>, <c>field_Public_*</c>, <c>_d__&lt;n&gt;</c>, <c>_PDM_&lt;n&gt;</c>)
    /// are the sanitised names Il2CppInterop gives to the game's closure classes, state machines and
    /// private fields. They are not public API: their names and signatures encode the private layout
    /// of one exact game build, so naming one of them breaks after any update.
    /// </summary>
    public static readonly DiagnosticDescriptor CompilerGeneratedMember = new(
        CompilerGeneratedDiagnosticId,
        "Banned compiler generated member name 禁止的编译器生成成员名",
        "标识符 '{0}' 是编译器生成的成员名，不属于公开 API. Banned identifier '{0}': a compiler generated member name is not public API and breaks on the next game build.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "These names come from the game's own compiler output, sanitised by "
            + "Il2CppInterop so that C# can name them at all. The SDK hides them behind its public "
            + "surface because they change with every game build. 这些名字随每次游戏更新变化.");

    /// <summary>
    /// A module initializer runs the moment the mod assembly is loaded, before the host has created
    /// the mod context, published the scene state or bound the bridge. It cannot be ordered, cannot
    /// report a problem to the player, and cannot be undone. Registration belongs in the generated
    /// entrance, which the host calls at the right point.
    /// </summary>
    public static readonly DiagnosticDescriptor ModuleInitializer = new(
        ModuleInitializerDiagnosticId,
        "Banned [ModuleInitializer] 禁止的 [ModuleInitializer]",
        "禁止使用 [ModuleInitializer]（'{0}'）. Banned [ModuleInitializer] on '{0}': the Mystia host decides when a mod assembly runs.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module initializer runs while the assembly loads, before the host publishes "
            + "the mod context, so the code has no logger, no scene and no ordering. "
            + "模块初始化器在程序集加载时立即执行，此时宿主上下文尚不存在.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        BannedDependency,
        Il2CppInjection,
        ReflectionEscape,
        UnityType,
        CompilerGeneratedMember,
        ModuleInitializer);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // Every name a mod writes: usings, base types, type arguments, attributes, member access.
            start.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);

            // A declaration is not an identifier name in the syntax tree, so a compiler generated
            // member name may only be seen on the declared symbol or on the declarator.
            start.RegisterSyntaxNodeAction(AnalyzeVariableDeclarator, SyntaxKind.VariableDeclarator);
            start.RegisterSymbolAction(
                AnalyzeDeclaration,
                SymbolKind.NamedType,
                SymbolKind.Method,
                SymbolKind.Property,
                SymbolKind.Event,
                SymbolKind.Parameter);
        });
    }

    /// <summary>
    /// Handles one identifier: a compiler generated name, a using directive, or a reference to a
    /// banned type. Namespace segments are skipped unless they make up a using directive, so
    /// <c>UnityEngine.GameObject</c> reports once, on <c>GameObject</c>.
    /// </summary>
    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var node = (SimpleNameSyntax)context.Node;
        var identifier = node.Identifier.ValueText;

        if (BanList.IsCompilerGeneratedName(identifier))
        {
            context.ReportDiagnostic(Diagnostic.Create(CompilerGeneratedMember, node.GetLocation(), identifier));
            return;
        }

        if (UsingDirectiveNameOf(node) is { } importedName)
        {
            // The directive is checked as text so that an alias or a static using is caught even
            // when the imported namespace has no assembly behind it yet.
            var imported = importedName.ToString();
            var importedByText = BanList.ClassifyNamespace(imported);
            if (importedByText is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    importedByText,
                    importedName.GetLocation(),
                    imported));
            }

            return;
        }

        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;
        var info = semanticModel.GetSymbolInfo(node, cancellationToken);
        var candidates = info.Symbol is not null
            ? ImmutableArray.Create(info.Symbol)
            : info.CandidateSymbols;
        if (candidates.IsDefaultOrEmpty
            && semanticModel.GetAliasInfo(node, cancellationToken) is { } aliasSymbol)
        {
            candidates = ImmutableArray.Create<ISymbol>(aliasSymbol);
        }

        // An ambiguous name (the same type in two referenced assemblies) has no single symbol but
        // still names a banned type, so every candidate is worth classifying.
        foreach (var candidate in candidates)
        {
            // An attribute name resolves to the constructor, so ask for the attribute type instead.
            var symbol = candidate is IAliasSymbol alias ? alias.Target : candidate;
            var type = symbol as INamedTypeSymbol;
            if (type is null
                && symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
                && IsAttributeName(node))
            {
                type = semanticModel.GetTypeInfo(node, cancellationToken).Type as INamedTypeSymbol;
            }

            if (type is null)
                continue;

            var descriptor = BanList.ClassifyType(type);
            if (descriptor is null)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), type.Name));
            return;
        }

        // An unresolved identifier cannot be classified, but a bare banned namespace root is still
        // worth reporting: the mod asked for the dependency, it simply also forgot to reference it.
        if (candidates.IsDefaultOrEmpty)
        {
            var descriptor = BanList.ClassifyNamespace(identifier);
            if (descriptor is not null)
                context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), identifier));
        }
    }

    /// <summary>
    /// Reports a declared field or local whose name is compiler generated. Roslyn rejects
    /// <see cref="SymbolKind.Local"/> for a symbol action, so a local is only reached through its
    /// declarator; the same declarator covers fields, which keeps a name to one report.
    /// </summary>
    private static void AnalyzeVariableDeclarator(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        var identifier = declarator.Identifier.ValueText;
        if (!BanList.IsCompilerGeneratedName(identifier))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            CompilerGeneratedMember,
            declarator.Identifier.GetLocation(),
            identifier));
    }

    /// <summary>Reports the declared name of every symbol whose name is compiler generated.</summary>
    private static void AnalyzeDeclaration(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;
        if (!BanList.IsCompilerGeneratedName(symbol.Name))
            return;

        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource)
                continue;
            context.ReportDiagnostic(Diagnostic.Create(CompilerGeneratedMember, location, symbol.Name));
            return;
        }
    }

    /// <summary>True when <paramref name="node"/> is the name of an attribute, e.g. <c>ModuleInitializer</c>.</summary>
    private static bool IsAttributeName(SimpleNameSyntax node) =>
        node.Parent switch
        {
            AttributeSyntax => true,
            QualifiedNameSyntax { Parent: AttributeSyntax } qualified => ReferenceEquals(qualified.Right, node),
            _ => false,
        };

    /// <summary>
    /// The name of the using directive that ends at <paramref name="node"/>, or <see langword="null"/>
    /// when the identifier is not part of a using directive. Reporting only the last identifier keeps
    /// a directive to one diagnostic no matter how many namespace segments it has.
    /// </summary>
    private static NameSyntax? UsingDirectiveNameOf(SimpleNameSyntax node) =>
        node.Parent switch
        {
            UsingDirectiveSyntax direct => direct.Name,
            QualifiedNameSyntax { Parent: UsingDirectiveSyntax qualified } name
                when ReferenceEquals(name.Right, node) => qualified.Name,
            _ => null,
        };
}
